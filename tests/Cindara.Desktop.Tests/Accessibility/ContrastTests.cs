using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cindara.Desktop.Accessibility;
using Cindara.Desktop.Tests.Navigation;

namespace Cindara.Desktop.Tests.Accessibility;

public sealed class ContrastTests
{
    private static readonly string[] TextRoles =
        ["Text", "TextSecondary", "TextMuted", "Accent", "AccentStrong", "Error", "Warning", "Success"];
    private static readonly string[] SurfaceRoles = ["Background", "Surface", "SurfaceRaised"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task AllTextRolesMeetNormalTextContrastOnEverySurface(bool highContrast) => TestAppBuilder.Run(() =>
    {
        var window = new Window();
        PresentationTheme.Apply(window, new PresentationPreferences(HighContrast: highContrast));
        foreach (var text in TextRoles)
        {
            foreach (var surface in SurfaceRoles)
            {
                RequireContrast(ColorToken(window, text), ColorToken(window, surface), 4.5);
            }
        }

        RequireContrast(ColorToken(window, "OnAccent"), ColorToken(window, "Accent"), 4.5);
        RequireContrast(ColorToken(window, "OnAccent"), ColorToken(window, "AccentStrong"), 4.5);
        foreach (var surface in SurfaceRoles)
        {
            RequireContrast(ColorToken(window, "Accent"), ColorToken(window, surface), 3);
            RequireContrast(ColorToken(window, "AccentStrong"), ColorToken(window, surface), 3);
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ActualPrimaryFocusHasContrastingInnerAndOuterEdges(bool highContrast) => TestAppBuilder.Run(() =>
    {
        var button = new Button { Content = "Primary action" };
        button.Classes.Add("primary");
        var window = new Window { Content = button, Width = 400, Height = 250 };
        try
        {
            PresentationTheme.Apply(window, new PresentationPreferences(HighContrast: highContrast));
            window.Show();
            button.Focus(NavigationMethod.Directional);
            Dispatcher.UIThread.RunJobs();
            var presenter = button.GetVisualDescendants().OfType<ContentPresenter>().First();
            RequireContrast(BrushColor(presenter.Foreground), BrushColor(presenter.Background), 4.5);
            RequireContrast(BrushColor(presenter.BorderBrush), BrushColor(presenter.Background), 3);
            Assert.Equal(1, presenter.BoxShadow.Count);
            var outerRing = presenter.BoxShadow[0];
            RequireContrast(outerRing.Color, BrushColor(window.Background), 3);
            Assert.True(outerRing.Spread >= 2);
        }
        finally
        {
            window.Close();
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ActualDefaultControlsAndFocusedInputRetainContrast(bool highContrast) => TestAppBuilder.Run(() =>
    {
        var button = new Button { Content = "Default action" };
        var textBox = new TextBox { PlaceholderText = "Placeholder" };
        var panel = new StackPanel();
        panel.Children.Add(button);
        panel.Children.Add(textBox);
        var window = new Window { Content = panel, Width = 400, Height = 250 };
        try
        {
            PresentationTheme.Apply(window, new PresentationPreferences(HighContrast: highContrast));
            window.Show();
            textBox.Focus(NavigationMethod.Tab);
            Dispatcher.UIThread.RunJobs();
            var presenter = button.GetVisualDescendants().OfType<ContentPresenter>().First();
            RequireContrast(BrushColor(presenter.Foreground), BrushColor(presenter.Background), 4.5);
            var border = textBox.GetVisualDescendants().OfType<Border>().Single(item => item.Name == "PART_BorderElement");
            RequireContrast(BrushColor(textBox.Foreground), BrushColor(border.Background), 4.5);
            RequireContrast(BrushColor(textBox.PlaceholderForeground), BrushColor(border.Background), 4.5);
            RequireContrast(BrushColor(border.BorderBrush), BrushColor(border.Background), 3);
            RequireContrast(BrushColor(textBox.SelectionForegroundBrush), BrushColor(textBox.SelectionBrush), 4.5);
        }
        finally
        {
            window.Close();
        }
    });

    private static Color ColorToken(Window window, string name) => (Color)window.Resources[$"Cindara.Color.{name}"]!;

    private static Color BrushColor(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static void RequireContrast(Color foreground, Color background, double minimum)
    {
        Assert.Equal(byte.MaxValue, foreground.A);
        Assert.Equal(byte.MaxValue, background.A);
        var first = Luminance(foreground);
        var second = Luminance(background);
        var ratio = (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
        Assert.True(ratio >= minimum, $"{foreground} on {background}: {ratio:F2}:1, expected at least {minimum}:1.");
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte channel)
        {
            var normalized = channel / 255d;
            return normalized <= 0.04045 ? normalized / 12.92 : Math.Pow((normalized + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }
}
