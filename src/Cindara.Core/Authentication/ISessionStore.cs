namespace Cindara.Core.Authentication;

public interface ISessionStore
{
    IReadOnlyCollection<AuthenticatedSession> Sessions { get; }

    AuthenticatedSession? Find(string serverId, string userId);

    void Save(AuthenticatedSession session);

    bool Remove(string serverId, string userId);
}
