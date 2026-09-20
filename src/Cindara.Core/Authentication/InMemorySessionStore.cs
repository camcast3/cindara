namespace Cindara.Core.Authentication;

public sealed class InMemorySessionStore : ISessionStore
{
    private readonly Dictionary<SessionKey, AuthenticatedSession> _sessions = [];
    private readonly object _sync = new();

    public IReadOnlyCollection<AuthenticatedSession> Sessions
    {
        get
        {
            lock (_sync)
            {
                return [.. _sessions.Values];
            }
        }
    }

    public AuthenticatedSession? Find(string serverId, string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        lock (_sync)
        {
            return _sessions.GetValueOrDefault(new SessionKey(serverId, userId));
        }
    }

    public void Save(AuthenticatedSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        lock (_sync)
        {
            _sessions[new SessionKey(session.Server.Id, session.UserId)] = session;
        }
    }

    public bool Remove(string serverId, string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        lock (_sync)
        {
            return _sessions.Remove(new SessionKey(serverId, userId));
        }
    }

    private sealed record SessionKey(string ServerId, string UserId);
}
