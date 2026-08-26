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

        await foreach (var chunk in runtime.GenerateAsync(request, cancellationToken).WithCancellation(cancellationToken))
        {
            yield return chunk;
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
}
