using SDL3;

namespace Cindara.Desktop.Input;

internal static class SdlGamepadButtonMapper
{
    public static bool TryMap(
        SDL.GamepadButton button,
        out ControllerAction action)
    {
        return TryMap(button, new ControllerProfile(), out action);
    }

    public static bool TryMap(
        SDL.GamepadButton button,
        ControllerProfile profile,
        out ControllerAction action)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!TryConvert(button, out var controllerButton))
        {
            action = default;
            return false;
        }

        return profile.TryMap(controllerButton, out action);
    }

    private static bool TryConvert(SDL.GamepadButton button, out ControllerButton controllerButton)
    {
        controllerButton = button switch
        {
            SDL.GamepadButton.South => ControllerButton.South,
            SDL.GamepadButton.East => ControllerButton.East,
            SDL.GamepadButton.West => ControllerButton.West,
            SDL.GamepadButton.North => ControllerButton.North,
            SDL.GamepadButton.Back => ControllerButton.Back,
            SDL.GamepadButton.Start => ControllerButton.Start,
            SDL.GamepadButton.LeftStick => ControllerButton.LeftStick,
            SDL.GamepadButton.RightStick => ControllerButton.RightStick,
            SDL.GamepadButton.LeftShoulder => ControllerButton.LeftShoulder,
            SDL.GamepadButton.RightShoulder => ControllerButton.RightShoulder,
            SDL.GamepadButton.DPadUp => ControllerButton.DPadUp,
            SDL.GamepadButton.DPadDown => ControllerButton.DPadDown,
            SDL.GamepadButton.DPadLeft => ControllerButton.DPadLeft,
            SDL.GamepadButton.DPadRight => ControllerButton.DPadRight,
            _ => default,
        };

        return button is SDL.GamepadButton.South or SDL.GamepadButton.East
            or SDL.GamepadButton.West or SDL.GamepadButton.North
            or SDL.GamepadButton.Back or SDL.GamepadButton.Start
            or SDL.GamepadButton.LeftStick or SDL.GamepadButton.RightStick
            or SDL.GamepadButton.LeftShoulder or SDL.GamepadButton.RightShoulder
            or SDL.GamepadButton.DPadUp or SDL.GamepadButton.DPadDown
            or SDL.GamepadButton.DPadLeft or SDL.GamepadButton.DPadRight;
    }
}
