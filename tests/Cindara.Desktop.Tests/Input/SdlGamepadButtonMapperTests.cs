using Cindara.Desktop.Input;
using SDL3;

namespace Cindara.Desktop.Tests.Input;

public sealed class SdlGamepadButtonMapperTests
{
    public static TheoryData<SDL.GamepadButton, ControllerAction> MappedButtons =>
        new()
        {
            { SDL.GamepadButton.DPadUp, ControllerAction.NavigateUp },
            { SDL.GamepadButton.DPadDown, ControllerAction.NavigateDown },
            { SDL.GamepadButton.DPadLeft, ControllerAction.NavigateLeft },
            { SDL.GamepadButton.DPadRight, ControllerAction.NavigateRight },
            { SDL.GamepadButton.South, ControllerAction.Accept },
            { SDL.GamepadButton.East, ControllerAction.Back },
            { SDL.GamepadButton.Start, ControllerAction.Menu },
        };

    [Theory]
    [MemberData(nameof(MappedButtons))]
    public void TryMapReturnsCouchNavigationAction(
        SDL.GamepadButton button,
        ControllerAction expected)
    {
        var mapped = SdlGamepadButtonMapper.TryMap(button, out var action);

        Assert.True(mapped);
        Assert.Equal(expected, action);
    }

    [Fact]
    public void TryMapRejectsUnassignedButton()
    {
        var mapped = SdlGamepadButtonMapper.TryMap(
            SDL.GamepadButton.LeftShoulder,
            out _);

        Assert.False(mapped);
    }
}
