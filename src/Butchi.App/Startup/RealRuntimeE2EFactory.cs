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
    public async ValueTask<IButchiRuntime> CreateAsync(AppConfig config, CancellationToken cancellationToken)
    {
        if (Application.Current is not App application)
            throw new InvalidOperationException("The real-runtime E2E requires the Butchi application.");

        var services = new StartupApplicationServices(dataDirectory);
        try
        {
            var factory = new ButchiRuntimeFactory(application, services, application, autoPrepareModel: false);
            var runtime = (ButchiRuntime)await factory.CreateAsync(config, cancellationToken);
            return new RealRuntimeE2E(runtime, services);
        }
        catch
        {
            await services.DisposeAsync();
            throw;
        }
    }

    private sealed class RealRuntimeE2E(
        ButchiRuntime runtime,
        StartupApplicationServices services) : IButchiRuntime
    {
        private Window? _resultWindow;
        private bool _disposed;

        public bool IsTrayStarted => runtime.IsTrayStarted;

        public void StartTray()
        {
            runtime.StartTray();
            runtime.ManagementWindow.Show(ManagementPage.General);
            Dispatcher.UIThread.Post(() => _ = ExercisePopoverAsync(), DispatcherPriority.Loaded);
        }

        private async Task ExercisePopoverAsync()
        {
            try
            {
                var popover = runtime.PopoverWindow;
                var vm = popover.ViewModel;
                vm.SetSession("Hello", TextAction.Translate, "Vietnamese");
                popover.ShowPersistent();
                await RenderAsync();
                AssertLogicalTree(popover);

                vm.Begin(TextAction.Translate, 1);
                await RenderAsync();
                if (!vm.IsCompact)
                    throw new InvalidOperationException("The popover did not enter compact mode.");
                AssertLogicalTree(popover);

                vm.Append(TextAction.Translate, 1, "Xin chào");
                vm.Complete(TextAction.Translate, 1);
                await RenderAsync();
                if (vm.IsCompact)
                    throw new InvalidOperationException("The popover did not return to expanded mode.");
                AssertLogicalTree(popover);

                popover.HidePersistent();
                popover.ShowPersistent();
                await RenderAsync();
                AssertLogicalTree(popover);

                popover.RequestedThemeVariant = ThemeVariant.Dark;
                await RenderAsync();
                AssertLogicalTree(popover);
                popover.RequestedThemeVariant = ThemeVariant.Light;
                await RenderAsync();
                AssertLogicalTree(popover);

                popover.HidePersistent();
                if (!_disposed)
                    ShowResult("Butchi Popover Lifecycle Complete", "Real desktop runtime and popover lifecycle completed.");
            }
            catch (Exception exception)
            {
                // Only synthetic E2E data is used here. Keep full details in the
                // CI log to identify the actual failing framework/application frame.
                Console.Error.WriteLine(exception);
                if (!_disposed)
                    ShowResult("Butchi Popover Lifecycle Failed", exception.ToString());
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
                Content = new ScrollViewer
                {
                    Content = new TextBlock
                    {
                        Text = details,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        Margin = new Thickness(16)
                    }
                }
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
