using Butchi.Core.Inference;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace Butchi.Inference;

public sealed class LLamaSharpRuntimeFactory : ILLamaRuntimeFactory
{
    private readonly Func<ModelLoadRequest, string> _modelPathResolver;

    public LLamaSharpRuntimeFactory(Func<ModelLoadRequest, string> modelPathResolver)
    {
        _modelPathResolver = modelPathResolver;
    }

    public async Task<ILLamaRuntime> LoadAsync(ModelLoadRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var modelPath = _modelPathResolver(request);
        var parameters = new ModelParams(modelPath)
        {
            ContextSize = request.ContextSize,
            GpuLayerCount = checked((int)request.GpuLayers)
        };

        var weights = await LLamaWeights.LoadFromFileAsync(parameters);
        cancellationToken.ThrowIfCancellationRequested();
        return new Runtime(weights, parameters);
    }

    private sealed class Runtime : ILLamaRuntime
    {
        private readonly LLamaWeights _weights;
        private readonly StatelessExecutor _executor;

        public Runtime(LLamaWeights weights, ModelParams parameters)
        {
            _weights = weights;
            _executor = new StatelessExecutor(weights, parameters)
            {
                ApplyTemplate = false
            };
        }

        public async IAsyncEnumerable<string> GenerateAsync(
            InferenceRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var sampling = new DefaultSamplingPipeline
            {
                Temperature = request.Temperature,
                Seed = request.Seed
            };
            var inferenceParams = new InferenceParams
            {
                MaxTokens = checked((int)request.MaxTokens),
                SamplingPipeline = sampling
            };

            await foreach (var chunk in _executor.InferAsync(request.Prompt, inferenceParams, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return chunk;
            }
        }

        public ValueTask DisposeAsync()
        {
            _weights.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
