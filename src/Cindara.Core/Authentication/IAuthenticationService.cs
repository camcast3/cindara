namespace Cindara.Core.Authentication;

public interface IAuthenticationService
{
    Task<AuthenticatedSession> AuthenticateAsync(
        AuthenticationRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SessionProfile>> GetSavedSessionsAsync(
        CancellationToken cancellationToken = default);

    Task<AuthenticatedSession> RestoreAsync(
        SessionProfile profile,
        CancellationToken cancellationToken = default);

    Task LogoutAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    // Removes a rejected token locally without contacting the server or deleting a newer credential.
    // Cleanup must complete even if the request that discovered the rejection was cancelled.
    Task InvalidateAsync(AuthenticatedSession session);

    Task RemoveAsync(
        SessionProfile profile,
        CancellationToken cancellationToken = default);
}
