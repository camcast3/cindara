using System.Collections.ObjectModel;
using Cindara.Core.Authentication;
using Cindara.Core.Jellyfin;
using Cindara.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cindara.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IJellyfinServerClient _serverClient;
    private readonly IAuthenticationService _authenticationService;
    private AuthenticatedSession? _currentSession;

    public MainViewModel(
        IJellyfinServerClient serverClient,
        IAuthenticationService authenticationService)
    {
        _serverClient = serverClient;
        _authenticationService = authenticationService;
    }

    public ObservableCollection<SessionProfile> SavedSessions { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private string _serverAddress = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    [NotifyCanExecuteChangedFor(nameof(UseSavedSessionCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSavedSessionCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddServerCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackToSessionsCommand))]
    [NotifyCanExecuteChangedFor(nameof(LogoutCommand))]
    [NotifyCanExecuteChangedFor(nameof(InitializeCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    private string _username = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    private string _password = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UseSavedSessionCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSavedSessionCommand))]
    private SessionProfile? _selectedSavedSession;

    [ObservableProperty]
    private string _statusMessage = "Loading saved Jellyfin sessions...";

    [ObservableProperty]
    private string _controllerStatus = "Initializing controller input...";

    [ObservableProperty]
    private bool _isServerEntryVisible;

    [ObservableProperty]
    private bool _isSignInVisible;

    [ObservableProperty]
    private bool _areSavedSessionsVisible;

    [ObservableProperty]
    private bool _isAuthenticatedVisible;

    [ObservableProperty]
    private string _authenticatedAccount = string.Empty;

    public void SetControllerStatus(string status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        ControllerStatus = status;
    }

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            await RefreshSavedSessionsAsync(cancellationToken);
            ShowSavedSessionsOrServerEntry();
        }
        catch (AuthenticationException exception)
        {
            ShowServerEntry();
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanConnect() => !IsBusy && !string.IsNullOrWhiteSpace(ServerAddress);

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        StatusMessage = "Checking server...";

        try
        {
            var server = await _serverClient.ConnectAsync(ServerAddress, cancellationToken);
            CurrentServer = server;
            Username = string.Empty;
            Password = string.Empty;
            ShowSignIn();
            StatusMessage = $"Connected to {server.DisplayName}. Sign in with your Jellyfin account.";
        }
        catch (ServerConnectionException exception)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private ServerIdentity? CurrentServer { get; set; }

    private bool CanSignIn() =>
        !IsBusy
        && CurrentServer is not null
        && !string.IsNullOrWhiteSpace(Username);

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync(CancellationToken cancellationToken)
    {
        if (CurrentServer is null)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Signing in...";
        try
        {
            _currentSession = await _authenticationService.AuthenticateAsync(
                new AuthenticationRequest(CurrentServer, Username, Password),
                cancellationToken);
            Password = string.Empty;
            await RefreshSavedSessionsAsync(cancellationToken);
            ShowAuthenticated(_currentSession);
        }
        catch (AuthenticationException exception)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            Password = string.Empty;
            IsBusy = false;
        }
    }

    private bool CanUseSavedSession() => !IsBusy && SelectedSavedSession is not null;

    [RelayCommand(CanExecute = nameof(CanUseSavedSession))]
    private async Task UseSavedSessionAsync(CancellationToken cancellationToken)
    {
        if (SelectedSavedSession is not { } profile)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Checking saved session...";
        try
        {
            _currentSession = await _authenticationService.RestoreAsync(profile, cancellationToken);
            ShowAuthenticated(_currentSession);
        }
        catch (AuthenticationException exception)
        {
            StatusMessage = exception.Message;
            if (exception.Error == AuthenticationError.RevokedSession)
            {
                _currentSession = null;
                SavedSessions.Remove(profile);
                SelectedSavedSession = SavedSessions.FirstOrDefault();
                CurrentServer = profile.Server;
                ServerAddress = profile.Server.BaseUri.ToString();
                Username = profile.Username;
                Password = string.Empty;
                ShowSignIn();
            }

            try
            {
                await RefreshSavedSessionsAsync(CancellationToken.None);
            }
            catch (AuthenticationException refreshException)
            {
                StatusMessage = $"{exception.Message} {refreshException.Message}";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRemoveSavedSession() => !IsBusy && SelectedSavedSession is not null;

    [RelayCommand(CanExecute = nameof(CanRemoveSavedSession))]
    private async Task RemoveSavedSessionAsync(CancellationToken cancellationToken)
    {
        if (SelectedSavedSession is not { } profile)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _authenticationService.RemoveAsync(profile, cancellationToken);
            await RefreshSavedSessionsAsync(cancellationToken);
            StatusMessage = $"Removed {profile.DisplayName}.";
            ShowSavedSessionsOrServerEntry(false);
        }
        catch (AuthenticationException exception)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private async Task LogoutAsync(CancellationToken cancellationToken)
    {
        if (_currentSession is null)
        {
            return;
        }

        IsBusy = true;
        var profile = _currentSession.Profile;
        try
        {
            await _authenticationService.LogoutAsync(_currentSession, cancellationToken);
            StatusMessage = $"Signed out of {profile.DisplayName}.";
        }
        catch (AuthenticationException exception)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            _currentSession = null;
            try
            {
                await RefreshSavedSessionsAsync(CancellationToken.None);
                ShowSavedSessionsOrServerEntry(false);
            }
            catch (AuthenticationException exception)
            {
                ShowServerEntry();
                StatusMessage = exception.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    private bool CanNavigate() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private void AddServer()
    {
        CurrentServer = null;
        ServerAddress = string.Empty;
        ShowServerEntry();
        StatusMessage = "Enter the address of the Jellyfin server you want to add.";
    }

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private void BackToSessions()
    {
        Password = string.Empty;
        ShowSavedSessionsOrServerEntry();
    }

    private async Task RefreshSavedSessionsAsync(CancellationToken cancellationToken)
    {
        var profiles = await _authenticationService.GetSavedSessionsAsync(cancellationToken);
        SavedSessions.Clear();
        foreach (var profile in profiles)
        {
            SavedSessions.Add(profile);
        }

        SelectedSavedSession = SavedSessions.FirstOrDefault();
    }

    private void ShowSavedSessionsOrServerEntry(bool updateStatus = true)
    {
        if (SavedSessions.Count == 0)
        {
            ShowServerEntry();
            if (updateStatus)
            {
                StatusMessage = "Connect to your Jellyfin server to get started.";
            }
        }
        else
        {
            SetVisibleState(savedSessions: true);
            if (updateStatus)
            {
                StatusMessage = "Choose a saved Jellyfin account or add another server.";
            }
        }
    }

    private void ShowServerEntry() => SetVisibleState(serverEntry: true);

    private void ShowSignIn() => SetVisibleState(signIn: true);

    private void ShowAuthenticated(AuthenticatedSession session)
    {
        AuthenticatedAccount = session.Profile.DisplayName;
        StatusMessage = $"Signed in to {session.Server.DisplayName}.";
        SetVisibleState(authenticated: true);
    }

    private void SetVisibleState(
        bool serverEntry = false,
        bool signIn = false,
        bool savedSessions = false,
        bool authenticated = false)
    {
        IsServerEntryVisible = serverEntry;
        IsSignInVisible = signIn;
        AreSavedSessionsVisible = savedSessions;
        IsAuthenticatedVisible = authenticated;
    }
}
