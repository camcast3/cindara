namespace Cindara.Desktop.Input;

public sealed class ControllerConnectionEventArgs(
    uint controllerId,
    string controllerName,
    bool isConnected,
    ControllerLayout layout = ControllerLayout.Generic) : EventArgs
{
    public uint ControllerId { get; } = controllerId;

    public string ControllerName { get; } = controllerName;

    public bool IsConnected { get; } = isConnected;

    public ControllerLayout Layout { get; } = layout;
}
