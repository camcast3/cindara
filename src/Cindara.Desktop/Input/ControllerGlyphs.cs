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
            (ControllerLayout.PlayStation, ControllerAction.Accept) => "Cross",
            (ControllerLayout.PlayStation, ControllerAction.Back) => "Circle",
            (ControllerLayout.PlayStation, ControllerAction.Menu) => "Options",
            (ControllerLayout.Nintendo, ControllerAction.Accept) => "B",
            (ControllerLayout.Nintendo, ControllerAction.Back) => "A",
            (ControllerLayout.Nintendo, ControllerAction.Menu) => "+",
            (_, ControllerAction.Accept) => "South",
            (_, ControllerAction.Back) => "East",
            (_, ControllerAction.Menu) => "Start",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
}
