namespace Butchi.Inference;

public sealed record ModelDownloadProgress(long BytesDownloaded, long? TotalBytes)
{
    public double? Fraction => TotalBytes is > 0 ? (double)BytesDownloaded / TotalBytes.Value : null;
}

public sealed record ModelDownloadStream(Stream Stream, long? ContentLength) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Stream.DisposeAsync();
}

public interface IModelDownloadSource
{
    Task<ModelDownloadStream> OpenAsync(string repo, string file, CancellationToken cancellationToken);
}

public sealed class ModelDownloader(IModelDownloadSource source)
{
    public async Task<string> DownloadAsync(
        string repo,
        string file,
        string finalPath,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(finalPath);

        var parent = Path.GetDirectoryName(finalPath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var tempPath = finalPath + ".download";
        TryDelete(tempPath);

        try
        {
            await using var download = await source.OpenAsync(repo, file, cancellationToken).ConfigureAwait(false);
            await using (var output = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 128,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[1024 * 128];
                long written = 0;

                while (true)
                {
                    var read = await download.Stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    written += read;
                    progress?.Report(new ModelDownloadProgress(written, download.ContentLength));
                }

                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, finalPath, overwrite: true);
            return finalPath;
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; the original operation remains authoritative.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup; the original operation remains authoritative.
        }
    }
}
