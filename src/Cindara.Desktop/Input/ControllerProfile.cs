namespace Cindara.Desktop.Input;

public sealed record ControllerProfile
{
    public const int DefaultDeadZone = 16_000;
    public const int MinimumDeadZone = 4_000;
    public const int MaximumDeadZone = 30_000;

    public int DeadZone { get; init; } = DefaultDeadZone;

    public bool InvertHorizontalAxis { get; init; }

    public bool InvertVerticalAxis { get; init; }

    public ControllerButton NavigateUp { get; init; } = ControllerButton.DPadUp;

    public ControllerButton NavigateDown { get; init; } = ControllerButton.DPadDown;

    public ControllerButton NavigateLeft { get; init; } = ControllerButton.DPadLeft;

    public ControllerButton NavigateRight { get; init; } = ControllerButton.DPadRight;

    public ControllerButton Accept { get; init; } = ControllerButton.South;

    public ControllerButton Back { get; init; } = ControllerButton.East;

    public ControllerButton Menu { get; init; } = ControllerButton.Start;

    public ControllerProfile Validate()
    {
        if (DeadZone is < MinimumDeadZone or > MaximumDeadZone)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DeadZone),
                $"Controller dead zone must be between {MinimumDeadZone} and {MaximumDeadZone}.");
        }

        var buttons = new[]
        {
            NavigateUp, NavigateDown, NavigateLeft, NavigateRight, Accept, Back, Menu,
        };
        if (buttons.Distinct().Count() != buttons.Length)
        {
            throw new ArgumentException("Each controller action must use a distinct button.");
        }

        return this;
    }

    public bool TryMap(ControllerButton button, out ControllerAction action)
    {
        action = button switch
        {
            _ when button == NavigateUp => ControllerAction.NavigateUp,
            _ when button == NavigateDown => ControllerAction.NavigateDown,
            _ when button == NavigateLeft => ControllerAction.NavigateLeft,
            _ when button == NavigateRight => ControllerAction.NavigateRight,
            _ when button == Accept => ControllerAction.Accept,
            _ when button == Back => ControllerAction.Back,
            _ when button == Menu => ControllerAction.Menu,
            _ => default,
        };

        return button == NavigateUp || button == NavigateDown
            || button == NavigateLeft || button == NavigateRight
            || button == Accept || button == Back || button == Menu;
    }

    public ControllerButton GetBinding(ControllerAction action) => action switch
    {
        ControllerAction.NavigateUp => NavigateUp,
        ControllerAction.NavigateDown => NavigateDown,
        ControllerAction.NavigateLeft => NavigateLeft,
        ControllerAction.NavigateRight => NavigateRight,
        ControllerAction.Accept => Accept,
        ControllerAction.Back => Back,
        ControllerAction.Menu => Menu,
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    public ControllerProfile WithBinding(ControllerAction action, ControllerButton button) => action switch
    {
        ControllerAction.NavigateUp => this with { NavigateUp = button },
        ControllerAction.NavigateDown => this with { NavigateDown = button },
        ControllerAction.NavigateLeft => this with { NavigateLeft = button },
        ControllerAction.NavigateRight => this with { NavigateRight = button },
        ControllerAction.Accept => this with { Accept = button },
        ControllerAction.Back => this with { Back = button },
        ControllerAction.Menu => this with { Menu = button },
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    internal static string GetKey(ControllerInfo controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        var identity = controller.PersistentId is { Length: > 0 } persistent
            ? $"device:{persistent}" : $"connection:{controller.Id}";
        return $"{controller.Layout}:{controller.Name.Trim().ToUpperInvariant()}:{identity}";
    }
}
