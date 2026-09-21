using Cindara.Core.Authentication;
using Cindara.Core.Models;

namespace Cindara.Core.Tests.Authentication;

public sealed class InMemorySessionStoreTests
{
    [Fact]
    public async Task SaveKeepsUsersAndServersIndependent()
    {
        var store = new InMemorySessionStore();
        var first = CreateSession("server-a", "user-a", "first-token");
        var second = CreateSession("server-b", "user-a", "second-token");

        await store.SaveAsync(first);
        await store.SaveAsync(second);

        Assert.Equal(first, await store.GetAsync(first.Profile));
        Assert.Equal(second, await store.GetAsync(second.Profile));
        Assert.Equal(2, (await store.GetProfilesAsync()).Count);
    }

    [Fact]
    public async Task SaveReplacesSessionForSameServerAndUser()
    {
        var store = new InMemorySessionStore();
        await store.SaveAsync(CreateSession("server-a", "user-a", "old-token"));
        var replacement = CreateSession("server-a", "user-a", "new-token");

        await store.SaveAsync(replacement);

        Assert.Equal(replacement, await store.GetAsync(replacement.Profile));
        Assert.Single(await store.GetProfilesAsync());
    }

    [Fact]
    public async Task RemoveOnlyRemovesMatchingSession()
    {
        var store = new InMemorySessionStore();
        var session = CreateSession("server-a", "user-a", "token");
        await store.SaveAsync(session);

        Assert.True(await store.RemoveAsync(session.Profile));
        Assert.False(await store.RemoveAsync(session.Profile));
        Assert.Empty(await store.GetProfilesAsync());
    }

    [Fact]
    public async Task ConditionalRemovalPreservesNewServerBindingEvenWhenTokenMatches()
    {
        var store = new InMemorySessionStore();
        var original = CreateSession("server-a", "user-a", "token");
        await store.SaveAsync(original);
        var replacement = original with
        {
            Server = original.Server with { BaseUri = new Uri("https://replacement.example.com/") },
        };
        await store.SaveAsync(replacement);

        Assert.False(await store.RemoveIfMatchesAsync(original.Profile, original.AccessToken));
        Assert.Equal(replacement, await store.GetAsync(replacement.Profile));
        Assert.True(await store.RemoveIfMatchesAsync(replacement.Profile, replacement.AccessToken));
        Assert.Null(await store.GetAsync(replacement.Profile));
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
