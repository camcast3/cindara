using SDL3;

namespace Cindara.Desktop.Input;

internal sealed class SdlGamepadBackend : ISdlGamepadBackend
{
    public bool Initialize() => SDL.Init(SDL.InitFlags.Gamepad);

    public string GetError() => SDL.GetError();

    public uint[] GetGamepads() => SDL.GetGamepads(out _) ?? [];

    public bool PollEvent(out SDL.Event sdlEvent) => SDL.PollEvent(out sdlEvent);

    public nint OpenGamepad(uint controllerId) => SDL.OpenGamepad(controllerId);

    public void CloseGamepad(nint gamepad) => SDL.CloseGamepad(gamepad);

    public ControllerInfo GetInfo(uint controllerId, nint gamepad) =>
        new(controllerId, SDL.GetGamepadName(gamepad) ?? "Gamepad", GetLayout(SDL.GetGamepadType(gamepad)));

    public bool GetButton(nint gamepad, SDL.GamepadButton button) => SDL.GetGamepadButton(gamepad, button);

    public short GetAxis(nint gamepad, SDL.GamepadAxis axis) => SDL.GetGamepadAxis(gamepad, axis);

    public void Quit() => SDL.QuitSubSystem(SDL.InitFlags.Gamepad);

    internal static ControllerLayout GetLayout(SDL.GamepadType type) => type switch
    {
        SDL.GamepadType.Xbox360 or SDL.GamepadType.XboxOne => ControllerLayout.Xbox,
        SDL.GamepadType.PS3 or SDL.GamepadType.PS4 or SDL.GamepadType.PS5 => ControllerLayout.PlayStation,
        SDL.GamepadType.NintendoSwitchPro or SDL.GamepadType.NintendoSwitchJoyconLeft
            or SDL.GamepadType.NintendoSwitchJoyconRight or SDL.GamepadType.NintendoSwitchJoyconPair
            => ControllerLayout.Nintendo,
        _ => ControllerLayout.Generic,
    };
}
