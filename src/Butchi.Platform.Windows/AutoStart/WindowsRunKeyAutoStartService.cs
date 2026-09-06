using System.Runtime.Versioning;
using Butchi.Core.Platform;
using Microsoft.Win32;

namespace Butchi.Platform.Windows.AutoStart;

internal interface IRunKeyStore
{
    string? Read();
    void Write(string command);
    void Delete();
}

[SupportedOSPlatform("windows")]
internal sealed class CurrentUserRunKeyStore(string valueName) : IRunKeyStore
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(valueName) as string;
    }

    public void Write(string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Could not open the current-user startup registry key.");
        key.SetValue(valueName, command, RegistryValueKind.String);
    }

    public void Delete()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}

internal sealed class WindowsRunKeyAutoStartService(
    IRunKeyStore store,
    string executablePath) : IAutoStartService
{
    private string Command => $"\"{executablePath}\"";

    public ValueTask<bool> GetEnabledAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(string.Equals(store.Read(), Command, StringComparison.OrdinalIgnoreCase));
    }

    public ValueTask EnableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        store.Write(Command);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        store.Delete();
        return ValueTask.CompletedTask;
    }
}
