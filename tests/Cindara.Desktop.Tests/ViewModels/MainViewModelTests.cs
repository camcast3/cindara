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
        var authentication = new RefreshFailingAuthenticationService();
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

    private static readonly ServerIdentity Server = new(
        "server-1",
        new Uri("https://media.example.com/"),
        "Living Room",
        "10.10.7",
        "Linux");

    private sealed class StubServerClient : IJellyfinServerClient
    {
        public Task<ServerIdentity> ConnectAsync(
            string serverAddress,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Server);
    }

    private sealed class RefreshFailingAuthenticationService : IAuthenticationService
    {
        private readonly AuthenticatedSession _session = new(
            Server,
            "user-1",
            "viewer",
            "token");

        public bool FailRefresh { get; set; }

        public Task<AuthenticatedSession> AuthenticateAsync(
            AuthenticationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_session);

        public Task<IReadOnlyList<SessionProfile>> GetSavedSessionsAsync(
            CancellationToken cancellationToken = default)
        {
            if (FailRefresh)
            {
                throw new AuthenticationException(
                    AuthenticationError.SecureStorageUnavailable,
                    "Saved session metadata could not be read.");
            }

            return Task.FromResult<IReadOnlyList<SessionProfile>>([_session.Profile]);
        }

        public Task<AuthenticatedSession> RestoreAsync(
            SessionProfile profile,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_session);

        public Task LogoutAsync(
            AuthenticatedSession session,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveAsync(
            SessionProfile profile,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
