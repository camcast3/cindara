using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Cindara.Core.Authentication;
using Cindara.Core.Jellyfin;
using Cindara.Core.Models;

namespace Cindara.Core.Tests.Jellyfin;

public sealed class ContinueWatchingTests
{
    private static readonly AuthenticatedSession Session = new(
        new ServerIdentity("server", new Uri("https://media.example/jellyfin/"), "Media", "10.11", "Linux"),
        "viewer", "Viewer", "test-token");

    [Fact]
    public async Task EntireRowSortsByLatestSeriesPlaybackRatherThanResumeOrNextEpisodeDates()
    {
        using var handler = new Handler(
            [
                Episode("resume-a", "a", 35, lastPlayed: "2026-09-20T12:00:00Z"),
                Movie("movie", 50, lastPlayed: "2026-09-22T14:00:00Z"),
                Episode("resume-c", "c", 60, lastPlayed: "2026-09-19T12:00:00Z"),
            ],
            [
                Episode("next-b", "b", 0),
                Episode("next-d", "d", 0),
                Episode("no-history", "e", 0),
            ])
        {
            History = uri => uri.Query.Contains("ParentId=b&", StringComparison.Ordinal)
                ? [Episode("last-watched-b", "b", 100, played: true, lastPlayed: "2026-09-22T15:00:00Z")]
                : uri.Query.Contains("ParentId=c&", StringComparison.Ordinal)
                    ? [Episode("rewatched-special-c", "c", 100, played: true, lastPlayed: "2026-09-22T16:00:00Z")]
                    : uri.Query.Contains("ParentId=d&", StringComparison.Ordinal)
                        ? [Episode("last-watched-d", "d", 100, played: true, lastPlayed: "2026-09-21T12:00:00Z")]
                        : [],
        };
        using var client = Client(handler);

        var home = await client.GetHomeAsync(Session);

        Assert.Equal(["resume-c", "next-b", "movie", "next-d", "resume-a", "no-history"],
            home.ContinueWatching.Select(item => item.Id));
        Assert.Equal("resume-c", home.Featured?.Id);
        var historyRequests = handler.Requests.Where(uri => uri.AbsolutePath.EndsWith("/Items", StringComparison.Ordinal)).ToArray();
        Assert.Equal(5, historyRequests.Length);
        Assert.All(historyRequests, uri =>
        {
            Assert.Contains("SortBy=DatePlayed&SortOrder=Descending", uri.Query, StringComparison.Ordinal);
            Assert.Contains("Limit=1", uri.Query, StringComparison.Ordinal);
            Assert.Contains("IncludeItemTypes=Episode", uri.Query, StringComparison.Ordinal);
            Assert.DoesNotContain("IsPlayed=", uri.Query, StringComparison.Ordinal);
            Assert.DoesNotContain("Season=", uri.Query, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task AdvancingAnEpisodeKeepsTheSeriesRecencyAndSortsBeforeLimitingTheRow()
    {
        using var handler = new Handler(
            Enumerable.Range(0, 20).Select(index => Movie($"movie-{index}", 30, "2026-09-20T12:00:00Z")).ToArray(),
            [Episode("nearly-complete", "show", 95)],
            _ => [Episode("following", "show", 0)])
        {
            History = _ => [Episode("recent-playback", "show", 100, lastPlayed: "2026-09-22T15:00:00Z")],
        };
        using var client = Client(handler);

        var home = await client.GetHomeAsync(Session);

        Assert.Equal(20, home.ContinueWatching.Count);
        Assert.Equal("following", home.ContinueWatching[0].Id);
        Assert.DoesNotContain(home.ContinueWatching, item => item.Id == "movie-19");
        Assert.DoesNotContain(handler.Requests, uri => uri.AbsolutePath.Contains("/movie-19/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EqualPlaybackTimesKeepStableServerOrder()
    {
        using var handler = new Handler([Episode("resume-a", "a", 10)],
            [Episode("next-b", "b", 0), Episode("next-c", "c", 0)])
        {
            History = _ => [new { UserData = new { LastPlayedDate = "2026-09-22T15:00:00Z" } }],
        };
        using var client = Client(handler);
        Assert.Equal(["resume-a", "next-b", "next-c"],
            (await client.GetHomeAsync(Session)).ContinueWatching.Select(item => item.Id));
    }

    [Fact]
    public async Task SeasonAndSeriesContainersNeverEnterContinueWatchingOrCrowdOutTheirEpisodes()
    {
        var season = new
        {
            Id = "season",
            Name = "Season 1",
            Type = "Season",
            SeriesId = "show",
            UserData = new { PlayedPercentage = 20 },
        };
        var series = new { Id = "show", Name = "Show", Type = "Series", UserData = new { PlayedPercentage = 40 } };
        using var handler = new Handler([season, series, Movie("movie", 30)],
            [season, series, Episode("next-episode", "show", 0)]);
        using var client = Client(handler);

        var home = await client.GetHomeAsync(Session);

        Assert.Equal(["movie", "next-episode"], home.ContinueWatching.Select(item => item.Id));
        Assert.All(home.ContinueWatching, item => Assert.True(item.MediaType is "Movie" or "Episode"));
        var resume = Assert.Single(handler.Requests, uri => uri.AbsolutePath.EndsWith("/Resume", StringComparison.Ordinal));
        Assert.Contains("IncludeItemTypes=Movie,Episode", resume.Query, StringComparison.Ordinal);
        Assert.DoesNotContain(handler.Requests, uri => uri.AbsolutePath.Contains("/season/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResumeAndNextUpBecomeOneOrderedCardPerShowBeforeArtworkLoading()
    {
        using var handler = new Handler(
            [
                Episode("a-older", "a", 25, lastPlayed: "2026-09-21T10:00:00Z"),
                Episode("a-current", "a", 40, lastPlayed: "2026-09-22T10:00:00Z"),
                Movie("movie", 65),
                Episode("b-current", "b", 20),
            ],
            [
                Episode("a-next", "a", 0),
                Episode("b-current", "b", 20),
                Episode("c-next", "c", 0),
                Episode("c-duplicate", "c", 0),
            ]);
        using var client = Client(handler);

        var home = await client.GetHomeAsync(Session);

        Assert.Equal(["a-current", "movie", "b-current", "c-next"], home.ContinueWatching.Select(item => item.Id));
        Assert.Equal("a-current", home.Featured?.Id);
        Assert.Equal("Show a", home.Featured?.Name);
        Assert.DoesNotContain(handler.Requests, uri => uri.AbsolutePath.Contains("a-older", StringComparison.Ordinal)
            || uri.AbsolutePath.Contains("a-next", StringComparison.Ordinal)
            || uri.AbsolutePath.Contains("c-duplicate", StringComparison.Ordinal));
        var nextUp = Assert.Single(handler.Requests, uri => uri.AbsolutePath.EndsWith("/NextUp", StringComparison.Ordinal));
        Assert.Contains("EnableResumable=true", nextUp.Query, StringComparison.Ordinal);
        Assert.Contains("DisableFirstEpisode=true", nextUp.Query, StringComparison.Ordinal);
        Assert.Contains("EnableRewatching=false", nextUp.Query, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(50, false)]
    [InlineData(89.999, false)]
    [InlineData(90, true)]
    [InlineData(90.001, true)]
    [InlineData(100, true)]
    public async Task NinetyPercentIsTheExactEpisodeCutoff(double percentage, bool advance)
    {
        using var handler = new Handler(
            [Episode("current", "show", percentage), Episode("older", "show", 10)],
            [Episode("current", "show", percentage)],
            _ => [Episode("following", "show", 0)]);
        using var client = Client(handler);

        var item = Assert.Single((await client.GetHomeAsync(Session)).ContinueWatching);

        Assert.Equal(advance ? "following" : "current", item.Id);
        Assert.Equal(advance ? 0 : percentage, item.PlaybackProgress);
        Assert.Equal(advance ? 1 : 0, handler.Requests.Count(uri => uri.AbsolutePath.EndsWith("/Episodes", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task NearCompleteEpisodesAdvanceAcrossSeasonsAndSkipPlayedOrNearCompleteEpisodes()
    {
        using var handler = new Handler(
            [Episode("season-one-finale", "show", 95)],
            [Episode("season-one-finale", "show", 95)],
            _ => [
                Episode("already-played", "show", 0, played: true),
                Episode("also-near-complete", "show", 91),
                Episode("season-two-premiere", "show", 0, season: 2),
            ]);
        using var client = Client(handler);

        var item = Assert.Single((await client.GetHomeAsync(Session)).ContinueWatching);

        Assert.Equal("season-two-premiere", item.Id);
        Assert.Equal(2, item.Metadata?.SeasonNumber);
        var uri = Assert.Single(handler.Requests, uri => uri.AbsolutePath.EndsWith("/Episodes", StringComparison.Ordinal));
        Assert.Contains("StartItemId=season-one-finale", uri.Query, StringComparison.Ordinal);
        Assert.Contains("StartIndex=1", uri.Query, StringComparison.Ordinal);
        Assert.Contains("IsMissing=false", uri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("Season=", uri.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(89.99, true)]
    [InlineData(90, false)]
    [InlineData(100, false)]
    public async Task MoviesAtOrAboveCutoffAreOmittedWithoutEpisodeRequests(double percentage, bool visible)
    {
        using var handler = new Handler([Movie("movie", percentage)], []);
        using var client = Client(handler);
        var home = await client.GetHomeAsync(Session);
        Assert.Equal(visible ? 1 : 0, home.ContinueWatching.Count);
        Assert.DoesNotContain(handler.Requests, uri => uri.AbsolutePath.EndsWith("/Episodes", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(8999L, "current")]
    [InlineData(9000L, "following")]
    public async Task PlaybackTicksSupplyPercentageWhenServerOmitsIt(long position, string expected)
    {
        var item = new
        {
            Id = "current",
            Name = "Current",
            Type = "Episode",
            SeriesId = "show",
            RunTimeTicks = 10000,
            UserData = new { PlaybackPositionTicks = position },
        };
        using var handler = new Handler([item], [], _ => [Episode("following", "show", 0)]);
        using var client = Client(handler);

        var card = Assert.Single((await client.GetHomeAsync(Session)).ContinueWatching);

        Assert.Equal(expected, card.Id);
        Assert.Equal(expected == "current" ? 89.99 : 0, card.PlaybackProgress!.Value, precision: 6);
    }

    [Fact]
    public async Task SeriesIdsKeepDifferentShowsWithTheSameTitleSeparate()
    {
        var first = new { Id = "first", Name = "Pilot", Type = "Episode", SeriesId = "original", SeriesName = "Same title" };
        var second = new { Id = "second", Name = "Pilot", Type = "Episode", SeriesId = "remake", SeriesName = "Same title" };
        using var handler = new Handler([first], [second]);
        using var client = Client(handler);

        Assert.Equal(["first", "second"], (await client.GetHomeAsync(Session)).ContinueWatching.Select(item => item.Id));
    }

    [Fact]
    public async Task NoFollowingEpisodeDoesNotResurrectAnOlderResumeOrNextUpCard()
    {
        using var handler = new Handler(
            [Episode("finished", "show", 100), Episode("stale", "show", 20)],
            [Episode("stale", "show", 20)], _ => []);
        using var client = Client(handler);

        var home = await client.GetHomeAsync(Session);

        Assert.Empty(home.ContinueWatching);
        Assert.Null(home.Featured);
        Assert.DoesNotContain(handler.Requests, uri => uri.AbsolutePath.Contains("/Images/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NextUpOnlyStillProducesContinueWatchingAndAppliesTheSameCutoff()
    {
        using var handler = new Handler([], [Episode("current", "show", 90)], _ => [Episode("following", "show", 0)]);
        using var client = Client(handler);
        var home = await client.GetHomeAsync(Session);
        Assert.Equal("following", Assert.Single(home.ContinueWatching).Id);
        Assert.Equal("Show show", home.Featured?.Name);
    }

    [Fact]
    public async Task FollowingEpisodeLookupPagesPastAlreadyPlayedEpisodes()
    {
        using var handler = new Handler([Episode("current", "show", 90)], [], uri =>
            uri.Query.Contains("StartItemId=current", StringComparison.Ordinal)
                ? Enumerable.Range(1, 20).Select(index => Episode($"played-{index}", "show", 100)).ToArray()
                : [Episode("following", "show", 0)]);
        using var client = Client(handler);
        Assert.Equal("following", Assert.Single((await client.GetHomeAsync(Session)).ContinueWatching).Id);
        Assert.Contains(handler.Requests, uri => uri.Query.Contains("StartItemId=played-20", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MalformedFollowingEpisodeCannotRepeatTheCurrentEpisode()
    {
        using var handler = new Handler([Episode("current", "show", 90)], [], _ => [Episode("current", "show", 90)]);
        using var client = Client(handler);
        var exception = await Assert.ThrowsAsync<MediaPreviewException>(() => client.GetHomeAsync(Session));
        Assert.Equal(MediaPreviewError.InvalidResponse, exception.Error);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, MediaPreviewError.AccessDenied)]
    [InlineData(HttpStatusCode.ServiceUnavailable, MediaPreviewError.UnexpectedStatus)]
    public async Task FollowingEpisodeFailureIsNotSilentlyTreatedAsEndOfSeries(HttpStatusCode status, MediaPreviewError error)
    {
        using var handler = new Handler([Episode("current", "show", 90)], [], _ => []) { FollowingStatus = status };
        using var client = Client(handler);
        var exception = await Assert.ThrowsAsync<MediaPreviewException>(() => client.GetHomeAsync(Session));
        Assert.Equal(error, exception.Error);
    }

    [Fact]
    public async Task UnifiedRowKeepsItsTwentyCardLimitAndDoesNotFetchDiscardedArtwork()
    {
        using var handler = new Handler(Enumerable.Range(0, 20).Select(index => Movie($"resume-{index}", 50)).ToArray(),
            Enumerable.Range(0, 20).Select(index => Episode($"next-{index}", $"show-{index}", 0)).ToArray());
        using var client = Client(handler);
        var home = await client.GetHomeAsync(Session);
        Assert.Equal(20, home.ContinueWatching.Count);
        Assert.DoesNotContain(handler.Requests, uri => uri.AbsolutePath.Contains("/next-", StringComparison.Ordinal));
    }

    private static object Movie(string id, double percentage, string? lastPlayed = null) => new
    {
        Id = id,
        Name = id,
        Type = "Movie",
        ImageTags = new { Primary = "tag" },
        UserData = new { PlayedPercentage = percentage, LastPlayedDate = lastPlayed },
    };

    private static object Episode(string id, string series, double percentage, bool played = false,
        string? lastPlayed = null, int season = 1) => new
        {
            Id = id,
            Name = id,
            Type = "Episode",
            SeriesId = series,
            SeriesName = $"Show {series}",
            ParentIndexNumber = season,
            IndexNumber = 2,
            ImageTags = new { Primary = "tag" },
            UserData = new { PlayedPercentage = percentage, Played = played, LastPlayedDate = lastPlayed },
        };

    private static JellyfinMediaPreviewClient Client(HttpMessageHandler handler) =>
        new(handler, new JellyfinClientIdentity("Cindara", "Tests", "device", "1.0"));

    private sealed class Handler(object[] resume, object[] nextUp, Func<Uri, object[]>? following = null) : HttpMessageHandler
    {
        public ConcurrentQueue<Uri> Requests { get; } = new();
        public HttpStatusCode FollowingStatus { get; init; } = HttpStatusCode.OK;
        public Func<Uri, object[]> History { get; init; } = _ => [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Requests.Enqueue(uri);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(Session.AccessToken, request.Headers.GetValues("X-Emby-Token").Single());
            Assert.StartsWith("/jellyfin/", uri.AbsolutePath, StringComparison.Ordinal);
            Assert.DoesNotContain(Session.AccessToken, uri.ToString(), StringComparison.Ordinal);
            if (uri.AbsolutePath.Contains("/Images/", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
            }

            var items = uri.AbsolutePath switch
            {
                "/jellyfin/Users/viewer/Items/Resume" => resume,
                "/jellyfin/Shows/NextUp" => nextUp,
                "/jellyfin/Users/viewer/Views" => [],
                "/jellyfin/Users/viewer/Items" => History(uri),
                _ when uri.AbsolutePath.EndsWith("/Episodes", StringComparison.Ordinal) => following!(uri),
                _ => throw new InvalidOperationException($"Unexpected request: {uri}"),
            };
            var status = uri.AbsolutePath.EndsWith("/Episodes", StringComparison.Ordinal) ? FollowingStatus : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { Items = items }), Encoding.UTF8, "application/json"),
            });
        }
    }
}
