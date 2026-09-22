using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cindara.Desktop.Accessibility;
using Cindara.Desktop.Tests.Navigation;

namespace Cindara.Desktop.Tests.Accessibility;

public sealed class PresentationThemeTests
{
    [Theory]
    [InlineData("display", 1)]
    [InlineData("title", 1)]
    [InlineData("heading", 2)]
    [InlineData("label", 0)]
    public Task TypographyExposesHeadingSemanticsWithoutPromotingLabels(string role, int level) => TestAppBuilder.Run(() =>
    {
        var text = new TextBlock { Text = "Semantic text" };
        text.Classes.Add(role);
        var window = new Window { Content = text, Width = 300, Height = 200 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(level, AutomationProperties.GetHeadingLevel(text));
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task ApplyingAndResettingPreferencesIsWindowScoped() => TestAppBuilder.Run(() =>
    {
        var window = new Window();
        var other = new Window();
        PresentationTheme.Apply(window, new PresentationPreferences(1.5, true, true));

        Assert.Equal(27d, window.Resources["Cindara.Type.Body"]);
        Assert.Equal(72d, window.Resources["Cindara.Type.Display"]);
        Assert.Equal(TimeSpan.Zero, window.Resources["Cindara.Motion.Standard"]);
        Assert.Equal(Colors.Black, window.Resources["Cindara.Color.Background"]);
        Assert.Contains("high-contrast", window.Classes);
        Assert.Contains("reduced-motion", window.Classes);
        Assert.Contains("large-text", window.Classes);
        Assert.False(other.Resources.ContainsKey("Cindara.Type.Body"));

        PresentationTheme.Apply(window, new PresentationPreferences());
        Assert.Equal(18d, window.Resources["Cindara.Type.Body"]);
        Assert.Equal(48d, window.Resources["Cindara.Type.Display"]);
        Assert.Equal(TimeSpan.FromMilliseconds(200), window.Resources["Cindara.Motion.Standard"]);
        Assert.Equal(Color.Parse("#080B12"), window.Resources["Cindara.Color.Background"]);
        Assert.Equal(Color.Parse("#131925"), ((ISolidColorBrush)window.Resources["TextControlBackground"]!).Color);
        Assert.DoesNotContain("high-contrast", window.Classes);
        Assert.DoesNotContain("reduced-motion", window.Classes);
        Assert.DoesNotContain("large-text", window.Classes);
    });

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public Task LiveControlsScaleWrapAndRestore(double scale) => TestAppBuilder.Run(() =>
    {
        var button = new Button { Content = "A deliberately long translated action that must wrap across several lines", Width = 220 };
        var caption = new TextBlock { Text = "Caption" };
        caption.Classes.Add("caption");
        var textBox = new TextBox { PlaceholderText = "An accessible placeholder" };
        var panel = new StackPanel();
        panel.Children.Add(button);
        panel.Children.Add(caption);
        panel.Children.Add(textBox);
        var window = new Window { Content = panel, Width = 500, Height = 500 };
        try
        {
            PresentationTheme.Apply(window, new PresentationPreferences(scale));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(18 * scale, button.FontSize);
            Assert.Equal(14 * scale, caption.FontSize);
            Assert.Equal(18 * scale, textBox.FontSize);
            var presenter = button.GetVisualDescendants().OfType<ContentPresenter>().First();
            Assert.Equal(TextWrapping.Wrap, presenter.TextWrapping);
            Assert.True(button.Bounds.Height > 48);

            PresentationTheme.Apply(window, new PresentationPreferences());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(18, button.FontSize);
            Assert.Equal(14, caption.FontSize);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task ReducedMotionDisablesFluentPressTransitionAndRestoresIt() => TestAppBuilder.Run(() =>
    {
        var button = new Button { Content = "Action" };
        var progress = new ProgressBar { Value = 50 };
        var panel = new StackPanel();
        panel.Children.Add(button);
        panel.Children.Add(progress);
        var window = new Window { Content = panel, Width = 300, Height = 200 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(button.Transitions);
            var progressPanel = progress.GetVisualDescendants().OfType<Panel>()
                .Single(item => item.Name == "DeterminateRoot");
            Assert.NotNull(progressPanel.Transitions);
            PresentationTheme.Apply(window, new PresentationPreferences(ReducedMotion: true));
            Dispatcher.UIThread.RunJobs();
            Assert.Null(button.Transitions);
            Assert.Null(progressPanel.Transitions);
            PresentationTheme.Apply(window, new PresentationPreferences());
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(button.Transitions);
            Assert.NotNull(progressPanel.Transitions);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task SelectedNavigationHasShapeAndFocusedCardKeepsOneOutline() => TestAppBuilder.Run(() =>
    {
        var selected = new Button { Content = "Selected" };
        selected.Classes.Add("nav");
        selected.Classes.Add("selected");
        var card = new Button { Content = "Card" };
        card.Classes.Add("card");
        var panel = new StackPanel();
        panel.Children.Add(selected);
        panel.Children.Add(card);
        var window = new Window { Content = panel, Width = 300, Height = 200 };
        try
        {
            window.Show();
            card.Focus(NavigationMethod.Directional);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new Thickness(0, 0, 0, 3),
                selected.GetVisualDescendants().OfType<ContentPresenter>().First().BorderThickness);
            Assert.Equal(new Thickness(4),
                card.GetVisualDescendants().OfType<ContentPresenter>().First().BorderThickness);
            Assert.Null(card.FocusAdorner);
        }
        finally
        {
            window.Close();
        }
    });
}
