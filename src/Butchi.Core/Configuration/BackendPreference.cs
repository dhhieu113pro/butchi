namespace Butchi.Core.Configuration;

public enum BackendPreference
{
    Auto,
    Gpu,
    Cpu
}

public static class BackendPreferenceParser
{
    public static BackendPreference ParseOrAuto(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "cpu" => BackendPreference.Cpu,
            "gpu" => BackendPreference.Gpu,
            _ => BackendPreference.Auto
        };
}
