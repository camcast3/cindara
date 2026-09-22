using Cindara.Desktop.Localization;

namespace Cindara.Desktop.Input;

public static class ControllerGlyphs
{
    public static string GetLabel(ControllerLayout layout, ControllerAction action) =>
        (layout, action) switch
        {
            (_, ControllerAction.NavigateUp) => "↑",
            (_, ControllerAction.NavigateDown) => "↓",
            (_, ControllerAction.NavigateLeft) => "←",
            (_, ControllerAction.NavigateRight) => "→",
            (ControllerLayout.Xbox, ControllerAction.Accept) => "A",
            (ControllerLayout.Xbox, ControllerAction.Back) => "B",
            (ControllerLayout.PlayStation, ControllerAction.Accept) => Loc.Get("Controller.Cross"),
            (ControllerLayout.PlayStation, ControllerAction.Back) => Loc.Get("Controller.Circle"),
            (ControllerLayout.PlayStation, ControllerAction.Menu) => Loc.Get("Controller.Options"),
            (ControllerLayout.Nintendo, ControllerAction.Accept) => "B",
            (ControllerLayout.Nintendo, ControllerAction.Back) => "A",
            (ControllerLayout.Nintendo, ControllerAction.Menu) => "+",
            (_, ControllerAction.Accept) => Loc.Get("Controller.South"),
            (_, ControllerAction.Back) => Loc.Get("Controller.East"),
            (_, ControllerAction.Menu) => Loc.Get("Controller.Start"),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
}
