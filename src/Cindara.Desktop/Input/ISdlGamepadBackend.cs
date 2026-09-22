using SDL3;

namespace Cindara.Desktop.Input;

internal interface ISdlGamepadBackend
{
    bool Initialize();
    string GetError();
    uint[] GetGamepads();
    bool PollEvent(out SDL.Event sdlEvent);
    nint OpenGamepad(uint controllerId);
    void CloseGamepad(nint gamepad);
    ControllerInfo GetInfo(uint controllerId, nint gamepad);
    bool GetButton(nint gamepad, SDL.GamepadButton button);
    short GetAxis(nint gamepad, SDL.GamepadAxis axis);
    void Quit();
}
