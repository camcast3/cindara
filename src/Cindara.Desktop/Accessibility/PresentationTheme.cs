using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;

namespace Cindara.Desktop.Accessibility;

public static class PresentationTheme
{
    private static readonly Uri TokensUri = new("avares://Cindara.Desktop/Styles/Tokens.axaml");
    private static readonly string[] ColorNames =
    [
        "Background", "Surface", "SurfaceRaised", "Accent", "AccentStrong", "OnAccent",
        "Text", "TextSecondary", "TextMuted", "Border", "Error", "Warning", "Success",
    ];

    private static readonly Dictionary<string, string> HighContrastColors = new(StringComparer.Ordinal)
    {
        ["Background"] = "#000000",
        ["Surface"] = "#000000",
        ["SurfaceRaised"] = "#000000",
        ["Accent"] = "#FFFF00",
        ["AccentStrong"] = "#FFFF00",
        ["OnAccent"] = "#000000",
        ["Text"] = "#FFFFFF",
        ["TextSecondary"] = "#FFFFFF",
        ["TextMuted"] = "#FFFFFF",
        ["Border"] = "#FFFFFF",
        ["Error"] = "#FFB3BE",
        ["Warning"] = "#FFFF00",
        ["Success"] = "#8CFFC2",
    };

    public static void Apply(Window window, PresentationPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(preferences);
        window.VerifyAccess();
        var defaults = new ResourceInclude(TokensUri) { Source = TokensUri }.Loaded;
        var brushes = new Dictionary<string, SolidColorBrush>(StringComparer.Ordinal);
        foreach (var name in ColorNames)
        {
            var color = preferences.HighContrast
                ? Color.Parse(HighContrastColors[name])
                : (Color)defaults[$"Cindara.Color.{name}"]!;
            window.Resources[$"Cindara.Color.{name}"] = color;
            // App-level brushes resolve their own resources, not a descendant window's palette.
            var brush = new SolidColorBrush(color);
            brushes[name] = brush;
            window.Resources[$"Cindara.Brush.{name}"] = brush;
        }

        foreach (var role in new[] { "Display", "Title", "Heading", "Body", "Caption" })
        {
            window.Resources[$"Cindara.Type.{role}"] =
                (double)defaults[$"Cindara.Type.{role}"]! * preferences.TextScale;
        }

        foreach (var duration in new[] { "Fast", "Standard", "Deliberate" })
        {
            window.Resources[$"Cindara.Motion.{duration}"] = preferences.ReducedMotion
                ? TimeSpan.Zero
                : defaults[$"Cindara.Motion.{duration}"];
        }

        window.Resources["Cindara.TextScale"] = preferences.TextScale;
        window.Resources["Cindara.Elevation.PrimaryFocus"] = new BoxShadows(new BoxShadow
        {
            Spread = 2,
            Color = brushes["Text"].Color,
        });

        ApplyFluentResources(window, brushes, preferences.TextScale);
        window.Classes.Set("high-contrast", preferences.HighContrast);
        window.Classes.Set("reduced-motion", preferences.ReducedMotion);
        window.Classes.Set("large-text", preferences.TextScale > 1);
    }

    private static void ApplyFluentResources(Window window, Dictionary<string, SolidColorBrush> brushes, double textScale)
    {
        void Set(string key, string color) => window.Resources[key] = brushes[color];

        foreach (var state in new[] { "", "PointerOver", "Pressed", "Disabled" })
        {
            Set($"ButtonBackground{state}", state == "PointerOver" ? "SurfaceRaised" : "Surface");
            Set($"ButtonForeground{state}", "Text");
            Set($"ButtonBorderBrush{state}", "Border");
            Set($"AccentButtonBackground{state}", "Accent");
            Set($"AccentButtonForeground{state}", "OnAccent");
            Set($"AccentButtonBorderBrush{state}", "OnAccent");
            Set($"TextControlButtonBackground{state}", "Surface");
            Set($"TextControlButtonForeground{state}", "Text");
            Set($"TextControlButtonBorderBrush{state}", "Border");
        }

        foreach (var state in new[] { "", "PointerOver", "Focused", "Disabled" })
        {
            Set($"TextControlBackground{state}", "Surface");
            Set($"TextControlForeground{state}", "Text");
            Set($"TextControlBorderBrush{state}", state == "Focused" ? "Accent" : "Border");
            Set($"TextControlPlaceholderForeground{state}", "TextSecondary");
        }

        Set("TextControlSelectionHighlightColor", "Accent");
        Set("ToolTipBackground", "SurfaceRaised");
        Set("ToolTipForeground", "Text");
        Set("ToolTipBorderBrush", "Border");
        Set("MenuFlyoutPresenterBackground", "SurfaceRaised");
        Set("MenuFlyoutPresenterBorderBrush", "Border");
        Set("SystemControlForegroundBaseHighBrush", "Text");
        Set("SystemControlBackgroundBaseLowBrush", "SurfaceRaised");
        Set("SystemControlBackgroundChromeMediumLowBrush", "Surface");
        Set("SystemControlHighlightAccentBrush", "Accent");
        foreach (var state in new[] { "", "PointerOver", "Pressed", "Disabled" })
        {
            Set($"MenuFlyoutItemForeground{state}", "Text");
            Set($"MenuFlyoutItemBackground{state}", "SurfaceRaised");
            Set($"MenuFlyoutItemKeyboardAcceleratorTextForeground{state}", "TextSecondary");
            Set($"MenuFlyoutSubItemChevron{state}", "Text");
        }

        window.Resources["TextControlPlaceholderOpacity"] = 1d;
        window.Resources["ControlContentThemeFontSize"] = 18 * textScale;
        window.Resources["ToolTipContentThemeFontSize"] = 14 * textScale;
    }
}
