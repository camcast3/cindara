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
    private string _settingsCategory = "Preferences";

    public ShellView()
    {
        InitializeComponent();
        SetSelected(HomeNavigation, true);
        AddHandler(GotFocusEvent, OnShellFocused);
        AddHandler(LostFocusEvent, (_, _) => UpdateRail());
        SizeChanged += (_, args) => UpdateWorkspaceLayout(args.NewSize.Width);
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
        "Settings" => SettingsPreferencesCategory,
        "Libraries" => LibraryView.IsEffectivelyVisible && LibraryView.InitialFocus != LibraryView
            ? LibraryView.InitialFocus : LibrariesNavigation,
        "Search" => SearchView.IsEffectivelyVisible ? SearchView.InitialFocus : SearchNavigation,
        _ => NavigationButtons.Children.OfType<Button>().Single(button => Equals(button.Tag, _destination)),
    };
    public Control ContentFocus => IsHomeLoading || _destination is "Libraries" or "Search" ? InitialFocus
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

        var resetSettingsFocus = destination == "Settings" && _destination != "Settings";
        if (resetSettingsFocus)
        {
            _contentMemory.Remove("Settings");
            ShowSettingsCategory("Preferences");
        }

        if (destination != "Libraries")
        {
            LibraryView.SuspendFocusMemory();
        }
        if (destination != "Search")
        {
            SearchView.SuspendFocusMemory();
        }

        _destination = destination;
        HomePage.IsVisible = destination == "Home";
        SettingsPage.IsVisible = destination == "Settings";
        LibrariesPage.IsVisible = destination == "Libraries";
        SearchPage.IsVisible = destination == "Search";
        PageScroll.IsVisible = !LibrariesPage.IsVisible && !SearchPage.IsVisible;
        PlaceholderPage.IsVisible = destination == "Downloads";
        if (resetSettingsFocus)
        {
            _contentMemory["Settings"] = SettingsPreferencesCategory;
        }
        DestinationTitle.Text = Loc.Get($"Nav.{destination}");
        DestinationMessage.Text = destination switch
        {
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
        if (_destination == "Settings" && TryMoveSettings(focused, direction))
        {
            return true;
        }

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
        var availableWidth = Math.Max(176, Bounds.Width);
        NavigationRail.Width = expanded
            ? Math.Min(availableWidth * 0.55, Math.Min(440, 280 * textScale * (Loc.IsPseudoLocalized ? 1.3 : 1)))
            : Math.Min(88, availableWidth * 0.25);
        foreach (var label in NavigationRail.GetVisualDescendants().OfType<TextBlock>()
                     .Where(label => label.Classes.Contains("rail-label")))
        {
            label.IsVisible = expanded;
        }
    }

    private void UpdateWorkspaceLayout(double width)
    {
        if (width <= 0)
        {
            return;
        }

        var compact = width < 960;
        SettingsWorkspace.ColumnDefinitions = compact
            ? new ColumnDefinitions("*")
            : new ColumnDefinitions("220,*");
        SettingsWorkspace.RowDefinitions = compact
            ? new RowDefinitions("Auto,Auto")
            : new RowDefinitions("Auto");
        Grid.SetColumn(SettingsDetail, compact ? 0 : 1);
        Grid.SetRow(SettingsDetail, compact ? 1 : 0);
    }

    private void OnDestinationClicked(object? sender, RoutedEventArgs args) => Navigate((string)((Button)sender!).Tag!);
    private void OnHomeClicked(object? sender, RoutedEventArgs args) => Navigate("Home");

    private void OnExitClicked(object? sender, RoutedEventArgs args) =>
        ExitRequested?.Invoke(this, EventArgs.Empty);

    private void OnLanguageClicked(object? sender, RoutedEventArgs args) =>
        LanguageRequested?.Invoke(this, EventArgs.Empty);

    private void OnLibraryLayoutClicked(object? sender, RoutedEventArgs args) =>
        LibraryLayoutRequested?.Invoke(this, EventArgs.Empty);

    private void OnSettingsCategoryClicked(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: string category })
        {
            ShowSettingsCategory(category);
        }
    }

    private void OnSettingsCategoryFocused(object? sender, RoutedEventArgs args) =>
        OnSettingsCategoryClicked(sender, args);

    private void ShowSettingsCategory(string category)
    {
        _settingsCategory = category;
        SettingsPreferencesPanel.IsVisible = category == "Preferences";
        SettingsApplicationPanel.IsVisible = category == "Application";
        SetSelected(SettingsPreferencesCategory, category == "Preferences");
        SetSelected(SettingsApplicationCategory, category == "Application");
    }

    private bool TryMoveSettings(Control? focused, NavigationDirection direction)
    {
        var categories = SettingsCategories.Children.OfType<Button>().ToArray();
        var categoryIndex = Array.IndexOf(categories, focused);
        if (categoryIndex >= 0)
        {
            if (direction is NavigationDirection.Up or NavigationDirection.Down)
            {
                var next = Math.Clamp(
                    categoryIndex + (direction == NavigationDirection.Up ? -1 : 1),
                    0,
                    categories.Length - 1);
                categories[next].Focus(NavigationMethod.Directional);
                return true;
            }

            if (direction == NavigationDirection.Right)
            {
                if (ActiveSettingsActions().FirstOrDefault() is { } action)
                {
                    action.Focus(NavigationMethod.Directional);
                    action.BringIntoView();
                }
                return true;
            }

            return false;
        }

        if (focused is not null && ActiveSettingsActions().Contains(focused)
            && direction == NavigationDirection.Left)
        {
            var category = categories.Single(button => Equals(button.Tag, _settingsCategory));
            category.Focus(NavigationMethod.Directional);
            category.BringIntoView();
            return true;
        }

        return false;
    }

    private Button[] ActiveSettingsActions() =>
        (_settingsCategory == "Preferences" ? SettingsPreferencesPanel : SettingsApplicationPanel)
        .GetVisualDescendants().OfType<Button>()
        .Where(button => button.IsEffectivelyVisible && button.IsEffectivelyEnabled)
        .ToArray();

    private void OnLibraryClicked(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: MediaLibrary library })
        {
            LibraryRequested?.Invoke(this, library);
        }
    }
}
