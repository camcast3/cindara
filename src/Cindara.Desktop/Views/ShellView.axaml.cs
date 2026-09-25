using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cindara.Desktop.DesignSystem;
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
        AddHandler(GotFocusEvent, OnShellFocused);
        SizeChanged += (_, args) => UpdateWorkspaceLayout(args.NewSize.Width);
        ShowSettingsCategory("Preferences");
        Navigate("Home");
    }

    public event EventHandler? DestinationChanged;
    public event EventHandler? ExitRequested;
    public event EventHandler? LanguageRequested;
    public event EventHandler? LibraryLayoutRequested;
    public event EventHandler? LibrarySwitcherRequested;
    public event EventHandler? DiagnosticsRequested;
    public string Destination => _destination;
    public Control? HomeLoadingAction { get; set; }

    private bool IsHomeLoading => _destination == "Home"
        && HomeLoadingAction is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true };

    public Control InitialFocus => _destination switch
    {
        "Home" when IsHomeLoading => HomeLoadingAction!,
        "Home" => RetryHomeButton,
        "Settings" => SettingsPreferencesCategory,
        "Libraries" => LibraryView.IsEffectivelyVisible && LibraryView.InitialFocus != LibraryView
            ? LibraryView.InitialFocus : DestinationBackButton,
        "Search" => SearchView.IsEffectivelyVisible ? SearchView.InitialFocus : DestinationBackButton,
        _ => DestinationBackButton,
    };

    public Control ContentFocus =>
        _contentMemory.TryGetValue(_destination, out var control)
        && control.IsEffectivelyVisible && control.IsEffectivelyEnabled
            ? control
            : InitialFocus;

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
            ShowSettingsCategory("Preferences");
            _contentMemory["Settings"] = SettingsPreferencesCategory;
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
        DestinationHeader.IsVisible = destination != "Home";
        DestinationBackButton.IsVisible = destination != "Home";
        HomePage.IsVisible = destination == "Home";
        SettingsPage.IsVisible = destination == "Settings";
        LibrariesPage.IsVisible = destination == "Libraries";
        SearchPage.IsVisible = destination == "Search";
        PageScroll.IsVisible = destination is "Home" or "Downloads" or "Settings";
        PlaceholderPage.IsVisible = destination == "Downloads";
        DestinationTitle.Text = Loc.Get($"Nav.{destination}");
        DestinationTitle.IsVisible = destination != "Libraries";
        LibrarySwitcher.IsVisible = destination == "Libraries";
        DestinationMessage.Text = destination == "Downloads"
            ? Loc.Get("Placeholder.Downloads")
            : string.Empty;
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

        if (_destination == "Libraries")
        {
            if (ReferenceEquals(focused, DestinationBackButton)
                && direction == NavigationDirection.Right && LibrarySwitcher.IsEffectivelyEnabled)
            {
                return LibrarySwitcher.Focus(NavigationMethod.Directional);
            }

            if (ReferenceEquals(focused, LibrarySwitcher))
            {
                if (direction == NavigationDirection.Left)
                {
                    return DestinationBackButton.Focus(NavigationMethod.Directional);
                }

                if (direction == NavigationDirection.Down)
                {
                    return LibraryView.FilterMenuButton.Focus(NavigationMethod.Directional);
                }
            }

            if ((ReferenceEquals(focused, LibraryView.FilterMenuButton)
                || ReferenceEquals(focused, LibraryView.SortMenuButton))
                && direction == NavigationDirection.Up)
            {
                return LibrarySwitcher.Focus(NavigationMethod.Directional);
            }
        }

        if (ReferenceEquals(focused, DestinationBackButton)
            && direction is NavigationDirection.Right or NavigationDirection.Down)
        {
            return ContentFocus.Focus(NavigationMethod.Directional);
        }

        return false;
    }

    public void FocusBack() => DestinationBackButton.Focus(NavigationMethod.Directional);

    public bool IsBackFocused =>
        ReferenceEquals(TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement(), DestinationBackButton);

    private void OnShellFocused(object? sender, RoutedEventArgs args)
    {
        if (args.Source is Control control && !ReferenceEquals(control, DestinationBackButton))
        {
            _contentMemory[_destination] = control;
        }
    }

    private void OnBackClicked(object? sender, RoutedEventArgs args) => Navigate("Home");
    private void OnHomeClicked(object? sender, RoutedEventArgs args) => Navigate("Home");
    private void OnExitClicked(object? sender, RoutedEventArgs args) =>
        ExitRequested?.Invoke(this, EventArgs.Empty);
    private void OnLanguageClicked(object? sender, RoutedEventArgs args) =>
        LanguageRequested?.Invoke(this, EventArgs.Empty);
    private void OnLibraryLayoutClicked(object? sender, RoutedEventArgs args) =>
        LibraryLayoutRequested?.Invoke(this, EventArgs.Empty);
    private void OnLibrarySwitcherClicked(object? sender, RoutedEventArgs args) =>
        LibrarySwitcherRequested?.Invoke(this, EventArgs.Empty);
    private void OnDiagnosticsClicked(object? sender, RoutedEventArgs args) =>
        DiagnosticsRequested?.Invoke(this, EventArgs.Empty);

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
        SettingsLibraryPanel.IsVisible = category == "Library";
        SettingsApplicationPanel.IsVisible = category == "Application";
        SettingsDiagnosticsPanel.IsVisible = category == "Diagnostics";
        SetSelected(SettingsPreferencesCategory, category == "Preferences");
        SetSelected(SettingsLibraryCategory, category == "Library");
        SetSelected(SettingsApplicationCategory, category == "Application");
        SetSelected(SettingsDiagnosticsCategory, category == "Diagnostics");
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
        (_settingsCategory switch
        {
            "Library" => SettingsLibraryPanel,
            "Application" => SettingsApplicationPanel,
            "Diagnostics" => SettingsDiagnosticsPanel,
            _ => SettingsPreferencesPanel,
        })
        .GetVisualDescendants().OfType<Button>()
        .Where(button => button.IsEffectivelyVisible && button.IsEffectivelyEnabled)
        .ToArray();

    private void UpdateWorkspaceLayout(double width)
    {
        if (width <= 0)
        {
            return;
        }

        var compact = width < 900;
        DestinationBrand.IsVisible = width >= 1400;
        SettingsCategories.Orientation = compact
            ? Avalonia.Layout.Orientation.Horizontal
            : Avalonia.Layout.Orientation.Vertical;
        SettingsCategoryScroll.HorizontalScrollBarVisibility = compact
            ? Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            : Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        SettingsCategoryScroll.VerticalScrollBarVisibility = compact
            ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
            : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        var textScale = TopLevel.GetTopLevel(this) is MainWindow window
            ? window.Preferences.TextScale
            : 1;
        var densityScale = TopLevel.GetTopLevel(this) is MainWindow densityWindow
            ? ResponsiveDensityProfile.Create(
                densityWindow.ClientSize.Width,
                densityWindow.ClientSize.Height).TypeScale
            : 1;
        var categoryWidth = Math.Min(
            520,
            Math.Max(280, 250 * textScale * densityScale
                * (Loc.IsPseudoLocalized ? 1.15 : 1)));
        SettingsWorkspace.ColumnDefinitions = compact
            ? new ColumnDefinitions("*")
            : new ColumnDefinitions(
                $"{categoryWidth.ToString(CultureInfo.InvariantCulture)},*");
        SettingsWorkspace.RowDefinitions = compact
            ? new RowDefinitions("Auto,Auto")
            : new RowDefinitions("Auto");
        Grid.SetColumn(SettingsDetail, compact ? 0 : 1);
        Grid.SetRow(SettingsDetail, compact ? 1 : 0);
        Dispatcher.UIThread.Post(() =>
            (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control)?.BringIntoView(),
            DispatcherPriority.Loaded);
    }

    private static void SetSelected(Button button, bool selected)
    {
        button.Classes.Set("selected", selected);
        AutomationProperties.SetItemStatus(
            button, selected ? Loc.Get("State.Selected") : string.Empty);
        AutomationProperties.SetHelpText(
            button, selected ? Loc.Get("State.Selected") : string.Empty);
    }
}
