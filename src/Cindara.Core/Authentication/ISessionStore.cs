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
}
