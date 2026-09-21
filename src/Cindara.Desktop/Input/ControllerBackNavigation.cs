namespace Cindara.Desktop.Input;

internal enum ControllerBackDestination
{
    None,
    Account,
    Windowed,
}

internal static class ControllerBackNavigation
{
    public static ControllerBackDestination Resolve(bool galleryVisible, bool fullscreen) =>
        galleryVisible
            ? ControllerBackDestination.Account
            : fullscreen
                ? ControllerBackDestination.Windowed
                : ControllerBackDestination.None;
}
