using System.Text;
using Butchi.Core.Configuration;
using Butchi.Core.Inference;

namespace Butchi.Inference;

public sealed class LLamaSharpInferenceEngine : IInferenceEngine
{
    private readonly ILLamaRuntimeFactory _factory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ILLamaRuntime? _runtime;
    private ModelLoadRequest? _loadedRequest;

    public LLamaSharpInferenceEngine(ILLamaRuntimeFactory factory)
    {
        _factory = factory;
    }

    public async Task LoadAsync(AppConfig config, CancellationToken cancellationToken)
    {
        var request = ModelLoadRequest.FromConfig(config);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_runtime is not null && _loadedRequest == request)
            {
                return;
            }

            if (_runtime is not null)
            {
                await _runtime.DisposeAsync();
                _runtime = null;
                _loadedRequest = null;
            }

            _runtime = await _factory.LoadAsync(request, cancellationToken);
            _loadedRequest = request;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UnloadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_runtime is not null)
            {
                await _runtime.DisposeAsync();
                _runtime = null;
                _loadedRequest = null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async IAsyncEnumerable<string> GenerateAsync(
        InferenceRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ILLamaRuntime runtime;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            runtime = _runtime ?? throw new InvalidOperationException("No model is loaded.");
        }
        finally
        {
            _gate.Release();
        }

        var filter = new LeadingThinkBlockFilter();
        await foreach (var chunk in runtime.GenerateAsync(request, cancellationToken).WithCancellation(cancellationToken))
        {
            var visible = filter.Push(chunk);
            if (visible.Length > 0)
            {
                yield return visible;
            }
        }

        var tail = filter.Complete();
        if (tail.Length > 0)
        {
            yield return tail;
        }
    }

    public InferenceStatus GetStatus() => _loadedRequest is null
        ? new InferenceStatus(false)
        : new InferenceStatus(true, _loadedRequest.ModelRepo, _loadedRequest.ModelFile);

    public async ValueTask DisposeAsync()
    {
        await UnloadAsync(CancellationToken.None);
        _gate.Dispose();
    }

    private sealed class LeadingThinkBlockFilter
    {
        private const string OpenTag = "<think>";
        private const string CloseTag = "</think>";
        private readonly StringBuilder _buffer = new();
        private bool _suppressing;
        private bool _passThrough;

        public string Push(string chunk)
        {
            if (_passThrough)
            {
                return chunk;
            }

            _buffer.Append(chunk);
            var buffered = _buffer.ToString();

            if (!_suppressing)
            {
                var candidate = buffered.TrimStart();
                if (candidate.Length == 0 || OpenTag.StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return string.Empty;
                }

                if (!candidate.StartsWith(OpenTag, StringComparison.OrdinalIgnoreCase))
                {
                    _buffer.Clear();
                    _passThrough = true;
                    return buffered;
                }

                _suppressing = true;
            }

            var closeIndex = buffered.IndexOf(CloseTag, StringComparison.OrdinalIgnoreCase);
            if (closeIndex < 0)
            {
                return string.Empty;
            }

            var visible = buffered[(closeIndex + CloseTag.Length)..].TrimStart();
            _buffer.Clear();
            _passThrough = true;
            return visible;
        }

        public string Complete()
        {
            if (_passThrough || _suppressing || _buffer.Length == 0)
            {
                return string.Empty;
            }

            var buffered = _buffer.ToString();
            _buffer.Clear();
            return buffered;
        }
    }
}
