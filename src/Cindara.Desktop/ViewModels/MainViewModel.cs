using System.Collections.ObjectModel;
using Cindara.Core.Authentication;
using Cindara.Core.Diagnostics;
using Cindara.Core.Jellyfin;
using Cindara.Core.Models;
using Cindara.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cindara.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly IJellyfinServerClient _serverClient;
    private readonly IAuthenticationService _authenticationService;
    private readonly IJellyfinMediaPreviewClient? _mediaPreviewClient;
    private readonly Func<MediaPreviewHome, DesignGalleryViewModel> _createGallery;
    private readonly LocalDiagnostics? _diagnostics;
    private AuthenticatedSession? _currentSession;
    private bool _disposed;

    public MainViewModel(
        IJellyfinServerClient serverClient,
        IAuthenticationService authenticationService,
        IJellyfinMediaPreviewClient? mediaPreviewClient = null,
        LocalDiagnostics? diagnostics = null)
        : this(serverClient, authenticationService, mediaPreviewClient, DesignGalleryViewModel.Create, diagnostics)
    {
    }

    internal MainViewModel(
        IJellyfinServerClient serverClient,
        IAuthenticationService authenticationService,
        IJellyfinMediaPreviewClient? mediaPreviewClient,
        Func<MediaPreviewHome, DesignGalleryViewModel> createGallery,
        LocalDiagnostics? diagnostics = null)
    {
        _serverClient = serverClient;
        _authenticationService = authenticationService;
        _mediaPreviewClient = mediaPreviewClient;
        _createGallery = createGallery;
        _diagnostics = diagnostics;
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
    [NotifyCanExecuteChangedFor(nameof(ShowDesignGalleryCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenHomeCommand))]
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
    [NotifyPropertyChangedFor(nameof(SelectedSavedSessionDisplayName))]
    private SessionProfile? _selectedSavedSession;

    public string SelectedSavedSessionDisplayName => SelectedSavedSession is { } profile
        ? LocaleFormat.SessionDisplayName(profile) : string.Empty;

    [ObservableProperty]
    private string _statusMessage = Loc.Get("Status.LoadingSessions");

    [ObservableProperty]
    private string _controllerStatus = Loc.Get("Status.InitializingController");

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

    [ObservableProperty]
    private bool _isDesignGalleryVisible;

    [ObservableProperty]
    private DesignGalleryViewModel? _designGallery;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLibraryBrowser))]
    private LibraryBrowserViewModel? _libraryBrowser;

    public bool HasLibraryBrowser => LibraryBrowser is not null;

    public void SetControllerStatus(string status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        ControllerStatus = status;
    }

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        using var operation = _diagnostics?.Begin(DiagnosticArea.Storage, DiagnosticAction.LoadSessions);
        IsBusy = true;
        try
        {
            await RefreshSavedSessionsAsync(cancellationToken);
            ShowSavedSessionsOrServerEntry();
            operation?.Complete();
        }
        catch (AuthenticationException exception)
        {
            operation?.Fail(exception);
            ShowServerEntry();
            StatusMessage = LocalizedErrors.Get(exception);
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
        using var operation = _diagnostics?.Begin(DiagnosticArea.Network, DiagnosticAction.Connect);
        IsBusy = true;
        StatusMessage = Loc.Get("Status.CheckingServer");

        try
        {
            var server = await _serverClient.ConnectAsync(ServerAddress, cancellationToken);
            CurrentServer = server;
            Username = string.Empty;
            Password = string.Empty;
            ShowSignIn();
            StatusMessage = Loc.Format("Status.Connected", server.DisplayName);
            operation?.Complete();
        }
        catch (ServerConnectionException exception)
        {
            operation?.Fail(exception);
            StatusMessage = LocalizedErrors.Get(exception);
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
        StatusMessage = Loc.Get("Status.SigningIn");
        using var operation = _diagnostics?.Begin(DiagnosticArea.Authentication, DiagnosticAction.SignIn);
        try
        {
            _currentSession = await _authenticationService.AuthenticateAsync(
                new AuthenticationRequest(CurrentServer, Username.Trim(), Password),
                cancellationToken);
            Password = string.Empty;
            await RefreshSavedSessionsAsync(cancellationToken);
            ShowAuthenticated(_currentSession);
            operation?.Complete();
        }
        catch (AuthenticationException exception)
        {
            operation?.Fail(exception);
            StatusMessage = LocalizedErrors.Get(exception);
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
        StatusMessage = Loc.Get("Status.CheckingSession");
        using var operation = _diagnostics?.Begin(DiagnosticArea.Authentication, DiagnosticAction.RestoreSession);
        try
        {
            _currentSession = await _authenticationService.RestoreAsync(profile, cancellationToken);
            ShowAuthenticated(_currentSession);
            operation?.Complete();
        }
        catch (AuthenticationException exception)
        {
            operation?.Fail(exception);
            StatusMessage = LocalizedErrors.Get(exception);
            if (exception.Error == AuthenticationError.RevokedSession)
            {
                PrepareForReauthentication(profile);
            }

            try
            {
                await RefreshSavedSessionsAsync(CancellationToken.None);
            }
            catch (AuthenticationException refreshException)
            {
                operation?.Fail(refreshException);
                StatusMessage = Loc.Format("Status.Combined",
                    LocalizedErrors.Get(exception),
                    Loc.Format("Error.SessionRefresh", LocalizedErrors.Get(refreshException)));
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
        using var operation = _diagnostics?.Begin(DiagnosticArea.Storage, DiagnosticAction.RemoveSession);
        try
        {
            await _authenticationService.RemoveAsync(profile, cancellationToken);
            await RefreshSavedSessionsAsync(cancellationToken);
            StatusMessage = Loc.Format("Status.RemovedAccount", LocaleFormat.SessionDisplayName(profile));
            ShowSavedSessionsOrServerEntry(false);
            operation?.Complete();
        }
        catch (AuthenticationException exception)
        {
            operation?.Fail(exception);
            StatusMessage = LocalizedErrors.Get(exception);
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

        LibraryBrowser?.CancelLoading();
        IsBusy = true;
        var profile = _currentSession.Profile;
        using var operation = _diagnostics?.Begin(DiagnosticArea.Authentication, DiagnosticAction.SignOut);
        try
        {
            await _authenticationService.LogoutAsync(_currentSession, cancellationToken);
            StatusMessage = Loc.Format("Status.SignedOut", LocaleFormat.SessionDisplayName(profile));
            operation?.Complete();
        }
        catch (AuthenticationException exception)
        {
            operation?.Fail(exception);
            StatusMessage = LocalizedErrors.Get(exception);
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
                operation?.Fail(exception);
                ShowServerEntry();
                StatusMessage = LocalizedErrors.Get(exception);
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    private bool CanNavigate() => !IsBusy;

    private bool CanShowDesignGallery() =>
        !_disposed && !IsBusy && _currentSession is not null && _mediaPreviewClient is not null;

    [RelayCommand(CanExecute = nameof(CanShowDesignGallery))]
    private async Task OpenHomeAsync()
    {
        if (DesignGallery is not null)
        {
            IsDesignGalleryVisible = true;
        }
        else
        {
            await ShowDesignGalleryCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand(CanExecute = nameof(CanShowDesignGallery), IncludeCancelCommand = true)]
    private async Task ShowDesignGalleryAsync(CancellationToken cancellationToken)
    {
        if (_currentSession is not { } session || _mediaPreviewClient is null)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = Loc.Get("Status.LoadingPreview");
        using var operation = _diagnostics?.Begin(DiagnosticArea.Network, DiagnosticAction.LoadHome);
        try
        {
            var home = await _mediaPreviewClient.GetHomeAsync(session, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed)
            {
                return;
            }

            var gallery = _createGallery(home);
            ClearDesignGallery();
            DesignGallery = gallery;
            LibraryBrowser = new LibraryBrowserViewModel(_mediaPreviewClient, session, home.Libraries,
                exception => HandleRejectedMediaSessionAsync(session, exception), _diagnostics);
            IsDesignGalleryVisible = true;
            StatusMessage = Loc.Get("Status.PreviewLoaded");
            operation?.Complete();
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            operation?.Fail(exception);
            StatusMessage = Loc.Get("Status.PreviewCanceled");
        }
        catch (MediaPreviewException exception)
        {
            operation?.Fail(exception);
            StatusMessage = LocalizedErrors.Get(exception);
            if (exception.Error == MediaPreviewError.AccessDenied)
            {
                await HandleRejectedMediaSessionAsync(session, exception);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task HandleRejectedMediaSessionAsync(AuthenticatedSession session, MediaPreviewException exception)
    {
        if (_disposed || _currentSession != session)
        {
            return;
        }

        IsBusy = true;
        using var operation = _diagnostics?.Begin(DiagnosticArea.Authentication, DiagnosticAction.RestoreSession);
        try
        {
            PrepareForReauthentication(session.Profile);
            StatusMessage = Loc.Format("Status.SignInAgain", LocalizedErrors.Get(exception));
            try
            {
                await _authenticationService.InvalidateAsync(session);
            }
            catch (AuthenticationException invalidationException)
            {
                operation?.Fail(invalidationException);
                StatusMessage = Loc.Format("Status.Combined", StatusMessage,
                    Loc.Format("Error.SessionInvalidation", LocalizedErrors.Get(invalidationException)));
            }

            try
            {
                await RefreshSavedSessionsAsync(CancellationToken.None);
            }
            catch (AuthenticationException refreshException)
            {
                operation?.Fail(refreshException);
                StatusMessage = Loc.Format("Status.Combined", StatusMessage,
                    Loc.Format("Error.SessionRefresh", LocalizedErrors.Get(refreshException)));
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void HideDesignGallery() => IsDesignGalleryVisible = false;

    private void PrepareForReauthentication(SessionProfile profile)
    {
        _currentSession = null;
        AuthenticatedAccount = string.Empty;
        SavedSessions.Remove(profile);
        SelectedSavedSession = SavedSessions.FirstOrDefault();
        CurrentServer = profile.Server;
        ServerAddress = profile.Server.BaseUri.ToString();
        Username = profile.Username;
        Password = string.Empty;
        ShowSignIn();
        ShowDesignGalleryCommand.NotifyCanExecuteChanged();
        OpenHomeCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private void AddServer()
    {
        CurrentServer = null;
        ServerAddress = string.Empty;
        ShowServerEntry();
        StatusMessage = Loc.Get("Status.EnterServer");
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
                StatusMessage = Loc.Get("Status.GetStarted");
            }
        }
        else
        {
            SetVisibleState(savedSessions: true);
            if (updateStatus)
            {
                StatusMessage = Loc.Get("Status.ChooseAccount");
            }
        }
    }

    private void ShowServerEntry() => SetVisibleState(serverEntry: true);

    private void ShowSignIn() => SetVisibleState(signIn: true);

    private void ShowAuthenticated(AuthenticatedSession session)
    {
        _mediaPreviewClient?.ClearImageCache();
        ClearDesignGallery();
        AuthenticatedAccount = LocaleFormat.SessionDisplayName(session.Profile);
        StatusMessage = Loc.Format("Status.SignedIn", session.Server.DisplayName);
        SetVisibleState(authenticated: true);
        ShowDesignGalleryCommand.NotifyCanExecuteChanged();
        OpenHomeCommand.NotifyCanExecuteChanged();
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
        if (!authenticated)
        {
            _mediaPreviewClient?.ClearImageCache();
            _currentSession = null;
            AuthenticatedAccount = string.Empty;
            ClearDesignGallery();
            ShowDesignGalleryCommand.NotifyCanExecuteChanged();
            OpenHomeCommand.NotifyCanExecuteChanged();
        }
    }

    private void ClearDesignGallery()
    {
        IsDesignGalleryVisible = false;
        var gallery = DesignGallery;
        DesignGallery = null;
        gallery?.Dispose();
        var browser = LibraryBrowser;
        LibraryBrowser = null;
        browser?.Dispose();
    }

    public void Dispose()
    {
        _disposed = true;
        ShowDesignGalleryCommand.Cancel();
        ClearDesignGallery();
        _mediaPreviewClient?.ClearImageCache();
        GC.SuppressFinalize(this);
    }
}
