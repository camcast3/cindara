using Cindara.Core.Authentication;
using Cindara.Core.Models;

namespace Cindara.Core.Tests.Authentication;

public sealed class InMemorySessionStoreTests
{
    [Fact]
    public void SaveKeepsUsersAndServersIndependent()
    {
        var store = new InMemorySessionStore();
        var first = CreateSession("server-a", "user-a", "first-token");
        var second = CreateSession("server-b", "user-a", "second-token");

        store.Save(first);
        store.Save(second);

        Assert.Equal(first, store.Find("server-a", "user-a"));
        Assert.Equal(second, store.Find("server-b", "user-a"));
        Assert.Equal(2, store.Sessions.Count);
    }

    [Fact]
    public void SaveReplacesSessionForSameServerAndUser()
    {
        var store = new InMemorySessionStore();
        store.Save(CreateSession("server-a", "user-a", "old-token"));
        var replacement = CreateSession("server-a", "user-a", "new-token");

        store.Save(replacement);

        Assert.Equal(replacement, store.Find("server-a", "user-a"));
        Assert.Single(store.Sessions);
    }

    [Fact]
    public void RemoveOnlyRemovesMatchingSession()
    {
        var store = new InMemorySessionStore();
        store.Save(CreateSession("server-a", "user-a", "token"));

        Assert.True(store.Remove("server-a", "user-a"));
        Assert.False(store.Remove("server-a", "user-a"));
        Assert.Empty(store.Sessions);
    }

    private static AuthenticatedSession CreateSession(
        string serverId,
        string userId,
        string token) =>
        new(
            new ServerIdentity(
                serverId,
                new Uri($"https://{serverId}.example.com"),
                serverId,
                "10.10.7",
                "Linux"),
            userId,
            "viewer",
            token);
}
