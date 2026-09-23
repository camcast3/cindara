using System.Net;
using System.Text;
using System.Text.Json;
using Cindara.Core.Authentication;
using Cindara.Core.Jellyfin;
using Cindara.Core.Models;

namespace Cindara.Core.Tests.Jellyfin;

public sealed class LibraryBrowsingTests
{
    private static readonly AuthenticatedSession Session = new(
        new ServerIdentity("server", new Uri("https://media.example/jellyfin/"), "Media", "10.11", "Linux"),
        "user", "Viewer", "private-token");
    private static readonly MediaLibrary Library = new("movies&other=1", "Movies", "movies");

    [Fact]
    public async Task HomeRequestsOnlySupportedVideoLibraries()
    {
        using var handler = new Handler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(Json(path switch
            {
                "/jellyfin/Users/user/Views" => """
                    {"Items":[
                      {"Id":"music","Name":"Music","CollectionType":"music"},
                      {"Id":"collections","Name":"Collections","CollectionType":"boxsets"},
                      {"Id":"people","Name":"People","CollectionType":"people"},
                      {"Id":"unknown","Name":"Unknown"},
                      {"Id":"anime","Name":"Anime","CollectionType":"tvshows"},
                      {"Id":"movies","Name":"Movies","CollectionType":"movies"},
                      {"Id":"tv","Name":"TV","CollectionType":"tvshows"}
                    ]}
                    """,
                "/jellyfin/Shows/NextUp" => """{"Items":[{"Id":"episode","Name":"Next episode","Type":"Episode","SeriesName":"A show"}]}""",
                "/jellyfin/Users/user/Items/Resume" => """{"Items":[]}""",
                "/jellyfin/Users/user/Items" => """{"Items":[]}""",
                "/jellyfin/Users/user/Items/Latest" => "[]",
                _ => "{}",
            }, path.Contains("/Images/", StringComparison.Ordinal) ? HttpStatusCode.NotFound : HttpStatusCode.OK));
        });
        using var client = Client(handler);

        var home = await client.GetHomeAsync(Session);

        Assert.Equal("episode", Assert.Single(home.ContinueWatching).Id);
        Assert.Equal("episode", home.Featured?.Id);
        Assert.Equal(["tv", "movies", "anime"], home.Libraries.Select(library => library.Id));
        Assert.Equal(["tv", "movies", "anime"], home.RecentlyAddedLibraries.Select(rail => rail.LibraryId));
        var latest = handler.Requests.Where(uri => uri.AbsolutePath.EndsWith("/Latest", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3, latest.Length);
        Assert.All(latest, uri => Assert.True(
            uri.Query.Contains("ParentId=tv&", StringComparison.Ordinal)
            || uri.Query.Contains("ParentId=movies&", StringComparison.Ordinal)
            || uri.Query.Contains("ParentId=anime&", StringComparison.Ordinal)));
        Assert.Contains(handler.Requests, uri => uri.Query.Contains("UserId=user", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("movies", "IncludeItemTypes=Movie")]
    [InlineData("tvshows", "IncludeItemTypes=Series")]
    public async Task PagesUseAuthenticatedStableBoundedQueries(string collectionType, string typeQuery)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json(PageJson(40, 7, 47))));
        using var client = Client(handler);

        var page = await client.GetLibraryPageAsync(Session, Library with { CollectionType = collectionType }, 40);

        Assert.Equal(40, page.StartIndex);
        Assert.Equal(47, page.TotalRecordCount);
        Assert.Equal(7, page.Items.Count);
        Assert.False(page.HasNextPage);
        var uri = Assert.Single(handler.Requests);
        Assert.Equal("/jellyfin/Users/user/Items", uri.AbsolutePath);
        Assert.Contains("ParentId=movies%26other%3D1", uri.Query, StringComparison.Ordinal);
        Assert.Contains("StartIndex=40", uri.Query, StringComparison.Ordinal);
        Assert.Contains("Limit=40", uri.Query, StringComparison.Ordinal);
        Assert.Contains("SortBy=SortName&", uri.Query, StringComparison.Ordinal);
        Assert.Contains(typeQuery, uri.Query, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("boxsets")]
    [InlineData("people")]
    [InlineData("music")]
    [InlineData("")]
    [InlineData(null)]
    public async Task UnsupportedTypesCannotIssueLibraryPageRequests(string? collectionType)
    {
        using var handler = new Handler((_, _) => throw new InvalidOperationException("No request expected."));
        using var client = Client(handler);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.GetLibraryPageAsync(Session, Library with { CollectionType = collectionType }, 0));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("Collections", "movies", true)]
    [InlineData("People", "tvshows", true)]
    [InlineData("TV", "boxsets", false)]
    [InlineData("Movies", "people", false)]
    [InlineData("Custom Anime", "tvshows", true)]
    public void SupportIsBasedOnLibraryTypeNotDisplayName(string name, string type, bool supported)
    {
        Assert.Equal(supported, new MediaLibrary("library", name, type).IsSupportedVideoLibrary);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"Items":[]}""")]
    [InlineData("""{"Items":[],"TotalRecordCount":1}""")]
    [InlineData("""{"Items":[],"TotalRecordCount":-1}""")]
    [InlineData("""{"Items":[{"Id":"one"}],"TotalRecordCount":1}""")]
    [InlineData("""{"Items":[null],"TotalRecordCount":1}""")]
    [InlineData("""{"Items":[{"Id":"one","Name":"One"}],"TotalRecordCount":0}""")]
    public async Task MalformedPagesAreErrorsInsteadOfEmptySuccess(string json)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json(json)));
        using var client = Client(handler);
        var exception = await Assert.ThrowsAsync<MediaPreviewException>(() => client.GetLibraryPageAsync(Session, Library, 0));
        Assert.Equal(MediaPreviewError.InvalidResponse, exception.Error);
    }

    [Fact]
    public async Task OversizedPageIsRejectedBeforeArtworkIsRequested()
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json(PageJson(0, 41, 41, artwork: true))));
        using var client = Client(handler);
        var exception = await Assert.ThrowsAsync<MediaPreviewException>(() => client.GetLibraryPageAsync(Session, Library, 0));
        Assert.Equal(MediaPreviewError.InvalidResponse, exception.Error);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task LibraryShrinkingBelowRequestedOffsetIsAnErrorInsteadOfAnEmptyPage()
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json(PageJson(0, 0, 47))));
        using var client = Client(handler);
        var exception = await Assert.ThrowsAsync<MediaPreviewException>(() =>
            client.GetLibraryPageAsync(Session, Library, 80));
        Assert.Equal(MediaPreviewError.InvalidResponse, exception.Error);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(40, 40)]
    [InlineData(40, 0)]
    [InlineData(80, 40)]
    public async Task NonInitialEmptyPageAtOrBeyondTotalIsRejected(int start, int total)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json(PageJson(0, 0, total))));
        using var client = Client(handler);
        var exception = await Assert.ThrowsAsync<MediaPreviewException>(() => client.GetLibraryPageAsync(Session, Library, start));
        Assert.Equal(MediaPreviewError.InvalidResponse, exception.Error);
    }

    [Fact]
    public async Task EmptyLibraryHasExplicitZeroTotal()
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json(PageJson(0, 0, 0))));
        using var client = Client(handler);
        var page = await client.GetLibraryPageAsync(Session, Library, 0);
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalRecordCount);
        Assert.False(page.HasNextPage);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, MediaPreviewError.AccessDenied)]
    [InlineData(HttpStatusCode.Forbidden, MediaPreviewError.AccessDenied)]
    [InlineData(HttpStatusCode.ServiceUnavailable, MediaPreviewError.UnexpectedStatus)]
    public async Task PageFailuresStayExplicit(HttpStatusCode status, MediaPreviewError error)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json("{}", status)));
        using var client = Client(handler);
        var exception = await Assert.ThrowsAsync<MediaPreviewException>(() => client.GetLibraryPageAsync(Session, Library, 0));
        Assert.Equal(error, exception.Error);
        Assert.DoesNotContain(Session.AccessToken, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OfflinePageReportsNetworkFailure()
    {
        using var handler = new Handler((_, _) => throw new HttpRequestException());
        using var client = Client(handler);
        var exception = await Assert.ThrowsAsync<MediaPreviewException>(() => client.GetLibraryPageAsync(Session, Library, 0));
        Assert.Equal(MediaPreviewError.Network, exception.Error);
    }

    [Fact]
    public async Task ImageCacheReusesPagesButNeverOtherAccountsOrClearedCredentials()
    {
        var imageRequests = 0;
        using var handler = new Handler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/Images/", StringComparison.Ordinal))
            {
                imageRequests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
            }

            return Task.FromResult(Json(PageJson(0, 1, 1, artwork: true)));
        }, checkToken: false);
        using var client = Client(handler);
        var first = await client.GetLibraryArtworkAsync(Session, "movie-0");
        var second = await client.GetLibraryArtworkAsync(Session, "movie-0");
        Assert.Same(first, second);
        Assert.Equal(1, imageRequests);

        await client.GetLibraryArtworkAsync(Session with { AccessToken = "replacement-token" }, "movie-0");
        Assert.Equal(2, imageRequests);
        await client.GetLibraryArtworkAsync(Session with { UserId = "another-user" }, "movie-0");
        Assert.Equal(3, imageRequests);
        await client.GetLibraryArtworkAsync(Session, "movie-0");
        Assert.Equal(4, imageRequests);
        client.ClearImageCache();
        await client.GetLibraryArtworkAsync(Session, "movie-0");
        Assert.Equal(5, imageRequests);
    }

    [Fact]
    public async Task MissingArtworkDoesNotLoseLibraryItems()
    {
        using var handler = new Handler((request, _) =>
            Task.FromResult(request.RequestUri!.AbsolutePath.Contains("/Images/", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json(PageJson(0, 1, 1, artwork: true))));
        using var client = Client(handler);
        var page = await client.GetLibraryPageAsync(Session, Library, 0);
        Assert.Null(Assert.Single(page.Items).Artwork);
        Assert.Equal("movie-0", page.Items[0].ArtworkItemId);
        Assert.Null(await client.GetLibraryArtworkAsync(Session, page.Items[0].ArtworkItemId!));
    }

    [Fact]
    public async Task PageMetadataReturnsWithoutWaitingForAnyArtwork()
    {
        using var handler = new Handler((request, _) =>
        {
            Assert.DoesNotContain("/Images/", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
            return Task.FromResult(Json(PageJson(0, 40, 400, artwork: true)));
        });
        using var client = Client(handler);
        var page = await client.GetLibraryPageAsync(Session, Library, 0);
        Assert.Equal(40, page.Items.Count);
        Assert.Single(handler.Requests);
        Assert.All(page.Items, item =>
        {
            Assert.Null(item.Artwork);
            Assert.Null(item.Backdrop);
            Assert.Equal(item.Id, item.ArtworkItemId);
        });
    }

    [Fact]
    public async Task InsecureLibraryRequestsNeverSendCredentials()
    {
        using var handler = new Handler((_, _) => throw new InvalidOperationException("No request expected."));
        using var client = Client(handler);
        var session = Session with { Server = Session.Server with { BaseUri = new Uri("http://media.example/") } };
        var exception = await Assert.ThrowsAsync<MediaPreviewException>(() => client.GetLibraryPageAsync(session, Library, 0));
        Assert.Equal(MediaPreviewError.InsecureConnection, exception.Error);
        exception = await Assert.ThrowsAsync<MediaPreviewException>(() => client.GetLibraryArtworkAsync(session, "movie-0"));
        Assert.Equal(MediaPreviewError.InsecureConnection, exception.Error);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, MediaPreviewError.AccessDenied)]
    [InlineData(HttpStatusCode.Forbidden, MediaPreviewError.AccessDenied)]
    [InlineData(HttpStatusCode.InternalServerError, MediaPreviewError.UnexpectedStatus)]
    public async Task ArtworkErrorsRemainTypedAndDoNotHideRejectedSessions(HttpStatusCode status, MediaPreviewError expected)
    {
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(status)));
        using var client = Client(handler);
        var exception = await Assert.ThrowsAsync<MediaPreviewException>(() => client.GetLibraryArtworkAsync(Session, "movie-0"));
        Assert.Equal(expected, exception.Error);
    }

    [Fact]
    public async Task IndividualPosterRetainsItsRequestTimeout()
    {
        using var handler = new Handler((_, _) => throw new OperationCanceledException());
        using var client = Client(handler);
        var exception = await Assert.ThrowsAsync<MediaPreviewException>(() => client.GetLibraryArtworkAsync(Session, "movie-0"));
        Assert.Equal(MediaPreviewError.TimedOut, exception.Error);
    }

    [Fact]
    public async Task CallerCanCancelAPosterWithoutChangingItToATimeout()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler(async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("Expected cancellation.");
        });
        using var client = Client(handler);
        using var cancellation = new CancellationTokenSource();
        var loading = client.GetLibraryArtworkAsync(Session, "movie-0", cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loading);
    }

    [Fact]
    public void ImageCacheEnforcesByteAndEntryLimitsAndEvictsLeastRecent()
    {
        var cache = new MediaImageCache();
        var bytes = new byte[1024 * 1024];
        for (var index = 0; index < 32; index++)
        {
            cache.Add(index.ToString(System.Globalization.CultureInfo.InvariantCulture), bytes);
        }

        Assert.NotNull(cache.Get("0"));
        cache.Add("new", bytes);
        Assert.Equal(MediaImageCache.MaximumBytes, cache.Bytes);
        Assert.Null(cache.Get("1"));
        Assert.NotNull(cache.Get("0"));
        for (var index = 0; index < 150; index++)
        {
            cache.Add($"small-{index}", [1]);
        }

        Assert.Equal(MediaImageCache.MaximumEntries, cache.Count);
        Assert.InRange(cache.Bytes, 0, MediaImageCache.MaximumBytes);
        cache.Add("too-large", new byte[MediaImageCache.MaximumBytes + 1]);
        Assert.Null(cache.Get("too-large"));
    }

    [Fact]
    public void CachedImagesExpireAtFiveMinutes()
    {
        var clock = new Clock();
        var cache = new MediaImageCache(clock);
        cache.Add("artwork", [1, 2, 3]);
        clock.Now = clock.Now.AddMinutes(5).AddTicks(-1);
        Assert.NotNull(cache.Get("artwork"));
        clock.Now = clock.Now.AddTicks(1);
        Assert.Null(cache.Get("artwork"));
        Assert.Equal(0, cache.Bytes);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public async Task OversizedHttpResponseIsRejectedAndNotCached()
    {
        using var handler = new Handler((request, _) =>
            Task.FromResult(request.RequestUri!.AbsolutePath.Contains("/Images/", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[8 * 1024 * 1024 + 1]) }
                : Json(PageJson(0, 1, 1, artwork: true))));
        using var client = Client(handler);
        var exception = await Assert.ThrowsAsync<MediaPreviewException>(() => client.GetLibraryArtworkAsync(Session, "movie-0"));
        Assert.Equal(MediaPreviewError.Network, exception.Error);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static string PageJson(int start, int count, int total, bool artwork = false) =>
        JsonSerializer.Serialize(new
        {
            Items = Enumerable.Range(start, count).Select(index => new
            {
                Id = $"movie-{index}",
                Name = $"Movie {index}",
                Type = "Movie",
                ImageTags = artwork ? new { Primary = "tag" } : null,
            }),
            TotalRecordCount = total,
        });

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static JellyfinMediaPreviewClient Client(HttpMessageHandler handler) =>
        new(handler, new JellyfinClientIdentity("Cindara", "Tests", "device", "1.0"));

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond,
        bool checkToken = true) : HttpMessageHandler
    {
        public System.Collections.Concurrent.ConcurrentQueue<Uri> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Enqueue(request.RequestUri!);
            if (checkToken)
            {
                Assert.Equal(Session.AccessToken, request.Headers.GetValues("X-Emby-Token").Single());
            }

            Assert.DoesNotContain(Session.AccessToken, request.RequestUri!.ToString(), StringComparison.Ordinal);
            return respond(request, cancellationToken);
        }
    }
}
