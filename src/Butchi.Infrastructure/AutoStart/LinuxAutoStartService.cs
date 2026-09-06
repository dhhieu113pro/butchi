using System.Text;
using Butchi.Core.Platform;

namespace Butchi.Infrastructure.AutoStart;

public sealed class LinuxAutoStartService(
    string configDirectory,
    string executablePath) : IAutoStartService
{
    private string RegistrationPath => Path.Combine(configDirectory, "autostart", "butchi.desktop");

    public async ValueTask<bool> GetEnabledAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(RegistrationPath))
        {
            return false;
        }

        var text = await File.ReadAllTextAsync(RegistrationPath, cancellationToken);
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Contains("[Desktop Entry]", StringComparer.Ordinal)
            && lines.Contains("Type=Application", StringComparer.Ordinal)
            && lines.Contains($"Exec={QuoteExec(executablePath)}", StringComparer.Ordinal)
            && lines.Contains("X-GNOME-Autostart-enabled=true", StringComparer.OrdinalIgnoreCase)
            && !lines.Contains("Hidden=true", StringComparer.OrdinalIgnoreCase);
    }

    public ValueTask EnableAsync(CancellationToken cancellationToken)
    {
        var contents =
            "[Desktop Entry]\n" +
            "Type=Application\n" +
            "Name=Butchi\n" +
            $"Exec={QuoteExec(executablePath)}\n" +
            "X-GNOME-Autostart-enabled=true\n";

        return AtomicTextFile.WriteAsync(RegistrationPath, contents, cancellationToken);
    }

    public ValueTask DisableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(RegistrationPath))
        {
            File.Delete(RegistrationPath);
        }

        return ValueTask.CompletedTask;
    }

    private static string QuoteExec(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '`':
                    builder.Append("\\`");
                    break;
                case '$':
                    builder.Append("\\$");
                    break;
                case '%':
                    builder.Append("%%");
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }
}
