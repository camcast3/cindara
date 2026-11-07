using System;
using Avalonia;
using Avalonia.Win32;
using Cindara.Core.Diagnostics;
using Cindara.Desktop.Startup;

namespace Cindara.Desktop;

sealed class Program
{
    private static StartupRecoveryStore? _startupRecovery;
    private static bool _startupCompleted;
    internal static LocalDiagnostics? Diagnostics { get; private set; }
    internal static bool SafeModeActive { get; private set; }
    internal static string? StartupRecoveryError { get; private set; }
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        var applicationData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cindara");
        Diagnostics = new LocalDiagnostics(Path.Combine(applicationData, "diagnostics"));
        _startupRecovery = new StartupRecoveryStore(Path.Combine(applicationData, "startup-recovery.json"));
        var recovery = _startupRecovery.Begin(args.Contains("--safe-mode", StringComparer.OrdinalIgnoreCase));
        SafeModeActive = recovery.UseSafeMode;
        StartupRecoveryError = recovery.Error;
        Diagnostics.Record(DiagnosticArea.Startup, DiagnosticAction.Start, DiagnosticOutcome.Started);
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            Diagnostics.Record(DiagnosticArea.Startup, DiagnosticAction.Stop, DiagnosticOutcome.Failed,
                DiagnosticLevel.Error, eventArgs.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            Diagnostics.Record(DiagnosticArea.Startup, DiagnosticAction.Stop, DiagnosticOutcome.Failed,
                DiagnosticLevel.Error, eventArgs.Exception);
            eventArgs.SetObserved();
        };
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

    internal static void MarkStartupSuccessful()
    {
        if (_startupCompleted)
        {
            return;
        }

        try
        {
            _startupRecovery?.Complete();
            _startupCompleted = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StartupRecoveryError = "Startup recovery state could not be completed.";
            Diagnostics?.Record(DiagnosticArea.Startup, DiagnosticAction.Start, DiagnosticOutcome.Failed,
                DiagnosticLevel.Warning, exception);
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseSkia()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont();

        if (SafeModeActive && OperatingSystem.IsWindows())
        {
            builder = builder.With(new Win32PlatformOptions
            {
                RenderingMode = [Win32RenderingMode.Software],
            });
        }

        return builder;
    }
}
