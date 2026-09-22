using System.Reflection;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Cindara.Core.Authentication;
using Cindara.Core.Diagnostics;
using Cindara.Core.Jellyfin;
using Cindara.Desktop.Authentication;
using Cindara.Desktop.Input;
using Cindara.Desktop.Localization;
using Cindara.Desktop.ViewModels;
using Cindara.Desktop.Views;

namespace Cindara.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        var buildLocale = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "CindaraPseudoLocale")?.Value;
        Loc.Configure(Environment.GetEnvironmentVariable("CINDARA_CULTURE") ?? buildLocale);
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var diagnostics = Program.Diagnostics;
            var httpClient = new HttpClient(diagnostics is null ? new HttpClientHandler()
                : new DiagnosticHttpHandler(diagnostics, new HttpClientHandler()))
            {
                Timeout = TimeSpan.FromSeconds(15),
            };
            var applicationData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Cindara");
            var deviceId = DeviceIdentityProvider.GetOrCreate(
                Path.Combine(applicationData, "device-id"));
            var sessionStore = new PersistentSessionStore(
                Path.Combine(applicationData, "sessions.json"),
                new OsSecureCredentialStore(Path.Combine(applicationData, "credentials")));
            var clientIdentity = new JellyfinClientIdentity(
                "Cindara",
                Environment.MachineName,
                deviceId,
                Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0");
            var authenticationService = new JellyfinAuthenticationService(
                diagnostics is null ? sessionStore : new DiagnosticSessionStore(sessionStore, diagnostics),
                clientIdentity, diagnostics);
            var mediaPreviewClient = new JellyfinMediaPreviewClient(clientIdentity, diagnostics);
            var viewModel = new MainViewModel(
                new JellyfinServerClient(httpClient),
                authenticationService,
                mediaPreviewClient, diagnostics);
            desktop.Exit += (_, _) =>
            {
                viewModel.Dispose();
                mediaPreviewClient.Dispose();
                authenticationService.Dispose();
                httpClient.Dispose();
                sessionStore.Dispose();
            };

            desktop.MainWindow = new MainWindow(new SdlGamepadInputSource(diagnostics), diagnostics: diagnostics)
            {
                DataContext = viewModel,
            };
            diagnostics?.Record(DiagnosticArea.Startup, DiagnosticAction.Start, DiagnosticOutcome.Completed);
            diagnostics?.Record(DiagnosticArea.Playback, DiagnosticAction.PlaybackUnavailable,
                DiagnosticOutcome.Unavailable);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
