using Cindara.Core.Authentication;
using Cindara.Core.Diagnostics;
using Cindara.Core.Jellyfin;
using Cindara.Core.Models;
using Cindara.Desktop.Localization;
using Cindara.Desktop.Tests.Localization;
using Cindara.Desktop.ViewModels;

namespace Cindara.Desktop.Tests.ViewModels;

[Collection(LocalizationTestGroup.Name)]
public sealed class MainViewModelTests
{
    [Fact]
    public async Task RejectedLibraryPageClearsMediaAndCacheAndInvalidatesOnlyTheCurrentAccount()
    {
        var other = new SessionProfile(Server, "other", "Other viewer");
        var authentication = new TestAuthenticationService
        {
            SavedProfiles = [new SessionProfile(Server, "user-1", "viewer"), other],
        };
        var library = new MediaLibrary("movies", "Movies", "movies");
        var preview = new TestMediaPreviewClient
        {
            Home = new MediaPreviewHome(null, [], []) { Libraries = [library] },
            LibraryError = MediaPreviewError.AccessDenied,
        };
        using var model = new MainViewModel(new StubServerClient(), authentication, preview);
        await model.InitializeCommand.ExecuteAsync(null);
        await model.UseSavedSessionCommand.ExecuteAsync(null);
        await model.OpenHomeCommand.ExecuteAsync(null);
        var browser = Assert.IsType<LibraryBrowserViewModel>(model.LibraryBrowser);
        var previousClears = preview.CacheClears;

        await browser.OpenLibraryCommand.ExecuteAsync(library);

        Assert.True(model.IsSignInVisible);
        Assert.False(model.IsAuthenticatedVisible);
        Assert.False(model.IsBusy);
        Assert.Null(model.LibraryBrowser);
        Assert.Null(model.DesignGallery);
        Assert.Equal("user-1", authentication.InvalidatedSession?.UserId);
        Assert.Equal(other, Assert.Single(model.SavedSessions));
        Assert.True(preview.CacheClears > previousClears);
        Assert.False(browser.OpenLibraryCommand.CanExecute(library));
    }

    [Fact]
    public async Task DiagnosticsCaptureAuthenticationStorageFailureWithNoAccountMetadata()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cindara-auth-diagnostics-{Guid.NewGuid():N}");
        try
        {
            var diagnostics = new LocalDiagnostics(directory);
            var authentication = new TestAuthenticationService { FailRefresh = true };
            using var model = new MainViewModel(new StubServerClient(), authentication, diagnostics: diagnostics);
            await model.InitializeCommand.ExecuteAsync(null);
            Assert.Contains(diagnostics.RecentErrors,
                entry => entry.Errors.Contains("Authentication.SecureStorageUnavailable"));
            var entries = diagnostics.Snapshot();
            Assert.All(entries, entry => Assert.Equal(1, entry.Operation));
            Assert.Equal(DiagnosticOutcome.Failed, entries[^1].Outcome);
            Assert.All(Directory.GetFiles(directory), file =>
            {
                var text = File.ReadAllText(file);
                Assert.DoesNotContain(Server.BaseUri.Host, text, StringComparison.Ordinal);
                Assert.DoesNotContain(Server.DisplayName, text, StringComparison.Ordinal);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanceledOrDisposedLoadNeverPublishesLateResults(bool dispose)
    {
        var preview = new PendingPreviewClient();
        using var model = new MainViewModel(new StubServerClient(), new TestAuthenticationService(), preview);
        await model.InitializeCommand.ExecuteAsync(null);
        await model.UseSavedSessionCommand.ExecuteAsync(null);
        var pending = model.ShowDesignGalleryCommand.ExecuteAsync(null);
        Assert.True(model.IsBusy);
        if (dispose)
        {
            model.Dispose();
        }
        else
        {
            model.ShowDesignGalleryCancelCommand.Execute(null);
        }

        preview.Completion.SetResult(new MediaPreviewHome(null, [], []));
        await pending;
        Assert.Null(model.DesignGallery);
        Assert.False(model.IsDesignGalleryVisible);
        Assert.False(model.IsBusy);
        Assert.Equal(!dispose, model.OpenHomeCommand.CanExecute(null));
    }

    [Fact]
    public async Task UnsupportedLanguageUsesEnglishResourcesAndLeavesServerNameUntouched()
    {
        using var scope = new CultureScope("fr-CA");
        using var viewModel = new MainViewModel(new StubServerClient(), new TestAuthenticationService())
        {
            ServerAddress = Server.BaseUri.ToString(),
        };

        Assert.Equal("Loading saved Jellyfin sessions...", viewModel.StatusMessage);
        await viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.Equal("Connected to Living Room. Sign in with your Jellyfin account.",
            viewModel.StatusMessage);
        Assert.Equal(string.Empty, viewModel.Username);
    }

    [Fact]
    public async Task PseudoStatusPreservesUsernamesAndServerData()
    {
        using var scope = new CultureScope("qps-ploc");
        using var viewModel = new MainViewModel(new StubServerClient(), new TestAuthenticationService());
        await viewModel.InitializeCommand.ExecuteAsync(null);
        await viewModel.UseSavedSessionCommand.ExecuteAsync(null);

        Assert.StartsWith("[!! ", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("Living Room", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("viewer", viewModel.AuthenticatedAccount, StringComparison.Ordinal);
        Assert.Contains("https://media.example.com", viewModel.AuthenticatedAccount, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LogoutRefreshFailureRestoresCommandsAndShowsError()
    {
        var authentication = new TestAuthenticationService();
        var viewModel = new MainViewModel(new StubServerClient(), authentication)
        {
            ServerAddress = Server.BaseUri.ToString(),
        };
        await viewModel.ConnectCommand.ExecuteAsync(null);
        viewModel.Username = "viewer";
        viewModel.Password = "password";
        await viewModel.SignInCommand.ExecuteAsync(null);
        authentication.FailRefresh = true;

        await viewModel.LogoutCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.IsServerEntryVisible);
        Assert.False(viewModel.IsAuthenticatedVisible);
        Assert.Equal(Loc.Get("Error.Authentication.SecureStorageUnavailable"), viewModel.StatusMessage);
        Assert.True(viewModel.ConnectCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemovingSavedAccountPreservesConfirmationInDestinationState(bool keepAnotherAccount)
    {
        var authentication = new TestAuthenticationService();
        var viewModel = new MainViewModel(new StubServerClient(), authentication);
        await viewModel.InitializeCommand.ExecuteAsync(null);
        var selected = Assert.IsType<SessionProfile>(viewModel.SelectedSavedSession);
        var other = selected with { UserId = "other-user", Username = "other" };
        if (keepAnotherAccount)
        {
            authentication.SavedProfiles = [selected, other];
            await viewModel.InitializeCommand.ExecuteAsync(null);
        }

        await viewModel.RemoveSavedSessionCommand.ExecuteAsync(null);

        Assert.Equal($"Removed {selected.DisplayName}.", viewModel.StatusMessage);
        Assert.False(viewModel.IsBusy);
        Assert.Equal(keepAnotherAccount, viewModel.AreSavedSessionsVisible);
        Assert.Equal(!keepAnotherAccount, viewModel.IsServerEntryVisible);
        Assert.DoesNotContain(selected, viewModel.SavedSessions);
        if (keepAnotherAccount)
        {
            Assert.Equal(other, Assert.Single(viewModel.SavedSessions));
            Assert.Equal(other, viewModel.SelectedSavedSession);
        }
        else
        {
            Assert.Empty(viewModel.SavedSessions);
            Assert.Null(viewModel.SelectedSavedSession);
        }
    }

    [Theory]
    [InlineData(AuthenticationError.RevokedSession)]
    [InlineData(AuthenticationError.Network)]
    [InlineData(AuthenticationError.SecureStorageUnavailable)]
    [InlineData(AuthenticationError.SessionChanged)]
    public async Task RestoreRefreshFailureIsReportedWithoutLosingRecoveryState(AuthenticationError error)
    {
        var authentication = new TestAuthenticationService();
        var viewModel = new MainViewModel(new StubServerClient(), authentication);
        await viewModel.InitializeCommand.ExecuteAsync(null);
        var profile = Assert.IsType<SessionProfile>(viewModel.SelectedSavedSession);
        var other = profile with { UserId = "other-user", Username = "other" };
        viewModel.SavedSessions.Add(other);
        authentication.RestoreError = error;
        authentication.FailRefresh = true;

        await viewModel.UseSavedSessionCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsBusy);
        Assert.Contains(Loc.Get($"Error.Authentication.{error}"), viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("Saved session metadata could not be read.", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Contains(other, viewModel.SavedSessions);
        if (error == AuthenticationError.RevokedSession)
        {
            Assert.True(viewModel.IsSignInVisible);
            Assert.Equal(profile.Username, viewModel.Username);
            Assert.Equal(profile.Server.BaseUri.ToString(), viewModel.ServerAddress);
            Assert.DoesNotContain(profile, viewModel.SavedSessions);
            viewModel.Password = "replacement-password";
            Assert.True(viewModel.SignInCommand.CanExecute(null));
            await viewModel.SignInCommand.ExecuteAsync(null);
            Assert.Equal(profile.Server, authentication.LastAuthenticationRequest?.Server);
        }
        else
        {
            Assert.True(viewModel.AreSavedSessionsVisible);
            Assert.Contains(profile, viewModel.SavedSessions);
        }
    }

    [Theory]
    [InlineData("Initialize", false)]
    [InlineData("Connect", false)]
    [InlineData("SignIn", false)]
    [InlineData("Restore", false)]
    [InlineData("Remove", false)]
    [InlineData("Logout", false)]
    [InlineData("SignIn", true)]
    [InlineData("Restore", true)]
    [InlineData("Remove", true)]
    [InlineData("Logout", true)]
    public async Task NavigationRemainsDisabledUntilOperationCompletes(string operation, bool fail)
    {
        var authentication = new TestAuthenticationService();
        var serverClient = new StubServerClient();
        var viewModel = new MainViewModel(serverClient, authentication)
        {
            ServerAddress = Server.BaseUri.ToString(),
        };
        await viewModel.ConnectCommand.ExecuteAsync(null);
        viewModel.Username = "viewer";
        viewModel.Password = "password";
        await viewModel.SignInCommand.ExecuteAsync(null);
        if (operation == "SignIn")
        {
            viewModel.BackToSessionsCommand.Execute(null);
            await viewModel.ConnectCommand.ExecuteAsync(null);
            viewModel.Username = "viewer";
            viewModel.Password = "password";
            Assert.True(viewModel.SignInCommand.CanExecute(null));
        }
        else if (operation is "Restore" or "Remove")
        {
            viewModel.BackToSessionsCommand.Execute(null);
        }

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        authentication.OperationGate = release.Task;
        authentication.FailOperation = fail;
        serverClient.OperationGate = release.Task;
        var notifications = 0;
        viewModel.AddServerCommand.CanExecuteChanged += (_, _) => notifications++;
        viewModel.BackToSessionsCommand.CanExecuteChanged += (_, _) => notifications++;
        var pending = operation switch
        {
            "Initialize" => viewModel.InitializeCommand.ExecuteAsync(null),
            "Connect" => viewModel.ConnectCommand.ExecuteAsync(null),
            "SignIn" => viewModel.SignInCommand.ExecuteAsync(null),
            "Restore" => viewModel.UseSavedSessionCommand.ExecuteAsync(null),
            "Remove" => viewModel.RemoveSavedSessionCommand.ExecuteAsync(null),
            "Logout" => viewModel.LogoutCommand.ExecuteAsync(null),
            _ => throw new ArgumentException("Unexpected operation.", nameof(operation)),
        };

        try
        {
            Assert.False(pending.IsCompleted);
            Assert.True(viewModel.IsBusy);
            Assert.False(viewModel.AddServerCommand.CanExecute(null));
            Assert.False(viewModel.BackToSessionsCommand.CanExecute(null));
            Assert.False(viewModel.LogoutCommand.CanExecute(null));
            Assert.Equal(2, notifications);
        }
        finally
        {
            release.TrySetResult();
            await pending;
        }

        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.AddServerCommand.CanExecute(null));
        Assert.True(viewModel.BackToSessionsCommand.CanExecute(null));
        Assert.True(viewModel.LogoutCommand.CanExecute(null));
        Assert.Equal(4, notifications);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task SignInSubmitsPasswordUnchangedWithoutRequiringNonblankValue(string password)
    {
        var authentication = new TestAuthenticationService();
        var viewModel = new MainViewModel(new StubServerClient(), authentication)
        {
            ServerAddress = Server.BaseUri.ToString(),
        };
        await viewModel.ConnectCommand.ExecuteAsync(null);
        Assert.False(viewModel.SignInCommand.CanExecute(null));
        viewModel.Username = "viewer";
        viewModel.Password = password;

        Assert.True(viewModel.SignInCommand.CanExecute(null));
        await viewModel.SignInCommand.ExecuteAsync(null);

        Assert.Equal(password, authentication.LastAuthenticationRequest?.Password);
        Assert.True(viewModel.IsAuthenticatedVisible);
        Assert.Empty(viewModel.Password);
    }

    [Fact]
    public async Task SignInTrimsUsernameWithoutChangingPassword()
    {
        var authentication = new TestAuthenticationService();
        var viewModel = new MainViewModel(new StubServerClient(), authentication)
        {
            ServerAddress = Server.BaseUri.ToString(),
        };
        await viewModel.ConnectCommand.ExecuteAsync(null);
        viewModel.Username = "  viewer  ";
        viewModel.Password = " password ";

        await viewModel.SignInCommand.ExecuteAsync(null);

        Assert.Equal("viewer", authentication.LastAuthenticationRequest?.Username);
        Assert.Equal(" password ", authentication.LastAuthenticationRequest?.Password);
    }

    [Fact]
    public async Task AuthenticatedUserCanLoadAndCloseMediaPreview()
    {
        var previewClient = new TestMediaPreviewClient();
        var viewModel = new MainViewModel(
            new StubServerClient(),
            new TestAuthenticationService(),
            previewClient)
        {
            ServerAddress = Server.BaseUri.ToString(),
        };
        await viewModel.ConnectCommand.ExecuteAsync(null);
        viewModel.Username = "viewer";
        await viewModel.SignInCommand.ExecuteAsync(null);

        await viewModel.ShowDesignGalleryCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsDesignGalleryVisible);
        Assert.NotNull(viewModel.DesignGallery);
        var rail = Assert.Single(viewModel.DesignGallery.RecentlyAddedLibraries);
        Assert.Equal("Recently Added in Movies", rail.Title);
        Assert.Equal("Moon Garden", Assert.Single(rail.Items).Name);
        Assert.Equal(1, previewClient.RequestCount);

        viewModel.HideDesignGalleryCommand.Execute(null);

        Assert.False(viewModel.IsDesignGalleryVisible);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedPreviewInvalidatesOnlyActiveAccountAndReturnsToSignIn(bool restoreSession)
    {
        var authentication = new TestAuthenticationService();
        var preview = new TestMediaPreviewClient();
        var viewModel = new MainViewModel(new StubServerClient(), authentication, preview);
        await viewModel.InitializeCommand.ExecuteAsync(null);
        var profile = Assert.IsType<SessionProfile>(viewModel.SelectedSavedSession);
        var other = profile with { UserId = "other-user", Username = "other" };
        var otherServer = profile with { Server = Server with { Id = "other-server" } };
        authentication.SavedProfiles = [profile, other, otherServer];
        await viewModel.InitializeCommand.ExecuteAsync(null);
        if (restoreSession)
        {
            await viewModel.UseSavedSessionCommand.ExecuteAsync(null);
        }
        else
        {
            viewModel.ServerAddress = Server.BaseUri.ToString();
            await viewModel.ConnectCommand.ExecuteAsync(null);
            viewModel.Username = profile.Username;
            await viewModel.SignInCommand.ExecuteAsync(null);
        }

        await viewModel.ShowDesignGalleryCommand.ExecuteAsync(null);
        Assert.NotNull(viewModel.DesignGallery);
        viewModel.HideDesignGalleryCommand.Execute(null);
        preview.Error = MediaPreviewError.AccessDenied;
        viewModel.Password = "must-be-cleared";

        await viewModel.ShowDesignGalleryCommand.ExecuteAsync(null);

        Assert.Equal(profile, authentication.InvalidatedSession?.Profile);
        Assert.Equal("token", authentication.InvalidatedSession?.AccessToken);
        Assert.DoesNotContain(profile, authentication.SavedProfiles!);
        Assert.Collection(viewModel.SavedSessions,
            saved => Assert.Equal(other, saved),
            saved => Assert.Equal(otherServer, saved));
        Assert.Equal(other, viewModel.SelectedSavedSession);
        Assert.True(viewModel.IsSignInVisible);
        Assert.False(viewModel.IsAuthenticatedVisible);
        Assert.False(viewModel.IsDesignGalleryVisible);
        Assert.False(viewModel.IsBusy);
        Assert.Null(viewModel.DesignGallery);
        Assert.Empty(viewModel.AuthenticatedAccount);
        Assert.Empty(viewModel.Password);
        Assert.Equal(profile.Username, viewModel.Username);
        Assert.Equal(profile.Server.BaseUri.ToString(), viewModel.ServerAddress);
        Assert.Contains("Sign in again", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.False(viewModel.ShowDesignGalleryCommand.CanExecute(null));
        Assert.True(viewModel.SignInCommand.CanExecute(null));
        await viewModel.SignInCommand.ExecuteAsync(null);
        Assert.Equal(profile.Server, authentication.LastAuthenticationRequest?.Server);
        Assert.Equal(profile.Username, authentication.LastAuthenticationRequest?.Username);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RejectedPreviewSurfacesCleanupFailuresWithoutKeepingActiveSession(
        bool failInvalidation, bool failRefresh)
    {
        var authentication = new TestAuthenticationService();
        var preview = new TestMediaPreviewClient { Error = MediaPreviewError.AccessDenied };
        var viewModel = new MainViewModel(new StubServerClient(), authentication, preview);
        await viewModel.InitializeCommand.ExecuteAsync(null);
        var profile = Assert.IsType<SessionProfile>(viewModel.SelectedSavedSession);
        var other = profile with { UserId = "other-user", Username = "other" };
        authentication.SavedProfiles = [profile, other];
        await viewModel.InitializeCommand.ExecuteAsync(null);
        await viewModel.UseSavedSessionCommand.ExecuteAsync(null);
        authentication.InvalidationError = failInvalidation ? AuthenticationError.SecureStorageUnavailable : null;
        authentication.FailRefresh = failRefresh;

        await viewModel.ShowDesignGalleryCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsSignInVisible);
        Assert.False(viewModel.IsAuthenticatedVisible);
        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.ShowDesignGalleryCommand.CanExecute(null));
        Assert.Contains(other, viewModel.SavedSessions);
        Assert.Contains(Loc.Get("Error.Preview.AccessDenied"), viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(failInvalidation,
            viewModel.StatusMessage.Contains("Could not invalidate", StringComparison.Ordinal));
        Assert.Equal(failRefresh,
            viewModel.StatusMessage.Contains("could not be read", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectedPreviewReportsConcurrentReplacementAndPreservesSavedProfile()
    {
        var authentication = new TestAuthenticationService();
        var preview = new TestMediaPreviewClient { Error = MediaPreviewError.AccessDenied };
        var viewModel = new MainViewModel(new StubServerClient(), authentication, preview);
        await viewModel.InitializeCommand.ExecuteAsync(null);
        var profile = Assert.IsType<SessionProfile>(viewModel.SelectedSavedSession);
        await viewModel.UseSavedSessionCommand.ExecuteAsync(null);
        authentication.InvalidationError = AuthenticationError.SessionChanged;

        await viewModel.ShowDesignGalleryCommand.ExecuteAsync(null);

        Assert.Contains(profile, viewModel.SavedSessions);
        Assert.True(viewModel.IsSignInVisible);
        Assert.False(viewModel.ShowDesignGalleryCommand.CanExecute(null));
        Assert.Contains("Could not invalidate", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(MediaPreviewError.Network)]
    [InlineData(MediaPreviewError.TimedOut)]
    [InlineData(MediaPreviewError.UnexpectedStatus)]
    [InlineData(MediaPreviewError.InvalidResponse)]
    public async Task OtherPreviewFailuresDoNotInvalidateCredentials(MediaPreviewError error)
    {
        var authentication = new TestAuthenticationService();
        var preview = new TestMediaPreviewClient { Error = error };
        var viewModel = new MainViewModel(new StubServerClient(), authentication, preview);
        await viewModel.InitializeCommand.ExecuteAsync(null);
        await viewModel.UseSavedSessionCommand.ExecuteAsync(null);

        await viewModel.ShowDesignGalleryCommand.ExecuteAsync(null);

        Assert.Null(authentication.InvalidatedSession);
        Assert.True(viewModel.IsAuthenticatedVisible);
        Assert.False(viewModel.IsSignInVisible);
        Assert.True(viewModel.ShowDesignGalleryCommand.CanExecute(null));
        Assert.Equal(Loc.Get($"Error.Preview.{error}"), viewModel.StatusMessage);
    }

    [Theory]
    [InlineData("Logout")]
    [InlineData("AddServer")]
    [InlineData("BackToSessions")]
    [InlineData("SwitchAccount")]
    [InlineData("Dispose")]
    public async Task LeavingAccountReleasesAllGalleryArtwork(string action)
    {
        var authentication = new TestAuthenticationService();
        var preview = new TestMediaPreviewClient { Home = HomeWithArtwork() };
        var decoder = new TestPreviewImageDecoder();
        var viewModel = new MainViewModel(new StubServerClient(), authentication, preview,
            home => DesignGalleryViewModel.Create(home, decoder.Decode));
        await viewModel.InitializeCommand.ExecuteAsync(null);
        var initial = Assert.IsType<SessionProfile>(viewModel.SelectedSavedSession);
        await viewModel.UseSavedSessionCommand.ExecuteAsync(null);
        await viewModel.ShowDesignGalleryCommand.ExecuteAsync(null);
        Assert.NotEmpty(decoder.Resources);
        Assert.All(decoder.Resources, resource => Assert.Equal(0, resource.DisposeCount));

        switch (action)
        {
            case "Logout":
                await viewModel.LogoutCommand.ExecuteAsync(null);
                break;
            case "AddServer":
                viewModel.AddServerCommand.Execute(null);
                break;
            case "BackToSessions":
                viewModel.BackToSessionsCommand.Execute(null);
                break;
            case "SwitchAccount":
                viewModel.SelectedSavedSession = initial with { UserId = "other-user", Username = "other" };
                await viewModel.UseSavedSessionCommand.ExecuteAsync(null);
                break;
            case "Dispose":
                viewModel.Dispose();
                break;
        }

        Assert.Null(viewModel.DesignGallery);
        Assert.False(viewModel.IsDesignGalleryVisible);
        Assert.All(decoder.Resources, resource => Assert.Equal(1, resource.DisposeCount));
        viewModel.Dispose();
        Assert.All(decoder.Resources, resource => Assert.Equal(1, resource.DisposeCount));
    }

    [Fact]
    public async Task CorruptArtworkShowsAnErrorWithoutDiscardingPreviousGallery()
    {
        var decoder = new TestPreviewImageDecoder();
        var preview = new TestMediaPreviewClient { Home = HomeWithArtwork() };
        using var viewModel = new MainViewModel(new StubServerClient(), new TestAuthenticationService(), preview,
            home => DesignGalleryViewModel.Create(home, decoder.Decode));
        await viewModel.InitializeCommand.ExecuteAsync(null);
        await viewModel.UseSavedSessionCommand.ExecuteAsync(null);
        await viewModel.ShowDesignGalleryCommand.ExecuteAsync(null);
        var previous = Assert.IsType<DesignGalleryViewModel>(viewModel.DesignGallery);
        var initialCount = decoder.Resources.Count;
        var cacheClears = preview.CacheClears;
        viewModel.HideDesignGalleryCommand.Execute(null);
        decoder.FailOnCall = initialCount + 2;

        await viewModel.ShowDesignGalleryCommand.ExecuteAsync(null);

        Assert.Same(previous, viewModel.DesignGallery);
        Assert.Equal(cacheClears + 1, preview.CacheClears);
        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.IsDesignGalleryVisible);
        Assert.True(viewModel.IsAuthenticatedVisible);
        Assert.True(viewModel.ShowDesignGalleryCommand.CanExecute(null));
        Assert.Contains("could not be decoded", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.All(decoder.Resources.Take(initialCount), resource => Assert.Equal(0, resource.DisposeCount));
        Assert.All(decoder.Resources.Skip(initialCount), resource => Assert.Equal(1, resource.DisposeCount));

        decoder.FailOnCall = null;
        await viewModel.ShowDesignGalleryCommand.ExecuteAsync(null);

        Assert.NotSame(previous, viewModel.DesignGallery);
        Assert.True(viewModel.IsDesignGalleryVisible);
        Assert.All(decoder.Resources.Take(initialCount), resource => Assert.Equal(1, resource.DisposeCount));
    }

    private static MediaPreviewHome HomeWithArtwork()
    {
        var item = new MediaPreviewItem("movie", "Movie", "2026", "Movie",
            Artwork: [1], Backdrop: [2], Overview: "Overview", Details: "2026", PlaybackProgress: null);
        return new MediaPreviewHome(item, [], [new MediaPreviewRail("movies", "Movies", [item])]);
    }

    private sealed class PendingPreviewClient : IJellyfinMediaPreviewClient
    {
        public Task<byte[]?> GetLibraryArtworkAsync(AuthenticatedSession session, string itemId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void ClearImageCache() { }
        public Task<MediaLibraryPage> GetLibraryPageAsync(AuthenticatedSession session, MediaLibrary library,
            int startIndex, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public TaskCompletionSource<MediaPreviewHome> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<MediaPreviewHome> GetHomeAsync(AuthenticatedSession session, CancellationToken cancellationToken = default) =>
            Completion.Task;
    }

    private static readonly ServerIdentity Server = new(
        "server-1",
        new Uri("https://media.example.com/"),
        "Living Room",
        "10.10.7",
        "Linux");

    private sealed class StubServerClient : IJellyfinServerClient
    {
        public Task OperationGate { get; set; } = Task.CompletedTask;

        public async Task<ServerIdentity> ConnectAsync(
            string serverAddress,
            CancellationToken cancellationToken = default)
        {
            await OperationGate.WaitAsync(cancellationToken);
            return Server;
        }
    }

    private sealed class TestAuthenticationService : IAuthenticationService
    {
        private readonly AuthenticatedSession _session = new(
            Server,
            "user-1",
            "viewer",
            "token");

        public bool FailRefresh { get; set; }

        public IReadOnlyList<SessionProfile>? SavedProfiles { get; set; }

        public bool FailOperation { get; set; }

        public AuthenticationError? RestoreError { get; set; }

        public Task OperationGate { get; set; } = Task.CompletedTask;

        public AuthenticationRequest? LastAuthenticationRequest { get; private set; }

        public AuthenticatedSession? InvalidatedSession { get; private set; }

        public AuthenticationError? InvalidationError { get; set; }

        public Task InvalidateAsync(AuthenticatedSession session)
        {
            InvalidatedSession = session;
            if (InvalidationError is { } error)
            {
                throw new AuthenticationException(error, "Could not invalidate the rejected session.");
            }

            SavedProfiles = (SavedProfiles ?? [_session.Profile])
                .Where(profile => profile != session.Profile).ToArray();
            return Task.CompletedTask;
        }

        public async Task<AuthenticatedSession> AuthenticateAsync(
            AuthenticationRequest request,
            CancellationToken cancellationToken = default)
        {
            LastAuthenticationRequest = request;
            await WaitForOperationAsync(cancellationToken);
            return _session;
        }

        public async Task<IReadOnlyList<SessionProfile>> GetSavedSessionsAsync(
            CancellationToken cancellationToken = default)
        {
            await OperationGate.WaitAsync(cancellationToken);
            if (FailRefresh)
            {
                throw new AuthenticationException(
                    AuthenticationError.SecureStorageUnavailable,
                    "Saved session metadata could not be read.");
            }

            return SavedProfiles ?? [_session.Profile];
        }

        public async Task<AuthenticatedSession> RestoreAsync(
            SessionProfile profile,
            CancellationToken cancellationToken = default)
        {
            await WaitForOperationAsync(cancellationToken);
            if (RestoreError is { } error)
            {
                throw new AuthenticationException(error, "Could not restore the saved session.");
            }

            return _session with { Server = profile.Server, UserId = profile.UserId, Username = profile.Username };
        }

        public Task LogoutAsync(
            AuthenticatedSession session,
            CancellationToken cancellationToken = default) =>
            WaitForOperationAsync(cancellationToken);

        public async Task RemoveAsync(
            SessionProfile profile,
            CancellationToken cancellationToken = default)
        {
            await WaitForOperationAsync(cancellationToken);
            SavedProfiles = (SavedProfiles ?? [_session.Profile])
                .Where(saved => saved.Server.Id != profile.Server.Id || saved.UserId != profile.UserId)
                .ToArray();
        }

        private async Task WaitForOperationAsync(CancellationToken cancellationToken)
        {
            await OperationGate.WaitAsync(cancellationToken);
            if (FailOperation)
            {
                throw new AuthenticationException(AuthenticationError.Network, "Simulated network failure.");
            }
        }
    }

    private sealed class TestMediaPreviewClient : IJellyfinMediaPreviewClient
    {
        public Task<byte[]?> GetLibraryArtworkAsync(AuthenticatedSession session, string itemId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public int CacheClears { get; private set; }
        public MediaPreviewError? LibraryError { get; set; }
        public void ClearImageCache() => CacheClears++;
        public Task<MediaLibraryPage> GetLibraryPageAsync(AuthenticatedSession session, MediaLibrary library,
            int startIndex, CancellationToken cancellationToken = default) =>
            LibraryError is { } error ? throw new MediaPreviewException(error, "Library failed.")
                : Task.FromResult(new MediaLibraryPage([], startIndex, 0));

        public int RequestCount { get; private set; }

        public MediaPreviewError? Error { get; set; }

        public MediaPreviewHome? Home { get; set; }

        public Task<MediaPreviewHome> GetHomeAsync(
            AuthenticatedSession session,
            CancellationToken cancellationToken = default)
        {
            RequestCount++;
            if (Error is { } error)
            {
                throw new MediaPreviewException(error, "Preview failed.");
            }

            if (Home is not null)
            {
                return Task.FromResult(Home);
            }

            var item = new MediaPreviewItem(
                "movie-1",
                "Moon Garden",
                "2026",
                "Movie",
                Artwork: null,
                Backdrop: null,
                Overview: "A garden on the moon.",
                Details: "2026",
                PlaybackProgress: null);
            return Task.FromResult(
                new MediaPreviewHome(
                    item,
                    [],
                    [new MediaPreviewRail("movies", "Recently Added in Movies", [item])]));
        }
    }
}
