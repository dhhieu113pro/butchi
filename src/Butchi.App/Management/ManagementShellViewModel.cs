using System.ComponentModel;

namespace Butchi.App.Management;

public enum ManagementPage
{
    General = 0,
    Prompts = 1,
    Model = 2,
    History = 3,
    AboutPrivacy = 4,

    // Compatibility aliases for existing tray/screenshot callers during Task 14 rollout.
    Settings = General,
    Models = Model,
    Status = AboutPrivacy
}

public sealed class ManagementShellViewModel : INotifyPropertyChanged
{
    private ManagementPage _selectedPage = ManagementPage.General;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ManagementPage SelectedPage => _selectedPage;

    public void Select(ManagementPage page)
    {
        if (_selectedPage == page)
            return;

        _selectedPage = page;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedPage)));
    }
}
