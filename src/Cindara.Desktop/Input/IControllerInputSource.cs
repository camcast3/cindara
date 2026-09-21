namespace Cindara.Desktop.Input;

public interface IControllerInputSource : IDisposable
{
    event EventHandler<ControllerActionEventArgs>? ActionPressed;

    event EventHandler<ControllerConnectionEventArgs>? ConnectionChanged;

    bool IsAvailable { get; }

    string? InitializationError { get; }

    int ConnectedGamepads { get; }

    ControllerInfo? ActiveController => null;

    event EventHandler? ActiveControllerChanged
    {
        add { }
        remove { }
    }

    // Call on both activation and deactivation; keep polling while inactive for hotplug.
    void SetApplicationActive(bool isActive) { }

    void Initialize();

    void Poll();
}
