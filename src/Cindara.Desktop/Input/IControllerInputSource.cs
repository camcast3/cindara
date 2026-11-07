namespace Cindara.Desktop.Input;

public interface IControllerInputSource : IDisposable
{
    event EventHandler<ControllerActionEventArgs>? ActionPressed;

    event EventHandler<ControllerConnectionEventArgs>? ConnectionChanged;

    bool IsAvailable { get; }

    string? InitializationError { get; }

    string? ConfigurationError => null;

    int ConnectedGamepads { get; }

    ControllerInfo? ActiveController => null;

    ControllerProfile ActiveProfile => new();

    event EventHandler? ActiveControllerChanged
    {
        add { }
        remove { }
    }

    // Call on both activation and deactivation; keep polling while inactive for hotplug.
    void SetApplicationActive(bool isActive) { }

    void Initialize();

    void Poll();

    void UpdateActiveProfile(ControllerProfile profile) { }

    void ResetActiveProfile() { }
}
