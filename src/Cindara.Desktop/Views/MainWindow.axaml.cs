using System.Text.Json;
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
using Cindara.Desktop.Accessibility;
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
    private readonly PresentationSettingsStore? _presentationStore;
    private bool _presentationLoadFailed;

    public PresentationPreferences Preferences { get; private set; } = new();

    public MainWindow() : this(new SdlGamepadInputSource())
    {
    }

    public MainWindow(IControllerInputSource controllerInput, PresentationSettingsStore? presentationStore = null)
    {
        _controllerInput = controllerInput;
        InitializeComponent();
        FlowDirection = Loc.IsRightToLeft
            ? Avalonia.Media.FlowDirection.RightToLeft : Avalonia.Media.FlowDirection.LeftToRight;
        _presentationStore = presentationStore;
        try
        {
            Preferences = presentationStore?.Load() ?? new();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _presentationLoadFailed = true;
            ShowPresentationError("Accessibility.LoadError");
        }

        ApplyPresentation();
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
            _screen = null;
            Dispatcher.UIThread.Post(RefreshScreen, DispatcherPriority.Loaded);
        };
        Shell.WindowOptionsRequested += (_, _) => ShowWindowOptions();
        Shell.AccessibilityRequested += (_, _) => ShowAccessibility();
    }

    private async void OnOpened(object? sender, EventArgs eventArgs)
    {
        _viewModel = DataContext as MainViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
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
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = null;
        }
    }

    private void OnControllerTimerTick(object? sender, EventArgs eventArgs) => _controllerInput.Poll();

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainViewModel.IsBusy))
        {
            BusyIndicator.IsIndeterminate = _viewModel?.IsBusy is true && !Preferences.ReducedMotion;
        }

        if (args.PropertyName is nameof(MainViewModel.IsServerEntryVisible)
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

        ShellViewport.IsVisible = !_viewModel.IsDesignGalleryVisible;
        Shell.IsVisible = _viewModel.IsAuthenticatedVisible;
        AuthenticationSurface.IsVisible = !Shell.IsVisible;
        var screen = _viewModel.IsDesignGalleryVisible ? "gallery"
            : _viewModel.IsAuthenticatedVisible ? $"shell:{Shell.Destination}"
            : _viewModel.IsSignInVisible ? "sign-in"
            : _viewModel.AreSavedSessionsVisible ? "accounts"
            : _viewModel.IsServerEntryVisible ? "server" : "loading";
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

        var returningFromGallery = _screen == "gallery" && Shell.IsVisible;
        _screen = screen;
        UpdateLayout();
        var initial = screen switch
        {
            "sign-in" => UsernameTextBox,
            "accounts" => SavedAccountButton,
            "server" => (Control)ServerAddressTextBox,
            "gallery" => GalleryBackButton,
            "loading" => WindowOptionsButton,
            _ => Shell.InitialFocus,
        };
        var contentFocus = Shell.ContentFocus;
        _navigation.SetScope(MainSurface, initial, screen);
        if (screen == "gallery")
        {
            GalleryView.FocusTopNavigation();
        }
        else if (returningFromGallery)
        {
            _navigation.Focus(Shell.PreviewAction);
        }
        else if (Shell.IsVisible)
        {
            _navigation.Focus(contentFocus);
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
        if (ModalOverlay.IsVisible)
        {
            DismissModal();
        }
        else if (_viewModel?.IsDesignGalleryVisible is true)
        {
            _viewModel.HideDesignGalleryCommand.Execute(null);
        }
        else if (_viewModel?.IsAuthenticatedVisible is true && !Shell.IsRailFocused)
        {
            Shell.FocusRail();
        }
        else if (_viewModel?.IsSignInVisible is true
            || _viewModel?.IsServerEntryVisible is true && _viewModel.SavedSessions.Count > 0)
        {
            if (_viewModel.BackToSessionsCommand.CanExecute(null))
            {
                _viewModel.BackToSessionsCommand.Execute(null);
            }
        }
        else
        {
            ShowWindowOptions();
        }
    }

    private void ToggleFullscreen() => WindowState =
        WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;

    private void OnWindowOptions(object? sender, RoutedEventArgs args) => ShowWindowOptions();

    private void ShowWindowOptions()
    {
        if (ModalOverlay.IsVisible)
        {
            return;
        }

        BeginModal(Loc.Get("Window.Title"));
        AddModalButton(Loc.Get("Window.Return"), DismissModal);
        AddModalButton(Loc.Get("Window.Desktop"), () => { WindowState = WindowState.Normal; DismissModal(); });
        AddModalButton(Loc.Get("Window.Fullscreen"), () => { WindowState = WindowState.FullScreen; DismissModal(); });
        AddModalButton(Loc.Get("Accessibility.Title"), ShowAccessibility);
        AddModalButton(Loc.Get("Window.Exit"), Close);
        FocusModal();
    }

    private void ShowAccessibility()
    {
        if (ModalOverlay.IsVisible)
        {
            // Replace window options while retaining the original non-modal focus owner.
            ModalActions.Children.Clear();
            ModalTitle.Text = Loc.Get("Accessibility.Title");
            AutomationProperties.SetName(ModalOverlay, ModalTitle.Text);
            _navigation.Forget("modal");
        }
        else
        {
            BeginModal(Loc.Get("Accessibility.Title"));
        }

        if (_presentationLoadFailed)
        {
            ModalActions.Children.Add(new TextBlock
            {
                Text = Loc.Get("Accessibility.LoadError"),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Classes = { "secondary" },
            });
        }

        var scale = AddPreference("Accessibility.TextScale", Loc.Format("Accessibility.TextScale", Preferences.TextScale),
            "Accessibility.CycleText");
        var contrast = AddPreference("Accessibility.HighContrast", StateLabel("Accessibility.HighContrast", Preferences.HighContrast),
            "Accessibility.Toggle");
        var motion = AddPreference("Accessibility.ReducedMotion", StateLabel("Accessibility.ReducedMotion", Preferences.ReducedMotion),
            "Accessibility.Toggle");
        scale.Click += (_, _) =>
        {
            UpdatePreferences(Preferences with { TextScale = Preferences.TextScale == 1 ? 1.25 : Preferences.TextScale == 1.25 ? 1.5 : 1 });
            scale.Content = Loc.Format("Accessibility.TextScale", Preferences.TextScale);
        };
        contrast.Click += (_, _) =>
        {
            UpdatePreferences(Preferences with { HighContrast = !Preferences.HighContrast });
            contrast.Content = StateLabel("Accessibility.HighContrast", Preferences.HighContrast);
        };
        motion.Click += (_, _) =>
        {
            UpdatePreferences(Preferences with { ReducedMotion = !Preferences.ReducedMotion });
            motion.Content = StateLabel("Accessibility.ReducedMotion", Preferences.ReducedMotion);
        };
        var error = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "secondary" } };
        error.Bind(TextBlock.TextProperty, PresentationStatus.GetObservable(TextBlock.TextProperty));
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Assertive);
        ModalActions.Children.Add(error);
        AddModalButton(Loc.Get("Action.Back"), DismissModal);
        FocusModal();

        Button AddPreference(string id, string label, string help)
        {
            var button = AddModalButton(label, () => { });
            AutomationProperties.SetAutomationId(button, id);
            AutomationProperties.SetHelpText(button, Loc.Get(help));
            button.IsEnabled = !_presentationLoadFailed;
            return button;
        }
    }

    private static string StateLabel(string key, bool enabled) =>
        Loc.Format(key, Loc.Get(enabled ? "State.On" : "State.Off"));

    private void UpdatePreferences(PresentationPreferences preferences)
    {
        try
        {
            _presentationStore?.Save(preferences);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowPresentationError("Accessibility.SaveError");
            return;
        }

        Preferences = preferences;
        PresentationStatus.Text = string.Empty;
        PresentationStatus.IsVisible = false;
        ApplyPresentation();
    }

    private void ApplyPresentation()
    {
        PresentationTheme.Apply(this, Preferences);
        GalleryView.ApplyPreferences(Preferences);
        BusyIndicator.IsIndeterminate = _viewModel?.IsBusy is true && !Preferences.ReducedMotion;
    }

    private void ShowPresentationError(string key)
    {
        PresentationStatus.Text = Loc.Get(key);
        PresentationStatus.IsVisible = true;
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

    private Button AddModalButton(string text, Action action)
    {
        var button = new Button { Content = text, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        button.Click += (_, _) => action();
        ModalActions.Children.Add(button);
        return button;
    }

    private void FocusModal() => Dispatcher.UIThread.Post(() =>
    {
        if (ModalOverlay.IsVisible && !_closed)
        {
            // Each modal is a new scope; do not retain removed options or entered credentials.
            _navigation.SetScope(ModalActions, ModalActions.GetVisualDescendants().OfType<Button>().FirstOrDefault(),
                "modal");
        }
    }, DispatcherPriority.Loaded);

    private void ClearModal()
    {
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

    private void ShowKeyboard(TextBox target)
    {
        if (ModalOverlay.IsVisible)
        {
            return;
        }

        BeginModal(AutomationProperties.GetName(target) ?? target.PlaceholderText ?? Loc.Get("Keyboard.EnterText"));
        var draft = new TextBox { Text = target.Text, PasswordChar = target.PasswordChar, MinWidth = 800, FlowDirection = target.FlowDirection };
        AutomationProperties.SetName(draft, ModalTitle.Text);
        draft.CaretIndex = draft.Text?.Length ?? 0;
        _keyboardDraft = draft;
        ModalActions.Children.Add(draft);
        var keys = new UniformGrid { Columns = 12, FlowDirection = Avalonia.Media.FlowDirection.LeftToRight };
        var letters = new List<Button>();
        foreach (var character in "1234567890-=" + "qwertyuiop[]" + "asdfghjkl;'\\"
                     + "zxcvbnm,./`" + "!@#$%^&*()_+{}:\"|<>?~")
        {
            var key = new Button { Content = character.ToString(), Margin = new Avalonia.Thickness(3) };
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
            var button = new Button { Content = text, Margin = new Avalonia.Thickness(3) };
            button.Click += (_, _) => action();
            actions.Children.Add(button);
        }
    }

    private static void InsertText(TextBox draft, string value)
    {
        var start = Math.Min(draft.SelectionStart, draft.SelectionEnd);
        var length = Math.Abs(draft.SelectionEnd - draft.SelectionStart);
        draft.Text = (draft.Text ?? string.Empty).Remove(start, length).Insert(start, value);
        draft.CaretIndex = start + value.Length;
        draft.SelectionStart = draft.SelectionEnd = draft.CaretIndex;
    }
}
