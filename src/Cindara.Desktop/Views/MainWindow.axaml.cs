using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cindara.Core.Authentication;
using Cindara.Core.Diagnostics;
using Cindara.Desktop.Accessibility;
using Cindara.Desktop.DesignSystem;
using Cindara.Desktop.Input;
using Cindara.Desktop.Localization;
using Cindara.Desktop.Navigation;
using Cindara.Desktop.ViewModels;

namespace Cindara.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly IControllerInputSource _controllerInput;
    private readonly DispatcherTimer _controllerTimer;
    private readonly FocusNavigationService _navigation;
    private MainViewModel? _viewModel;
    private Control? _modalReturnFocus;
    private TextBox? _keyboardDraft;
    private string? _screen;
    private bool _closed;
    private bool _openHomeOnReady;
    private readonly LocalDiagnostics? _diagnostics;

    public PresentationPreferences Preferences { get; }

    public MainWindow() : this(new SdlGamepadInputSource())
    {
    }

    public MainWindow(IControllerInputSource controllerInput, PresentationPreferences? preferences = null,
        LocalDiagnostics? diagnostics = null)
    {
        _controllerInput = controllerInput;
        _diagnostics = diagnostics;
        InitializeComponent();
        SizeChanged += (_, args) => ApplyAdaptiveLayout(args.NewSize);
        DiagnosticsButton.IsVisible = diagnostics is not null;
        Shell.HomeLoadingAction = CancelLoadingButton;
        FlowDirection = Loc.IsRightToLeft
            ? Avalonia.Media.FlowDirection.RightToLeft : Avalonia.Media.FlowDirection.LeftToRight;
        Preferences = preferences ?? new();
        ApplyPresentation();
        ApplyAdaptiveLayout(ClientSize);
        _navigation = new FocusNavigationService(this);
        _controllerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _controllerTimer.Tick += OnControllerTimerTick;
        Opened += OnOpened;
        Closed += OnClosed;
        Activated += (_, _) => OnApplicationActiveChanged();
        Deactivated += (_, _) => OnApplicationActiveChanged();
        LayoutUpdated += (_, _) =>
        {
            if (IsActive && !_closed)
            {
                _navigation.EnsureFocus();
            }
        };
        AddHandler(GotFocusEvent, (_, _) => _navigation.Remember());
        AddHandler(KeyDownEvent, OnShellKeyDown, RoutingStrategies.Tunnel);
        Shell.DestinationChanged += (_, _) =>
        {
            if (Shell.Destination != "Libraries")
            {
                _viewModel?.LibraryBrowser?.CancelLoading();
            }
            if (Shell.Destination != "Search")
            {
                _viewModel?.SearchBrowser?.CancelLoading();
            }

            if (Shell.Destination != "Home" && _viewModel?.ShowDesignGalleryCommand.IsRunning is true)
            {
                _viewModel.ShowDesignGalleryCommand.Cancel();
            }

            if (Shell.Destination == "Home" && _viewModel?.OpenHomeCommand.CanExecute(null) is true)
            {
                _viewModel.OpenHomeCommand.Execute(null);
            }

            _screen = null;
            Dispatcher.UIThread.Post(RefreshScreen, DispatcherPriority.Loaded);
        };
        Shell.ExitRequested += (_, _) => Close();
        Shell.LanguageRequested += (_, _) => ShowLanguage();
        Shell.LibraryLayoutRequested += (_, _) => ShowLibraryLayout();
        Shell.DiagnosticsRequested += (_, _) => ShowDiagnostics();
        GalleryView.SettingsRequested += (_, _) =>
        {
            GalleryView.SuspendFocusMemory();
            Shell.Navigate("Settings");
            _viewModel?.HideDesignGalleryCommand.Execute(null);
        };
        GalleryView.SearchRequested += (_, _) =>
        {
            GalleryView.SuspendFocusMemory();
            Shell.Navigate("Search");
            _viewModel?.HideDesignGalleryCommand.Execute(null);
        };
        GalleryView.DownloadsRequested += (_, _) =>
        {
            GalleryView.SuspendFocusMemory();
            Shell.Navigate("Downloads");
            _viewModel?.HideDesignGalleryCommand.Execute(null);
        };
        GalleryView.LibraryRequested += OnLibraryRequested;
        GalleryView.ItemRequested += (_, item) => ShowMediaSummary(item);
        Shell.LibraryView.ItemRequested += (_, item) => ShowMediaSummary(item);
        Shell.SearchView.ItemRequested += (_, item) => ShowMediaSummary(item);
        Shell.SearchView.KeyboardRequested += (_, target) => ShowKeyboard(target, fullScreen: true);
        GalleryView.NavigationWidthChanged += (_, _) => UpdateGalleryFooter();
        UpdateGalleryFooter();
    }

    private void OpenLibraries()
    {
        GalleryView.SuspendFocusMemory();
        Shell.Navigate("Libraries");
        _viewModel?.HideDesignGalleryCommand.Execute(null);
    }

    private async void OnLibraryRequested(object? sender, Cindara.Core.Jellyfin.MediaLibrary library)
    {
        OpenLibraries();
        if (_viewModel?.LibraryBrowser is { } browser && browser.OpenLibraryCommand.CanExecute(library))
        {
            await browser.OpenLibraryCommand.ExecuteAsync(library);
        }
    }

    private void ShowMediaSummary(MediaPreviewCardViewModel item)
    {
        if (ModalOverlay.IsVisible)
        {
            return;
        }

        BeginModal(item.Name);
        foreach (var text in new[] { item.Subtitle, item.Details, item.Overview })
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                ModalActions.Children.Add(new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            }
        }

        AddModalButton(Loc.Get("Action.Back"), DismissModal);
        FocusModal();
    }

    private async void OnOpened(object? sender, EventArgs eventArgs)
    {
        _viewModel = DataContext as MainViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.ShowDesignGalleryCommand.PropertyChanged += OnHomeLoadPropertyChanged;
        }

        _controllerInput.ActionPressed += OnControllerActionPressed;
        _controllerInput.ConnectionChanged += OnControllerConnectionChanged;
        _controllerInput.ActiveControllerChanged += OnActiveControllerChanged;
        _controllerInput.SetApplicationActive(IsActive);
        _controllerInput.Initialize();
        if (_controllerInput.IsAvailable)
        {
            _controllerTimer.Start();
        }

        UpdateControllerStatus();
        if (_viewModel is not null)
        {
            await _viewModel.InitializeCommand.ExecuteAsync(null);
        }

        if (!_closed)
        {
            RefreshScreen();
        }
    }

    private void OnApplicationActiveChanged()
    {
        if (_closed)
        {
            return;
        }

        _controllerInput.SetApplicationActive(IsActive);
        if (IsActive)
        {
            _navigation.EnsureFocus();
        }
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        _closed = true;
        _controllerTimer.Stop();
        _controllerTimer.Tick -= OnControllerTimerTick;
        _controllerInput.ActionPressed -= OnControllerActionPressed;
        _controllerInput.ConnectionChanged -= OnControllerConnectionChanged;
        _controllerInput.ActiveControllerChanged -= OnActiveControllerChanged;
        _controllerInput.Dispose();
        ClearModal();
        if (_viewModel is not null)
        {
            _viewModel.ShowDesignGalleryCommand.Cancel();
            _viewModel.LibraryBrowser?.CancelLoading();
            _viewModel.SearchBrowser?.CancelLoading();
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.ShowDesignGalleryCommand.PropertyChanged -= OnHomeLoadPropertyChanged;
            _viewModel = null;
        }
    }

    private void OnControllerTimerTick(object? sender, EventArgs eventArgs)
    {
        try
        {
            _controllerInput.Poll();
        }
        catch (Exception exception)
        {
            _diagnostics?.Record(DiagnosticArea.Controller, DiagnosticAction.PollController,
                DiagnosticOutcome.Failed, DiagnosticLevel.Error, exception);
            throw;
        }
    }

    private void OnHomeLoadPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainViewModel.ShowDesignGalleryCommand.IsRunning))
        {
            Dispatcher.UIThread.Post(RefreshScreen, DispatcherPriority.Loaded);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainViewModel.IsAuthenticatedVisible))
        {
            _openHomeOnReady = _viewModel?.IsAuthenticatedVisible is true;
        }

        if (args.PropertyName == nameof(MainViewModel.IsBusy))
        {
            BusyIndicator.IsIndeterminate = _viewModel?.IsBusy is true && !Preferences.ReducedMotion;
        }

        if (args.PropertyName is nameof(MainViewModel.IsServerEntryVisible)
            or nameof(MainViewModel.IsServerSelectionVisible)
            or nameof(MainViewModel.IsSignInVisible) or nameof(MainViewModel.AreSavedSessionsVisible)
            or nameof(MainViewModel.IsAuthenticatedVisible) or nameof(MainViewModel.IsDesignGalleryVisible)
            or nameof(MainViewModel.IsBusy))
        {
            Dispatcher.UIThread.Post(RefreshScreen, DispatcherPriority.Loaded);
        }
    }

    private void RefreshScreen()
    {
        if (_closed || _viewModel is null)
        {
            return;
        }

        if (_openHomeOnReady && !_viewModel.IsBusy && _viewModel.OpenHomeCommand.CanExecute(null))
        {
            _openHomeOnReady = false;
            _viewModel.OpenHomeCommand.Execute(null);
        }

        ShellViewport.IsVisible = !_viewModel.IsDesignGalleryVisible;
        Shell.IsVisible = _viewModel.IsAuthenticatedVisible;
        AuthenticationSurface.IsVisible = !Shell.IsVisible;
        ShellFooter.IsVisible = !Shell.IsVisible || Shell.Destination == "Home";
        var screen = _viewModel.IsDesignGalleryVisible ? "gallery"
            : _viewModel.IsAuthenticatedVisible && Shell.Destination == "Home"
                && _viewModel.ShowDesignGalleryCommand.IsRunning ? "shell:Home:loading"
            : _viewModel.IsAuthenticatedVisible ? $"shell:{Shell.Destination}"
            : _viewModel.IsSignInVisible ? "sign-in"
            : _viewModel.AreSavedSessionsVisible ? "accounts"
            : _viewModel.IsServerSelectionVisible ? "server-selection"
            : _viewModel.IsServerEntryVisible ? "server" : "loading";
        if (_diagnosticsOpen)
        {
            _screen = screen;
            return;
        }

        if (_screen == screen)
        {
            _navigation.EnsureFocus();
            return;
        }

        if (ModalOverlay.IsVisible)
        {
            ClearModal();
        }

        // Account boundaries must not restore focus into a previous account's preview.
        if (_screen?.StartsWith("shell:", StringComparison.Ordinal) is true && !Shell.IsVisible
            || _screen == "gallery" && !Shell.IsVisible)
        {
            _navigation.Reset();
            Shell.Reset();
        }

        _screen = screen;
        var contentFocus = Shell.ContentFocus;
        UpdateLayout();
        AuthenticationProgress.Text = screen switch
        {
            "server-selection" or "server" => Loc.Get("Auth.StepServer"),
            "accounts" => Loc.Get("Auth.StepAccount"),
            "sign-in" => Loc.Get("Auth.StepSignIn"),
            _ => string.Empty,
        };
        var initial = screen switch
        {
            "sign-in" => UsernameTextBox,
            "accounts" => SavedAccountsList.GetVisualDescendants().OfType<Button>().FirstOrDefault()
                ?? AddAccountButton,
            "server-selection" => SavedServersList.GetVisualDescendants().OfType<Button>().FirstOrDefault()
                ?? AddServerFromSelectionButton,
            "server" => (Control)ServerAddressTextBox,
            "gallery" => GalleryView.HomeNavigation,
            "loading" => LanguageButton,
            _ => Shell.InitialFocus,
        };
        _navigation.SetScope(MainSurface, initial, screen);
        if (screen == "gallery")
        {
            GalleryView.RestoreHomeFocus();
        }
        else if (Shell.IsVisible)
        {
            _navigation.Focus(contentFocus);
            if (Shell.Destination == "Libraries")
            {
                Shell.LibraryView.ResumeFocusMemory();
            }
            else if (Shell.Destination == "Search")
            {
                Shell.SearchView.ResumeFocusMemory();
            }
        }
    }

    private void OnControllerConnectionChanged(object? sender, ControllerConnectionEventArgs args) =>
        UpdateControllerStatus();

    private void OnActiveControllerChanged(object? sender, EventArgs args) => UpdateControllerStatus();

    private void UpdateControllerStatus()
    {
        var controller = _controllerInput.ActiveController;
        var layout = controller?.Layout ?? ControllerLayout.Generic;
        var accept = ControllerGlyphs.GetLabel(layout, ControllerAction.Accept);
        var back = ControllerGlyphs.GetLabel(layout, ControllerAction.Back);
        var menu = ControllerGlyphs.GetLabel(layout, ControllerAction.Menu);
        var name = controller?.Name ?? Loc.Get(_controllerInput.ConnectedGamepads > 0 ? "Input.Ready" : "Input.Connect");
        _viewModel?.SetControllerStatus(_controllerInput.IsAvailable
            ? Loc.Format("Input.Prompts", name, accept, back, menu)
            : Loc.Get("Input.Unavailable"));
    }

    private void OnControllerActionPressed(object? sender, ControllerActionEventArgs args)
    {
        if (!IsActive || _closed)
        {
            return;
        }

        switch (args.Action)
        {
            case ControllerAction.NavigateUp:
                MoveFocus(NavigationDirection.Up);
                break;
            case ControllerAction.NavigateDown:
                MoveFocus(NavigationDirection.Down);
                break;
            case ControllerAction.NavigateLeft:
                MoveFocus(NavigationDirection.Left);
                break;
            case ControllerAction.NavigateRight:
                MoveFocus(NavigationDirection.Right);
                break;
            case ControllerAction.Accept:
                ActivateFocusedControl();
                break;
            case ControllerAction.Back:
                GoBack();
                break;
            case ControllerAction.Menu:
                if (!ModalOverlay.IsVisible)
                {
                    ToggleFullscreen();
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(args), args.Action, "Unknown controller action.");
        }
    }

    private void OnShellKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key is Key.PageUp or Key.PageDown && !ModalOverlay.IsVisible
            && _viewModel?.IsDesignGalleryVisible is true)
        {
            GalleryView.ScrollDescription(args.Key == Key.PageDown);
            args.Handled = true;
        }
        else if (args.Key == Key.F11)
        {
            ToggleFullscreen();
            args.Handled = true;
        }
        else if (args.Key == Key.Escape)
        {
            GoBack();
            args.Handled = true;
        }
        else if (args.Key == Key.Tab)
        {
            _navigation.Move(args.KeyModifiers.HasFlag(KeyModifiers.Shift)
                ? NavigationDirection.Previous : NavigationDirection.Next);
            args.Handled = true;
        }
        else if (FocusManager?.GetFocusedElement() is not TextBox && args.KeyModifiers == KeyModifiers.None)
        {
            var direction = args.Key switch
            {
                Key.Up => NavigationDirection.Up,
                Key.Down => NavigationDirection.Down,
                Key.Left => NavigationDirection.Left,
                Key.Right => NavigationDirection.Right,
                _ => (NavigationDirection?)null,
            };
            if (direction is { } value)
            {
                MoveFocus(value);
                args.Handled = true;
            }
        }
    }

    private void MoveFocus(NavigationDirection direction)
    {
        _navigation.EnsureFocus();
        if (!ModalOverlay.IsVisible)
        {
            if (Shell.LibraryView.IsEffectivelyVisible && Shell.LibraryView.IsKeyboardFocusWithin
                && Shell.LibraryView.TryMove(direction))
            {
                return;
            }
            if (Shell.SearchView.IsEffectivelyVisible && Shell.SearchView.IsKeyboardFocusWithin
                && Shell.SearchView.TryMove(direction))
            {
                return;
            }

            if (_viewModel?.IsDesignGalleryVisible is true && GalleryView.TryMoveGalleryFocus(direction))
            {
                return;
            }

            if (Shell.IsEffectivelyVisible
                && FocusManager?.GetFocusedElement() is Control focused
                && focused.GetVisualAncestors().Contains(Shell) && Shell.TryMove(direction))
            {
                return;
            }
        }

        _navigation.Move(direction);
    }

    private void ActivateFocusedControl()
    {
        _navigation.EnsureFocus();
        switch (FocusManager?.GetFocusedElement())
        {
            case Button button when button.IsEffectivelyEnabled:
                ((IInvokeProvider)new ButtonAutomationPeer(button)).Invoke();
                break;
            case TextBox textBox when Shell.SearchView.IsEffectivelyVisible
                && Shell.SearchView.TryActivateTextBox(textBox):
                break;
            case TextBox textBox when textBox != _keyboardDraft:
                ShowKeyboard(textBox);
                break;
            case TextBox:
                _navigation.Move(NavigationDirection.Next);
                break;
        }
    }

    private void GoBack()
    {
        if (_exporting)
        {
            return;
        }

        if (ModalOverlay.IsVisible)
        {
            DismissModal();
        }
        else if (_viewModel?.ShowDesignGalleryCommand.IsRunning is true)
        {
            _viewModel.ShowDesignGalleryCommand.Cancel();
        }
        else if (_viewModel?.LibraryBrowser?.IsAnyLoading is true)
        {
            _viewModel.LibraryBrowser.CancelLoading();
        }
        else if (_viewModel?.SearchBrowser?.IsLoading is true)
        {
            _viewModel.SearchBrowser.CancelLoading();
        }
        else if (_viewModel?.IsDesignGalleryVisible is true)
        {
            GalleryView.HomeNavigation.Focus(NavigationMethod.Directional);
        }
        else if (_viewModel?.IsSignInVisible is true
            || _viewModel?.AreSavedSessionsVisible is true
            || _viewModel?.IsServerEntryVisible is true && _viewModel.SavedSessions.Count > 0)
        {
            if (_viewModel.BackToSessionsCommand.CanExecute(null))
            {
                _viewModel.BackToSessionsCommand.Execute(null);
            }
        }
        else if (_viewModel?.IsAuthenticatedVisible is true && Shell.Destination != "Home")
        {
            Shell.Navigate("Home");
        }
        else
        {
            LanguageButton.Focus(NavigationMethod.Directional);
        }
    }

    private void ToggleFullscreen() => WindowState =
        WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;

    private void OnLanguage(object? sender, RoutedEventArgs args) => ShowLanguage();

    private void ShowLanguage()
    {
        if (ModalOverlay.IsVisible)
        {
            ModalActions.Children.Clear();
            ModalTitle.Text = Loc.Get("Language.Title");
            AutomationProperties.SetName(ModalOverlay, ModalTitle.Text);
            _navigation.Forget("modal");
        }
        else
        {
            BeginModal(Loc.Get("Language.Title"));
        }

        var english = AddModalButton(Loc.Get("Language.English"), DismissModal);
        english.Classes.Add("selected");
        AutomationProperties.SetItemStatus(english, Loc.Get("State.Selected"));
        AutomationProperties.SetHelpText(english, Loc.Get("Language.EnglishOnly"));
        AddModalButton(Loc.Get("Action.Back"), DismissModal);
        FocusModal();
    }

    private void ApplyPresentation()
    {
        PresentationTheme.Apply(this, Preferences);
        GalleryView.ApplyPreferences(Preferences);
        BusyIndicator.IsIndeterminate = _viewModel?.IsBusy is true && !Preferences.ReducedMotion;
    }

    private void ApplyAdaptiveLayout(Size size)
    {
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        var profile = AdaptiveLayoutProfile.Create(size.Width, size.Height);
        Resources["Cindara.Adaptive.SafeMargin"] = new Thickness(profile.SafeMargin);
        Resources["Cindara.Adaptive.ContentSpacing"] = profile.ContentSpacing;
        Resources["Cindara.Adaptive.FormMaxWidth"] = 800 * profile.UiScale;
        Resources["Cindara.Adaptive.DialogMaxHeight"] =
            Math.Max(240, size.Height - (profile.SafeMargin * 2) - 96);
        var typographyScale = Preferences.TextScale * profile.UiScale;
        Resources["Cindara.Type.Display"] = 48 * typographyScale;
        Resources["Cindara.Type.Title"] = 32 * typographyScale;
        Resources["Cindara.Type.Heading"] = 24 * typographyScale;
        Resources["Cindara.Type.Body"] = 18 * typographyScale;
        Resources["Cindara.Type.Caption"] = 14 * typographyScale;
        var compact = profile.ViewportClass == AdaptiveViewportClass.Compact;
        ShellFooter.ColumnDefinitions = compact
            ? new ColumnDefinitions("*")
            : new ColumnDefinitions("*,Auto");
        ShellFooter.RowDefinitions = compact
            ? new RowDefinitions("Auto,Auto")
            : new RowDefinitions("Auto");
        Grid.SetColumn(FooterActions, compact ? 0 : 1);
        Grid.SetRow(FooterActions, compact ? 1 : 0);
        FooterActions.HorizontalAlignment = compact
            ? Avalonia.Layout.HorizontalAlignment.Stretch
            : Avalonia.Layout.HorizontalAlignment.Right;
    }

    private void UpdateGalleryFooter()
    {
        GalleryFooter.ColumnDefinitions = new ColumnDefinitions(
            $"{GalleryView.NavigationWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)},*");
        var fontSize = 14 * GalleryView.UiScale * Preferences.TextScale;
        GalleryReadHelp.FontSize = fontSize;
        GalleryControllerPrompts.FontSize = fontSize;
    }

    private void OnChooseAccount(object? sender, RoutedEventArgs args)
    {
        if (_viewModel is null || _viewModel.IsBusy || ModalOverlay.IsVisible)
        {
            return;
        }

        BeginModal(Loc.Get("Window.ChooseAccount"));
        foreach (var profile in _viewModel.SavedSessions)
        {
            AddModalButton(LocaleFormat.SessionDisplayName(profile), () =>
            {
                _viewModel.SelectedSavedSession = profile;
                DismissModal();
            });
        }

        AddModalButton(Loc.Get("Action.Cancel"), DismissModal);
        FocusModal();
    }

    private void OnSelectServer(object? sender, RoutedEventArgs args)
    {
        if (_viewModel is not null
            && sender is Button { DataContext: Cindara.Core.Models.ServerIdentity server }
            && _viewModel.SelectServerCommand.CanExecute(server))
        {
            _viewModel.SelectServerCommand.Execute(server);
        }
    }

    private async void OnUseSavedAccount(object? sender, RoutedEventArgs args)
    {
        if (_viewModel is not null
            && sender is Button { DataContext: SessionProfile profile })
        {
            _viewModel.SelectedSavedSession = profile;
            if (_viewModel.UseSavedSessionCommand.CanExecute(null))
            {
                await _viewModel.UseSavedSessionCommand.ExecuteAsync(null);
            }
        }
    }

    private async void OnRemoveSavedAccount(object? sender, RoutedEventArgs args)
    {
        if (_viewModel is not null
            && sender is Button { DataContext: SessionProfile profile })
        {
            _viewModel.SelectedSavedSession = profile;
            if (_viewModel.RemoveSavedSessionCommand.CanExecute(null))
            {
                await _viewModel.RemoveSavedSessionCommand.ExecuteAsync(null);
            }
        }
    }

    private void BeginModal(string title)
    {
        _navigation.Remember();
        _modalReturnFocus = FocusManager?.GetFocusedElement() as Control;
        ModalTitle.Text = title;
        AutomationProperties.SetName(ModalOverlay, title);
        ModalActions.Children.Clear();
        ModalOverlay.IsVisible = true;
        MainSurface.IsEnabled = false;
    }

    private void BeginFullScreenModal(string title)
    {
        BeginModal(title);
        ModalDialog.MaxWidth = double.PositiveInfinity;
        ModalDialog.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        ModalDialog.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
    }

    private Button AddModalButton(string text, Action action)
    {
        var button = new Button { Content = text, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        button.Click += (_, _) => action();
        ModalActions.Children.Add(button);
        return button;
    }

    private void FocusModal(Control? initial = null) => Dispatcher.UIThread.Post(() =>
    {
        if (ModalOverlay.IsVisible && !_closed)
        {
            // Each modal is a new scope; do not retain removed options or entered credentials.
            _navigation.SetScope(ModalActions, initial ?? ModalActions.GetVisualDescendants().OfType<Button>().FirstOrDefault(),
                "modal");
            if (initial is not null)
            {
                _navigation.Focus(initial);
            }
        }
    }, DispatcherPriority.Loaded);

    private void ClearModal()
    {
        _diagnosticsOpen = false;
        ModalDialog.MaxWidth = 1200;
        ModalDialog.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        ModalDialog.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        if (_keyboardDraft is not null)
        {
            _keyboardDraft.Text = string.Empty;
            _keyboardDraft = null;
        }

        ModalOverlay.IsVisible = false;
        ModalActions.Children.Clear();
        MainSurface.IsEnabled = true;
        _navigation.Forget("modal");
    }

    private void DismissModal()
    {
        var returnFocus = _modalReturnFocus;
        _modalReturnFocus = null;
        ClearModal();
        _navigation.SetScope(MainSurface, key: _screen ?? "server");
        if (returnFocus is not null)
        {
            _navigation.Focus(returnFocus);
        }
    }

    private void ShowKeyboard(TextBox target, bool fullScreen = false)
    {
        if (ModalOverlay.IsVisible)
        {
            return;
        }

        var title = AutomationProperties.GetName(target)
            ?? target.PlaceholderText
            ?? Loc.Get("Keyboard.EnterText");
        if (fullScreen)
        {
            BeginFullScreenModal(title);
        }
        else
        {
            BeginModal(title);
        }
        var draft = new TextBox
        {
            Text = target.Text,
            PasswordChar = target.PasswordChar,
            MaxLength = target.MaxLength,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            FlowDirection = target.FlowDirection,
        };
        AutomationProperties.SetName(draft, ModalTitle.Text);
        draft.CaretIndex = draft.Text?.Length ?? 0;
        _keyboardDraft = draft;
        ModalActions.Children.Add(draft);
        var keys = new UniformGrid { Columns = 12, FlowDirection = Avalonia.Media.FlowDirection.LeftToRight };
        var letters = new List<Button>();
        foreach (var character in "1234567890-=" + "qwertyuiop[]" + "asdfghjkl;'\\"
                     + "zxcvbnm,./`" + "!@#$%^&*()_+{}:\"|<>?~")
        {
            var key = new Button
            {
                Content = character.ToString(),
                Margin = new Avalonia.Thickness(3),
                MinWidth = 48,
                MinHeight = 48,
            };
            key.Click += (_, _) => InsertText(draft, (string)key.Content!);
            keys.Children.Add(key);
            if (char.IsLetter(character))
            {
                letters.Add(key);
            }
        }

        ModalActions.Children.Add(keys);
        var actions = new WrapPanel();
        AddKeyboardAction(Loc.Get("Keyboard.Shift"), () =>
        {
            foreach (var letter in letters)
            {
                var value = (string)letter.Content!;
                letter.Content = char.IsLower(value[0]) ? value.ToUpperInvariant() : value.ToLowerInvariant();
            }
        });
        AddKeyboardAction(Loc.Get("Keyboard.Space"), () => InsertText(draft, " "));
        AddKeyboardAction(Loc.Get("Keyboard.Backspace"), () =>
        {
            if (draft.SelectionStart == draft.SelectionEnd && draft.CaretIndex > 0)
            {
                draft.SelectionStart = draft.CaretIndex - 1;
                draft.SelectionEnd = draft.CaretIndex;
            }

            InsertText(draft, string.Empty);
        });
        AddKeyboardAction(Loc.Get("Keyboard.Clear"), () => draft.Text = string.Empty);
        AddKeyboardAction(Loc.Get("Keyboard.Done"), () =>
        {
            target.Text = draft.Text;
            target.CaretIndex = target.Text?.Length ?? 0;
            DismissModal();
        });
        AddKeyboardAction(Loc.Get("Action.Cancel"), DismissModal);
        ModalActions.Children.Add(actions);
        FocusModal();

        void AddKeyboardAction(string text, Action action)
        {
            var button = new Button
            {
                Content = text,
                Margin = new Avalonia.Thickness(3),
                MinHeight = 48,
            };
            button.Click += (_, _) => action();
            actions.Children.Add(button);
        }
    }

    private static void InsertText(TextBox draft, string value)
    {
        var start = Math.Min(draft.SelectionStart, draft.SelectionEnd);
        var length = Math.Abs(draft.SelectionEnd - draft.SelectionStart);
        var text = draft.Text ?? string.Empty;
        start = Math.Clamp(start, 0, text.Length);
        length = Math.Min(length, text.Length - start);
        var available = draft.MaxLength > 0
            ? Math.Max(0, draft.MaxLength - (text.Length - length))
            : int.MaxValue;
        var insertion = value.Length <= available ? value : value[..available];
        draft.Text = text.Remove(start, length).Insert(start, insertion);
        draft.CaretIndex = start + insertion.Length;
        draft.SelectionStart = draft.SelectionEnd = draft.CaretIndex;
    }
}
