using Butchi.Core.Inference;

namespace Butchi.Infrastructure;

public sealed class LocalAiDataManager(AppPaths paths, IInferenceEngine inferenceEngine)
{
    public async Task ClearModelsAsync(CancellationToken cancellationToken)
    {
        await inferenceEngine.UnloadAsync(cancellationToken).ConfigureAwait(false);

        if (Directory.Exists(paths.ModelsDirectory))
        {
            Directory.Delete(paths.ModelsDirectory, recursive: true);
        }

        Directory.CreateDirectory(paths.ModelsDirectory);
    }
}
