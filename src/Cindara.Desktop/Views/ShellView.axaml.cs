using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Cindara.Desktop.Localization;

namespace Cindara.Desktop.Views;

public partial class ShellView : UserControl
{
    private readonly Dictionary<string, Control> _contentMemory = [];
    private string _destination = "Home";
    private Button _category;

    public ShellView()
    {
        InitializeComponent();
        _category = AccountCategory;
        SetSelected(AccountCategory, true);
        SetSelected(HomeNavigation, true);
        AddHandler(GotFocusEvent, OnShellFocused);
        AddHandler(LostFocusEvent, (_, _) => UpdateRail());
        NavigationRail.PointerEntered += (_, _) => UpdateRail();
        NavigationRail.PointerExited += (_, _) => UpdateRail();
    }

    public event EventHandler? DestinationChanged;
    public event EventHandler? WindowOptionsRequested;
    public event EventHandler? AccessibilityRequested;
    public string Destination => _destination;
    public Control PreviewAction => PreviewButton;
    public Control InitialFocus => _destination switch
    {
        "Home" => HomeHeader,
        "Settings" => _category,
        _ => ReturnHomeButton,
    };
    public Control ContentFocus =>
        _contentMemory.TryGetValue(_destination, out var control)
        && control.IsEffectivelyVisible && control.IsEffectivelyEnabled ? control : InitialFocus;

    public void Reset()
    {
        _contentMemory.Clear();
        SelectCategory(AccountCategory);
        Navigate("Home");
    }

    public void Navigate(string destination)
    {
        if (destination is not ("Home" or "Libraries" or "Search" or "Downloads" or "Settings"))
        {
            throw new ArgumentOutOfRangeException(nameof(destination));
        }

        _destination = destination;
        HomePage.IsVisible = destination == "Home";
        SettingsPage.IsVisible = destination == "Settings";
        PlaceholderPage.IsVisible = !HomePage.IsVisible && !SettingsPage.IsVisible;
        DestinationTitle.Text = Loc.Get($"Nav.{destination}");
        DestinationMessage.Text = destination switch
        {
            "Libraries" => Loc.Get("Placeholder.Libraries"),
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
        var buttons = NavigationButtons.Children.OfType<Button>().ToArray();
        var index = Array.FindIndex(buttons, button => button == focused);
        if (index >= 0)
        {
            switch (direction)
            {
                case NavigationDirection.Up:
                case NavigationDirection.Down:
                    buttons[Math.Clamp(index + (direction == NavigationDirection.Up ? -1 : 1), 0, buttons.Length - 1)]
                        .Focus(NavigationMethod.Directional);
                    return true;
                case NavigationDirection.Left:
                    return true;
                case NavigationDirection.Right:
                    ContentFocus.Focus(NavigationMethod.Directional);
                    return true;
            }
        }

        if (_destination == "Settings" && focused is not null)
        {
            if (SettingsCategories.Children.Contains(focused))
            {
                if (direction == NavigationDirection.Right)
                {
                    SelectCategory((Button)focused);
                    ActiveSettings().GetVisualDescendants().OfType<Button>()
                        .FirstOrDefault(button => button.IsEffectivelyEnabled)?.Focus(NavigationMethod.Directional);
                    return true;
                }

                if (direction is NavigationDirection.Up or NavigationDirection.Down)
                {
                    var categories = SettingsCategories.Children.OfType<Button>().ToArray();
                    var categoryIndex = Array.IndexOf(categories, focused);
                    categories[Math.Clamp(categoryIndex + (direction == NavigationDirection.Up ? -1 : 1), 0, categories.Length - 1)]
                        .Focus(NavigationMethod.Directional);
                    return true;
                }
            }
            else if (direction == NavigationDirection.Left)
            {
                _category.Focus(NavigationMethod.Directional);
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

    private StackPanel ActiveSettings() => _category == AccountCategory
        ? AccountSettings : _category == DisplayCategory ? DisplaySettings : ControlsSettings;

    private void SelectCategory(Button category)
    {
        _category = category;
        AccountSettings.IsVisible = category == AccountCategory;
        DisplaySettings.IsVisible = category == DisplayCategory;
        ControlsSettings.IsVisible = category == ControlsCategory;
        foreach (var button in SettingsCategories.Children.OfType<Button>())
        {
            SetSelected(button, button == category);
        }
    }

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
    private void OnCategoryClicked(object? sender, RoutedEventArgs args)
    {
        SelectCategory((Button)sender!);
        ActiveSettings().GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.IsEffectivelyEnabled)?
            .Focus(NavigationMethod.Directional);
    }

    private void OnWindowOptionsClicked(object? sender, RoutedEventArgs args) =>
        WindowOptionsRequested?.Invoke(this, EventArgs.Empty);

    private void OnAccessibilityClicked(object? sender, RoutedEventArgs args) =>
        AccessibilityRequested?.Invoke(this, EventArgs.Empty);
}
