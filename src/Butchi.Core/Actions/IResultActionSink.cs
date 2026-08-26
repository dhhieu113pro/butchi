namespace Butchi.Core.Actions;

public interface IResultActionSink
{
    Task CopyAsync(string text, CancellationToken cancellationToken);
    Task ReplaceAsync(string text, CancellationToken cancellationToken);
}
