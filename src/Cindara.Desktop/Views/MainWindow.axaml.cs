using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Cindara.Desktop.Input;
using Cindara.Desktop.ViewModels;

namespace Cindara.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly IControllerInputSource _controllerInput;
    private readonly DispatcherTimer _controllerTimer;
    private MainViewModel? _viewModel;

    public MainWindow()
        : this(new SdlGamepadInputSource())
    {
    }

    public MainWindow(IControllerInputSource controllerInput)
    {
        _controllerInput = controllerInput;
        InitializeComponent();

        _controllerTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        _controllerTimer.Tick += OnControllerTimerTick;

        Opened += OnOpened;
        Closed += OnClosed;
    }

    private async void OnOpened(object? sender, EventArgs eventArgs)
    {
        _controllerInput.ActionPressed += OnControllerActionPressed;
        _controllerInput.ConnectionChanged += OnControllerConnectionChanged;
        _controllerInput.Initialize();

        if (DataContext is MainViewModel viewModel)
        {
            _viewModel = viewModel;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            await viewModel.InitializeCommand.ExecuteAsync(null);
            viewModel.SetControllerStatus(
                _controllerInput.IsAvailable
                    ? "Controller ready: D-pad or left stick navigates, A selects, and Start toggles fullscreen."
                    : _controllerInput.InitializationError ?? "Controller input is unavailable.");
        }

        if (_controllerInput.IsAvailable)
        {
            _controllerTimer.Start();
        }

        FocusCurrentState();
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        _controllerTimer.Stop();
        _controllerTimer.Tick -= OnControllerTimerTick;
        _controllerInput.ActionPressed -= OnControllerActionPressed;
        _controllerInput.ConnectionChanged -= OnControllerConnectionChanged;
        _controllerInput.Dispose();
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = null;
        }
    }

    private void OnControllerTimerTick(object? sender, EventArgs eventArgs) =>
        _controllerInput.Poll();

    private void OnViewModelPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(MainViewModel.IsServerEntryVisible)
            or nameof(MainViewModel.IsSignInVisible)
            or nameof(MainViewModel.AreSavedSessionsVisible)
            or nameof(MainViewModel.IsAuthenticatedVisible)
            or nameof(MainViewModel.IsDesignGalleryVisible))
        {
            Dispatcher.UIThread.Post(FocusCurrentState, DispatcherPriority.Loaded);
        }
    }

    private void FocusCurrentState()
    {
        if (_viewModel is null)
        {
            return;
        }

        if (_viewModel.IsSignInVisible)
        {
            UsernameTextBox.Focus();
        }
        else if (_viewModel.AreSavedSessionsVisible)
        {
            SavedSessionsComboBox.Focus();
        }
        else if (_viewModel.IsDesignGalleryVisible)
        {
            GalleryView.FocusTopNavigation();
        }
        else if (_viewModel.IsAuthenticatedVisible)
        {
            PreviewButton.Focus();
        }
        else if (_viewModel.IsServerEntryVisible)
        {
            ServerAddressTextBox.Focus();
        }
    }

    private void OnControllerConnectionChanged(
        object? sender,
        ControllerConnectionEventArgs eventArgs)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        viewModel.SetControllerStatus(
            eventArgs.IsConnected
                ? $"{eventArgs.ControllerName} connected. D-pad or left stick navigates, A selects, and Start toggles fullscreen."
                : _controllerInput.ConnectedGamepads > 0
                    ? $"{eventArgs.ControllerName} disconnected. {_controllerInput.ConnectedGamepads} controller(s) remain connected."
                    : $"{eventArgs.ControllerName} disconnected. Connect another controller to continue couch navigation.");
    }

    private void OnControllerActionPressed(
        object? sender,
        ControllerActionEventArgs eventArgs)
    {
        if (!IsActive)
        {
            return;
        }

        switch (eventArgs.Action)
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
            case ControllerAction.Menu:
                WindowState = WindowState == WindowState.FullScreen
                    ? WindowState.Normal
                    : WindowState.FullScreen;
                break;
            case ControllerAction.Back:
                switch (ControllerBackNavigation.Resolve(
                    _viewModel?.IsDesignGalleryVisible is true,
                    WindowState == WindowState.FullScreen))
                {
                    case ControllerBackDestination.Account:
                        _viewModel!.HideDesignGalleryCommand.Execute(null);
                        break;
                    case ControllerBackDestination.Windowed:
                        WindowState = WindowState.Normal;
                        break;
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(eventArgs),
                    eventArgs.Action,
                    "Unknown controller action.");
        }
    }

    private void MoveFocus(NavigationDirection direction)
    {
        if (_viewModel?.IsDesignGalleryVisible is true && GalleryView.TryMoveGalleryFocus(direction))
        {
            return;
        }

        FocusManager?.TryMoveFocus(
            direction,
            new FindNextElementOptions
            {
                SearchRoot = this,
            });
    }

    private void ActivateFocusedControl()
    {
        switch (FocusManager?.GetFocusedElement())
        {
            case Button button when button.Command?.CanExecute(button.CommandParameter) is true:
                button.Command.Execute(button.CommandParameter);
                break;
            case TextBox:
                MoveFocus(NavigationDirection.Next);
                break;
        }
    }
}
