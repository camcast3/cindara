using SDL3;

namespace Cindara.Desktop.Input;

public sealed class SdlGamepadInputSource : IControllerInputSource
{
    private readonly Dictionary<uint, nint> _gamepads = [];
    private readonly Dictionary<(uint ControllerId, SDL.GamepadAxis Axis), ControllerAction>
        _activeAxisActions = [];
    private bool _initialized;
    private bool _disposed;

    public event EventHandler<ControllerActionEventArgs>? ActionPressed;

    public event EventHandler<ControllerConnectionEventArgs>? ConnectionChanged;

    public bool IsAvailable { get; private set; }

    public string? InitializationError { get; private set; }

    public int ConnectedGamepads => _gamepads.Count;

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_initialized)
        {
            return;
        }

        _initialized = true;

        try
        {
            if (!SDL.Init(SDL.InitFlags.Gamepad))
            {
                InitializationError = $"SDL could not initialize gamepad input: {SDL.GetError()}";
                return;
            }

            IsAvailable = true;
            Poll();
        }
        catch (DllNotFoundException exception)
        {
            InitializationError = $"SDL gamepad runtime was not found: {exception.Message}";
        }
        catch (EntryPointNotFoundException exception)
        {
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

        while (SDL.PollEvent(out var sdlEvent))
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
                    RaiseAction(sdlEvent.GButton);
                    break;

                case SDL.EventType.GamepadAxisMotion:
                    RaiseAxisAction(sdlEvent.GAxis);
                    break;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var gamepad in _gamepads.Values)
        {
            SDL.CloseGamepad(gamepad);
        }

        _gamepads.Clear();
        _activeAxisActions.Clear();

        if (IsAvailable)
        {
            SDL.QuitSubSystem(SDL.InitFlags.Gamepad);
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

        var gamepad = SDL.OpenGamepad(controllerId);
        if (gamepad == 0)
        {
            return;
        }

        _gamepads.Add(controllerId, gamepad);
        var name = SDL.GetGamepadName(gamepad) ?? "Gamepad";
        ConnectionChanged?.Invoke(
            this,
            new ControllerConnectionEventArgs(controllerId, name, true));
    }

    private void RemoveGamepad(uint controllerId)
    {
        if (!_gamepads.Remove(controllerId, out var gamepad))
        {
            return;
        }

        var name = SDL.GetGamepadName(gamepad) ?? "Gamepad";
        SDL.CloseGamepad(gamepad);
        foreach (var key in _activeAxisActions.Keys
                     .Where(key => key.ControllerId == controllerId)
                     .ToArray())
        {
            _activeAxisActions.Remove(key);
        }

        ConnectionChanged?.Invoke(
            this,
            new ControllerConnectionEventArgs(controllerId, name, false));
    }

    private void RaiseAction(SDL.GamepadButtonEvent buttonEvent)
    {
        if (SdlGamepadButtonMapper.TryMap(
                (SDL.GamepadButton)buttonEvent.Button,
                out var action))
        {
            ActionPressed?.Invoke(
                this,
                new ControllerActionEventArgs(buttonEvent.Which, action));
        }
    }

    private void RaiseAxisAction(SDL.GamepadAxisEvent axisEvent)
    {
        var axis = (SDL.GamepadAxis)axisEvent.Axis;
        var key = (axisEvent.Which, axis);

        if (!SdlGamepadAxisMapper.TryMap(axis, axisEvent.Value, out var action))
        {
            _activeAxisActions.Remove(key);
            return;
        }

        if (_activeAxisActions.TryGetValue(key, out var activeAction)
            && activeAction == action)
        {
            return;
        }

        _activeAxisActions[key] = action;
        ActionPressed?.Invoke(
            this,
            new ControllerActionEventArgs(axisEvent.Which, action));
    }
}
