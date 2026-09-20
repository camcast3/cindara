using SDL3;

namespace Cindara.Desktop.Input;

internal static class SdlGamepadAxisMapper
{
    private const short DeadZone = 16_000;

    public static bool TryMap(
        SDL.GamepadAxis axis,
        short value,
        out ControllerAction action)
    {
        action = (axis, value) switch
        {
            (SDL.GamepadAxis.LeftX, < -DeadZone) => ControllerAction.NavigateLeft,
            (SDL.GamepadAxis.LeftX, > DeadZone) => ControllerAction.NavigateRight,
            (SDL.GamepadAxis.LeftY, < -DeadZone) => ControllerAction.NavigateUp,
            (SDL.GamepadAxis.LeftY, > DeadZone) => ControllerAction.NavigateDown,
            _ => default,
        };

        return axis is SDL.GamepadAxis.LeftX or SDL.GamepadAxis.LeftY
            && Math.Abs((int)value) > DeadZone;
    }
}
