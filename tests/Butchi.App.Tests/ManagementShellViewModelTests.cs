using Butchi.App.Management;
using Xunit;

namespace Butchi.App.Tests;

public sealed class ManagementShellViewModelTests
{
    [Fact]
    public void Defaults_to_settings_and_selects_each_management_page()
    {
        var vm = new ManagementShellViewModel();
        Assert.Equal(ManagementPage.Settings, vm.SelectedPage);

        vm.Select(ManagementPage.History);
        Assert.Equal(ManagementPage.History, vm.SelectedPage);

        vm.Select(ManagementPage.Models);
        Assert.Equal(ManagementPage.Models, vm.SelectedPage);

        vm.Select(ManagementPage.Status);
        Assert.Equal(ManagementPage.Status, vm.SelectedPage);
    }

    [Fact]
    public void Selecting_same_page_is_idempotent()
    {
        var vm = new ManagementShellViewModel();
        var notifications = 0;
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ManagementShellViewModel.SelectedPage))
                notifications++;
        };

        vm.Select(ManagementPage.Settings);

        Assert.Equal(0, notifications);
    }
}
