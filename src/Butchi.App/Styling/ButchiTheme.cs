using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Butchi.Core.Configuration;

namespace Butchi.App.Styling;

public static class ButchiTheme
{
    public static readonly Color Cobalt = Color.Parse("#365CF5");
    public static readonly Color CobaltDark = Color.Parse("#2445D8");
    public static readonly Color CobaltSoft = Color.Parse("#E9EEFF");
    public static readonly Color Success = Color.Parse("#159455");
    public static readonly Color Warning = Color.Parse("#B66A00");
    public static readonly Color Error = Color.Parse("#C73A3A");

    public static readonly IBrush CobaltBrush = new SolidColorBrush(Cobalt);
    public static readonly IBrush CobaltDarkBrush = new SolidColorBrush(CobaltDark);
    public static readonly IBrush CobaltSoftBrush = new SolidColorBrush(CobaltSoft);
    public static readonly IBrush WhiteBrush = Brushes.White;
    public static readonly IBrush DividerBrush = new SolidColorBrush(Color.FromArgb(40, 120, 130, 155));
    public static readonly IBrush SubtleSurfaceBrush = new SolidColorBrush(Color.FromArgb(18, 54, 92, 245));

    public static void Initialize(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (!application.Styles.OfType<FluentTheme>().Any())
            application.Styles.Add(new FluentTheme());
    }

    public static void Apply(Application application, AppThemePreference preference)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.RequestedThemeVariant = preference switch
        {
            AppThemePreference.Light => ThemeVariant.Light,
            AppThemePreference.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }
}
