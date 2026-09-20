using Cindara.Core.Jellyfin;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cindara.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IJellyfinServerClient _serverClient;

    public MainViewModel(IJellyfinServerClient serverClient)
    {
        _serverClient = serverClient;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private string _serverAddress = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private bool _isConnecting;

    [ObservableProperty]
    private string _statusMessage = "Connect to your Jellyfin server to get started.";

    private bool CanConnect() => !IsConnecting && !string.IsNullOrWhiteSpace(ServerAddress);

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        IsConnecting = true;
        StatusMessage = "Checking server...";

        try
        {
            var server = await _serverClient.ConnectAsync(ServerAddress, cancellationToken);
            StatusMessage = $"Connected to {server.DisplayName} (Jellyfin {server.Version}).";
        }
        catch (ServerConnectionException exception)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsConnecting = false;
        }
    }
}
