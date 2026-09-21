using System.Net;
using System.Text;
using Cindara.Core.Authentication;
using Cindara.Core.Models;

namespace Cindara.Core.Tests.Authentication;

public sealed class JellyfinAuthenticationServiceTests
{
    [Fact]
    public async Task AuthenticateAsyncSavesSessionAndSendsStableClientIdentity()
    {
        var handler = new QueueHttpMessageHandler(
            Response(HttpStatusCode.OK, """
                {
                  "AccessToken": "secret-token",
                  "User": { "Id": "user-1", "Name": "viewer" }
                }
                """));
        var store = new InMemorySessionStore();
        var service = CreateService(handler, store);

        var session = await service.AuthenticateAsync(
            new AuthenticationRequest(Server, "viewer", "password"));

        Assert.Equal("secret-token", session.AccessToken);
        Assert.Equal(session, await store.GetAsync(session.Profile));
        Assert.Contains("Client=\"Cindara\"", handler.Requests[0].Authorization, StringComparison.Ordinal);
        Assert.Contains("DeviceId=\"device-1\"", handler.Requests[0].Authorization, StringComparison.Ordinal);
        Assert.Equal("""{"username":"viewer","pw":"password"}""", handler.Requests[0].Body);
        Assert.DoesNotContain("secret-token", session.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task AuthenticateAsyncReportsInvalidCredentials(HttpStatusCode status)
    {
        var service = CreateService(
            new QueueHttpMessageHandler(Response(status, "{}")),
            new InMemorySessionStore());

        var exception = await Assert.ThrowsAsync<AuthenticationException>(
            () => service.AuthenticateAsync(
                new AuthenticationRequest(Server, "viewer", "wrong")));

        Assert.Equal(AuthenticationError.InvalidCredentials, exception.Error);
    }

    [Fact]
    public async Task AuthenticateAsyncRejectsInsecureRemoteServer()
    {
        var service = CreateService(new QueueHttpMessageHandler(), new InMemorySessionStore());
        var insecureServer = Server with { BaseUri = new Uri("http://192.0.2.10/") };

        var exception = await Assert.ThrowsAsync<AuthenticationException>(
            () => service.AuthenticateAsync(
                new AuthenticationRequest(insecureServer, "viewer", "password")));

        Assert.Equal(AuthenticationError.InsecureConnection, exception.Error);
    }

    [Fact]
    public async Task AuthenticateAsyncRejectsUnsupportedLoopbackScheme()
    {
        var service = CreateService(new QueueHttpMessageHandler(), new InMemorySessionStore());
        var unsupportedServer = Server with { BaseUri = new Uri("ftp://localhost/") };

        var exception = await Assert.ThrowsAsync<AuthenticationException>(
            () => service.AuthenticateAsync(
                new AuthenticationRequest(unsupportedServer, "viewer", "password")));

        Assert.Equal(AuthenticationError.InsecureConnection, exception.Error);
    }

    [Fact]
    public void AuthenticationRequestDoesNotRenderPassword()
    {
        var request = new AuthenticationRequest(Server, "viewer", "secret-password");

        var rendered = request.ToString();

        Assert.Contains("viewer", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-password", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthenticateAsyncRejectsMalformedResponse()
    {
        var service = CreateService(
            new QueueHttpMessageHandler(Response(HttpStatusCode.OK, """{"User":{}}""")),
            new InMemorySessionStore());

        var exception = await Assert.ThrowsAsync<AuthenticationException>(
            () => service.AuthenticateAsync(
                new AuthenticationRequest(Server, "viewer", "password")));

        Assert.Equal(AuthenticationError.InvalidResponse, exception.Error);
    }

    [Fact]
    public async Task RestoreAsyncRemovesOnlyRejectedSession()
    {
        var rejected = Session("user-1", "first", "rejected-token");
        var retained = Session("user-2", "second", "valid-token");
        var store = new InMemorySessionStore();
        await store.SaveAsync(rejected);
        await store.SaveAsync(retained);
        var service = CreateService(
            new QueueHttpMessageHandler(Response(HttpStatusCode.Unauthorized, "{}")),
            store);

        var exception = await Assert.ThrowsAsync<AuthenticationException>(
            () => service.RestoreAsync(rejected.Profile));

        Assert.Equal(AuthenticationError.RevokedSession, exception.Error);
        Assert.Null(await store.GetAsync(rejected.Profile));
        Assert.Equal(retained, await store.GetAsync(retained.Profile));
    }

    [Fact]
    public async Task RestoreAsyncReturnsValidSavedSession()
    {
        var session = Session("user-1", "viewer", "token");
        var store = new InMemorySessionStore();
        await store.SaveAsync(session);
        var service = CreateService(
            new QueueHttpMessageHandler(
                Response(HttpStatusCode.OK, """{"Id":"user-1","Name":"viewer"}""")),
            store);

        var restored = await service.RestoreAsync(session.Profile);

        Assert.Equal(session, restored);
    }

    [Theory]
    [InlineData("http://media.example.com/")]
    [InlineData("ftp://localhost/")]
    public async Task RestoreValidatesResolvedDestinationBeforeSendingToken(string savedAddress)
    {
        var session = Session("user-1", "viewer", "saved-token");
        var saved = session with { Server = Server with { BaseUri = new Uri(savedAddress) } };
        var store = new InMemorySessionStore();
        await store.SaveAsync(saved);
        var handler = new QueueHttpMessageHandler();
        using var service = CreateService(handler, store);

        var exception = await Assert.ThrowsAsync<AuthenticationException>(
            () => service.RestoreAsync(session.Profile));

        Assert.Equal(AuthenticationError.InsecureConnection, exception.Error);
        Assert.Empty(handler.Requests);
        Assert.Equal(saved, await store.GetAsync(session.Profile));
    }

    [Theory]
    [InlineData(false, HttpStatusCode.Unauthorized)]
    [InlineData(false, HttpStatusCode.Forbidden)]
    [InlineData(true, HttpStatusCode.NoContent)]
    [InlineData(true, HttpStatusCode.Unauthorized)]
    public async Task StaleSessionResponsePreservesConcurrentSignIn(bool logout, HttpStatusCode status)
    {
        var old = Session("user-1", "viewer", "old-token");
        var store = new InMemorySessionStore();
        await store.SaveAsync(old);
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HttpResponseMessage> DelayedResponse()
        {
            requestStarted.SetResult();
            await releaseResponse.Task;
            return Response(status, "{}");
        }

        var handler = new QueueHttpMessageHandler(
            (Func<Task<HttpResponseMessage>>)DelayedResponse,
            Response(HttpStatusCode.OK, """
                {"AccessToken":"new-token","User":{"Id":"user-1","Name":"viewer"}}
                """));
        using var service = CreateService(handler, store);
        var staleOperation = logout ? service.LogoutAsync(old) : service.RestoreAsync(old.Profile);
        AuthenticatedSession? replacement = null;
        try
        {
            await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            replacement = await service.AuthenticateAsync(new AuthenticationRequest(Server, "viewer", "password"));
        }
        finally
        {
            releaseResponse.TrySetResult();
        }

        var exception = await Assert.ThrowsAsync<AuthenticationException>(() => staleOperation);
        Assert.Equal(AuthenticationError.SessionChanged, exception.Error);
        Assert.NotNull(replacement);
        Assert.Equal(replacement, await store.GetAsync(old.Profile));
        Assert.Single(await store.GetProfilesAsync());
        Assert.Equal("old-token", handler.Requests[0].AccessToken);
    }

    [Fact]
    public async Task RestoreAsyncRemovesProfileWhenCredentialIsMissing()
    {
        var profile = Session("user-1", "viewer", "token").Profile;
        var store = new MissingCredentialSessionStore(profile);
        var service = CreateService(new QueueHttpMessageHandler(), store);

        var exception = await Assert.ThrowsAsync<AuthenticationException>(
            () => service.RestoreAsync(profile));

        Assert.Equal(AuthenticationError.RevokedSession, exception.Error);
        Assert.True(store.WasRemoved);
        Assert.Empty(await store.GetProfilesAsync());
    }

    [Fact]
    public async Task LogoutAsyncRemovesLocalSessionWhenServerIsOffline()
    {
        var session = Session("user-1", "viewer", "token");
        var store = new InMemorySessionStore();
        await store.SaveAsync(session);
        var service = CreateService(new QueueHttpMessageHandler(new HttpRequestException()), store);

        var exception = await Assert.ThrowsAsync<AuthenticationException>(
            () => service.LogoutAsync(session));

        Assert.Equal(AuthenticationError.Network, exception.Error);
        Assert.Null(await store.GetAsync(session.Profile));
        Assert.Contains("local credential was removed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("could not complete", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthenticationMapsPersistenceFailure()
    {
        var handler = new QueueHttpMessageHandler(
            Response(HttpStatusCode.OK, """
                    {
                      "AccessToken": "token",
                      "User": { "Id": "user-1", "Name": "viewer" }
                    }
                    """),
            Response(HttpStatusCode.NoContent, string.Empty));
        var service = CreateService(handler, new FailingSessionStore());

        var exception = await Assert.ThrowsAsync<AuthenticationException>(
            () => service.AuthenticateAsync(
                new AuthenticationRequest(Server, "viewer", "password")));

        Assert.Equal(AuthenticationError.SecureStorageUnavailable, exception.Error);
        Assert.Equal(2, handler.Requests.Count);
        Assert.EndsWith(
            "/Sessions/Logout",
            handler.Requests[1].Uri.AbsolutePath,
            StringComparison.Ordinal);
        Assert.Equal("token", handler.Requests[1].AccessToken);
    }

    [Fact]
    public async Task AuthenticationCancellationDuringPersistenceRevokesIssuedToken()
    {
        var handler = new QueueHttpMessageHandler(
            Response(HttpStatusCode.OK, """
                {
                  "AccessToken": "token",
                  "User": { "Id": "user-1", "Name": "viewer" }
                }
                """),
            Response(HttpStatusCode.NoContent, string.Empty));
        var service = CreateService(handler, new CancelingSessionStore());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.AuthenticateAsync(
                new AuthenticationRequest(Server, "viewer", "password")));

        Assert.Equal(2, handler.Requests.Count);
        Assert.EndsWith(
            "/Sessions/Logout",
            handler.Requests[1].Uri.AbsolutePath,
            StringComparison.Ordinal);
        Assert.Equal("token", handler.Requests[1].AccessToken);
    }

    [Fact]
    public void ProductionTransportDisablesAutomaticRedirects()
    {
        using var handler = JellyfinAuthenticationService.CreateSecureTransport();

        Assert.False(handler.AllowAutoRedirect);
    }

    private static JellyfinAuthenticationService CreateService(
        HttpMessageHandler handler,
        ISessionStore store) =>
        new(
            handler,
            store,
            new JellyfinClientIdentity("Cindara", "Living Room", "device-1", "1.0.0"));

    private static AuthenticatedSession Session(string id, string username, string token) =>
        new(Server, id, username, token);

    private static HttpResponseMessage Response(HttpStatusCode status, string content) =>
        new(status)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json"),
        };

    private static readonly ServerIdentity Server = new(
        "server-1",
        new Uri("https://media.example.com/jellyfin/"),
        "Living Room",
        "10.10.7",
        "Linux");

    private sealed class QueueHttpMessageHandler(params object[] results) : HttpMessageHandler
    {
        private readonly Queue<object> _results = new(results);

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(
                request.RequestUri ?? throw new InvalidOperationException("Request URI is missing."),
                request.Headers.Authorization?.ToString()
                    ?? request.Headers.GetValues("Authorization").Single(),
                request.Headers.TryGetValues("X-Emby-Token", out var accessTokens)
                    ? accessTokens.Single()
                    : null,
                request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync(cancellationToken)));

            return _results.Dequeue() switch
            {
                HttpResponseMessage response => response,
                Func<Task<HttpResponseMessage>> response => await response(),
                Exception exception => throw exception,
                _ => throw new InvalidOperationException("Unsupported test response."),
            };
        }
    }

    private sealed record CapturedRequest(
        Uri Uri,
        string Authorization,
        string? AccessToken,
        string? Body);

    private sealed class FailingSessionStore : ISessionStore
    {
        public Task<IReadOnlyList<SessionProfile>> GetProfilesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SessionProfile>>([]);

        public Task<AuthenticatedSession?> GetAsync(
            SessionProfile profile,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AuthenticatedSession?>(null);

        public Task SaveAsync(
            AuthenticatedSession session,
            CancellationToken cancellationToken = default) =>
            throw new SessionStoreException(
                SessionStoreError.SecureStorageUnavailable,
                "Secure storage is locked.");

        public Task<bool> RemoveAsync(
            SessionProfile profile,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> RemoveIfMatchesAsync(
            SessionProfile profile,
            string? expectedAccessToken,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(expectedAccessToken is null);
    }

    private sealed class MissingCredentialSessionStore(SessionProfile profile) : ISessionStore
    {
        private SessionProfile? _profile = profile;

        public bool WasRemoved { get; private set; }

        public Task<IReadOnlyList<SessionProfile>> GetProfilesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SessionProfile>>(
                _profile is null ? [] : [_profile]);

        public Task<AuthenticatedSession?> GetAsync(
            SessionProfile requestedProfile,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AuthenticatedSession?>(null);

        public Task SaveAsync(
            AuthenticatedSession session,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> RemoveAsync(
            SessionProfile requestedProfile,
            CancellationToken cancellationToken = default)
        {
            WasRemoved = _profile is not null;
            _profile = null;
            return Task.FromResult(WasRemoved);
        }

        public async Task<bool> RemoveIfMatchesAsync(
            SessionProfile requestedProfile,
            string? expectedAccessToken,
            CancellationToken cancellationToken = default)
        {
            if (expectedAccessToken is not null)
            {
                return false;
            }

            await RemoveAsync(requestedProfile, cancellationToken);
            return true;
        }
    }

    private sealed class CancelingSessionStore : ISessionStore
    {
        public Task<IReadOnlyList<SessionProfile>> GetProfilesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SessionProfile>>([]);

        public Task<AuthenticatedSession?> GetAsync(
            SessionProfile profile,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AuthenticatedSession?>(null);

        public Task SaveAsync(
            AuthenticatedSession session,
            CancellationToken cancellationToken = default) =>
            Task.FromCanceled(new CancellationToken(true));

        public Task<bool> RemoveAsync(
            SessionProfile profile,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> RemoveIfMatchesAsync(
            SessionProfile profile,
            string? expectedAccessToken,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(expectedAccessToken is null);
    }
}
