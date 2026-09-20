using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Cindara.Core.Jellyfin;
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

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(new JellyfinServerClient(httpClient)),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
