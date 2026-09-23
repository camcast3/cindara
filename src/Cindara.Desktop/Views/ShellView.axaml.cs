using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Cindara.Core.Jellyfin;
using Cindara.Desktop.Localization;

namespace Cindara.Desktop.Views;

public partial class ShellView : UserControl
{
    private readonly Dictionary<string, Control> _contentMemory = [];
    private string _destination = "Home";

    public ShellView()
    {
        InitializeComponent();
        SetSelected(HomeNavigation, true);
        AddHandler(GotFocusEvent, OnShellFocused);
        AddHandler(LostFocusEvent, (_, _) => UpdateRail());
        NavigationRail.PointerEntered += (_, _) => UpdateRail();
        NavigationRail.PointerExited += (_, _) => UpdateRail();
    }

    public event EventHandler? DestinationChanged;
    public event EventHandler? ExitRequested;
    public event EventHandler? LanguageRequested;
    public event EventHandler? LibraryLayoutRequested;
    public event EventHandler<MediaLibrary>? LibraryRequested;
    public string Destination => _destination;
    public Control? HomeLoadingAction { get; set; }
    private bool IsHomeLoading => _destination == "Home"
        && HomeLoadingAction is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true };
    public Control InitialFocus => _destination switch
    {
        "Home" when IsHomeLoading => HomeLoadingAction!,
        "Home" => RetryHomeButton.IsEffectivelyVisible && RetryHomeButton.IsEffectivelyEnabled ? RetryHomeButton : HomeNavigation,
        "Settings" => SettingsLanguageButton,
        "Libraries" => LibraryView.IsEffectivelyVisible && LibraryView.InitialFocus != LibraryView
            ? LibraryView.InitialFocus : LibrariesNavigation,
        _ => NavigationButtons.Children.OfType<Button>().Single(button => Equals(button.Tag, _destination)),
    };
    public Control ContentFocus => IsHomeLoading || _destination == "Libraries" ? InitialFocus
        : _contentMemory.TryGetValue(_destination, out var control)
        && control.IsEffectivelyVisible && control.IsEffectivelyEnabled ? control : InitialFocus;

    public void Reset()
    {
        _contentMemory.Clear();
        Navigate("Home");
    }

    public void Navigate(string destination)
    {
        if (destination is not ("Home" or "Libraries" or "Search" or "Downloads" or "Settings"))
        {
            throw new ArgumentOutOfRangeException(nameof(destination));
        }

        if (destination == "Settings" && _destination != "Settings")
        {
            _contentMemory.Remove("Settings");
        }

        if (destination != "Libraries")
        {
            LibraryView.SuspendFocusMemory();
        }

        _destination = destination;
        HomePage.IsVisible = destination == "Home";
        SettingsPage.IsVisible = destination == "Settings";
        LibrariesPage.IsVisible = destination == "Libraries";
        PageScroll.IsVisible = !LibrariesPage.IsVisible;
        PlaceholderPage.IsVisible = !HomePage.IsVisible && !SettingsPage.IsVisible && !LibrariesPage.IsVisible;
        DestinationTitle.Text = Loc.Get($"Nav.{destination}");
        DestinationMessage.Text = destination switch
        {
            "Search" => Loc.Get("Placeholder.Search"),
            "Downloads" => Loc.Get("Placeholder.Downloads"),
            _ => string.Empty,
        };
        foreach (var button in NavigationButtons.Children.OfType<Button>())
        {
            SetSelected(button, Equals(button.Tag, destination));
        }

        DestinationChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool TryMove(NavigationDirection direction)
    {
        if (FlowDirection == Avalonia.Media.FlowDirection.RightToLeft)
        {
            direction = direction switch
            {
                NavigationDirection.Left => NavigationDirection.Right,
                NavigationDirection.Right => NavigationDirection.Left,
                _ => direction,
            };
        }

        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        var buttons = NavigationButtons.GetVisualDescendants().OfType<Button>().ToArray();
        var index = Array.FindIndex(buttons, button => button == focused);
        if (index >= 0)
        {
            switch (direction)
            {
                case NavigationDirection.Up:
                case NavigationDirection.Down:
                    buttons[Math.Clamp(index + (direction == NavigationDirection.Up ? -1 : 1), 0, buttons.Length - 1)]
                        .Focus(NavigationMethod.Directional);
                    (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control)?.BringIntoView();
                    return true;
                case NavigationDirection.Left:
                    return true;
                case NavigationDirection.Right:
                    ContentFocus.Focus(NavigationMethod.Directional);
                    return true;
            }
        }

        if (direction == NavigationDirection.Left)
        {
            FocusRail();
            return true;
        }

        return false;
    }

    public void FocusRail() => NavigationButtons.Children.OfType<Button>()
        .Single(button => Equals(button.Tag, _destination)).Focus(NavigationMethod.Directional);

    public bool IsRailFocused => NavigationButtons.IsKeyboardFocusWithin;

    private static void SetSelected(Button button, bool selected)
    {
        button.Classes.Set("selected", selected);
        AutomationProperties.SetItemStatus(button, selected ? Loc.Get("State.Selected") : string.Empty);
        AutomationProperties.SetHelpText(button, selected ? Loc.Get("State.Selected") : string.Empty);
    }

    private void OnShellFocused(object? sender, RoutedEventArgs args)
    {
        if (args.Source is Control control && !control.GetVisualAncestors().Contains(NavigationRail))
        {
            _contentMemory[_destination] = control;
        }

        UpdateRail();
    }

    private void UpdateRail()
    {
        var expanded = NavigationRail.IsKeyboardFocusWithin || NavigationRail.IsPointerOver;
        var textScale = TopLevel.GetTopLevel(this) is MainWindow window ? window.Preferences.TextScale : 1;
        NavigationRail.Width = expanded ? Math.Min(440, 280 * textScale * (Loc.IsPseudoLocalized ? 1.3 : 1)) : 88;
        foreach (var label in NavigationRail.GetVisualDescendants().OfType<TextBlock>()
                     .Where(label => label.Classes.Contains("rail-label")))
        {
            label.IsVisible = expanded;
        }
    }

    private void OnDestinationClicked(object? sender, RoutedEventArgs args) => Navigate((string)((Button)sender!).Tag!);
    private void OnHomeClicked(object? sender, RoutedEventArgs args) => Navigate("Home");

    private void OnExitClicked(object? sender, RoutedEventArgs args) =>
        ExitRequested?.Invoke(this, EventArgs.Empty);

    private void OnLanguageClicked(object? sender, RoutedEventArgs args) =>
        LanguageRequested?.Invoke(this, EventArgs.Empty);

    private void OnLibraryLayoutClicked(object? sender, RoutedEventArgs args) =>
        LibraryLayoutRequested?.Invoke(this, EventArgs.Empty);

    private void OnLibraryClicked(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: MediaLibrary library })
        {
            LibraryRequested?.Invoke(this, library);
        }
    }
}
