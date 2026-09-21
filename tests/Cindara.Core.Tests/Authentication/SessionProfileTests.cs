using Cindara.Core.Authentication;
using Cindara.Core.Models;

namespace Cindara.Core.Tests.Authentication;

public sealed class SessionProfileTests
{
    [Fact]
    public void DisplayNameDistinguishesServersWithMatchingNames()
    {
        var first = CreateProfile("https://first.example.com/jellyfin/");
        var second = CreateProfile("https://second.example.com/jellyfin/");

        Assert.NotEqual(first.DisplayName, second.DisplayName);
        Assert.Contains("https://first.example.com/jellyfin", first.DisplayName, StringComparison.Ordinal);
        Assert.Contains("https://second.example.com/jellyfin", second.DisplayName, StringComparison.Ordinal);
    }

    private static SessionProfile CreateProfile(string uri) =>
        new(
            new ServerIdentity(
                "server-1",
                new Uri(uri),
                "Jellyfin",
                "10.10.7",
                "Linux"),
            "user-1",
            "viewer");
}
