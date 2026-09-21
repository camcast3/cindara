using Cindara.Core.Authentication;
using Cindara.Core.Jellyfin;
using Cindara.Core.Models;
using Cindara.Desktop.ViewModels;

namespace Cindara.Desktop.Tests.ViewModels;

public sealed class MainViewModelTests
{
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
        Assert.Equal("Saved session metadata could not be read.", viewModel.StatusMessage);
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
        Assert.Contains("Could not restore the saved session.", viewModel.StatusMessage, StringComparison.Ordinal);
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

            return _session;
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
}
