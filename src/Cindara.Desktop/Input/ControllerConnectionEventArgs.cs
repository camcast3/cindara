namespace Cindara.Desktop.Input;

public sealed class ControllerConnectionEventArgs(
    uint controllerId,
    string controllerName,
    bool isConnected) : EventArgs
{
    public uint ControllerId { get; } = controllerId;

    public string ControllerName { get; } = controllerName;

    public bool IsConnected { get; } = isConnected;
}
