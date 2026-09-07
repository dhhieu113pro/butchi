using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Butchi.App.Management;
using Butchi.App.Popover;
using Butchi.Core.Actions;
using Butchi.Core.Configuration;

namespace Butchi.App.Startup;

/// <summary>
/// Windows E2E-only host. Uses the production runtime and real Avalonia windows,
/// but keeps model downloads and inference out of the first-run lifecycle test.
/// </summary>
internal sealed class RealRuntimeE2EFactory(string dataDirectory) : IButchiRuntimeFactory
{
    private void Record(string stage, Exception? exception = null)
    {
        // This mode uses a fresh temporary data directory and synthetic input only.
        // Do not enable full exception logging for the normal user-facing startup path.
        File.AppendAllText(
            Path.Combine(dataDirectory, "startup-diagnostics.txt"),
            stage + Environment.NewLine +
            (exception is null ? string.Empty : exception + Environment.NewLine));
    }

    public async ValueTask<IButchiRuntime> CreateAsync(AppConfig config, CancellationToken cancellationToken)
    {
        if (Application.Current is not App application)
            throw new InvalidOperationException("The real-runtime E2E requires the Butchi application.");

        Record("Creating production application services");
        var services = new StartupApplicationServices(dataDirectory);
        try
        {
            Record("Creating production desktop runtime");
            var factory = new ButchiRuntimeFactory(application, services, application, autoPrepareModel: false);
            var runtime = (ButchiRuntime)await factory.CreateAsync(config, cancellationToken);
            Record("Production desktop runtime created");
            return new RealRuntimeE2E(runtime, services, Record);
        }
        catch (Exception exception)
        {
            Record("Production runtime composition failed", exception);
            await services.DisposeAsync();
            throw;
        }
    }

    private sealed class RealRuntimeE2E(
        ButchiRuntime runtime,
        StartupApplicationServices services,
        Action<string, Exception?> record) : IButchiRuntime
    {
        private Window? _resultWindow;
        private bool _disposed;

        public bool IsTrayStarted => runtime.IsTrayStarted;

        public void StartTray()
        {
            try
            {
                record("Starting production tray and interaction runtime", null);
                runtime.StartTray();
                runtime.ManagementWindow.Show(ManagementPage.General);
                record("Production tray and management window started", null);
                Dispatcher.UIThread.Post(() => _ = ExercisePopoverAsync(), DispatcherPriority.Loaded);
            }
            catch (Exception exception)
            {
                record("Production tray startup failed", exception);
                throw;
            }
        }

        private async Task ExercisePopoverAsync()
        {
            try
            {
                var popover = runtime.PopoverWindow;
                var vm = popover.ViewModel;
                record("Showing expanded popover", null);
                vm.SetSession("Hello", TextAction.Translate, "Vietnamese");
                popover.ShowPersistent();
                await RenderAsync();
                AssertLogicalTree(popover);

                record("Switching to compact popover", null);
                vm.Begin(TextAction.Translate, 1);
                await RenderAsync();
                if (!vm.IsCompact)
                    throw new InvalidOperationException("The popover did not enter compact mode.");
                AssertLogicalTree(popover);

                record("Switching to expanded result", null);
                vm.Append(TextAction.Translate, 1, "Xin chào");
                vm.Complete(TextAction.Translate, 1);
                await RenderAsync();
                if (vm.IsCompact)
                    throw new InvalidOperationException("The popover did not return to expanded mode.");
                AssertLogicalTree(popover);

                record("Hiding and showing the popover", null);
                popover.HidePersistent();
                popover.ShowPersistent();
                await RenderAsync();
                AssertLogicalTree(popover);

                record("Changing popover theme to dark", null);
                popover.RequestedThemeVariant = ThemeVariant.Dark;
                await RenderAsync();
                AssertLogicalTree(popover);
                record("Changing popover theme to light", null);
                popover.RequestedThemeVariant = ThemeVariant.Light;
                await RenderAsync();
                AssertLogicalTree(popover);

                popover.HidePersistent();
                record("Real popover lifecycle completed", null);
                if (!_disposed)
                    ShowResult("Butchi Popover Lifecycle Complete", "Real desktop runtime and popover lifecycle completed.");
            }
            catch (Exception exception)
            {
                record("Real popover lifecycle failed", exception);
                if (!_disposed)
                    ShowResult("Butchi Popover Lifecycle Failed", exception.GetType().Name);
            }
        }

        private static async Task RenderAsync()
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        }

        private static void AssertLogicalTree(ILogical root)
        {
            foreach (var child in root.LogicalChildren)
            {
                if (!ReferenceEquals(child.GetLogicalParent(), root))
                    throw new InvalidOperationException($"Logical child {child.GetType().Name} has an unexpected parent.");
                AssertLogicalTree(child);
            }
        }

        private void ShowResult(string title, string details)
        {
            _resultWindow = new Window
            {
                Title = title,
                Width = 600,
                Height = 360,
                Content = new TextBlock { Text = details, Margin = new Thickness(16) }
            };
            _resultWindow.Show();
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            _resultWindow?.Close();
            try
            {
                await runtime.DisposeAsync();
            }
            finally
            {
                await services.DisposeAsync();
            }
        }
    }
}
