using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Butchi.Core.Configuration;

namespace Butchi.App.Styling;

public static class ButchiTheme
{
    public static readonly Color BrandCyan = Color.Parse("#24C8DB");
    public static readonly Color BrandYellow = Color.Parse("#FFC131");
    public static readonly Color Cobalt = Color.Parse("#365CF5");
    public static readonly Color CobaltDark = Color.Parse("#2445D8");
    public static readonly Color CobaltSoft = Color.Parse("#E9EEFF");
    public static readonly Color Success = Color.Parse("#159455");
    public static readonly Color Warning = Color.Parse("#B66A00");
    public static readonly Color Error = Color.Parse("#C73A3A");

    public static readonly IBrush BrandCyanBrush = new SolidColorBrush(BrandCyan);
    public static readonly IBrush BrandYellowBrush = new SolidColorBrush(BrandYellow);
    public static readonly IBrush CobaltBrush = new SolidColorBrush(Cobalt);
    public static readonly IBrush CobaltDarkBrush = new SolidColorBrush(CobaltDark);
    public static readonly IBrush CobaltSoftBrush = new SolidColorBrush(CobaltSoft);
    public static readonly IBrush WhiteBrush = Brushes.White;
    public static readonly IBrush DividerBrush = new SolidColorBrush(Color.FromArgb(40, 120, 130, 155));
    public static readonly IBrush SubtleSurfaceBrush = new SolidColorBrush(Color.FromArgb(18, 120, 130, 155));

    public static IBrush NavigationSurfaceBrush(ThemeVariant theme) =>
        Brush(theme, "#090D18", "#FFFFFF");

    public static IBrush NavigationForegroundBrush(ThemeVariant theme) =>
        Brush(theme, "#F5F7FF", "#172033");

    public static IBrush SelectedNavigationSurfaceBrush(ThemeVariant theme) =>
        Brush(theme, "#132A36", "#E8F9FB");

    public static IBrush SelectedNavigationForegroundBrush(ThemeVariant theme) =>
        Brush(theme, "#A9F2F7", "#087A88");

    public static IBrush SelectedNavigationIndicatorBrush(ThemeVariant theme) => BrandCyanBrush;

    public static IBrush CardSurfaceBrush(ThemeVariant theme) =>
        Brush(theme, "#141A2B", "#F4F7FC");

    public static IBrush LocalStatusSurfaceBrush(ThemeVariant theme) =>
        Brush(theme, "#0D2D31", "#E7FAFC");

    public static IBrush LocalStatusForegroundBrush(ThemeVariant theme) =>
        Brush(theme, "#6BE7F3", "#087A88");

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

    private static IBrush Brush(ThemeVariant theme, string dark, string light) =>
        new SolidColorBrush(Color.Parse(theme == ThemeVariant.Dark ? dark : light));
}
