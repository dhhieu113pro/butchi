using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Butchi.App.Management;

public enum ManagementPage
{
    Settings,
    History,
    Models,
    Status
}

public sealed class ManagementShellViewModel : INotifyPropertyChanged
{
    private ManagementPage _selectedPage = ManagementPage.Settings;

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
