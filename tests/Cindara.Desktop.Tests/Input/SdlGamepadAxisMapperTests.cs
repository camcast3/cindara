using Cindara.Desktop.Input;
using SDL3;

namespace Cindara.Desktop.Tests.Input;

public sealed class SdlGamepadAxisMapperTests
{
    [Theory]
    [InlineData(SDL.GamepadAxis.LeftX, -20_000, ControllerAction.NavigateLeft)]
    [InlineData(SDL.GamepadAxis.LeftX, 20_000, ControllerAction.NavigateRight)]
    [InlineData(SDL.GamepadAxis.LeftY, -20_000, ControllerAction.NavigateUp)]
    [InlineData(SDL.GamepadAxis.LeftY, 20_000, ControllerAction.NavigateDown)]
    public void TryMapReturnsDirectionalAction(
        SDL.GamepadAxis axis,
        short value,
        ControllerAction expected)
    {
        var mapped = SdlGamepadAxisMapper.TryMap(axis, value, out var action);

        Assert.True(mapped);
        Assert.Equal(expected, action);
    }

    [Theory]
    [InlineData(SDL.GamepadAxis.LeftX, 0)]
    [InlineData(SDL.GamepadAxis.LeftY, 16_000)]
    [InlineData(SDL.GamepadAxis.RightX, 20_000)]
    public void TryMapRejectsDeadZoneAndUnassignedAxes(
        SDL.GamepadAxis axis,
        short value)
    {
        var mapped = SdlGamepadAxisMapper.TryMap(axis, value, out _);

        Assert.False(mapped);
    }
}
