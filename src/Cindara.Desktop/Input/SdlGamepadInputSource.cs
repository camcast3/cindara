using Cindara.Core.Diagnostics;
using SDL3;

namespace Cindara.Desktop.Input;

public sealed class SdlGamepadInputSource : IControllerInputSource
{
    private static readonly SDL.GamepadButton[] MappedButtons =
    [
        SDL.GamepadButton.DPadUp, SDL.GamepadButton.DPadDown,
        SDL.GamepadButton.DPadLeft, SDL.GamepadButton.DPadRight,
        SDL.GamepadButton.South, SDL.GamepadButton.East, SDL.GamepadButton.Start,
    ];
    private static readonly SDL.GamepadAxis[] MappedAxes = [SDL.GamepadAxis.LeftX, SDL.GamepadAxis.LeftY];
    private readonly ISdlGamepadBackend _backend;
    private readonly ControllerInputState _state;
    private readonly LocalDiagnostics? _diagnostics;
    private readonly Dictionary<uint, nint> _gamepads = [];
    private readonly Dictionary<uint, ControllerInfo> _controllers = [];
    private bool _applicationActive;
    private bool _initialized;
    private bool _backendInitialized;
    private bool _disposed;

    public SdlGamepadInputSource(LocalDiagnostics? diagnostics = null)
        : this(new SdlGamepadBackend(), TimeProvider.System, diagnostics)
    {
    }

    internal SdlGamepadInputSource(ISdlGamepadBackend backend, TimeProvider timeProvider, LocalDiagnostics? diagnostics = null)
    {
        _backend = backend;
        _diagnostics = diagnostics;
        _state = new ControllerInputState(timeProvider);
        _state.ActionPressed += (_, args) => ActionPressed?.Invoke(this, args);
        _state.ActiveControllerChanged += (_, args) => ActiveControllerChanged?.Invoke(this, args);
    }

    public event EventHandler<ControllerActionEventArgs>? ActionPressed;

    public event EventHandler<ControllerConnectionEventArgs>? ConnectionChanged;

    public event EventHandler? ActiveControllerChanged;

    public bool IsAvailable { get; private set; }

    public string? InitializationError { get; private set; }

    public int ConnectedGamepads => _gamepads.Count;

    public ControllerInfo? ActiveController => _disposed ? null : _state.ActiveController;

    public void SetApplicationActive(bool isActive)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_applicationActive == isActive)
        {
            return;
        }

        if (isActive && IsAvailable)
        {
            // Drain events accumulated without focus before enabling actions. Native
            // snapshots also suppress controls whose down edge was never delivered.
            Poll();
            foreach (var (id, gamepad) in _gamepads)
            {
                SuppressHeldControls(id, gamepad);
            }
        }

        _applicationActive = isActive;
        _state.SetApplicationActive(isActive);
    }

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_initialized)
        {
            return;
        }

        _initialized = true;
        using var operation = _diagnostics?.Begin(DiagnosticArea.Controller, DiagnosticAction.InitializeController);
        _state.SetApplicationActive(false);

        try
        {
            if (!_backend.Initialize())
            {
                InitializationError = $"SDL could not initialize gamepad input: {_backend.GetError()}";
                _diagnostics?.Record(DiagnosticArea.Controller, DiagnosticAction.InitializeController,
                    DiagnosticOutcome.Unavailable, DiagnosticLevel.Error);
                return;
            }

            _backendInitialized = true;
            IsAvailable = true;
            foreach (var controllerId in _backend.GetGamepads())
            {
                AddGamepad(controllerId);
            }

            Poll();
            foreach (var (id, gamepad) in _gamepads)
            {
                SuppressHeldControls(id, gamepad);
            }

            _state.SetApplicationActive(_applicationActive);
            operation?.Complete();
        }
        catch (DllNotFoundException exception)
        {
            operation?.Fail(exception);
            IsAvailable = false;
            InitializationError = $"SDL gamepad runtime was not found: {exception.Message}";
        }
        catch (EntryPointNotFoundException exception)
        {
            operation?.Fail(exception);
            IsAvailable = false;
            InitializationError = $"The bundled SDL gamepad runtime is incompatible: {exception.Message}";
        }
    }

    public void Poll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsAvailable)
        {
            return;
        }

        while (_backend.PollEvent(out var sdlEvent))
        {
            switch ((SDL.EventType)sdlEvent.Type)
            {
                case SDL.EventType.GamepadAdded:
                    AddGamepad(sdlEvent.GDevice.Which);
                    break;

                case SDL.EventType.GamepadRemoved:
                    RemoveGamepad(sdlEvent.GDevice.Which);
                    break;

                case SDL.EventType.GamepadButtonDown:
                case SDL.EventType.GamepadButtonUp:
                    SetButton(sdlEvent.GButton, (SDL.EventType)sdlEvent.Type == SDL.EventType.GamepadButtonDown);
                    break;

                case SDL.EventType.GamepadAxisMotion:
                    SetAxis(sdlEvent.GAxis.Which, (SDL.GamepadAxis)sdlEvent.GAxis.Axis, sdlEvent.GAxis.Value);
                    break;

                case SDL.EventType.GamepadRemapped:
                    UpdateGamepad(sdlEvent.GDevice.Which);
                    break;
            }
        }

        _state.Poll();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var gamepad in _gamepads.Values)
        {
            _backend.CloseGamepad(gamepad);
        }

        _gamepads.Clear();
        _controllers.Clear();
        _state.SetApplicationActive(false);

        if (_backendInitialized)
        {
            _backend.Quit();
            _backendInitialized = false;
        }

        IsAvailable = false;
        _disposed = true;
    }

    private void AddGamepad(uint controllerId)
    {
        if (_gamepads.ContainsKey(controllerId))
        {
            return;
        }

        var gamepad = _backend.OpenGamepad(controllerId);
        if (gamepad == 0)
        {
            _diagnostics?.Record(DiagnosticArea.Controller, DiagnosticAction.OpenController,
                DiagnosticOutcome.Failed, DiagnosticLevel.Warning);
            return;
        }

        _gamepads.Add(controllerId, gamepad);
        var info = _backend.GetInfo(controllerId, gamepad);
        _controllers.Add(controllerId, info);
        _state.Connect(info);
        _diagnostics?.Record(DiagnosticArea.Controller, DiagnosticAction.ControllerConnected, DiagnosticOutcome.Completed);
        SuppressHeldControls(controllerId, gamepad);
        ConnectionChanged?.Invoke(
            this,
            new ControllerConnectionEventArgs(controllerId, info.Name, true, info.Layout));
    }

    private void RemoveGamepad(uint controllerId)
    {
        if (!_gamepads.Remove(controllerId, out var gamepad))
        {
            return;
        }

        var info = _controllers[controllerId];
        _controllers.Remove(controllerId);
        _backend.CloseGamepad(gamepad);
        _state.Disconnect(controllerId);
        _diagnostics?.Record(DiagnosticArea.Controller, DiagnosticAction.ControllerDisconnected, DiagnosticOutcome.Completed);

        ConnectionChanged?.Invoke(
            this,
            new ControllerConnectionEventArgs(controllerId, info.Name, false, info.Layout));
    }

    private void UpdateGamepad(uint controllerId)
    {
        if (_gamepads.TryGetValue(controllerId, out var gamepad))
        {
            var info = _backend.GetInfo(controllerId, gamepad);
            _controllers[controllerId] = info;
            _state.Connect(info);
            SuppressHeldControls(controllerId, gamepad);
        }
    }

    private void SetButton(SDL.GamepadButtonEvent buttonEvent, bool isDown)
    {
        if (SdlGamepadButtonMapper.TryMap((SDL.GamepadButton)buttonEvent.Button, out var action))
        {
            _state.SetControl(buttonEvent.Which, buttonEvent.Button, isDown ? action : null);
        }
    }

    private void SetAxis(uint controllerId, SDL.GamepadAxis axis, short value, bool suppress = false)
    {
        if (axis is SDL.GamepadAxis.LeftX or SDL.GamepadAxis.LeftY)
        {
            _state.SetControl(
                controllerId,
                256 + (int)axis,
                SdlGamepadAxisMapper.TryMap(axis, value, out var action) ? action : null,
                suppress);
        }
    }

    private void SuppressHeldControls(uint controllerId, nint gamepad)
    {
        foreach (var button in MappedButtons)
        {
            SdlGamepadButtonMapper.TryMap(button, out var action);
            _state.SetControl(controllerId, (int)button, _backend.GetButton(gamepad, button) ? action : null, true);
        }

        foreach (var axis in MappedAxes)
        {
            SetAxis(controllerId, axis, _backend.GetAxis(gamepad, axis), true);
        }
    }
}
