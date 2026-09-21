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
        var service = CreateService(
            new QueueHttpMessageHandler(
                Response(HttpStatusCode.OK, """
                    {
                      "AccessToken": "token",
                      "User": { "Id": "user-1", "Name": "viewer" }
                    }
                    """)),
            new FailingSessionStore());

        var exception = await Assert.ThrowsAsync<AuthenticationException>(
            () => service.AuthenticateAsync(
                new AuthenticationRequest(Server, "viewer", "password")));

        Assert.Equal(AuthenticationError.SecureStorageUnavailable, exception.Error);
    }

    private static JellyfinAuthenticationService CreateService(
        HttpMessageHandler handler,
        ISessionStore store) =>
        new(
            new HttpClient(handler),
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
                request.Headers.Authorization?.ToString()
                    ?? request.Headers.GetValues("Authorization").Single(),
                request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync(cancellationToken)));

            return _results.Dequeue() switch
            {
                HttpResponseMessage response => response,
                Exception exception => throw exception,
                _ => throw new InvalidOperationException("Unsupported test response."),
            };
        }
    }

    private sealed record CapturedRequest(string Authorization, string? Body);

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
    }
}
