namespace Cindara.Core.Authentication;

public interface ISessionStore
{
    Task<IReadOnlyList<SessionProfile>> GetProfilesAsync(
        CancellationToken cancellationToken = default);

    Task<AuthenticatedSession?> GetAsync(
        SessionProfile profile,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(
        SessionProfile profile,
        CancellationToken cancellationToken = default);

    // Atomically removes only the expected token (null means missing).
    // Returns false when a different token or bound server address is now stored.
    Task<bool> RemoveIfMatchesAsync(
        SessionProfile profile,
        string? expectedAccessToken,
        CancellationToken cancellationToken = default);
}
