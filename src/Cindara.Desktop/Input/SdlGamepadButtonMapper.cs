using SDL3;

namespace Cindara.Desktop.Input;

internal static class SdlGamepadButtonMapper
{
    public static bool TryMap(
        SDL.GamepadButton button,
        out ControllerAction action)
    {
        action = button switch
        {
            SDL.GamepadButton.DPadUp => ControllerAction.NavigateUp,
            SDL.GamepadButton.DPadDown => ControllerAction.NavigateDown,
            SDL.GamepadButton.DPadLeft => ControllerAction.NavigateLeft,
            SDL.GamepadButton.DPadRight => ControllerAction.NavigateRight,
            SDL.GamepadButton.South => ControllerAction.Accept,
            SDL.GamepadButton.East => ControllerAction.Back,
            SDL.GamepadButton.Start => ControllerAction.Menu,
            _ => default,
        };

        return button is SDL.GamepadButton.DPadUp
            or SDL.GamepadButton.DPadDown
            or SDL.GamepadButton.DPadLeft
            or SDL.GamepadButton.DPadRight
            or SDL.GamepadButton.South
            or SDL.GamepadButton.East
            or SDL.GamepadButton.Start;
    }
}
