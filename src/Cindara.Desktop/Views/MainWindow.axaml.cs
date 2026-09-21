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

        if (DataContext is MainViewModel { AreSavedSessionsVisible: true })
        {
            SavedSessionsComboBox.Focus();
        }
        else
        {
            ServerAddressTextBox.Focus();
        }
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        _controllerTimer.Stop();
        _controllerTimer.Tick -= OnControllerTimerTick;
        _controllerInput.ActionPressed -= OnControllerActionPressed;
        _controllerInput.ConnectionChanged -= OnControllerConnectionChanged;
        _controllerInput.Dispose();
    }

    private void OnControllerTimerTick(object? sender, EventArgs eventArgs) =>
        _controllerInput.Poll();

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
            case ControllerAction.Back when WindowState == WindowState.FullScreen:
                WindowState = WindowState.Normal;
                break;
            case ControllerAction.Menu:
                WindowState = WindowState == WindowState.FullScreen
                    ? WindowState.Normal
                    : WindowState.FullScreen;
                break;
            case ControllerAction.Back:
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
