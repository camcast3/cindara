using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Cindara.Desktop.Tests.Navigation.TestAppBuilder))]

namespace Cindara.Desktop.Tests.Navigation;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .UseSkia()
        .WithInterFont();

    public static Task Run(Action test) => HeadlessUnitTestSession.GetOrStartForAssembly(typeof(TestAppBuilder).Assembly)
        .Dispatch(test, CancellationToken.None);

    public static Task Run(Func<Task> test) => HeadlessUnitTestSession.GetOrStartForAssembly(typeof(TestAppBuilder).Assembly)
        .Dispatch<int>(async () =>
        {
            // Use the async overload so Avalonia pumps the dispatcher until the test finishes.
            await test();
            return 0;
        }, CancellationToken.None);
}
