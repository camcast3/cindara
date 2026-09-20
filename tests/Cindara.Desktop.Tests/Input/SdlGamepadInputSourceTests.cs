using Cindara.Desktop.Input;

namespace Cindara.Desktop.Tests.Input;

public sealed class SdlGamepadInputSourceTests
{
    [Fact]
    public void InitializeLoadsBundledNativeRuntime()
    {
        using var input = new SdlGamepadInputSource();

        input.Initialize();

        Assert.True(input.IsAvailable, input.InitializationError);
    }
}
