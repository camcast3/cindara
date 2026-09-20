namespace Cindara.Desktop.Input;

public sealed class ControllerActionEventArgs(
    uint controllerId,
    ControllerAction action) : EventArgs
{
    public uint ControllerId { get; } = controllerId;

    public ControllerAction Action { get; } = action;
}
