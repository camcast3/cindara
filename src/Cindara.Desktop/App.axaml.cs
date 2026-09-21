using System.Reflection;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Cindara.Core.Authentication;
using Cindara.Core.Jellyfin;
using Cindara.Desktop.Authentication;
using Cindara.Desktop.Input;
using Cindara.Desktop.ViewModels;
using Cindara.Desktop.Views;

namespace Cindara.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var httpClient = new HttpClient
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
                sessionStore,
                clientIdentity);
            var mediaPreviewClient = new JellyfinMediaPreviewClient(clientIdentity);
            desktop.Exit += (_, _) =>
            {
                mediaPreviewClient.Dispose();
                authenticationService.Dispose();
                httpClient.Dispose();
            };

            desktop.MainWindow = new MainWindow(new SdlGamepadInputSource())
            {
                DataContext = new MainViewModel(
                    new JellyfinServerClient(httpClient),
                    authenticationService,
                    mediaPreviewClient),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
