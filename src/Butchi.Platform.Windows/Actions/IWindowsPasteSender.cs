namespace Butchi.Platform.Windows.Actions;

public interface IWindowsPasteSender
{
    Task SendPasteAsync(CancellationToken cancellationToken);
}
