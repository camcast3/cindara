using System.Net;
using System.Text;
using Cindara.Core.Authentication;
using Cindara.Core.Jellyfin;
using Cindara.Core.Models;

namespace Cindara.Core.Tests.Jellyfin;

public sealed class JellyfinMediaPreviewClientTests
{
    [Fact]
    public void PreviewTransportDoesNotFollowRedirectsWithSessionHeaders()
    {
        using var transport = JellyfinMediaPreviewClient.CreateSecureTransport();

        Assert.False(transport.AllowAutoRedirect);
    }

    [Fact]
    public async Task GetHomeAsyncLoadsAuthenticatedMediaAndArtwork()
    {
        var handler = new PreviewHandler();
        var client = CreateClient(handler);

        var home = await client.GetHomeAsync(Session);

        var continuing = Assert.Single(home.ContinueWatching);
        Assert.Equal("Pilot", continuing.Name);
        Assert.Equal("Northstar · S1 E2", continuing.Subtitle);
        Assert.Equal(42.5, continuing.PlaybackProgress);
        Assert.Equal([1, 2, 3], continuing.Artwork);
        Assert.Equal("Northstar", home.Featured?.Name);
        Assert.Equal("S1 E2 · Pilot", home.Featured?.Subtitle);
        Assert.Equal([4, 5, 6], home.Featured?.Backdrop);
        Assert.Collection(
            home.RecentlyAddedLibraries,
            rail =>
            {
                Assert.Equal("Recently Added in TV Shows", rail.Title);
                var episode = Assert.Single(rail.Items);
                Assert.Equal("Second Nature", episode.Name);
                Assert.Equal("S1 E6 · Return Migration", episode.Subtitle);
            },
            rail =>
            {
                Assert.Equal("Recently Added in Movies", rail.Title);
                Assert.Equal("Moon Garden", Assert.Single(rail.Items).Name);
            },
            rail =>
            {
                Assert.Equal("Recently Added in Anime", rail.Title);
                Assert.Collection(
                    rail.Items,
                    series => Assert.Equal("Skyward", series.Name),
                    season =>
                    {
                        Assert.Equal("That Time I Got Reincarnated as a Slime", season.Name);
                        Assert.Equal("Season 3", season.Subtitle);
                    });
            });
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("secret-token", request.Token);
            Assert.Contains("Client=\"Cindara\"", request.Authorization, StringComparison.Ordinal);
            Assert.Contains("DeviceId=\"device-1\"", request.Authorization, StringComparison.Ordinal);
            Assert.Contains("Token=\"secret-token\"", request.Authorization, StringComparison.Ordinal);
        });
        Assert.DoesNotContain(
            handler.Requests,
            request => request.Uri.Query.Contains("secret-token", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetHomeAsyncReportsRejectedSession()
    {
        var client = CreateClient(new RejectingHandler());

        var exception = await Assert.ThrowsAsync<MediaPreviewException>(
            () => client.GetHomeAsync(Session));

        Assert.Equal(MediaPreviewError.AccessDenied, exception.Error);
    }

    private static readonly AuthenticatedSession Session = new(
        new ServerIdentity(
            "server-1",
            new Uri("https://media.example.com/jellyfin/"),
            "Living Room",
            "10.10.7",
            "Linux"),
        "user-1",
        "viewer",
        "secret-token");

    private static JellyfinMediaPreviewClient CreateClient(HttpMessageHandler handler) =>
        new(
            handler,
            new JellyfinClientIdentity("Cindara", "Test Device", "device-1", "1.0.0"));

    private sealed class PreviewHandler : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(
                request.RequestUri ?? throw new InvalidOperationException("Missing request URI."),
                request.Headers.GetValues("Authorization").Single(),
                request.Headers.GetValues("X-Emby-Token").Single()));

            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("/Items/Resume", StringComparison.Ordinal))
            {
                return Json(
                    """
                    {
                      "Items": [{
                        "Id": "episode-1",
                        "Name": "Pilot",
                        "Type": "Episode",
                        "SeriesName": "Northstar",
                        "SeriesId": "series-1",
                        "ParentIndexNumber": 1,
                        "IndexNumber": 2,
                        "ImageTags": { "Primary": "tag" },
                        "UserData": { "PlayedPercentage": 42.5 }
                      }]
                    }
                    """);
            }

            if (path.EndsWith("/Items/Latest", StringComparison.Ordinal))
            {
                var query = request.RequestUri.Query;
                var parentId = query.Contains("ParentId=tv", StringComparison.Ordinal)
                    ? "tv"
                    : query.Contains("ParentId=anime", StringComparison.Ordinal)
                        ? "anime"
                        : query.Contains("ParentId=movies", StringComparison.Ordinal)
                            ? "movies"
                            : null;
                return parentId switch
                {
                    "tv" => Json(
                        """[{"Id":"episode-2","Name":"Return Migration","Type":"Episode","SeriesName":"Second Nature","ParentIndexNumber":1,"IndexNumber":6,"ImageTags":{}}]"""),
                    "anime" => Json(
                        """[{"Id":"anime-1","Name":"Skyward","Type":"Series","ProductionYear":2025,"ImageTags":{}},{"Id":"season-3","Name":"Season 3","Type":"Season","SeriesName":"That Time I Got Reincarnated as a Slime","IndexNumber":3,"ImageTags":{}}]"""),
                    "movies" => Json(
                        """[{"Id":"movie-1","Name":"Moon Garden","Type":"Movie","ProductionYear":2026,"ImageTags":{}}]"""),
                    _ => throw new InvalidOperationException($"Unexpected parent: {parentId}"),
                };
            }

            if (path.EndsWith("/Views", StringComparison.Ordinal))
            {
                return Json(
                    """
                    {
                      "Items": [
                        {"Id":"anime","Name":"Anime","CollectionType":"tvshows"},
                        {"Id":"movies","Name":"Movies","CollectionType":"movies"},
                        {"Id":"tv","Name":"TV Shows","CollectionType":"tvshows"}
                      ]
                    }
                    """);
            }

            if (path.EndsWith("/Items/episode-1/Images/Thumb", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([1, 2, 3]),
                });
            }

            if (path.EndsWith("/Items/series-1/Images/Backdrop/0", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([4, 5, 6]),
                });
            }

            throw new InvalidOperationException($"Unexpected request: {request.RequestUri}");
        }

        private static Task<HttpResponseMessage> Json(string content) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class RejectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
    }

    private sealed record CapturedRequest(Uri Uri, string Authorization, string Token);
}
