using System.Security;
using System.Xml.Linq;
using Butchi.Core.Platform;

namespace Butchi.Infrastructure.AutoStart;

public sealed class MacOsAutoStartService(
    string launchAgentsDirectory,
    string executablePath) : IAutoStartService
{
    private const string FileName = "io.github.dhhieu113pro.butchi.plist";
    private const string Label = "io.github.dhhieu113pro.butchi";

    private string RegistrationPath => Path.Combine(launchAgentsDirectory, FileName);

    public async ValueTask<bool> GetEnabledAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(RegistrationPath))
        {
            return false;
        }

        try
        {
            var xml = await File.ReadAllTextAsync(RegistrationPath, cancellationToken);
            var document = XDocument.Parse(xml);
            var dict = document.Root?.Element("dict");
            if (dict is null)
            {
                return false;
            }

            var elements = dict.Elements().ToArray();
            for (var i = 0; i < elements.Length - 1; i++)
            {
                if (elements[i].Name.LocalName != "key" ||
                    !string.Equals(elements[i].Value, "ProgramArguments", StringComparison.Ordinal))
                {
                    continue;
                }

                var array = elements[i + 1];
                var program = array.Name.LocalName == "array"
                    ? array.Elements("string").FirstOrDefault()?.Value
                    : null;
                return string.Equals(program, executablePath, StringComparison.Ordinal);
            }

            return false;
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }

    public ValueTask EnableAsync(CancellationToken cancellationToken)
    {
        var escapedExecutable = SecurityElement.Escape(executablePath) ?? executablePath;
        var contents =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
            "<plist version=\"1.0\">\n" +
            "<dict>\n" +
            $"  <key>Label</key><string>{Label}</string>\n" +
            $"  <key>ProgramArguments</key><array><string>{escapedExecutable}</string></array>\n" +
            "  <key>RunAtLoad</key><true/>\n" +
            "</dict>\n" +
            "</plist>\n";

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
}
