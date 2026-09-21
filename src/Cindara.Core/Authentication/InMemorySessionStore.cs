namespace Cindara.Core.Authentication;

public sealed class InMemorySessionStore : ISessionStore
{
    private readonly Dictionary<SessionKey, AuthenticatedSession> _sessions = [];
    private readonly object _sync = new();

    public Task<IReadOnlyList<SessionProfile>> GetProfilesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            IReadOnlyList<SessionProfile> profiles = [.. _sessions.Values.Select(session => session.Profile)];
            return Task.FromResult(profiles);
        }
    }

    public Task<AuthenticatedSession?> GetAsync(
        SessionProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            return Task.FromResult(
                _sessions.GetValueOrDefault(new SessionKey(profile.Server.Id, profile.UserId)));
        }
    }

    public Task SaveAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            _sessions[new SessionKey(session.Server.Id, session.UserId)] = session;
        }

        return Task.CompletedTask;
    }

    public Task<bool> RemoveAsync(
        SessionProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            return Task.FromResult(
                _sessions.Remove(new SessionKey(profile.Server.Id, profile.UserId)));
        }
    }

    public Task<bool> RemoveIfMatchesAsync(
        SessionProfile profile,
        string? expectedAccessToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            var key = new SessionKey(profile.Server.Id, profile.UserId);
            if (!string.Equals(_sessions.GetValueOrDefault(key)?.AccessToken, expectedAccessToken, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            _sessions.Remove(key);
            return Task.FromResult(true);
        }
    }

    private sealed record SessionKey(string ServerId, string UserId);
}
