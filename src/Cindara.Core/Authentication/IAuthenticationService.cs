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

    Task RemoveAsync(
        SessionProfile profile,
        CancellationToken cancellationToken = default);
}
