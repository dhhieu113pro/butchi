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
        await foreach (var chunk in GenerateDetailedAsync(request, cancellationToken).WithCancellation(cancellationToken))
        {
            if (chunk.Kind == InferenceStreamChunkKind.Answer && chunk.Text.Length > 0)
            {
                yield return chunk.Text;
            }
        }
    }

    public async IAsyncEnumerable<InferenceStreamChunk> GenerateDetailedAsync(
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

        var parser = new LeadingThinkBlockParser();
        await foreach (var chunk in runtime.GenerateAsync(request, cancellationToken).WithCancellation(cancellationToken))
        {
            foreach (var parsed in parser.Push(chunk))
            {
                if (parsed.Text.Length > 0)
                {
                    yield return parsed;
                }
            }
        }

        foreach (var parsed in parser.Complete())
        {
            if (parsed.Text.Length > 0)
            {
                yield return parsed;
            }
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

    private sealed class LeadingThinkBlockParser
    {
        private const string OpenTag = "<think>";
        private const string CloseTag = "</think>";
        private readonly StringBuilder _buffer = new();
        private ParseState _state;
        private bool _reasoningStarted;

        public IReadOnlyList<InferenceStreamChunk> Push(string chunk)
        {
            if (_state == ParseState.Answer)
            {
                return chunk.Length == 0
                    ? []
                    : [new InferenceStreamChunk(InferenceStreamChunkKind.Answer, chunk)];
            }

            _buffer.Append(chunk);
            var output = new List<InferenceStreamChunk>(2);

            if (_state == ParseState.Undetermined)
            {
                var buffered = _buffer.ToString();
                var candidate = buffered.TrimStart();
                if (candidate.Length == 0 || OpenTag.StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return output;
                }

                if (!candidate.StartsWith(OpenTag, StringComparison.OrdinalIgnoreCase))
                {
                    _buffer.Clear();
                    _state = ParseState.Answer;
                    output.Add(new InferenceStreamChunk(InferenceStreamChunkKind.Answer, buffered));
                    return output;
                }

                var openIndex = buffered.IndexOf(OpenTag, StringComparison.OrdinalIgnoreCase);
                _buffer.Remove(0, openIndex + OpenTag.Length);
                _state = ParseState.Reasoning;
            }

            EmitReasoning(output);
            return output;
        }

        public IReadOnlyList<InferenceStreamChunk> Complete()
        {
            if (_buffer.Length == 0)
            {
                return [];
            }

            var buffered = _buffer.ToString();
            _buffer.Clear();

            return _state switch
            {
                ParseState.Undetermined =>
                    [new InferenceStreamChunk(InferenceStreamChunkKind.Answer, buffered)],
                ParseState.Reasoning =>
                    [new InferenceStreamChunk(
                        InferenceStreamChunkKind.Reasoning,
                        NormalizeReasoningStart(buffered).TrimEnd('\r', '\n'))],
                _ => []
            };
        }

        private void EmitReasoning(List<InferenceStreamChunk> output)
        {
            while (_state == ParseState.Reasoning)
            {
                var buffered = _buffer.ToString();
                var closeIndex = buffered.IndexOf(CloseTag, StringComparison.OrdinalIgnoreCase);
                if (closeIndex >= 0)
                {
                    var reasoning = buffered[..closeIndex].TrimEnd('\r', '\n');
                    reasoning = NormalizeReasoningStart(reasoning);
                    if (reasoning.Length > 0)
                    {
                        output.Add(new InferenceStreamChunk(InferenceStreamChunkKind.Reasoning, reasoning));
                    }

                    var answer = buffered[(closeIndex + CloseTag.Length)..].TrimStart();
                    _buffer.Clear();
                    _state = ParseState.Answer;
                    if (answer.Length > 0)
                    {
                        output.Add(new InferenceStreamChunk(InferenceStreamChunkKind.Answer, answer));
                    }
                    return;
                }

                var retainedLength = LongestCloseTagPrefixSuffix(buffered);
                var safeLength = buffered.Length - retainedLength;
                if (safeLength == 0)
                {
                    return;
                }

                var safe = buffered[..safeLength];
                _buffer.Remove(0, safeLength);
                if (retainedLength > 0)
                {
                    safe = safe.TrimEnd('\r', '\n');
                }

                safe = NormalizeReasoningStart(safe);
                if (safe.Length > 0)
                {
                    output.Add(new InferenceStreamChunk(InferenceStreamChunkKind.Reasoning, safe));
                }
                return;
            }
        }

        private string NormalizeReasoningStart(string text)
        {
            if (_reasoningStarted || text.Length == 0)
            {
                return text;
            }

            var normalized = text.TrimStart('\r', '\n');
            if (normalized.Length > 0)
            {
                _reasoningStarted = true;
            }
            return normalized;
        }

        private static int LongestCloseTagPrefixSuffix(string text)
        {
            var max = Math.Min(CloseTag.Length - 1, text.Length);
            for (var length = max; length > 0; length--)
            {
                var suffix = text[^length..];
                if (CloseTag.StartsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return length;
                }
            }

            return 0;
        }

        private enum ParseState
        {
            Undetermined,
            Reasoning,
            Answer
        }
    }
}
