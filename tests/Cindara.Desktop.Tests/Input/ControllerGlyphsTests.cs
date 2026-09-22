using Cindara.Desktop.Input;
using Cindara.Desktop.Tests.Localization;
using SDL3;

namespace Cindara.Desktop.Tests.Input;

[Collection(LocalizationTestGroup.Name)]
public sealed class ControllerGlyphsTests
{
    [Theory]
    [InlineData("fr", "Cross", "Circle")]
    [InlineData("fr-CA", "Cross", "Circle")]
    public void UnsupportedLanguagesKeepEnglishButtonNamesAndPhysicalSymbols(string culture, string accept, string back)
    {
        using var scope = new CultureScope(culture);

        Assert.Equal(accept, ControllerGlyphs.GetLabel(ControllerLayout.PlayStation, ControllerAction.Accept));
        Assert.Equal(back, ControllerGlyphs.GetLabel(ControllerLayout.PlayStation, ControllerAction.Back));
        Assert.Equal("A", ControllerGlyphs.GetLabel(ControllerLayout.Xbox, ControllerAction.Accept));
        Assert.Equal("+", ControllerGlyphs.GetLabel(ControllerLayout.Nintendo, ControllerAction.Menu));
        Assert.Equal("↑", ControllerGlyphs.GetLabel(ControllerLayout.Generic, ControllerAction.NavigateUp));
    }

    [Theory]
    [InlineData(ControllerLayout.Xbox, "A", "B", "Start")]
    [InlineData(ControllerLayout.PlayStation, "Cross", "Circle", "Options")]
    [InlineData(ControllerLayout.Nintendo, "B", "A", "+")]
    [InlineData(ControllerLayout.Generic, "South", "East", "Start")]
    public void PromptsMatchPhysicalButtonActions(
        ControllerLayout layout, string accept, string back, string menu)
    {
        Assert.True(SdlGamepadButtonMapper.TryMap(SDL.GamepadButton.South, out var acceptAction));
        Assert.True(SdlGamepadButtonMapper.TryMap(SDL.GamepadButton.East, out var backAction));
        Assert.True(SdlGamepadButtonMapper.TryMap(SDL.GamepadButton.Start, out var menuAction));
        Assert.Equal(accept, ControllerGlyphs.GetLabel(layout, acceptAction));
        Assert.Equal(back, ControllerGlyphs.GetLabel(layout, backAction));
        Assert.Equal(menu, ControllerGlyphs.GetLabel(layout, menuAction));
    }

    [Theory]
    [InlineData(SDL.GamepadType.Unknown, ControllerLayout.Generic)]
    [InlineData(SDL.GamepadType.Standard, ControllerLayout.Generic)]
    [InlineData(SDL.GamepadType.GameCube, ControllerLayout.Generic)]
    [InlineData(SDL.GamepadType.Xbox360, ControllerLayout.Xbox)]
    [InlineData(SDL.GamepadType.XboxOne, ControllerLayout.Xbox)]
    [InlineData(SDL.GamepadType.PS3, ControllerLayout.PlayStation)]
    [InlineData(SDL.GamepadType.PS4, ControllerLayout.PlayStation)]
    [InlineData(SDL.GamepadType.PS5, ControllerLayout.PlayStation)]
    [InlineData(SDL.GamepadType.NintendoSwitchPro, ControllerLayout.Nintendo)]
    [InlineData(SDL.GamepadType.NintendoSwitchJoyconLeft, ControllerLayout.Nintendo)]
    [InlineData(SDL.GamepadType.NintendoSwitchJoyconRight, ControllerLayout.Nintendo)]
    [InlineData(SDL.GamepadType.NintendoSwitchJoyconPair, ControllerLayout.Nintendo)]
    public void LayoutUsesSdlMetadataInsteadOfDeviceNames(SDL.GamepadType type, ControllerLayout layout) =>
        Assert.Equal(layout, SdlGamepadBackend.GetLayout(type));

    [Theory]
    [InlineData(ControllerAction.NavigateUp, "↑")]
    [InlineData(ControllerAction.NavigateDown, "↓")]
    [InlineData(ControllerAction.NavigateLeft, "←")]
    [InlineData(ControllerAction.NavigateRight, "→")]
    public void DirectionPromptsAreLayoutIndependent(ControllerAction action, string label)
    {
        foreach (var layout in Enum.GetValues<ControllerLayout>())
        {
            Assert.Equal(label, ControllerGlyphs.GetLabel(layout, action));
        }
    }
}
