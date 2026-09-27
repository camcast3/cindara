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

    [Fact]
    public async Task AsyncUiSessionKeepsPumpingUntilDelayedAssertionsFinish()
    {
        Avalonia.Application? completedApplication = null;
        var run = TestAppBuilder.Run(async () =>
        {
            completedApplication = Avalonia.Application.Current;
            await Task.Delay(50);
            Assert.True(Avalonia.Threading.Dispatcher.UIThread.CheckAccess());
            Assert.Same(completedApplication, Avalonia.Application.Current);
            throw new InvalidOperationException("The UI session must outlive asynchronous work.");
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        await TestAppBuilder.Run(() => Assert.NotSame(completedApplication, Avalonia.Application.Current));
    }
}
