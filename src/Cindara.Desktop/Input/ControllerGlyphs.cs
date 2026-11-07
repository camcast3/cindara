using Cindara.Desktop.Localization;

namespace Cindara.Desktop.Input;

public static class ControllerGlyphs
{
    public static string GetLabel(ControllerLayout layout, ControllerAction action) =>
        GetLabel(layout, action, new ControllerProfile());

    public static string GetLabel(
        ControllerLayout layout,
        ControllerAction action,
        ControllerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return GetButtonLabel(layout, profile.GetBinding(action));
    }

    public static string GetButtonLabel(ControllerLayout layout, ControllerButton button) =>
        (layout, button) switch
        {
            (_, ControllerButton.DPadUp) => "↑",
            (_, ControllerButton.DPadDown) => "↓",
            (_, ControllerButton.DPadLeft) => "←",
            (_, ControllerButton.DPadRight) => "→",
            (ControllerLayout.Xbox, ControllerButton.South) => "A",
            (ControllerLayout.Xbox, ControllerButton.East) => "B",
            (ControllerLayout.Xbox, ControllerButton.West) => "X",
            (ControllerLayout.Xbox, ControllerButton.North) => "Y",
            (ControllerLayout.PlayStation, ControllerButton.South) => Loc.Get("Controller.Cross"),
            (ControllerLayout.PlayStation, ControllerButton.East) => Loc.Get("Controller.Circle"),
            (ControllerLayout.PlayStation, ControllerButton.West) => Loc.Get("Controller.Square"),
            (ControllerLayout.PlayStation, ControllerButton.North) => Loc.Get("Controller.Triangle"),
            (ControllerLayout.Nintendo, ControllerButton.South) => "B",
            (ControllerLayout.Nintendo, ControllerButton.East) => "A",
            (ControllerLayout.Nintendo, ControllerButton.West) => "Y",
            (ControllerLayout.Nintendo, ControllerButton.North) => "X",
            (_, ControllerButton.South) => Loc.Get("Controller.South"),
            (_, ControllerButton.East) => Loc.Get("Controller.East"),
            (_, ControllerButton.West) => Loc.Get("Controller.West"),
            (_, ControllerButton.North) => Loc.Get("Controller.North"),
            (ControllerLayout.PlayStation, ControllerButton.Start) => Loc.Get("Controller.Options"),
            (ControllerLayout.Nintendo, ControllerButton.Start) => "+",
            (_, ControllerButton.Start) => Loc.Get("Controller.Start"),
            (_, ControllerButton.Back) => Loc.Get("Controller.Select"),
            (_, ControllerButton.LeftStick) => "L3",
            (_, ControllerButton.RightStick) => "R3",
            (_, ControllerButton.LeftShoulder) => "LB",
            (_, ControllerButton.RightShoulder) => "RB",
            _ => throw new ArgumentOutOfRangeException(nameof(button)),
        };
}
