namespace Cindara.Desktop.Input;

public sealed class ControllerBindingRequestedEventArgs(ControllerAction action) : EventArgs
{
    public ControllerAction Action { get; } = action;
}
