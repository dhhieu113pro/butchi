# Cross-platform Launch-at-login Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a single `Launch Butchi at login` preference that reflects and controls the real per-user login-startup registration on unpackaged Windows, packaged/MSIX Windows, macOS, and Linux.

**Architecture:** Put the platform-neutral `IAutoStartService` contract and persisted preference in `Butchi.Core`. Put macOS/Linux file-based registration in `Butchi.Infrastructure`, Windows Run-key and MSIX `StartupTask` registration in `Butchi.Platform.Windows`, and select the implementation in the app composition root. `GeneralSettingsViewModel` treats the OS registration as the source of truth and persists only verified state.

**Tech Stack:** .NET 10, C# 14, Avalonia 12.1.1, xUnit 2.9.3, Windows App SDK/WinRT targeting references via `Microsoft.Windows.SDK.NET.Ref` 10.0.26100.87, Windows Registry, macOS LaunchAgents plist, Linux XDG autostart desktop entries, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-09-06-cross-platform-autostart-design.md`

## Global Constraints

- The user-facing setting is exactly `Launch Butchi at login` with description `Start Butchi automatically when you sign in.`
- The persisted default is `false`.
- Registration is per-user only; never request elevation or machine-wide startup.
- Do not use shell commands where Registry/file/WinRT APIs are available.
- Opening General settings may reconcile persisted state to the real OS state, but must never silently enable startup.
- Packaged Windows must respect `DisabledByUser` and `DisabledByPolicy`; do not fall back to the Run key in those states.
- macOS registration path is `~/Library/LaunchAgents/io.github.dhhieu113pro.butchi.plist`.
- Linux registration path is `$XDG_CONFIG_HOME/autostart/butchi.desktop`, falling back to `~/.config/autostart/butchi.desktop`.
- File-based startup registrations must be written atomically.
- Tests must not modify the CI runner's real login-startup registration.
- Existing Windows hotkey/selection/paste runtime is outside this feature.

---

### Task 1: Add the shared preference and autostart contract

**Files:**
- Create: `src/Butchi.Core/Platform/IAutoStartService.cs`
- Modify: `src/Butchi.Core/Configuration/AppConfig.cs`
- Modify: `tests/Butchi.Infrastructure.Tests/PersistenceCompatibilityTests.cs`

**Interfaces:**
- Produces: `Butchi.Core.Platform.IAutoStartService`
- Produces: `AppConfig.LaunchAtLogin : bool`
- Consumes: existing `JsonConfigStore` serialization semantics.

- [ ] **Step 1: Write failing persistence/contract tests**

Add these assertions to `PersistenceCompatibilityTests`:

```csharp
[Fact]
public async Task Config_store_defaults_launch_at_login_to_false_for_legacy_json()
{
    Directory.CreateDirectory(_root);
    await File.WriteAllTextAsync(
        Path.Combine(_root, "config.json"),
        """{"targetLanguage":"English"}""");

    var config = await new JsonConfigStore(new AppPaths(_root)).LoadAsync();

    Assert.False(config.LaunchAtLogin);
}

[Fact]
public async Task Config_store_round_trips_launch_at_login()
{
    var store = new JsonConfigStore(new AppPaths(_root));
    await store.SaveAsync(AppConfig.Default with { LaunchAtLogin = true });

    using var document = JsonDocument.Parse(
        await File.ReadAllTextAsync(Path.Combine(_root, "config.json")));

    Assert.True(document.RootElement.GetProperty("launchAtLogin").GetBoolean());
    Assert.True((await store.LoadAsync()).LaunchAtLogin);
}
```

- [ ] **Step 2: Run the focused tests and verify they fail**

Run:

```bash
dotnet test tests/Butchi.Infrastructure.Tests/Butchi.Infrastructure.Tests.csproj --filter "Config_store_defaults_launch_at_login_to_false_for_legacy_json|Config_store_round_trips_launch_at_login"
```

Expected: compile/test failure because `AppConfig.LaunchAtLogin` does not exist.

- [ ] **Step 3: Add the shared contract and config property**

Create `src/Butchi.Core/Platform/IAutoStartService.cs`:

```csharp
namespace Butchi.Core.Platform;

public interface IAutoStartService
{
    ValueTask<bool> GetEnabledAsync(CancellationToken cancellationToken);
    ValueTask EnableAsync(CancellationToken cancellationToken);
    ValueTask DisableAsync(CancellationToken cancellationToken);
}
```

Add to `AppConfig` near the theme/application-level preferences:

```csharp
public bool LaunchAtLogin { get; init; } = false;
```

Also extend the existing round-trip equality assertions in `PersistenceCompatibilityTests`:

```csharp
Assert.Equal(config.LaunchAtLogin, restored.LaunchAtLogin);
```

- [ ] **Step 4: Run persistence tests and verify they pass**

Run:

```bash
dotnet test tests/Butchi.Infrastructure.Tests/Butchi.Infrastructure.Tests.csproj --filter "PersistenceCompatibilityTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Butchi.Core/Platform/IAutoStartService.cs src/Butchi.Core/Configuration/AppConfig.cs tests/Butchi.Infrastructure.Tests/PersistenceCompatibilityTests.cs
git commit -m "feat: add launch-at-login config contract"
```

---

### Task 2: Implement macOS LaunchAgent and Linux XDG autostart

**Files:**
- Create: `src/Butchi.Infrastructure/AutoStart/AtomicTextFile.cs`
- Create: `src/Butchi.Infrastructure/AutoStart/MacOsAutoStartService.cs`
- Create: `src/Butchi.Infrastructure/AutoStart/LinuxAutoStartService.cs`
- Create: `tests/Butchi.Infrastructure.Tests/AutoStartFileServiceTests.cs`

**Interfaces:**
- Consumes: `IAutoStartService` from Task 1.
- Produces: `MacOsAutoStartService(string launchAgentsDirectory, string executablePath)`.
- Produces: `LinuxAutoStartService(string configDirectory, string executablePath)`.
- Both implementations verify that an existing registration targets the current executable before returning `true`.

- [ ] **Step 1: Write failing temp-directory tests**

Create `tests/Butchi.Infrastructure.Tests/AutoStartFileServiceTests.cs` with tests shaped as follows:

```csharp
using Butchi.Infrastructure.AutoStart;
using Xunit;

namespace Butchi.Infrastructure.Tests;

public sealed class AutoStartFileServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "butchi-autostart-tests", Guid.NewGuid().ToString("N"));
    private readonly string _exe = Path.Combine(Path.GetTempPath(), "Butchi Folder", "butchi executable");

    [Fact]
    public async Task Mac_service_writes_verifies_and_removes_only_its_launch_agent()
    {
        var service = new MacOsAutoStartService(_root, _exe);

        Assert.False(await service.GetEnabledAsync(CancellationToken.None));
        await service.EnableAsync(CancellationToken.None);

        var path = Path.Combine(_root, "io.github.dhhieu113pro.butchi.plist");
        var xml = await File.ReadAllTextAsync(path);
        Assert.Contains("<key>Label</key>", xml, StringComparison.Ordinal);
        Assert.Contains("io.github.dhhieu113pro.butchi", xml, StringComparison.Ordinal);
        Assert.Contains(System.Security.SecurityElement.Escape(_exe), xml, StringComparison.Ordinal);
        Assert.Contains("<key>RunAtLoad</key>", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("KeepAlive", xml, StringComparison.Ordinal);
        Assert.True(await service.GetEnabledAsync(CancellationToken.None));

        await service.DisableAsync(CancellationToken.None);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Linux_service_writes_verifies_and_removes_its_desktop_entry()
    {
        var service = new LinuxAutoStartService(_root, _exe);

        await service.EnableAsync(CancellationToken.None);

        var path = Path.Combine(_root, "autostart", "butchi.desktop");
        var desktop = await File.ReadAllTextAsync(path);
        Assert.Contains("[Desktop Entry]", desktop, StringComparison.Ordinal);
        Assert.Contains("Type=Application", desktop, StringComparison.Ordinal);
        Assert.Contains("Name=Butchi", desktop, StringComparison.Ordinal);
        Assert.Contains("X-GNOME-Autostart-enabled=true", desktop, StringComparison.Ordinal);
        Assert.True(await service.GetEnabledAsync(CancellationToken.None));

        await service.DisableAsync(CancellationToken.None);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task File_services_report_false_for_foreign_executable_registration()
    {
        var mac = new MacOsAutoStartService(_root, _exe);
        await mac.EnableAsync(CancellationToken.None);
        var plist = Path.Combine(_root, "io.github.dhhieu113pro.butchi.plist");
        await File.WriteAllTextAsync(plist, (await File.ReadAllTextAsync(plist)).Replace(_exe, "/tmp/other", StringComparison.Ordinal));
        Assert.False(await mac.GetEnabledAsync(CancellationToken.None));

        var linuxRoot = Path.Combine(_root, "linux");
        var linux = new LinuxAutoStartService(linuxRoot, _exe);
        await linux.EnableAsync(CancellationToken.None);
        var desktop = Path.Combine(linuxRoot, "autostart", "butchi.desktop");
        await File.WriteAllTextAsync(desktop, (await File.ReadAllTextAsync(desktop)).Replace(_exe, "/tmp/other", StringComparison.Ordinal));
        Assert.False(await linux.GetEnabledAsync(CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
```

- [ ] **Step 2: Run and verify the tests fail**

Run:

```bash
dotnet test tests/Butchi.Infrastructure.Tests/Butchi.Infrastructure.Tests.csproj --filter "AutoStartFileServiceTests"
```

Expected: compile failure because the service types do not exist.

- [ ] **Step 3: Implement atomic writes and macOS LaunchAgent registration**

Create `AtomicTextFile` with one focused method:

```csharp
internal static class AtomicTextFile
{
    public static async ValueTask WriteAsync(string path, string contents, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("A parent directory is required.", nameof(path));
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temp, contents, cancellationToken);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
```

Implement `MacOsAutoStartService` with constants and XML generation:

```csharp
public sealed class MacOsAutoStartService(string launchAgentsDirectory, string executablePath) : IAutoStartService
{
    private const string FileName = "io.github.dhhieu113pro.butchi.plist";
    private string RegistrationPath => Path.Combine(launchAgentsDirectory, FileName);

    public async ValueTask<bool> GetEnabledAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(RegistrationPath)) return false;
        var xml = await File.ReadAllTextAsync(RegistrationPath, cancellationToken);
        var escaped = System.Security.SecurityElement.Escape(executablePath) ?? executablePath;
        return xml.Contains($"<string>{escaped}</string>", StringComparison.Ordinal);
    }

    public ValueTask EnableAsync(CancellationToken cancellationToken)
    {
        var escaped = System.Security.SecurityElement.Escape(executablePath) ?? executablePath;
        var xml = $"""<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>Label</key><string>io.github.dhhieu113pro.butchi</string>
<key>ProgramArguments</key><array><string>{escaped}</string></array>
<key>RunAtLoad</key><true/>
</dict></plist>
""";
        return AtomicTextFile.WriteAsync(RegistrationPath, xml, cancellationToken);
    }

    public ValueTask DisableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(RegistrationPath)) File.Delete(RegistrationPath);
        return ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 4: Implement Linux XDG desktop registration with escaping**

Use XDG desktop-entry quoting rules for spaces, quotes and backslashes instead of invoking a shell:

```csharp
public sealed class LinuxAutoStartService(string configDirectory, string executablePath) : IAutoStartService
{
    private string RegistrationPath => Path.Combine(configDirectory, "autostart", "butchi.desktop");

    internal static string QuoteExec(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
                     .Replace("\"", "\\\"", StringComparison.Ordinal)
                     .Replace("`", "\\`", StringComparison.Ordinal)
                     .Replace("$", "\\$", StringComparison.Ordinal) + "\"";

    public async ValueTask<bool> GetEnabledAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(RegistrationPath)) return false;
        var text = await File.ReadAllTextAsync(RegistrationPath, cancellationToken);
        return text.Contains($"Exec={QuoteExec(executablePath)}", StringComparison.Ordinal)
            && !text.Contains("Hidden=true", StringComparison.OrdinalIgnoreCase);
    }

    public ValueTask EnableAsync(CancellationToken cancellationToken) => AtomicTextFile.WriteAsync(
        RegistrationPath,
        $"""[Desktop Entry]
Type=Application
Name=Butchi
Exec={QuoteExec(executablePath)}
X-GNOME-Autostart-enabled=true
""",
        cancellationToken);

    public ValueTask DisableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(RegistrationPath)) File.Delete(RegistrationPath);
        return ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 5: Add malformed/escaping assertions and run tests**

Add assertions that malformed files return `false` and that a Linux executable path containing spaces, `$`, quotes, and backslashes is serialized through `QuoteExec`. Then run:

```bash
dotnet test tests/Butchi.Infrastructure.Tests/Butchi.Infrastructure.Tests.csproj --filter "AutoStartFileServiceTests"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Butchi.Infrastructure/AutoStart tests/Butchi.Infrastructure.Tests/AutoStartFileServiceTests.cs
git commit -m "feat: add macos and linux login autostart"
```

---

### Task 3: Implement unpackaged and packaged Windows autostart

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `src/Butchi.Platform.Windows/Butchi.Platform.Windows.csproj`
- Create: `src/Butchi.Platform.Windows/AutoStart/WindowsRunKeyAutoStartService.cs`
- Create: `src/Butchi.Platform.Windows/AutoStart/WindowsStartupTaskAutoStartService.cs`
- Create: `src/Butchi.Platform.Windows/AutoStart/WindowsAutoStartService.cs`
- Create: `src/Butchi.Platform.Windows/AutoStart/WindowsPackageIdentity.cs`
- Create: `tests/Butchi.Platform.Windows.Tests/WindowsAutoStartServiceTests.cs`

**Interfaces:**
- Consumes: `IAutoStartService` from Task 1.
- Produces: `WindowsAutoStartService.CreateDefault(string executablePath)`.
- Produces internal test seams `IRunKeyStore`, `IStartupTaskAccessor`, and `IWindowsPackageIdentity`.
- `IStartupTaskAccessor.GetStateAsync` and `RequestEnableAsync` return a project-local `WindowsStartupTaskStatus` enum so unit tests do not require a real packaged app.

- [ ] **Step 1: Write failing Windows selection, Run-key, and startup-task tests**

Create tests that never touch the real Registry or a real startup task:

```csharp
[Fact]
public async Task Unpackaged_windows_uses_run_key_and_validates_the_current_executable()
{
    var store = new FakeRunKeyStore();
    var service = new WindowsRunKeyAutoStartService(store, @"C:\Program Files\Butchi\butchi.exe");

    await service.EnableAsync(CancellationToken.None);

    Assert.Equal("\"C:\\Program Files\\Butchi\\butchi.exe\"", store.Value);
    Assert.True(await service.GetEnabledAsync(CancellationToken.None));
    store.Value = "\"C:\\Other\\other.exe\"";
    Assert.False(await service.GetEnabledAsync(CancellationToken.None));
}

[Theory]
[InlineData(WindowsStartupTaskStatus.Enabled, true)]
[InlineData(WindowsStartupTaskStatus.EnabledByPolicy, true)]
[InlineData(WindowsStartupTaskStatus.Disabled, false)]
[InlineData(WindowsStartupTaskStatus.DisabledByUser, false)]
[InlineData(WindowsStartupTaskStatus.DisabledByPolicy, false)]
public async Task Packaged_windows_maps_startup_task_state(WindowsStartupTaskStatus state, bool expected)
{
    var accessor = new FakeStartupTaskAccessor { State = state };
    var service = new WindowsStartupTaskAutoStartService(accessor);
    Assert.Equal(expected, await service.GetEnabledAsync(CancellationToken.None));
}

[Theory]
[InlineData(WindowsStartupTaskStatus.DisabledByUser)]
[InlineData(WindowsStartupTaskStatus.DisabledByPolicy)]
public async Task Packaged_windows_does_not_bypass_user_or_policy_disable(WindowsStartupTaskStatus state)
{
    var accessor = new FakeStartupTaskAccessor { State = state };
    var service = new WindowsStartupTaskAutoStartService(accessor);
    await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        await service.EnableAsync(CancellationToken.None));
    Assert.Equal(0, accessor.RequestEnableCalls);
}

[Fact]
public async Task Windows_dispatcher_uses_packaged_service_only_when_package_identity_exists()
{
    var packaged = new FakeAutoStartService();
    var unpackaged = new FakeAutoStartService();
    var service = new WindowsAutoStartService(new FixedPackageIdentity(true), packaged, unpackaged);

    await service.EnableAsync(CancellationToken.None);

    Assert.Equal(1, packaged.EnableCalls);
    Assert.Equal(0, unpackaged.EnableCalls);
}
```

- [ ] **Step 2: Run and verify the tests fail**

Run:

```bash
dotnet test tests/Butchi.Platform.Windows.Tests/Butchi.Platform.Windows.Tests.csproj --filter "WindowsAutoStartServiceTests"
```

Expected: compile failure because the Windows autostart types do not exist.

- [ ] **Step 3: Add the Windows SDK targeting reference**

Add to `Directory.Packages.props`:

```xml
<PackageVersion Include="Microsoft.Windows.SDK.NET.Ref" Version="10.0.26100.87" />
```

Add to `src/Butchi.Platform.Windows/Butchi.Platform.Windows.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Windows.SDK.NET.Ref" />
</ItemGroup>
```

Keep the project target inherited as `net10.0`; do not change the whole app to a Windows-only target framework.

- [ ] **Step 4: Implement unpackaged Run-key registration behind an injectable store**

Define:

```csharp
internal interface IRunKeyStore
{
    string? Read();
    void Write(string command);
    void Delete();
}
```

Production `CurrentUserRunKeyStore` opens `Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")`, uses value name `Butchi`, and does not access `LocalMachine`.

Implement the service with exact command generation:

```csharp
internal static string BuildCommand(string executablePath) => $"\"{executablePath}\"";
```

`GetEnabledAsync` must compare the stored command to `BuildCommand(executablePath)` using `StringComparison.OrdinalIgnoreCase`; a foreign value named `Butchi` is disabled, not accepted.

- [ ] **Step 5: Implement package detection and the `StartupTask` wrapper**

Use a package-identity check based on `GetCurrentPackageFullName`; error code `APPMODEL_ERROR_NO_PACKAGE` means unpackaged and success means packaged. Keep the P/Invoke in `WindowsPackageIdentity.cs`.

Define the local status enum:

```csharp
internal enum WindowsStartupTaskStatus
{
    Disabled,
    DisabledByUser,
    Enabled,
    DisabledByPolicy,
    EnabledByPolicy
}
```

Define the accessor:

```csharp
internal interface IStartupTaskAccessor
{
    ValueTask<WindowsStartupTaskStatus> GetStateAsync(CancellationToken cancellationToken);
    ValueTask<WindowsStartupTaskStatus> RequestEnableAsync(CancellationToken cancellationToken);
    ValueTask DisableAsync(CancellationToken cancellationToken);
}
```

Production `WinRtStartupTaskAccessor` gets `StartupTask.GetAsync("ButchiStartup")`, maps all five `StartupTaskState` values, calls `RequestEnableAsync()` only from `EnableAsync`, and calls `Disable()` for disable.

`WindowsStartupTaskAutoStartService.EnableAsync` must:

```csharp
var current = await accessor.GetStateAsync(cancellationToken);
if (current is WindowsStartupTaskStatus.DisabledByUser or WindowsStartupTaskStatus.DisabledByPolicy)
    throw new InvalidOperationException("Windows startup is disabled by the user or policy.");
if (current is WindowsStartupTaskStatus.Enabled or WindowsStartupTaskStatus.EnabledByPolicy)
    return;
var result = await accessor.RequestEnableAsync(cancellationToken);
if (result is not WindowsStartupTaskStatus.Enabled and not WindowsStartupTaskStatus.EnabledByPolicy)
    throw new InvalidOperationException("Windows did not enable the Butchi startup task.");
```

- [ ] **Step 6: Implement the Windows packaged/unpackaged dispatcher**

```csharp
public sealed class WindowsAutoStartService(
    IWindowsPackageIdentity packageIdentity,
    IAutoStartService packaged,
    IAutoStartService unpackaged) : IAutoStartService
{
    private IAutoStartService Current => packageIdentity.IsPackaged ? packaged : unpackaged;
    public ValueTask<bool> GetEnabledAsync(CancellationToken cancellationToken) => Current.GetEnabledAsync(cancellationToken);
    public ValueTask EnableAsync(CancellationToken cancellationToken) => Current.EnableAsync(cancellationToken);
    public ValueTask DisableAsync(CancellationToken cancellationToken) => Current.DisableAsync(cancellationToken);

    public static WindowsAutoStartService CreateDefault(string executablePath) => new(
        new WindowsPackageIdentity(),
        new WindowsStartupTaskAutoStartService(new WinRtStartupTaskAccessor("ButchiStartup")),
        new WindowsRunKeyAutoStartService(new CurrentUserRunKeyStore("Butchi"), executablePath));
}
```

- [ ] **Step 7: Run Windows platform tests**

Run:

```bash
dotnet test tests/Butchi.Platform.Windows.Tests/Butchi.Platform.Windows.Tests.csproj --filter "WindowsAutoStartServiceTests"
```

Expected: PASS without changing the runner's real startup state.

- [ ] **Step 8: Commit**

```bash
git add Directory.Packages.props src/Butchi.Platform.Windows tests/Butchi.Platform.Windows.Tests/WindowsAutoStartServiceTests.cs
git commit -m "feat: add windows login autostart"
```

---

### Task 4: Add platform selection to the app composition root

**Files:**
- Create: `src/Butchi.App/Startup/AutoStartServiceFactory.cs`
- Modify: `src/Butchi.App/Startup/StartupApplicationServices.cs`
- Modify: `src/Butchi.App/Startup/ButchiRuntimeFactory.cs`
- Create: `tests/Butchi.App.Tests/AutoStartCompositionTests.cs`

**Interfaces:**
- Consumes: `WindowsAutoStartService.CreateDefault`, `MacOsAutoStartService`, `LinuxAutoStartService`.
- Produces: `AutoStartServiceFactory.Create(string executablePath, string userProfile, string? xdgConfigHome = null)` returning `IAutoStartService`.
- Produces: `StartupApplicationServices.AutoStartService`.

- [ ] **Step 1: Write failing factory/composition tests**

Test deterministic path construction through injectable OS selection instead of depending on the CI host OS. Give the factory an internal `AutoStartPlatform` enum overload:

```csharp
internal enum AutoStartPlatform { Windows, MacOs, Linux, Unsupported }
```

Test:

```csharp
[Fact]
public void Factory_returns_file_services_with_expected_user_paths()
{
    var mac = AutoStartServiceFactory.CreateForPlatform(
        AutoStartPlatform.MacOs, "/Applications/Butchi/Butchi", "/Users/quinn", null);
    var linux = AutoStartServiceFactory.CreateForPlatform(
        AutoStartPlatform.Linux, "/opt/butchi/butchi", "/home/quinn", "/tmp/xdg");

    Assert.IsType<MacOsAutoStartService>(mac);
    Assert.IsType<LinuxAutoStartService>(linux);
}
```

Also add a test that `Unsupported` returns a service whose enable/disable operations throw `PlatformNotSupportedException` and whose `GetEnabledAsync` returns `false`.

- [ ] **Step 2: Run and verify the tests fail**

Run:

```bash
dotnet test tests/Butchi.App.Tests/Butchi.App.Tests.csproj --filter "AutoStartCompositionTests"
```

Expected: compile failure because the factory does not exist.

- [ ] **Step 3: Implement platform selection and path resolution**

Production `Create` resolves:

```csharp
var platform = OperatingSystem.IsWindows() ? AutoStartPlatform.Windows
    : OperatingSystem.IsMacOS() ? AutoStartPlatform.MacOs
    : OperatingSystem.IsLinux() ? AutoStartPlatform.Linux
    : AutoStartPlatform.Unsupported;
```

`CreateForPlatform` returns:

```csharp
AutoStartPlatform.Windows => WindowsAutoStartService.CreateDefault(executablePath),
AutoStartPlatform.MacOs => new MacOsAutoStartService(
    Path.Combine(userProfile, "Library", "LaunchAgents"), executablePath),
AutoStartPlatform.Linux => new LinuxAutoStartService(
    string.IsNullOrWhiteSpace(xdgConfigHome) ? Path.Combine(userProfile, ".config") : xdgConfigHome,
    executablePath),
_ => new UnsupportedAutoStartService()
```

`UnsupportedAutoStartService.GetEnabledAsync` returns `false`; enable/disable throw `PlatformNotSupportedException`.

- [ ] **Step 4: Wire `StartupApplicationServices` and management creation**

In `StartupApplicationServices`:

```csharp
var executablePath = Environment.ProcessPath
    ?? throw new InvalidOperationException("Could not determine the Butchi executable path.");
var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
AutoStartService = AutoStartServiceFactory.Create(
    executablePath,
    userProfile,
    Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"));
```

Expose:

```csharp
public IAutoStartService AutoStartService { get; }
```

Change `ButchiRuntimeFactory.CreateManagementAsync` to pass the service:

```csharp
var general = await GeneralSettingsViewModel.CreateAsync(
    services.ConfigStore,
    services.AutoStartService,
    cancellationToken);
```

This call will intentionally fail to compile until Task 5 changes the view-model signature; keep Task 4 and Task 5 in the same implementation checkpoint if using a strict always-green branch policy.

- [ ] **Step 5: Commit the composition slice after Task 5 makes the branch compile**

Stage these files with the Task 5 view-model changes and make the commit after both focused test sets pass. Do not leave a non-compiling intermediate commit.

---

### Task 5: Make General settings reconcile and verify real OS state

**Files:**
- Modify: `src/Butchi.App/Settings/GeneralSettingsViewModel.cs`
- Modify: `tests/Butchi.App.Tests/GeneralSettingsViewModelTests.cs`

**Interfaces:**
- Consumes: `IAutoStartService`.
- Changes: `GeneralSettingsViewModel.CreateAsync(IAppConfigStore, IAutoStartService, CancellationToken)`.
- Produces: `bool LaunchAtLogin` and `ValueTask SetLaunchAtLoginAsync(bool, CancellationToken)`.

- [ ] **Step 1: Extend the fake and write failing reconciliation tests**

Add a fake with operation tracing:

```csharp
private sealed class FakeAutoStartService(bool enabled) : IAutoStartService
{
    public bool Enabled { get; private set; } = enabled;
    public bool FailEnable { get; set; }
    public bool FailDisable { get; set; }
    public bool RefuseStateChange { get; set; }
    public List<string> Calls { get; } = [];

    public ValueTask<bool> GetEnabledAsync(CancellationToken cancellationToken)
    {
        Calls.Add("get");
        return ValueTask.FromResult(Enabled);
    }

    public ValueTask EnableAsync(CancellationToken cancellationToken)
    {
        Calls.Add("enable");
        if (FailEnable) throw new InvalidOperationException("enable failed");
        if (!RefuseStateChange) Enabled = true;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisableAsync(CancellationToken cancellationToken)
    {
        Calls.Add("disable");
        if (FailDisable) throw new InvalidOperationException("disable failed");
        if (!RefuseStateChange) Enabled = false;
        return ValueTask.CompletedTask;
    }
}
```

Add tests for:

```csharp
[Fact]
public async Task Creation_reconciles_persisted_launch_preference_to_actual_platform_state()
{
    var store = new FakeConfigStore(AppConfig.Default with { LaunchAtLogin = true });
    var autoStart = new FakeAutoStartService(false);

    var vm = await GeneralSettingsViewModel.CreateAsync(store, autoStart, CancellationToken.None);

    Assert.False(vm.LaunchAtLogin);
    Assert.False(store.Value.LaunchAtLogin);
    Assert.Equal(1, store.SaveCalls);
}

[Fact]
public async Task Enable_is_verified_before_config_is_persisted()
{
    var store = new FakeConfigStore(AppConfig.Default);
    var autoStart = new FakeAutoStartService(false);
    var vm = await GeneralSettingsViewModel.CreateAsync(store, autoStart, CancellationToken.None);
    autoStart.Calls.Clear();

    await vm.SetLaunchAtLoginAsync(true, CancellationToken.None);

    Assert.Equal(new[] { "enable", "get" }, autoStart.Calls);
    Assert.True(vm.LaunchAtLogin);
    Assert.True(store.Value.LaunchAtLogin);
}

[Fact]
public async Task Verification_mismatch_does_not_persist_enabled_state()
{
    var store = new FakeConfigStore(AppConfig.Default);
    var autoStart = new FakeAutoStartService(false) { RefuseStateChange = true };
    var vm = await GeneralSettingsViewModel.CreateAsync(store, autoStart, CancellationToken.None);

    await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        await vm.SetLaunchAtLoginAsync(true, CancellationToken.None));

    Assert.False(store.Value.LaunchAtLogin);
    Assert.False(vm.LaunchAtLogin);
    Assert.Equal("Couldn't save", vm.SaveStatus);
}
```

Also add disable, platform-exception, `PropertyChanged(nameof(LaunchAtLogin))`, and save-failure rollback tests.

- [ ] **Step 2: Update existing tests to pass a fake service and verify they fail for missing implementation**

Change each existing creation call from:

```csharp
await GeneralSettingsViewModel.CreateAsync(store, CancellationToken.None)
```

to:

```csharp
await GeneralSettingsViewModel.CreateAsync(store, new FakeAutoStartService(store.Value.LaunchAtLogin), CancellationToken.None)
```

Run:

```bash
dotnet test tests/Butchi.App.Tests/Butchi.App.Tests.csproj --filter "GeneralSettingsViewModelTests"
```

Expected: compile failure until the new signature/property/method exist.

- [ ] **Step 3: Implement creation-time reconciliation**

Add `_autoStartService`, constructor injection, and:

```csharp
public bool LaunchAtLogin => _config.LaunchAtLogin;

public static async ValueTask<GeneralSettingsViewModel> CreateAsync(
    IAppConfigStore store,
    IAutoStartService autoStartService,
    CancellationToken cancellationToken)
{
    ArgumentNullException.ThrowIfNull(store);
    ArgumentNullException.ThrowIfNull(autoStartService);

    var config = await store.LoadAsync(cancellationToken);
    var actual = await autoStartService.GetEnabledAsync(cancellationToken);
    if (config.LaunchAtLogin != actual)
    {
        config = config with { LaunchAtLogin = actual };
        await store.SaveAsync(config, cancellationToken);
    }

    return new GeneralSettingsViewModel(store, autoStartService, config);
}
```

- [ ] **Step 4: Implement verified change plus compensation**

The setter must not persist until OS verification succeeds:

```csharp
public async ValueTask SetLaunchAtLoginAsync(bool value, CancellationToken cancellationToken)
{
    if (value == _config.LaunchAtLogin) return;

    var previous = _config.LaunchAtLogin;
    SetSaveStatus("Saving…");
    try
    {
        if (value)
            await _autoStartService.EnableAsync(cancellationToken);
        else
            await _autoStartService.DisableAsync(cancellationToken);

        var actual = await _autoStartService.GetEnabledAsync(cancellationToken);
        if (actual != value)
            throw new InvalidOperationException("Login startup registration did not match the requested state.");

        var candidate = _config with { LaunchAtLogin = value };
        await _store.SaveAsync(candidate, cancellationToken);
        _config = candidate;
        OnPropertyChanged(nameof(LaunchAtLogin));
        SetSaveStatus("Saved");
    }
    catch
    {
        await TryRestoreAutoStartAsync(previous);
        OnPropertyChanged(nameof(LaunchAtLogin));
        SetSaveStatus("Couldn't save");
        throw;
    }
}

private async ValueTask TryRestoreAutoStartAsync(bool previous)
{
    try
    {
        if (previous)
            await _autoStartService.EnableAsync(CancellationToken.None);
        else
            await _autoStartService.DisableAsync(CancellationToken.None);
    }
    catch
    {
        // Preserve the original failure; reopening settings will reconcile actual platform state.
    }
}
```

Add `OnPropertyChanged(nameof(LaunchAtLogin))` to `RaiseAllGeneralProperties()` so normal config updates keep the UI consistent.

- [ ] **Step 5: Run view-model and composition tests**

Run:

```bash
dotnet test tests/Butchi.App.Tests/Butchi.App.Tests.csproj --filter "GeneralSettingsViewModelTests|AutoStartCompositionTests"
```

Expected: PASS.

- [ ] **Step 6: Commit Tasks 4 and 5 together**

```bash
git add src/Butchi.App/Startup/AutoStartServiceFactory.cs src/Butchi.App/Startup/StartupApplicationServices.cs src/Butchi.App/Startup/ButchiRuntimeFactory.cs src/Butchi.App/Settings/GeneralSettingsViewModel.cs tests/Butchi.App.Tests/AutoStartCompositionTests.cs tests/Butchi.App.Tests/GeneralSettingsViewModelTests.cs
git commit -m "feat: wire verified launch-at-login setting"
```

---

### Task 6: Add the General settings toggle and rollback synchronization

**Files:**
- Modify: `src/Butchi.App/Settings/GeneralSettingsView.cs`
- Modify: `tests/Butchi.App.Tests/Task14GeneralUiContractTests.cs`

**Interfaces:**
- Consumes: `GeneralSettingsViewModel.LaunchAtLogin` and `SetLaunchAtLoginAsync`.
- Produces: one Avalonia `ToggleSwitch` in the Appearance card.

- [ ] **Step 1: Write the failing UI contract assertions**

Add to `Task14GeneralUiContractTests`:

```csharp
Assert.Contains("SetLaunchAtLoginAsync", generalVm, StringComparison.Ordinal);

var generalView = File.ReadAllText(generalViewPath);
Assert.Contains("Launch Butchi at login", generalView, StringComparison.Ordinal);
Assert.Contains("Start Butchi automatically when you sign in.", generalView, StringComparison.Ordinal);
Assert.Contains("nameof(GeneralSettingsViewModel.LaunchAtLogin)", generalView, StringComparison.Ordinal);
```

- [ ] **Step 2: Run and verify the test fails**

Run:

```bash
dotnet test tests/Butchi.App.Tests/Butchi.App.Tests.csproj --filter "Task14GeneralUiContractTests"
```

Expected: FAIL because the view does not contain the launch-at-login surface.

- [ ] **Step 3: Add the toggle to `BuildAppearanceCard`**

Add a field:

```csharp
private readonly ToggleSwitch _launchAtLogin;
```

Initialize it before `BuildContent()`:

```csharp
_launchAtLogin = new ToggleSwitch
{
    IsChecked = _viewModel.LaunchAtLogin,
    OnContent = "On",
    OffContent = "Off"
};
_launchAtLogin.PropertyChanged += async (_, args) =>
{
    if (_ready && args.Property == ToggleSwitch.IsCheckedProperty)
        await RunAsync(() => _viewModel.SetLaunchAtLoginAsync(
            _launchAtLogin.IsChecked == true,
            CancellationToken.None));
};
```

Add the row in the Appearance card after Theme:

```csharp
body.Children.Add(RowField(
    "Launch Butchi at login",
    "Start Butchi automatically when you sign in.",
    _launchAtLogin));
```

- [ ] **Step 4: Synchronize rollback/reconciliation notifications without causing a second write**

Extend the existing `PropertyChanged` subscription:

```csharp
if (args.PropertyName == nameof(GeneralSettingsViewModel.LaunchAtLogin))
{
    _ready = false;
    _launchAtLogin.IsChecked = _viewModel.LaunchAtLogin;
    _ready = true;
}
```

Keep the existing SaveStatus handling in the same subscription.

- [ ] **Step 5: Run General UI/view-model regression tests**

Run:

```bash
dotnet test tests/Butchi.App.Tests/Butchi.App.Tests.csproj --filter "Task14GeneralUiContractTests|GeneralSettingsViewModelTests|GeneralSettingsPopoverTimeoutRegressionTests"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Butchi.App/Settings/GeneralSettingsView.cs tests/Butchi.App.Tests/Task14GeneralUiContractTests.cs
git commit -m "feat: add launch-at-login settings toggle"
```

---

### Task 7: Add the MSIX startup-task declaration and release validation

**Files:**
- Modify: `store/Package.appxmanifest.template`
- Modify: `scripts/Release/Validate-StorePackage.ps1`
- Modify: `tests/Butchi.App.Tests/StorePackageContractTests.cs`

**Interfaces:**
- Consumes: packaged Windows runtime task id `ButchiStartup` from Task 3.
- Produces: a disabled-by-default `uap5` startup task in every Store MSIX.

- [ ] **Step 1: Write failing manifest/release contract tests**

Add to `StorePackageContractTests`:

```csharp
[Fact]
public void Store_manifest_declares_disabled_butchi_startup_task()
{
    var repoRoot = FindRepositoryRoot();
    var manifest = File.ReadAllText(Path.Combine(repoRoot, "store", "Package.appxmanifest.template"));

    Assert.Contains("xmlns:uap5=\"http://schemas.microsoft.com/appx/manifest/uap/windows10/5\"", manifest, StringComparison.Ordinal);
    Assert.Contains("windows.startupTask", manifest, StringComparison.Ordinal);
    Assert.Contains("TaskId=\"ButchiStartup\"", manifest, StringComparison.Ordinal);
    Assert.Contains("Executable=\"butchi.exe\"", manifest, StringComparison.Ordinal);
    Assert.Contains("EntryPoint=\"Windows.FullTrustApplication\"", manifest, StringComparison.Ordinal);
    Assert.Contains("Enabled=\"false\"", manifest, StringComparison.Ordinal);
}
```

Extend the validator contract test:

```csharp
Assert.Contains("ButchiStartup", validator, StringComparison.Ordinal);
Assert.Contains("windows.startupTask", validator, StringComparison.Ordinal);
```

- [ ] **Step 2: Run and verify tests fail**

Run:

```bash
dotnet test tests/Butchi.App.Tests/Butchi.App.Tests.csproj --filter "StorePackageContractTests"
```

Expected: FAIL because the manifest/validator do not declare or enforce the startup task.

- [ ] **Step 3: Add the exact `uap5` packaged-desktop startup extension**

Change the manifest root to include:

```xml
xmlns:uap5="http://schemas.microsoft.com/appx/manifest/uap/windows10/5"
IgnorableNamespaces="uap uap5 rescap"
```

Inside the existing `<Application>` after `<uap:VisualElements ... />`, add:

```xml
<Extensions>
  <uap5:Extension Category="windows.startupTask" Executable="butchi.exe" EntryPoint="Windows.FullTrustApplication">
    <uap5:StartupTask TaskId="ButchiStartup" Enabled="false" DisplayName="Butchi" />
  </uap5:Extension>
</Extensions>
```

- [ ] **Step 4: Extend Store validation to parse and enforce the task**

Add the namespace:

```powershell
$ns.AddNamespace('uap5', 'http://schemas.microsoft.com/appx/manifest/uap/windows10/5')
```

Validate:

```powershell
$startupExtension = $manifest.SelectSingleNode(
    '/f:Package/f:Applications/f:Application/f:Extensions/uap5:Extension[@Category="windows.startupTask"]',
    $ns)
if (-not $startupExtension) { throw 'Butchi startupTask extension is missing.' }
if ($startupExtension.Executable -ne 'butchi.exe') { throw 'Butchi startup task must launch butchi.exe.' }
if ($startupExtension.EntryPoint -ne 'Windows.FullTrustApplication') { throw 'Butchi startup task must use Windows.FullTrustApplication.' }
$startupTask = $startupExtension.SelectSingleNode('uap5:StartupTask', $ns)
if (-not $startupTask -or $startupTask.TaskId -ne 'ButchiStartup') { throw 'ButchiStartup task is missing.' }
if ([string]$startupTask.Enabled -ne 'false') { throw 'ButchiStartup must be disabled by default.' }
```

Repeat the same logical checks against the extracted packaged manifest when `$PackagePath` is provided, so packaging cannot accidentally drop the extension.

- [ ] **Step 5: Run package contract tests**

Run:

```bash
dotnet test tests/Butchi.App.Tests/Butchi.App.Tests.csproj --filter "StorePackageContractTests"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add store/Package.appxmanifest.template scripts/Release/Validate-StorePackage.ps1 tests/Butchi.App.Tests/StorePackageContractTests.cs
git commit -m "feat: declare packaged windows startup task"
```

---

### Task 8: Full verification, release build, and PR readiness

**Files:**
- Verify all files changed by Tasks 1-7.
- No new production behavior in this task.

**Interfaces:**
- Consumes the complete feature.
- Produces a branch ready for PR review with no failing existing tests.

- [ ] **Step 1: Run the entire solution test suite**

```bash
dotnet test Butchi.slnx -c Release
```

Expected: PASS with no warnings promoted to errors.

- [ ] **Step 2: Build the application normally**

```bash
dotnet build src/Butchi.App/Butchi.App.csproj -c Release
```

Expected: PASS.

- [ ] **Step 3: On Windows CI/local validation, publish both Store RIDs**

```powershell
dotnet publish src/Butchi.App/Butchi.App.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
dotnet publish src/Butchi.App/Butchi.App.csproj -c Release -r win-arm64 --self-contained true -o artifacts/publish/win-arm64
```

Expected: both publishes contain `Butchi.App.exe`; the release workflow will rename it to `butchi.exe` before MSIX staging.

- [ ] **Step 4: Review branch diff for scope**

```bash
git diff main...HEAD --stat
git diff main...HEAD -- src/Butchi.Core src/Butchi.Infrastructure src/Butchi.Platform.Windows src/Butchi.App/Settings src/Butchi.App/Startup store scripts/Release tests
```

Confirm there is no unrelated refactor of trigger/selection/paste code and no machine-wide startup path.

- [ ] **Step 5: Ensure working tree is clean**

```bash
git status --short
```

Expected: no output.

- [ ] **Step 6: Open the PR**

Use title:

```text
feat: add cross-platform launch-at-login setting
```

Use a body that states:

```text
## Summary
- add a verified Launch Butchi at login preference
- support unpackaged Windows Run-key, packaged Windows StartupTask, macOS LaunchAgents, and Linux XDG autostart
- keep OS registration as the source of truth and avoid persisting false-success state
- validate ButchiStartup in Store packaging

## Testing
- dotnet test Butchi.slnx -c Release
- Windows x64/ARM64 publish and Store package validation
```
