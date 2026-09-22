using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
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
    public async Task GetHomeAsyncRejectsRelativeAddressBeforeSendingAnyRequest()
    {
        var handler = new PreviewHandler();
        using var client = CreateClient(handler);
        var session = Session with { Server = Session.Server with { BaseUri = new Uri("jellyfin/", UriKind.Relative) } };

        var exception = await Assert.ThrowsAsync<MediaPreviewException>(() => client.GetHomeAsync(session));

        Assert.Equal(MediaPreviewError.InsecureConnection, exception.Error);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("http://media.example.com/jellyfin/")]
    [InlineData("http://192.0.2.10/jellyfin/")]
    [InlineData("http://[2001:db8::1]/jellyfin/")]
    [InlineData("http://localhost.example.com/jellyfin/")]
    [InlineData("ftp://localhost/jellyfin/")]
    [InlineData("file:///jellyfin/")]
    public async Task GetHomeAsyncRejectsInsecureAddressBeforeSendingAnyRequest(string address)
    {
        var handler = new PreviewHandler();
        using var client = CreateClient(handler);
        var session = Session with { Server = Session.Server with { BaseUri = new Uri(address) } };

        var exception = await Assert.ThrowsAsync<MediaPreviewException>(() => client.GetHomeAsync(session));

        Assert.Equal(MediaPreviewError.InsecureConnection, exception.Error);
        Assert.Empty(handler.Requests);
        Assert.DoesNotContain(session.AccessToken, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://media.example.com/jellyfin/")]
    [InlineData("http://localhost/jellyfin/")]
    [InlineData("http://127.0.0.1/jellyfin/")]
    [InlineData("http://[::1]/jellyfin/")]
    public async Task GetHomeAsyncAllowsHttpsAndHttpLoopbackForAllRequests(string address)
    {
        var handler = new PreviewHandler();
        using var client = CreateClient(handler);
        var session = Session with { Server = Session.Server with { BaseUri = new Uri(address) } };

        await client.GetHomeAsync(session);

        Assert.NotEmpty(handler.Requests);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal(session.Server.BaseUri.Scheme, request.Uri.Scheme);
            Assert.Equal(session.Server.BaseUri.Host, request.Uri.Host);
            Assert.Equal(session.AccessToken, request.Token);
        });
    }

    [Fact]
    public async Task GetHomeAsyncLoadsAuthenticatedMediaAndArtwork()
    {
        var handler = new PreviewHandler();
        var client = CreateClient(handler);

        var home = await client.GetHomeAsync(Session);

        var continuing = Assert.Single(home.ContinueWatching);
        Assert.Equal("Pilot", continuing.Name);
        var metadata = Assert.IsType<MediaPreviewMetadata>(continuing.Metadata);
        Assert.Equal(new MediaPreviewMetadata("Pilot", "Northstar", 1, 2, null, null, null, false), metadata);
        Assert.Empty(continuing.Subtitle);
        Assert.Empty(continuing.Details);
        Assert.Equal(42.5, continuing.PlaybackProgress);
        Assert.Equal([1, 2, 3], continuing.Artwork);
        Assert.Equal("Northstar", home.Featured?.Name);
        Assert.Equal(metadata with { PreferSeriesTitle = true }, home.Featured?.Metadata);
        Assert.Equal([4, 5, 6], home.Featured?.Backdrop);
        Assert.Collection(
            home.RecentlyAddedLibraries,
            rail =>
            {
                Assert.Equal("TV Shows", rail.LibraryName);
                Assert.Equal("TV Shows", rail.Title);
                var episode = Assert.Single(rail.Items);
                Assert.Equal("Second Nature", episode.Name);
                Assert.Equal(new MediaPreviewMetadata("Return Migration", "Second Nature", 1, 6,
                    null, null, null, true), episode.Metadata);
            },
            rail =>
            {
                Assert.Equal("Movies", rail.LibraryName);
                var movie = Assert.Single(rail.Items);
                Assert.Equal("Moon Garden", movie.Name);
                Assert.Equal(new MediaPreviewMetadata("Moon Garden", null, null, null,
                    2026, TimeSpan.FromMinutes(65).Ticks, "PG-13", true), movie.Metadata);
                Assert.Empty(movie.Subtitle);
                Assert.Empty(movie.Details);
            },
            rail =>
            {
                Assert.Equal("Anime", rail.LibraryName);
                Assert.Collection(
                    rail.Items,
                    series => Assert.Equal("Skyward", series.Name),
                    season =>
                    {
                        Assert.Equal("That Time I Got Reincarnated as a Slime", season.Name);
                        Assert.Equal("Season 3", season.Metadata?.Name);
                        Assert.True(season.Metadata?.PreferSeriesTitle);
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

    [Fact]
    public async Task GetHomeAsyncOverlapsMetadataAndArtworkWithOneGlobalLimitAndStableOrdering()
    {
        var resume = NewSignal();
        var television = NewSignal();
        var movies = NewSignal();
        var images = NewSignal();
        var firstResumeImages = NewSignal();
        using var handler = new AsyncPreviewHandler(async (request, token) =>
        {
            var path = request.Uri.AbsolutePath;
            if (path.EndsWith("/Views", StringComparison.Ordinal))
            {
                return JsonResponse(
                    """
                    {"Items":[
                      {"Id":"anime","Name":"Anime","CollectionType":"tvshows"},
                      {"Id":"movies","Name":"Movies","CollectionType":"movies"},
                      {"Id":"tv","Name":"TV Shows","CollectionType":"tvshows"}
                    ]}
                    """);
            }

            if (path.EndsWith("/Items/Resume", StringComparison.Ordinal))
            {
                await resume.Task.WaitAsync(token);
                return JsonResponse(WrappedItems("resume", 2));
            }

            if (path.EndsWith("/Items/Latest", StringComparison.Ordinal))
            {
                if (request.Uri.Query.Contains("ParentId=tv", StringComparison.Ordinal))
                {
                    await television.Task.WaitAsync(token);
                    return JsonResponse(Items("tv", 2));
                }

                if (request.Uri.Query.Contains("ParentId=movies", StringComparison.Ordinal))
                {
                    await movies.Task.WaitAsync(token);
                    return JsonResponse(Items("movie", 2));
                }

                return JsonResponse("[]");
            }

            await images.Task.WaitAsync(token);
            if (path.Contains("/resume-0/", StringComparison.Ordinal))
            {
                await firstResumeImages.Task.WaitAsync(token);
            }

            return ImageResponse(path);
        });
        using var client = CreateClient(handler);

        var loading = client.GetHomeAsync(Session);
        await handler.WaitForAsync(requests =>
            requests.Count(request => request.Uri.AbsolutePath.EndsWith("/Items/Latest", StringComparison.Ordinal)) == 3);
        Assert.False(resume.Task.IsCompleted);

        resume.SetResult();
        await handler.WaitForAsync(requests => requests.Count(IsImage) == 4);
        Assert.False(television.Task.IsCompleted);
        Assert.False(movies.Task.IsCompleted);

        movies.SetResult();
        television.SetResult();
        await handler.WaitForAsync(requests => requests.Count(IsImage) == 6);
        Assert.Equal(6, handler.ActiveImages);
        Assert.False(loading.IsCompleted);

        images.SetResult();
        await handler.WaitForAsync(requests => requests.Count(IsImage) == 8 && handler.ActiveRequests == 2);
        Assert.False(loading.IsCompleted);
        firstResumeImages.SetResult();
        var home = await loading.WaitAsync(TestTimeout);

        Assert.Equal(6, handler.MaximumImages);
        Assert.Equal(6, handler.MaximumRequests);
        Assert.Equal(0, handler.ActiveRequests);
        Assert.Equal(["resume-0", "resume-1"], home.ContinueWatching.Select(item => item.Id));
        Assert.Equal(["tv", "movies", "anime"], home.RecentlyAddedLibraries.Select(rail => rail.LibraryId));
        Assert.Equal(["tv-0", "tv-1"], home.RecentlyAddedLibraries[0].Items.Select(item => item.Id));
        Assert.Equal(["movie-0", "movie-1"], home.RecentlyAddedLibraries[1].Items.Select(item => item.Id));
        Assert.Equal("resume-0", home.Featured?.Id);
        Assert.All(home.ContinueWatching, item =>
            Assert.Equal($"/jellyfin/Items/{item.Id}/Images/Thumb", Encoding.UTF8.GetString(item.Artwork!)));
        Assert.All(home.RecentlyAddedLibraries.SelectMany(rail => rail.Items), item =>
            Assert.Equal($"/jellyfin/Items/{item.Id}/Images/Primary", Encoding.UTF8.GetString(item.Artwork!)));
    }

    [Fact]
    public async Task GetHomeAsyncSharesSeriesImagesOnlyWithinTheCurrentLoad()
    {
        const string episodes =
            """
            [
              {"Id":"episode-1","Name":"One","Type":"Episode","SeriesId":"shared","SeriesPrimaryImageTag":"tag","ParentBackdropItemId":"shared"},
              {"Id":"episode-2","Name":"Two","Type":"Episode","SeriesId":"shared","SeriesPrimaryImageTag":"tag","ParentBackdropItemId":"shared"}
            ]
            """;
        var images = NewSignal();
        using var handler = new AsyncPreviewHandler(async (request, token) =>
        {
            if (!IsImage(request))
            {
                return MetadataResponse(request, $$"""{"Items":{{episodes}}}""", episodes);
            }

            await images.Task.WaitAsync(token);
            return ImageResponse(request.Uri.AbsolutePath);
        });
        using var client = CreateClient(handler);
        var loading = client.GetHomeAsync(Session);
        await handler.WaitForAsync(requests => requests.Count(IsImage) == 2);
        images.SetResult();
        var home = await loading.WaitAsync(TestTimeout);

        Assert.Equal(2, handler.Requests.Count(IsImage));
        var continuing = home.ContinueWatching;
        var latest = Assert.Single(home.RecentlyAddedLibraries).Items;
        Assert.Same(continuing[0].Backdrop, continuing[1].Backdrop);
        Assert.Same(continuing[0].Backdrop, latest[0].Backdrop);
        Assert.Same(latest[0].Backdrop, latest[1].Backdrop);
        Assert.Same(latest[0].Artwork, latest[1].Artwork);

        var otherSession = Session with
        {
            UserId = "another-user",
            AccessToken = "another-token",
            Server = Session.Server with { BaseUri = new Uri("https://other.example.com/jellyfin/") },
        };
        var otherHome = await client.GetHomeAsync(otherSession).WaitAsync(TestTimeout);

        Assert.Equal(4, handler.Requests.Count(IsImage));
        Assert.NotSame(home.ContinueWatching[0].Backdrop, otherHome.ContinueWatching[0].Backdrop);
        Assert.All(handler.Requests.Where(request => request.Uri.Host == "other.example.com"), request =>
        {
            Assert.Equal("another-token", request.Token);
            Assert.Contains("Token=\"another-token\"", request.Authorization, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetHomeAsyncDeduplicatesThumbFallbackAndMissingFeaturedBackdrop(bool missingPrimary)
    {
        var images = NewSignal();
        using var handler = new AsyncPreviewHandler(async (request, token) =>
        {
            if (!IsImage(request))
            {
                return MetadataResponse(request, WrappedItems("shared", 1), Items("shared", 1));
            }

            await images.Task.WaitAsync(token);
            return !missingPrimary && request.Uri.AbsolutePath.EndsWith("/Primary", StringComparison.Ordinal)
                ? ImageResponse("primary")
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var client = CreateClient(handler);
        var loading = client.GetHomeAsync(Session);
        await handler.WaitForAsync(requests => requests.Count(IsImage) == 3);
        images.SetResult();
        var home = await loading.WaitAsync(TestTimeout);

        var imageRequests = handler.Requests.Where(IsImage).ToArray();
        Assert.Equal(3, imageRequests.Length);
        Assert.Equal(3, imageRequests.Select(request => request.Uri).Distinct().Count());
        var continuing = Assert.Single(home.ContinueWatching);
        var latest = Assert.Single(Assert.Single(home.RecentlyAddedLibraries).Items);
        if (missingPrimary)
        {
            Assert.Null(continuing.Artwork);
            Assert.Null(latest.Artwork);
            Assert.Null(home.Featured?.Backdrop);
        }
        else
        {
            Assert.Equal(Encoding.UTF8.GetBytes("primary"), continuing.Artwork);
            Assert.Same(continuing.Artwork, latest.Artwork);
            Assert.Same(continuing.Artwork, home.Featured?.Backdrop);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetHomeAsyncTimesOutTheWholeLoadAndDrainsRequests(bool duringArtwork)
    {
        using var clock = new ManualDeadlineTimeProvider();
        var blocked = NewSignal();
        using var handler = CreateBlockedHandler(blocked, duringArtwork);
        using var client = CreateClient(handler, clock);
        var loading = client.GetHomeAsync(Session);
        await handler.WaitForAsync(requests => duringArtwork
            ? requests.Count(IsImage) == 6
            : requests.Length == 2);
        Assert.Equal(TimeSpan.FromSeconds(30), clock.DueTime);
        Assert.False(loading.IsCompleted);

        clock.Expire();
        var exception = await Assert.ThrowsAsync<MediaPreviewException>(
            () => loading.WaitAsync(TestTimeout));

        Assert.Equal(MediaPreviewError.TimedOut, exception.Error);
        Assert.Equal(0, handler.ActiveRequests);
        Assert.Equal(duringArtwork ? 6 : 0, handler.Requests.Count(IsImage));
        Assert.True(clock.IsDisposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetHomeAsyncPreservesCallerCancellationAndDrainsRequests(bool duringArtwork)
    {
        var blocked = NewSignal();
        using var handler = CreateBlockedHandler(blocked, duringArtwork);
        using var client = CreateClient(handler);
        using var cancellation = new CancellationTokenSource();
        var loading = client.GetHomeAsync(Session, cancellation.Token);
        await handler.WaitForAsync(requests => duringArtwork
            ? requests.Count(IsImage) == 6
            : requests.Length == 2);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loading.WaitAsync(TestTimeout));

        Assert.Equal(0, handler.ActiveRequests);
        Assert.Equal(duringArtwork ? 6 : 0, handler.Requests.Count(IsImage));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, MediaPreviewError.AccessDenied)]
    [InlineData(HttpStatusCode.Forbidden, MediaPreviewError.AccessDenied)]
    [InlineData(HttpStatusCode.InternalServerError, MediaPreviewError.UnexpectedStatus)]
    public async Task GetHomeAsyncCancelsSiblingRequestsOnArtworkFailure(
        HttpStatusCode status,
        MediaPreviewError expectedError)
    {
        var failure = NewSignal();
        var blocked = NewSignal();
        using var handler = new AsyncPreviewHandler(async (request, token) =>
        {
            if (!IsImage(request))
            {
                return MetadataResponse(request, WrappedItems("resume", 5), "[]");
            }

            if (request.Uri.AbsolutePath.EndsWith("/resume-0/Images/Thumb", StringComparison.Ordinal))
            {
                await failure.Task.WaitAsync(token);
                return new HttpResponseMessage(status);
            }

            await blocked.Task.WaitAsync(token);
            return ImageResponse("unused");
        });
        using var client = CreateClient(handler);
        var loading = client.GetHomeAsync(Session);
        await handler.WaitForAsync(requests => requests.Count(IsImage) == 6);

        failure.SetResult();
        var exception = await Assert.ThrowsAsync<MediaPreviewException>(() => loading.WaitAsync(TestTimeout));

        Assert.Equal(expectedError, exception.Error);
        Assert.Equal(0, handler.ActiveRequests);
        Assert.Equal(6, handler.Requests.Count(IsImage));
        Assert.DoesNotContain(Session.AccessToken, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetHomeAsyncPreservesIndividualRequestTimeout()
    {
        using var handler = new AsyncPreviewHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new OperationCanceledException()));
        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<MediaPreviewException>(() => client.GetHomeAsync(Session));

        Assert.Equal(MediaPreviewError.TimedOut, exception.Error);
        Assert.Equal(0, handler.ActiveRequests);
    }

    [Fact]
    public async Task GetHomeAsyncDoesNotSendRequestsWhenAlreadyCancelled()
    {
        using var handler = new PreviewHandler();
        using var client = CreateClient(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetHomeAsync(Session, cancellation.Token));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetHomeAsyncReportsMalformedMetadataAndCancelsItsSibling()
    {
        var failure = NewSignal();
        var blocked = NewSignal();
        using var handler = new AsyncPreviewHandler(async (request, token) =>
        {
            if (request.Uri.AbsolutePath.EndsWith("/Items/Resume", StringComparison.Ordinal))
            {
                await failure.Task.WaitAsync(token);
                return JsonResponse("{");
            }

            await blocked.Task.WaitAsync(token);
            return JsonResponse("""{"Items":[]}""");
        });
        using var client = CreateClient(handler);
        var loading = client.GetHomeAsync(Session);
        await handler.WaitForAsync(requests => requests.Length == 2);

        failure.SetResult();
        var exception = await Assert.ThrowsAsync<MediaPreviewException>(() => loading.WaitAsync(TestTimeout));

        Assert.Equal(MediaPreviewError.InvalidResponse, exception.Error);
        Assert.Equal(0, handler.ActiveRequests);
    }

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

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

    private static JellyfinMediaPreviewClient CreateClient(
        HttpMessageHandler handler,
        TimeProvider? timeProvider = null) =>
        new(
            handler,
            new JellyfinClientIdentity("Cindara", "Test Device", "device-1", "1.0.0"),
            timeProvider);

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static bool IsImage(CapturedRequest request) =>
        request.Uri.AbsolutePath.Contains("/Images/", StringComparison.Ordinal);

    private static string Items(string prefix, int count) =>
        JsonSerializer.Serialize(Enumerable.Range(0, count).Select(index => new
        {
            Id = $"{prefix}-{index}",
            Name = $"{prefix} {index}",
            Type = "Movie",
            ImageTags = new { Primary = "tag" },
        }));

    private static string WrappedItems(string prefix, int count) => $$"""{"Items":{{Items(prefix, count)}}}""";

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage ImageResponse(string value) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(value)) };

    private static HttpResponseMessage MetadataResponse(CapturedRequest request, string resume, string latest)
    {
        var path = request.Uri.AbsolutePath;
        if (path.EndsWith("/Items/Resume", StringComparison.Ordinal))
        {
            return JsonResponse(resume);
        }

        if (path.EndsWith("/Views", StringComparison.Ordinal))
        {
            return JsonResponse("""{"Items":[{"Id":"movies","Name":"Movies","CollectionType":"movies"}]}""");
        }

        if (path.EndsWith("/Items/Latest", StringComparison.Ordinal))
        {
            return JsonResponse(latest);
        }

        throw new InvalidOperationException($"Unexpected metadata request: {request.Uri}");
    }

    private static AsyncPreviewHandler CreateBlockedHandler(TaskCompletionSource blocked, bool duringArtwork) =>
        new(async (request, token) =>
        {
            if (duringArtwork && !IsImage(request))
            {
                return MetadataResponse(request, WrappedItems("resume", 5), "[]");
            }

            await blocked.Task.WaitAsync(token);
            return ImageResponse("unused");
        });

    private sealed class AsyncPreviewHandler(
        Func<CapturedRequest, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        private readonly Channel<bool> _started = Channel.CreateUnbounded<bool>();
        private readonly object _sync = new();
        private int _activeRequests;
        private int _activeImages;
        private int _maximumRequests;
        private int _maximumImages;

        public ConcurrentQueue<CapturedRequest> Requests { get; } = new();

        public int ActiveRequests => Volatile.Read(ref _activeRequests);

        public int ActiveImages => Volatile.Read(ref _activeImages);

        public int MaximumRequests => Volatile.Read(ref _maximumRequests);

        public int MaximumImages => Volatile.Read(ref _maximumImages);

        public async Task WaitForAsync(Func<CapturedRequest[], bool> condition)
        {
            using var timeout = new CancellationTokenSource(TestTimeout);
            while (!condition(Requests.ToArray()))
            {
                await _started.Reader.ReadAsync(timeout.Token);
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var captured = new CapturedRequest(
                request.RequestUri ?? throw new InvalidOperationException("Missing request URI."),
                request.Headers.GetValues("Authorization").Single(),
                request.Headers.GetValues("X-Emby-Token").Single());
            lock (_sync)
            {
                _maximumRequests = Math.Max(_maximumRequests, ++_activeRequests);
                if (IsImage(captured))
                {
                    _maximumImages = Math.Max(_maximumImages, ++_activeImages);
                }

                Requests.Enqueue(captured);
                _started.Writer.TryWrite(true);
            }

            try
            {
                return await respond(captured, cancellationToken);
            }
            finally
            {
                lock (_sync)
                {
                    _activeRequests--;
                    if (IsImage(captured))
                    {
                        _activeImages--;
                    }

                    _started.Writer.TryWrite(true);
                }
            }
        }
    }

    private sealed class ManualDeadlineTimeProvider : TimeProvider, IDisposable
    {
        private ManualTimer? _timer;

        public TimeSpan? DueTime => _timer?.DueTime;

        public bool IsDisposed => _timer?.IsDisposed is true;

        public void Expire() => (_timer ?? throw new InvalidOperationException("No deadline scheduled.")).Fire();

        public void Dispose() => _timer?.Dispose();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Assert.Null(_timer);
            Assert.Equal(Timeout.InfiniteTimeSpan, period);
            _timer = new ManualTimer(callback, state, dueTime);
            return _timer;
        }

        private sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan dueTime) : ITimer
        {
            public TimeSpan DueTime { get; private set; } = dueTime;

            public bool IsDisposed { get; private set; }

            public void Fire() => callback(state);

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                DueTime = dueTime;
                return !IsDisposed;
            }

            public void Dispose() => IsDisposed = true;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class PreviewHandler : HttpMessageHandler
    {
        public ConcurrentQueue<CapturedRequest> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Enqueue(new CapturedRequest(
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
                        """[{"Id":"movie-1","Name":"Moon Garden","Type":"Movie","ProductionYear":2026,"RunTimeTicks":39000000000,"OfficialRating":"PG-13","ImageTags":{}}]"""),
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
