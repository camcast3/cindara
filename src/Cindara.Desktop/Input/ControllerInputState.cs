namespace Cindara.Desktop.Input;

internal sealed class ControllerInputState(TimeProvider timeProvider)
{
    private static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(100);
    private readonly Dictionary<uint, ControllerInfo> _controllers = [];
    private readonly Dictionary<(uint ControllerId, int Control), HeldControl> _held = [];
    private bool _applicationActive;
    private long _sequence;

    public event EventHandler<ControllerActionEventArgs>? ActionPressed;

    public event EventHandler? ActiveControllerChanged;

    public ControllerInfo? ActiveController { get; private set; }

    public void Connect(ControllerInfo controller)
    {
        _controllers[controller.Id] = controller;
        if (ActiveController is null || ActiveController.Id == controller.Id)
        {
            SetActiveController(controller);
        }
    }

    public void Disconnect(uint controllerId)
    {
        _controllers.Remove(controllerId);
        foreach (var key in _held.Keys.Where(key => key.ControllerId == controllerId).ToArray())
        {
            _held.Remove(key);
        }

        if (ActiveController?.Id == controllerId)
        {
            SetActiveController(_controllers.Values.FirstOrDefault());
        }
    }

    public void SetApplicationActive(bool isActive)
    {
        _applicationActive = isActive;
        if (!isActive)
        {
            foreach (var control in _held.Values)
            {
                control.Suppressed = true;
            }
        }
    }

    public void SetControl(uint controllerId, int control, ControllerAction? action, bool suppress = false)
    {
        if (!_controllers.TryGetValue(controllerId, out var controller))
        {
            return;
        }

        var key = (controllerId, control);
        if (action is null)
        {
            _held.Remove(key);
            return;
        }

        var previous = _held.GetValueOrDefault(key);
        var suppressed = suppress || !_applicationActive || previous?.Suppressed == true;
        if (previous?.Action == action)
        {
            previous.Suppressed |= suppressed;
            return;
        }

        _held[key] = new HeldControl(action.Value, suppressed, ++_sequence, timeProvider.GetTimestamp());
        if (suppressed)
        {
            return;
        }

        SetActiveController(controller);
        ActionPressed?.Invoke(this, new ControllerActionEventArgs(controllerId, action.Value));
    }

    public void Poll()
    {
        if (!_applicationActive || ActiveController is null)
        {
            return;
        }

        var held = _held
            .Where(pair => pair.Key.ControllerId == ActiveController.Id
                && !pair.Value.Suppressed
                && IsDirectional(pair.Value.Action))
            .Select(pair => pair.Value)
            .MaxBy(control => control.Sequence);
        if (held is null)
        {
            return;
        }

        var now = timeProvider.GetTimestamp();
        if (timeProvider.GetElapsedTime(held.LastTimestamp, now)
            < (held.HasRepeated ? RepeatInterval : RepeatDelay))
        {
            return;
        }

        held.LastTimestamp = now;
        held.HasRepeated = true;
        ActionPressed?.Invoke(this, new ControllerActionEventArgs(ActiveController.Id, held.Action, true));
    }

    private void SetActiveController(ControllerInfo? controller)
    {
        if (ActiveController == controller)
        {
            return;
        }

        ActiveController = controller;
        ActiveControllerChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsDirectional(ControllerAction action) =>
        action is ControllerAction.NavigateUp or ControllerAction.NavigateDown
            or ControllerAction.NavigateLeft or ControllerAction.NavigateRight;

    private sealed class HeldControl(ControllerAction action, bool suppressed, long sequence, long timestamp)
    {
        public ControllerAction Action { get; } = action;
        public bool Suppressed { get; set; } = suppressed;
        public long Sequence { get; } = sequence;
        public long LastTimestamp { get; set; } = timestamp;
        public bool HasRepeated { get; set; }
    }
}
