namespace Cindara.Desktop.Input;

public enum ControllerLayout
{
    Generic,
    Xbox,
    PlayStation,
    Nintendo,
}

public sealed record ControllerInfo(uint Id, string Name, ControllerLayout Layout);
