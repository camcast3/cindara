namespace Cindara.Desktop.Input;

public sealed class ControllerActionEventArgs(
    uint controllerId,
    ControllerAction action,
    bool isRepeat = false) : EventArgs
{
    public uint ControllerId { get; } = controllerId;

    public ControllerAction Action { get; } = action;

    public bool IsRepeat { get; } = isRepeat;
}
