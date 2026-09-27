namespace Cindara.Desktop.Tests.Navigation;

public sealed class TestAppBuilderTests
{
    [Fact]
    public async Task AsyncUiAssertionsAreAwaited()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => TestAppBuilder.Run(async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("An asynchronous UI assertion must reach the test runner.");
        }));
    }
}
