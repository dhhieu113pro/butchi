# Cross-platform launch-at-login design

Date: 2026-09-06

## Goal

Add a single General setting, **Launch Butchi at login**, that works consistently on Windows, macOS, and Linux while keeping OS-specific startup registration details outside the UI and configuration layers.

The setting is off by default. Users can enable or disable it at any time. The UI reflects the actual operating-system registration state rather than only trusting the persisted preference.

## Scope

This change adds cross-platform autostart infrastructure and wires it into the existing General settings flow. It does not make Butchi's existing interaction runtime cross-platform; the current trigger, selection, pointer, and paste implementations remain Windows-specific. The autostart abstraction is intentionally independent so future macOS/Linux runtimes can reuse it without changing the settings contract.

## User experience

General settings gains a new row near the other application-level preferences:

- Label: `Launch Butchi at login`
- Description: `Start Butchi automatically when you sign in.`
- Control: toggle switch
- Default: Off

When the user changes the toggle, Butchi immediately asks the platform autostart service to enable or disable startup registration. The persisted `AppConfig.LaunchAtLogin` value is updated only after the platform operation succeeds.

When General settings is opened, the view model queries the real platform registration state and uses that as the displayed toggle value. If the persisted preference disagrees with the OS state because the user or another tool removed the startup entry, Butchi reconciles the stored preference to the actual state.

If enabling or disabling autostart fails, the toggle returns to its prior state and the existing save/status area reports failure. No partially-successful preference is persisted.

## Architecture

### Core contract

Add an `IAutoStartService` abstraction in a platform-neutral project used by the application layer. It exposes asynchronous operations:

- `GetEnabledAsync(CancellationToken)`
- `EnableAsync(CancellationToken)`
- `DisableAsync(CancellationToken)`

The contract represents user-level login startup only. It does not request administrator privileges or machine-wide startup.

### Platform selection

Add an `AutoStartServiceFactory` or equivalent composition helper that selects the implementation by `OperatingSystem.IsWindows()`, `OperatingSystem.IsMacOS()`, or `OperatingSystem.IsLinux()`.

Unsupported operating systems receive a no-op/unsupported implementation whose operations fail predictably. The UI should not claim success when the platform cannot support the feature.

### Windows implementation

Provide a Windows autostart implementation in the Windows platform project.

For unpackaged builds, use the current-user Run key (`HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run`) with a stable value name such as `Butchi`. The command points to the current executable and is quoted safely.

For packaged/MSIX builds, prefer the package-compatible startup path when the package exposes a startup task. If the current package does not yet define such a startup task, fall back only when the registration mechanism is valid for the installed executable. The implementation must not require elevation.

The service determines enabled state by checking the real registration entry and validating that it targets the current Butchi executable rather than merely checking for any value with the same name.

### macOS implementation

Use a per-user LaunchAgent plist at:

`~/Library/LaunchAgents/io.github.dhhieu113pro.butchi.plist`

The plist launches the current Butchi executable at login and does not keep restarting the app after normal exit. Writes are atomic: create parent directory if needed, write a temporary file, then replace/move into place.

`GetEnabledAsync` verifies that the plist exists and points to the current executable. Disable removes only Butchi's own plist.

### Linux implementation

Use the XDG autostart convention. Resolve the config root from `XDG_CONFIG_HOME`, falling back to `~/.config`, and manage:

`<config-root>/autostart/butchi.desktop`

The desktop entry uses the current executable path with correct escaping, `Type=Application`, `Name=Butchi`, and an enabled autostart entry. Writes are atomic and create the autostart directory when needed.

`GetEnabledAsync` verifies that Butchi's desktop file exists and points to the current executable. Disable removes only that file.

## Configuration and view model

Add `bool LaunchAtLogin { get; init; } = false;` to `AppConfig`.

`GeneralSettingsViewModel` receives `IAutoStartService` in addition to `IAppConfigStore`. Creation performs these steps:

1. Load `AppConfig`.
2. Query `IAutoStartService.GetEnabledAsync`.
3. Use the platform state as the source of truth for `LaunchAtLogin`.
4. If the stored value differs, persist a reconciled config value.

Add `SetLaunchAtLoginAsync(bool, CancellationToken)`:

1. Return when the requested value already equals the current actual state.
2. Set save status to `Saving…`.
3. Call the platform service to enable or disable.
4. Re-query platform state to verify the operation.
5. If verification does not match the requested state, treat the operation as failure.
6. Persist the new `AppConfig.LaunchAtLogin` value.
7. Raise property changes and set status to `Saved`.
8. On failure, restore the prior state, set status to `Couldn't save`, and rethrow so the view can keep its existing error-handling behavior.

This ordering prevents configuration from claiming startup is enabled when OS registration failed.

## UI wiring

`GeneralSettingsView` adds the new toggle in an application-level card, preferably alongside Appearance because it controls app startup rather than Translate/Rewrite behavior.

The toggle uses the same asynchronous `RunAsync` pattern as the other General controls. Property-change notifications keep the control synchronized if the view model restores state after an error.

## Composition

`StartupApplicationServices` creates the platform-neutral autostart service through the factory and exposes it to `ButchiRuntimeFactory`.

`ButchiRuntimeFactory.CreateManagementAsync` passes the service to `GeneralSettingsViewModel.CreateAsync`.

No autostart operation runs merely because the application starts. Startup registration changes only when the user changes the setting or when General settings reconciles stale persisted state.

## Security and reliability

- User-level registration only; never request administrator privileges.
- Quote/escape executable paths for every platform format.
- Never execute shell commands to create startup entries when a direct file/registry API is available.
- Disable operations delete only Butchi-owned entries with stable identifiers.
- File-based registrations use atomic writes to avoid truncated plist/desktop files.
- Reading malformed or foreign registration data returns disabled rather than throwing wherever practical.
- Cancellation is honored for file I/O and view-model operations.

## Testing

Use TDD for implementation.

### Core/config tests

- `AppConfig.Default.LaunchAtLogin` is false.
- Existing JSON configs missing the new property deserialize as false.
- New values round-trip through the config store.

### View-model tests

Use a fake `IAutoStartService` to verify:

- platform state wins when stored config disagrees;
- reconciliation persists the real state;
- enable calls the platform service before persisting config;
- disable calls the platform service before persisting config;
- failed platform operations do not persist a false success;
- verification mismatch is treated as failure;
- property notifications and save status are correct.

### Platform implementation tests

Avoid modifying the CI runner's real login startup state.

- Windows: separate registry access behind a tiny injectable registry abstraction or test pure entry-generation/parsing logic with an in-memory fake.
- macOS: inject the LaunchAgents root and executable path, then test generated plist content and enable/disable behavior in a temporary directory.
- Linux: inject the XDG config root and executable path, then test generated desktop-entry content and enable/disable behavior in a temporary directory.

### CI expectations

All existing tests must continue to pass. Platform-specific tests should run only where their implementation is supported, while pure generation/parsing tests should remain runnable cross-platform where possible.

## Non-goals

- Starting Butchi as a background service or daemon.
- Machine-wide startup registration.
- Administrator/elevated startup.
- Delayed-start scheduling.
- Adding a second startup-related UI setting.
- Refactoring the current Windows-only hotkey/selection runtime as part of this feature.

## Acceptance criteria

1. General settings exposes `Launch Butchi at login`, default Off.
2. Enabling it creates a valid user-level login startup registration on Windows, macOS, and Linux.
3. Disabling it removes only Butchi's own registration.
4. The displayed toggle reflects actual OS state when settings opens.
5. Persisted config is never left enabled after a failed registration operation.
6. No implementation requires administrator privileges.
7. Tests cover the shared behavior and platform registration logic without mutating CI runners' real startup configuration.
