using Butchi.App.About;
using Xunit;

namespace Butchi.App.Tests;

public sealed class AboutPrivacyViewModelTests
{
    [Fact]
    public void Create_exposes_version_project_license_and_runtime_status()
    {
        var cleanup = new FakeCleanup();
        var vm = new AboutPrivacyViewModel(
            cleanup,
            new AboutPrivacyMetadata("1.2.3", "Butchi", "MIT", "https://github.com/dhhieu113pro/butchi"),
            new AboutRuntimeStatus(true, "Vulkan", "GPU"));

        Assert.Equal("1.2.3", vm.Version);
        Assert.Equal("Butchi", vm.ProjectName);
        Assert.Equal("MIT", vm.License);
        Assert.Contains("github.com", vm.ProjectUrl);
        Assert.True(vm.IsModelLoaded);
        Assert.Equal("Vulkan", vm.Backend);
        Assert.Equal("GPU", vm.Device);
    }

    [Fact]
    public async Task Delete_local_data_requires_confirmation_and_clears_history_and_models_only()
    {
        var cleanup = new FakeCleanup();
        var vm = new AboutPrivacyViewModel(
            cleanup,
            new AboutPrivacyMetadata("1.0.0", "Butchi", "MIT", "project"),
            new AboutRuntimeStatus(false, null, null));

        await vm.DeleteLocalDataAsync(false, CancellationToken.None);
        Assert.Equal(0, cleanup.Calls);

        await vm.DeleteLocalDataAsync(true, CancellationToken.None);

        Assert.Equal(1, cleanup.Calls);
        Assert.True(cleanup.HistoryCleared);
        Assert.True(cleanup.ModelsDeleted);
        Assert.False(cleanup.SettingsTouched);
        Assert.Equal("Local data deleted", vm.DeleteStatus);
    }

    private sealed class FakeCleanup : ILocalAiDataCleanup
    {
        public int Calls { get; private set; }
        public bool HistoryCleared { get; private set; }
        public bool ModelsDeleted { get; private set; }
        public bool SettingsTouched { get; private set; }

        public ValueTask DeleteLocalDataAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            HistoryCleared = true;
            ModelsDeleted = true;
            return ValueTask.CompletedTask;
        }
    }
}
