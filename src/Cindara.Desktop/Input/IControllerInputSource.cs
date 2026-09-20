namespace Cindara.Desktop.Input;

public interface IControllerInputSource : IDisposable
{
    event EventHandler<ControllerActionEventArgs>? ActionPressed;

    event EventHandler<ControllerConnectionEventArgs>? ConnectionChanged;

    bool IsAvailable { get; }

    string? InitializationError { get; }

    int ConnectedGamepads { get; }

    void Initialize();

    void Poll();
}
