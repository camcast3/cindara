using SDL3;

namespace Cindara.Desktop.Input;

internal static class SdlGamepadAxisMapper
{
    public static bool TryMap(
        SDL.GamepadAxis axis,
        short value,
        out ControllerAction action)
        => TryMap(axis, value, new ControllerProfile(), out action);

    public static bool TryMap(
        SDL.GamepadAxis axis,
        short value,
        ControllerProfile profile,
        out ControllerAction action)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var adjustedValue = axis switch
        {
            SDL.GamepadAxis.LeftX when profile.InvertHorizontalAxis => -value,
            SDL.GamepadAxis.LeftY when profile.InvertVerticalAxis => -value,
            _ => value,
        };
        action = (axis, adjustedValue) switch
        {
            (SDL.GamepadAxis.LeftX, var axisValue) when axisValue < -profile.DeadZone
                => ControllerAction.NavigateLeft,
            (SDL.GamepadAxis.LeftX, var axisValue) when axisValue > profile.DeadZone
                => ControllerAction.NavigateRight,
            (SDL.GamepadAxis.LeftY, var axisValue) when axisValue < -profile.DeadZone
                => ControllerAction.NavigateUp,
            (SDL.GamepadAxis.LeftY, var axisValue) when axisValue > profile.DeadZone
                => ControllerAction.NavigateDown,
            _ => default,
        };

        return axis is SDL.GamepadAxis.LeftX or SDL.GamepadAxis.LeftY
            && Math.Abs((int)adjustedValue) > profile.DeadZone;
    }
}
