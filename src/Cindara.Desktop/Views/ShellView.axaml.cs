using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cindara.Desktop.DesignSystem;
using Cindara.Desktop.Input;
using Cindara.Desktop.Localization;
using Cindara.Desktop.Navigation;

namespace Cindara.Desktop.Views;

public partial class ShellView : UserControl
{
    private readonly Dictionary<string, Control> _contentMemory = [];
    private readonly Dictionary<string, string> _settingsCategories = [];
    public DesktopNavigationState Routes { get; } = new();
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
    public event EventHandler? BackRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? LanguageRequested;
    public event EventHandler? LibraryLayoutRequested;
    public event EventHandler? LibrarySwitcherRequested;
    public event EventHandler? DiagnosticsRequested;
    public event EventHandler? ControllerDeadZoneDecreaseRequested;
    public event EventHandler? ControllerDeadZoneIncreaseRequested;
    public event EventHandler? ControllerInvertHorizontalRequested;
    public event EventHandler? ControllerInvertVerticalRequested;
    public event EventHandler? ControllerResetRequested;
    public event EventHandler<ControllerBindingRequestedEventArgs>? ControllerBindingRequested;
    public string Destination => _destination;
    public string FocusKey => Routes.FocusKey;
    public LibraryBrowserView LibraryView => Routes.Primary == PrimaryDestination.Libraries
        ? SectionLibraryView : DashboardLibraryView;
    public IEnumerable<LibraryBrowserView> LibraryViews => [DashboardLibraryView, SectionLibraryView];
    public Control? HomeLoadingAction { get; set; }

    private bool IsHomeLoading => _destination == "Home"
        && HomeLoadingAction is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true };

    public Control InitialFocus => _destination switch
    {
        "Home" when IsHomeLoading => HomeLoadingAction!,
        "Home" => RetryHomeButton,
        "Settings" => SettingsPreferencesCategory,
        "LibraryLanding" => LibraryLanding.InitialFocus is { Focusable: true } initial
            ? initial : DestinationBackButton,
        "Libraries" => LibraryView.IsEffectivelyVisible && LibraryView.InitialFocus != LibraryView
            ? LibraryView.InitialFocus : DestinationBackButton,
        "Search" => SearchView.IsEffectivelyVisible ? SearchView.InitialFocus : DestinationBackButton,
        _ => DestinationBackButton,
    };

    public Control ContentFocus =>
        _contentMemory.TryGetValue(FocusKey, out var control)
        && control.IsEffectivelyVisible && control.IsEffectivelyEnabled
            ? control
            : InitialFocus;

    public void Reset()
    {
        _contentMemory.Clear();
        _settingsCategories.Clear();
        Routes.Reset();
        LibraryLanding.Reset();
        ShowSettingsCategory("Preferences");
        ApplyRoute();
    }

    public void Navigate(string destination)
    {
        switch (destination)
        {
            case "Home":
                Routes.Select(PrimaryDestination.Dashboard);
                break;
            case "LibraryLanding":
                Routes.Select(PrimaryDestination.Libraries);
                break;
            case "Favorites":
                Routes.Select(PrimaryDestination.Favorites);
                break;
            case "Libraries":
                Routes.Push(DesktopPage.LibraryResults);
                break;
            case "Search":
                Routes.Push(DesktopPage.Search);
                break;
            case "Settings":
                Routes.Push(DesktopPage.Settings);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(destination));
        }

        ApplyRoute();
    }

    public bool Back()
    {
        if (!Routes.Back())
            return false;
        ApplyRoute();
        return true;
    }

    private void GoHome()
    {
        Routes.GoHome();
        ApplyRoute();
    }

    private void ApplyRoute()
    {
        DashboardLibraryView.SuspendFocusMemory();
        SectionLibraryView.SuspendFocusMemory();
        DashboardLibraryView.IsVisible = Routes.Primary != PrimaryDestination.Libraries;
        SectionLibraryView.IsVisible = Routes.Primary == PrimaryDestination.Libraries;
        var destination = Routes.Page switch
        {
            DesktopPage.Dashboard => "Home",
            DesktopPage.LibraryLanding => "LibraryLanding",
            DesktopPage.LibraryResults => "Libraries",
            DesktopPage.Favorites => "Favorites",
            DesktopPage.Search => "Search",
            DesktopPage.Settings => "Settings",
            _ => throw new InvalidOperationException("Unknown desktop route."),
        };
        if (destination == "Settings")
        {
            ShowSettingsCategory(_settingsCategories.GetValueOrDefault(FocusKey, "Preferences"));
            _contentMemory.TryAdd(FocusKey, SettingsPreferencesCategory);
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
        LibraryLanding.IsVisible = destination == "LibraryLanding";
        LibrariesPage.IsVisible = destination == "Libraries";
        SearchPage.IsVisible = destination == "Search";
        PageScroll.IsVisible = destination is "Home" or "Favorites" or "Settings";
        PlaceholderPage.IsVisible = destination == "Favorites";
        DestinationTitle.Text = Loc.Get(destination == "LibraryLanding" ? "Nav.Libraries" : $"Nav.{destination}");
        DestinationTitle.IsVisible = destination != "Libraries";
        LibrarySwitcher.IsVisible = destination == "Libraries";
        DestinationMessage.Text = destination switch
        {
            "Favorites" => Loc.Get("Placeholder.Favorites"),
            _ => string.Empty,
        };
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

        if (_destination == "LibraryLanding" && LibraryLanding.TryMove(direction))
            return true;

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
            _contentMemory[FocusKey] = control;
        }
    }

    private void OnBackClicked(object? sender, RoutedEventArgs args) =>
        BackRequested?.Invoke(this, EventArgs.Empty);
    private void OnHomeClicked(object? sender, RoutedEventArgs args) => GoHome();
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
    private void OnControllerDeadZoneDecreaseClicked(object? sender, RoutedEventArgs args) =>
        ControllerDeadZoneDecreaseRequested?.Invoke(this, EventArgs.Empty);
    private void OnControllerDeadZoneIncreaseClicked(object? sender, RoutedEventArgs args) =>
        ControllerDeadZoneIncreaseRequested?.Invoke(this, EventArgs.Empty);
    private void OnControllerInvertHorizontalClicked(object? sender, RoutedEventArgs args) =>
        ControllerInvertHorizontalRequested?.Invoke(this, EventArgs.Empty);
    private void OnControllerInvertVerticalClicked(object? sender, RoutedEventArgs args) =>
        ControllerInvertVerticalRequested?.Invoke(this, EventArgs.Empty);
    private void OnControllerResetClicked(object? sender, RoutedEventArgs args) =>
        ControllerResetRequested?.Invoke(this, EventArgs.Empty);
    private void OnControllerBindingClicked(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: string actionName }
            && Enum.TryParse<ControllerAction>(actionName, out var action))
        {
            ControllerBindingRequested?.Invoke(this, new ControllerBindingRequestedEventArgs(action));
        }
    }

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
        if (Routes.Page == DesktopPage.Settings)
            _settingsCategories[FocusKey] = category;
        SettingsPreferencesPanel.IsVisible = category == "Preferences";
        SettingsLibraryPanel.IsVisible = category == "Library";
        SettingsApplicationPanel.IsVisible = category == "Application";
        SettingsControlsPanel.IsVisible = category == "Controls";
        SettingsDiagnosticsPanel.IsVisible = category == "Diagnostics";
        SetSelected(SettingsPreferencesCategory, category == "Preferences");
        SetSelected(SettingsLibraryCategory, category == "Library");
        SetSelected(SettingsApplicationCategory, category == "Application");
        SetSelected(SettingsControlsCategory, category == "Controls");
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

            if (direction == NavigationDirection.Left)
                return DestinationBackButton.Focus(NavigationMethod.Directional);

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
            "Controls" => SettingsControlsPanel,
            "Diagnostics" => SettingsDiagnosticsPanel,
            _ => SettingsPreferencesPanel,
        })
        .GetVisualDescendants().OfType<Button>()
        .Where(button => button.IsEffectivelyVisible && button.IsEffectivelyEnabled)
        .ToArray();

    public void UpdateControllerSettings(
        ControllerInfo? controller,
        ControllerProfile profile,
        string? configurationError)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var connected = controller is not null;
        var layout = controller?.Layout ?? ControllerLayout.Generic;
        ControllerProfileSummary.Text = connected
            ? Loc.Format("Controller.ProfileSummary", controller!.Name)
            : Loc.Get("Controller.NotConnected");
        if (controller is not null && string.IsNullOrEmpty(controller.PersistentId))
            ControllerProfileSummary.Text = $"{ControllerProfileSummary.Text} {Loc.Get("Controller.SessionOnly")}";
        if (!string.IsNullOrWhiteSpace(configurationError))
        {
            ControllerProfileSummary.Text = $"{ControllerProfileSummary.Text} {configurationError}";
        }

        ControllerDeadZoneSummary.Text = Loc.Format(
            "Controller.DeadZoneSummary",
            Math.Round(profile.DeadZone / (double)short.MaxValue * 100));
        ControllerInvertHorizontalButton.Content = Loc.Format(
            "Controller.InvertHorizontal",
            Loc.Get(profile.InvertHorizontalAxis ? "State.On" : "State.Off"));
        ControllerInvertVerticalButton.Content = Loc.Format(
            "Controller.InvertVertical",
            Loc.Get(profile.InvertVerticalAxis ? "State.On" : "State.Off"));

        foreach (var (button, action) in ControllerBindingButtons())
        {
            button.Content = Loc.Format(
                "Controller.BindingSummary",
                Loc.Get($"Controller.Action.{action}"),
                ControllerGlyphs.GetLabel(layout, action, profile));
            button.IsEnabled = connected;
        }

        ControllerDeadZoneDecreaseButton.IsEnabled = connected && profile.DeadZone > ControllerProfile.MinimumDeadZone;
        ControllerDeadZoneIncreaseButton.IsEnabled = connected && profile.DeadZone < ControllerProfile.MaximumDeadZone;
        ControllerInvertHorizontalButton.IsEnabled = connected;
        ControllerInvertVerticalButton.IsEnabled = connected;
        ControllerResetButton.IsEnabled = connected;
    }

    private (Button Button, ControllerAction Action)[] ControllerBindingButtons() =>
    [
        (ControllerNavigateUpButton, ControllerAction.NavigateUp),
        (ControllerNavigateDownButton, ControllerAction.NavigateDown),
        (ControllerNavigateLeftButton, ControllerAction.NavigateLeft),
        (ControllerNavigateRightButton, ControllerAction.NavigateRight),
        (ControllerAcceptButton, ControllerAction.Accept),
        (ControllerBackButton, ControllerAction.Back),
        (ControllerMenuButton, ControllerAction.Menu),
    ];

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
