using Avalonia;
using Butchi.App.About;
using Butchi.App.History;
using Butchi.App.Management;
using Butchi.App.Models;
using Butchi.App.Popover;
using Butchi.App.Screenshots;
using Butchi.App.Settings;
using Butchi.App.Styling;
using Butchi.App.Tray;
using Butchi.App.Windows;
using Butchi.Core.Actions;
using Butchi.Core.Configuration;
using Butchi.Infrastructure;
using Butchi.Platform.Windows.Actions;
using Butchi.Platform.Windows.Pointer;
using Butchi.Platform.Windows.Selection;
using Butchi.Platform.Windows.Triggers;

namespace Butchi.App.Startup;

public sealed class ButchiRuntimeFactory(
    Application application,
    StartupApplicationServices services,
    IApplicationShutdown shutdown) : IButchiRuntimeFactory
{
    public async ValueTask<IButchiRuntime> CreateAsync(AppConfig config, CancellationToken cancellationToken)
    {
        ButchiTheme.Apply(application, config.Theme);
        var management = await CreateManagementAsync(services.HistoryStore, cancellationToken);

        var clipboard = new WindowsClipboardSelectionSource();
        var pasteSender = new WindowsPasteSender();
        var resultSink = new WindowsResultActionSink(
            clipboard,
            pasteSender,
            TimeSpan.FromMilliseconds(80));
        var scheduler = new TextActionScheduler(services.InferenceEngine, resultSink);
        var popoverViewModel = new PopoverViewModel();
        popoverViewModel.SetSession(string.Empty, TextAction.Translate, config.TargetLanguage);
        var popoverActionController = new PopoverActionController(
            popoverViewModel,
            scheduler,
            services.ConfigStore,
            resultSink,
            historyStore: services.HistoryStore);
        var popoverWindowController = new PopoverWindowController();
        var popover = new PopoverWindow(popoverViewModel, popoverWindowController);
        popover.Deactivated += (_, _) =>
        {
            if (popoverWindowController.HandleDeactivated())
                popover.Hide();
        };
        var selectionReader = new WindowsSelectionReader(
            new WindowsUiAutomationSelectionSource(),
            clipboard);
        var activation = new WindowsActivationCoordinator(
            selectionReader,
            new WindowsPointerContext(new Win32PointerSource()),
            popover,
            pasteSender,
            services.ConfigStore.LoadAsync);
        var trigger = new WindowsTriggerService(
            new WindowsKeyboardHookSource(),
            TimeSpan.FromMilliseconds(350));
        var interaction = new WindowsInteractionRuntime(trigger, activation);
        return new ButchiRuntime(
            application,
            management,
            popover,
            interaction,
            popoverActionController, scheduler,
            shutdown);
    }

    public async ValueTask<ManagementWindow> CreateManagementScreenshotAsync(
        ScreenshotRequest request,
        CancellationToken cancellationToken)
    {
        ButchiTheme.Apply(application, request.Theme);
        return await CreateManagementAsync(
            new ScreenshotHistoryStore(request.Fixture != "empty"),
            cancellationToken,
            autoPrepareModel: false);
    }

    public PopoverWindow CreatePopoverScreenshot(string fixture, AppThemePreference theme)
    {
        ButchiTheme.Apply(application, theme);
        return new PopoverWindow(CreatePopoverScreenshotViewModel(fixture));
    }

    private async ValueTask<ManagementWindow> CreateManagementAsync(
        IHistoryStore historyStore,
        CancellationToken cancellationToken,
        bool autoPrepareModel = true)
    {
        var general = await GeneralSettingsViewModel.CreateAsync(services.ConfigStore, cancellationToken);
        var prompts = await PromptsViewModel.CreateAsync(services.ConfigStore, cancellationToken);
        var models = await ModelManagementViewModel.CreateAsync(services.ModelManager, services.ConfigStore, cancellationToken);
        ManagementWindow? management = null;
        var history = await HistoryViewModel.CreateAsync(
            historyStore,
            new AvaloniaHistoryClipboard(() => management?.Clipboard),
            services.ConfigStore,
            cancellationToken);
        var status = services.ModelManager.GetStatus();
        var about = new AboutPrivacyViewModel(
            new LocalAiDataCleanup(historyStore, new LocalAiDataManager(services.Paths, services.InferenceEngine)),
            new AboutPrivacyMetadata(
                typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
                "Butchi",
                "MIT",
                "https://github.com/dhhieu113pro/butchi"),
            new AboutRuntimeStatus(status.IsLoaded, status.ActualBackend, status.ActualDevice));
        management = new ManagementWindow(
            new ManagementShellViewModel(), general, prompts, models, history, about,
            preference => ButchiTheme.Apply(application, preference),
            autoPrepareModel);
        return management;
    }

    private static PopoverViewModel CreatePopoverScreenshotViewModel(string fixture)
    {
        var vm = new PopoverViewModel();
        vm.SetSession("Good morning, could you send the report before lunch?", TextAction.Translate, "Vietnamese");
        switch (fixture.Trim().ToLowerInvariant())
        {
            case "idle": break;
            case "loading":
                vm.Begin(TextAction.Translate, 1);
                vm.Append(TextAction.Translate, 1, "Chào buổi sáng");
                vm.FlushPendingUpdates();
                break;
            case "error":
                vm.Begin(TextAction.Translate, 1);
                vm.Fail(TextAction.Translate, 1, "Local model is not loaded. Open Model settings to continue.");
                break;
            default:
                vm.Begin(TextAction.Translate, 1);
                vm.Append(TextAction.Translate, 1, "Chào buổi sáng, bạn có thể gửi báo cáo trước bữa trưa không?");
                vm.FlushPendingUpdates();
                vm.Complete(TextAction.Translate, 1);
                break;
        }
        return vm;
    }
}
