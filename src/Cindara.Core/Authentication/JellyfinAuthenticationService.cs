using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Cindara.Core.Authentication;

public sealed class JellyfinAuthenticationService : IAuthenticationService, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly JellyfinClientIdentity _clientIdentity;
    private readonly HttpClient _httpClient;
    private readonly ISessionStore _sessionStore;

    public JellyfinAuthenticationService(
        ISessionStore sessionStore,
        JellyfinClientIdentity clientIdentity)
        : this(CreateSecureTransport(), sessionStore, clientIdentity)
    {
    }

    internal JellyfinAuthenticationService(
        HttpMessageHandler httpHandler,
        ISessionStore sessionStore,
        JellyfinClientIdentity clientIdentity)
    {
        ArgumentNullException.ThrowIfNull(httpHandler);
        ArgumentNullException.ThrowIfNull(sessionStore);
        ArgumentNullException.ThrowIfNull(clientIdentity);

        _httpClient = new HttpClient(httpHandler)
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        _sessionStore = sessionStore;
        _clientIdentity = clientIdentity;
    }

    public void Dispose() => _httpClient.Dispose();

    public async Task<AuthenticatedSession> AuthenticateAsync(
        AuthenticationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureSecureConnection(request.Server.BaseUri);

        using var message = CreateRequest(
            HttpMethod.Post,
            new Uri(request.Server.BaseUri, "Users/AuthenticateByName"));
        message.Content = JsonContent.Create(new
        {
            request.Username,
            Pw = request.Password,
        });

        using var response = await SendAsync(message, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new AuthenticationException(
                AuthenticationError.InvalidCredentials,
                "The username or password was not accepted by Jellyfin.");
        }

        EnsureSuccess(response);
        var result = await ReadAuthenticationResultAsync(response, CancellationToken.None)
            .ConfigureAwait(false);
        var session = new AuthenticatedSession(
            request.Server,
            result.UserId,
            result.Username,
            result.AccessToken);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _sessionStore.SaveAsync(session, cancellationToken).ConfigureAwait(false);
        }
        catch (SessionStoreException exception)
        {
            await TryRevokeServerSessionAsync(session).ConfigureAwait(false);
            throw MapStorageException(exception);
        }
        catch (OperationCanceledException)
        {
            await TryRevokeServerSessionAsync(session).ConfigureAwait(false);
            throw;
        }

        return session;
    }

    public async Task<IReadOnlyList<SessionProfile>> GetSavedSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _sessionStore.GetProfilesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SessionStoreException exception)
        {
            throw MapStorageException(exception);
        }
    }

    public async Task<AuthenticatedSession> RestoreAsync(
        SessionProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        EnsureSecureConnection(profile.Server.BaseUri);

        AuthenticatedSession? session;
        try
        {
            session = await _sessionStore.GetAsync(profile, cancellationToken).ConfigureAwait(false);
        }
        catch (SessionStoreException exception)
        {
            throw MapStorageException(exception);
        }

        if (session is null)
        {
            await RemoveIgnoringCancellationAsync(profile, null).ConfigureAwait(false);
            throw new AuthenticationException(
                AuthenticationError.RevokedSession,
                "The saved credential is missing. Sign in again to restore this account.");
        }

        EnsureSecureConnection(session.Server.BaseUri);
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Get,
            new Uri(session.Server.BaseUri, "Users/Me"),
            session.AccessToken);
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            await RemoveIgnoringCancellationAsync(session.Profile, session.AccessToken).ConfigureAwait(false);
            throw new AuthenticationException(
                AuthenticationError.RevokedSession,
                "This Jellyfin session is no longer valid. Sign in again to continue.");
        }

        EnsureSuccess(response);
        await ValidateUserResponseAsync(response, session.UserId, cancellationToken)
            .ConfigureAwait(false);
        return session;
    }

    public async Task LogoutAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        AuthenticationException? serverFailure = null;
        try
        {
            EnsureSecureConnection(session.Server.BaseUri);
            using var request = CreateAuthenticatedRequest(
                HttpMethod.Post,
                new Uri(session.Server.BaseUri, "Sessions/Logout"),
                session.AccessToken);
            using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode
                && response.StatusCode is not HttpStatusCode.Unauthorized
                && response.StatusCode is not HttpStatusCode.Forbidden)
            {
                serverFailure = CreateUnexpectedStatus(response);
            }
        }
        catch (AuthenticationException exception)
        {
            serverFailure = exception;
        }
        finally
        {
            await RemoveIgnoringCancellationAsync(session.Profile, session.AccessToken).ConfigureAwait(false);
        }

        if (serverFailure is not null)
        {
            throw new AuthenticationException(
                serverFailure.Error,
                "The local credential was removed, but Jellyfin could not complete sign-out.",
                serverFailure);
        }
    }

    public async Task RemoveAsync(
        SessionProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        try
        {
            await _sessionStore.RemoveAsync(profile, cancellationToken).ConfigureAwait(false);
        }
        catch (SessionStoreException exception)
        {
            throw MapStorageException(exception);
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.TryAddWithoutValidation("Authorization", BuildAuthorizationHeader());
        return request;
    }

    private HttpRequestMessage CreateAuthenticatedRequest(
        HttpMethod method,
        Uri uri,
        string accessToken)
    {
        var request = CreateRequest(method, uri);
        request.Headers.TryAddWithoutValidation("X-Emby-Token", accessToken);
        return request;
    }

    private string BuildAuthorizationHeader() =>
        $"MediaBrowser Client=\"{Escape(_clientIdentity.ClientName)}\", "
        + $"Device=\"{Escape(_clientIdentity.DeviceName)}\", "
        + $"DeviceId=\"{Escape(_clientIdentity.DeviceId)}\", "
        + $"Version=\"{Escape(_clientIdentity.Version)}\"";

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AuthenticationException(
                AuthenticationError.Network,
                "Jellyfin did not respond before the request timed out.");
        }
        catch (HttpRequestException exception)
        {
            throw new AuthenticationException(
                AuthenticationError.Network,
                "Jellyfin could not be reached. Check the server and network connection.",
                exception);
        }
    }

    private static async Task<AuthenticationResult> ReadAuthenticationResultAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        AuthenticationResponse? result;
        try
        {
            result = await response.Content
                .ReadFromJsonAsync<AuthenticationResponse>(SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new AuthenticationException(
                AuthenticationError.InvalidResponse,
                "Jellyfin returned an invalid authentication response.",
                exception);
        }

        var accessToken = result?.AccessToken;
        var userId = result?.User?.Id;
        var username = result?.User?.Name;
        if (string.IsNullOrWhiteSpace(accessToken)
            || string.IsNullOrWhiteSpace(userId)
            || string.IsNullOrWhiteSpace(username))
        {
            throw new AuthenticationException(
                AuthenticationError.InvalidResponse,
                "Jellyfin returned an incomplete authentication response.");
        }

        return new AuthenticationResult(accessToken, userId, username);
    }

    private static async Task ValidateUserResponseAsync(
        HttpResponseMessage response,
        string expectedUserId,
        CancellationToken cancellationToken)
    {
        UserResult? user;
        try
        {
            user = await response.Content
                .ReadFromJsonAsync<UserResult>(SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new AuthenticationException(
                AuthenticationError.InvalidResponse,
                "Jellyfin returned invalid user information for the saved session.",
                exception);
        }

        if (user is null || !string.Equals(user.Id, expectedUserId, StringComparison.Ordinal))
        {
            throw new AuthenticationException(
                AuthenticationError.InvalidResponse,
                "Jellyfin returned unexpected user information for the saved session.");
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw CreateUnexpectedStatus(response);
        }
    }

    private static AuthenticationException CreateUnexpectedStatus(HttpResponseMessage response) =>
        new(
            AuthenticationError.UnexpectedStatus,
            $"Jellyfin returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");

    private async Task RemoveIgnoringCancellationAsync(SessionProfile profile, string? expectedAccessToken)
    {
        try
        {
            if (!await _sessionStore.RemoveIfMatchesAsync(profile, expectedAccessToken, CancellationToken.None)
                .ConfigureAwait(false))
            {
                throw new AuthenticationException(
                    AuthenticationError.SessionChanged,
                    "This account was signed in again while the request was running. The newer saved session was kept. Select it again to continue.");
            }
        }
        catch (SessionStoreException exception)
        {
            throw MapStorageException(exception);
        }
    }

    private static AuthenticationException MapStorageException(SessionStoreException exception) =>
        new(
            AuthenticationError.SecureStorageUnavailable,
            exception.Message,
            exception);

    private async Task TryRevokeServerSessionAsync(AuthenticatedSession session)
    {
        try
        {
            using var request = CreateAuthenticatedRequest(
                HttpMethod.Post,
                new Uri(session.Server.BaseUri, "Sessions/Logout"),
                session.AccessToken);
            using var response = await SendAsync(request, CancellationToken.None).ConfigureAwait(false);
        }
        catch (AuthenticationException)
        {
            // Preserve the storage failure that made the token inaccessible locally.
        }
    }

    internal static SocketsHttpHandler CreateSecureTransport() =>
        new()
        {
            AllowAutoRedirect = false,
        };

    private static void EnsureSecureConnection(Uri serverUri)
    {
        var isSecure = serverUri.Scheme == Uri.UriSchemeHttps;
        var isLoopbackHttp = serverUri.Scheme == Uri.UriSchemeHttp && serverUri.IsLoopback;
        if (!isSecure && !isLoopbackHttp)
        {
            throw new AuthenticationException(
                AuthenticationError.InsecureConnection,
                "Sign-in requires HTTPS so passwords and access tokens are not exposed on the network.");
        }
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private sealed record AuthenticationResponse(string? AccessToken, UserResult? User);

    private sealed record AuthenticationResult(string AccessToken, string UserId, string Username);

    private sealed record UserResult(string? Id, string? Name);
}
