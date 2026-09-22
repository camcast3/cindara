using System;
using Avalonia;
using Cindara.Core.Diagnostics;

namespace Cindara.Desktop;

sealed class Program
{
    internal static LocalDiagnostics? Diagnostics { get; private set; }
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        Diagnostics = new LocalDiagnostics(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cindara", "diagnostics"));
        Diagnostics.Record(DiagnosticArea.Startup, DiagnosticAction.Start, DiagnosticOutcome.Started);
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            Diagnostics.Record(DiagnosticArea.Startup, DiagnosticAction.Stop, DiagnosticOutcome.Failed,
                DiagnosticLevel.Error, eventArgs.ExceptionObject as Exception);
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            Diagnostics.Record(DiagnosticArea.Startup, DiagnosticAction.Stop, DiagnosticOutcome.Completed);
        }
        catch (Exception exception)
        {
            Diagnostics.Record(DiagnosticArea.Startup, DiagnosticAction.Start, DiagnosticOutcome.Failed,
                DiagnosticLevel.Error, exception);
            // Do not let the runtime print unsanitized exception text to redirected output.
            Console.Error.WriteLine("Cindara could not start or continue. See local diagnostics.");
            Environment.ExitCode = 1;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseSkia()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont();
}
