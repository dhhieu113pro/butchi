using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Butchi.App.About;
using Butchi.App.Branding;
using Butchi.App.History;
using Butchi.App.Management;
using Butchi.App.Models;
using Butchi.App.Popover;
using Butchi.App.Screenshots;
using Butchi.App.Settings;
using Butchi.App.Styling;
using Butchi.App.Tray;
using Butchi.Core.Actions;
using Butchi.Inference;
using Butchi.Infrastructure;

namespace Butchi.App;

public sealed class App : Application, IApplicationShutdown
{
    private TrayIcons? _trayIcons;
    private TrayIcon? _trayIcon;
    private HttpClient? _modelHttpClient;
    private LLamaSharpInferenceEngine? _inferenceEngine;

    public PopoverWindow? PopoverWindow { get; private set; }
    public ManagementWindow? ManagementWindow { get; private set; }
    public TrayCommandRouter? TrayRouter { get; private set; }

    public override void OnFrameworkInitializationCompleted()
    {
        ButchiTheme.Initialize(this);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var paths = new AppPaths();
            paths.EnsureDirectories();
            var configStore = new JsonAppConfigStoreAdapter(new JsonConfigStore(paths));
            var generalSettings = GeneralSettingsViewModel.CreateAsync(configStore, CancellationToken.None).AsTask().GetAwaiter().GetResult();
            var prompts = PromptsViewModel.CreateAsync(configStore, CancellationToken.None).AsTask().GetAwaiter().GetResult();

            _modelHttpClient = new HttpClient();
            var downloader = new ModelDownloader(new HuggingFaceModelDownloadSource(_modelHttpClient));
            _inferenceEngine = new LLamaSharpInferenceEngine(new LLamaSharpRuntimeFactory(request => paths.ModelPath(request.ModelRepo, request.ModelFile)));
            var modelManager = new FileModelManager(paths, downloader, _inferenceEngine, configStore);
            var models = ModelManagementViewModel.CreateAsync(modelManager, configStore, CancellationToken.None).AsTask().GetAwaiter().GetResult();

            var hasManagementScreenshot = ScreenshotRequest.TryParse(Program.StartupArgs, out var screenshotRequest);
            IHistoryStore historyStore = hasManagementScreenshot && screenshotRequest!.Page == ManagementPage.History
                ? new ScreenshotHistoryStore(screenshotRequest.Fixture != "empty")
                : new SqliteHistoryStoreAdapter(new SqliteHistoryStore(paths));
            var historyClipboard = new AvaloniaHistoryClipboard(() => ManagementWindow?.Clipboard);
            var history = HistoryViewModel.CreateAsync(historyStore, historyClipboard, configStore, CancellationToken.None).AsTask().GetAwaiter().GetResult();

            var runtime = modelManager.GetStatus();
            var about = new AboutPrivacyViewModel(
                new LocalAiDataCleanup(historyStore, new LocalAiDataManager(paths, _inferenceEngine)),
                new AboutPrivacyMetadata(typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0", "Butchi", "MIT", "https://github.com/dhhieu113pro/butchi"),
                new AboutRuntimeStatus(runtime.IsLoaded, runtime.ActualBackend, runtime.ActualDevice));

            ButchiTheme.Apply(this, generalSettings.Theme);
            ManagementWindow = new ManagementWindow(new ManagementShellViewModel(), generalSettings, prompts, models, history, about, preference => ButchiTheme.Apply(this, preference));

            var popoverScreenshotIndex = Array.IndexOf(Program.StartupArgs, "--screenshot-popover");
            if (popoverScreenshotIndex >= 0)
            {
                if (popoverScreenshotIndex + 1 >= Program.StartupArgs.Length || string.IsNullOrWhiteSpace(Program.StartupArgs[popoverScreenshotIndex + 1]))
                    throw new ArgumentException("--screenshot-popover requires an output path.", nameof(Program.StartupArgs));

                PopoverWindow = new PopoverWindow(CreatePopoverScreenshotViewModel(GetOptionValue(Program.StartupArgs, "--fixture") ?? "success"));
                ScreenshotRunner.RunPopover(Program.StartupArgs[popoverScreenshotIndex + 1], PopoverWindow, Shutdown);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            if (hasManagementScreenshot)
            {
                ScreenshotRunner.Run(screenshotRequest!, ManagementWindow, Shutdown);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            PopoverWindow = new PopoverWindow(new PopoverViewModel());
            TrayRouter = new TrayCommandRouter(ManagementWindow, this);
            ConfigureTray(TrayRouter);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static PopoverViewModel CreatePopoverScreenshotViewModel(string fixture)
    {
        var vm = new PopoverViewModel();
        vm.SetSession("Good morning, could you send the report before lunch?", TextAction.Translate, "Vietnamese");
        switch (fixture.Trim().ToLowerInvariant())
        {
            case "idle":
                break;
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

    private static string? GetOptionValue(string[] args, string option)
    {
        var index = Array.IndexOf(args, option);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    public void Shutdown()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
        _trayIcons = null;
        PopoverWindow?.Destroy();
        PopoverWindow = null;

        if (ManagementWindow is { } management)
        {
            management.Hide();
            ManagementWindow = null;
        }

        if (_inferenceEngine is not null)
        {
            _inferenceEngine.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _inferenceEngine = null;
        }
        _modelHttpClient?.Dispose();
        _modelHttpClient = null;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private void ConfigureTray(TrayCommandRouter router)
    {
        var menu = new NativeMenu
        {
            Item("Settings", TrayCommand.OpenSettings, router),
            Item("History", TrayCommand.OpenHistory, router),
            Item("Models", TrayCommand.OpenModels, router),
            Item("Status", TrayCommand.OpenStatus, router),
            new NativeMenuItemSeparator(),
            Item("Exit", TrayCommand.Exit, router)
        };

        _trayIcon = new TrayIcon { Icon = BrandAssets.CreateWindowIcon(), ToolTipText = "Butchi", Menu = menu };
        _trayIcons = new TrayIcons { _trayIcon };
        TrayIcon.SetIcons(this, _trayIcons);
    }

    private static NativeMenuItem Item(string header, TrayCommand command, TrayCommandRouter router)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => router.Execute(command);
        return item;
    }
}
