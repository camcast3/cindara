using System.Net;
using System.Text;
using Cindara.Core.Authentication;
using Cindara.Core.Jellyfin;
using Cindara.Core.Models;

namespace Cindara.Core.Tests.Jellyfin;

public sealed class MediaSearchAndDetailsTests
{
    private static readonly AuthenticatedSession Session = new(
        new ServerIdentity("server", new Uri("https://media.example/jellyfin/"), "Media", "10.11", "Linux"),
        "user", "Viewer", "private-token");

    [Fact]
    public async Task SearchIsAuthenticatedBoundedEncodedAndDeterministicallyGrouped()
    {
        using var handler = new Handler((_, _) => Json("""
            {"Items":[
              {"Id":"e","Name":"Zulu","Type":"Episode","SeriesName":"Show","SeriesId":"s","SeasonId":"season","ParentIndexNumber":1,"IndexNumber":2},
              {"Id":"m2","Name":"beta","Type":"Movie"},
              {"Id":"s","Name":"Alpha","Type":"Series"},
              {"Id":"m1","Name":"Alpha","Type":"Movie"}
            ],"TotalRecordCount":44}
            """));
        using var client = Client(handler);

        var page = await client.SearchAsync(Session, "  alpha & beta  ", 0);

        Assert.Equal(["m1", "m2", "s", "e"], page.Items.Select(item => item.Id));
        Assert.Equal(44, page.TotalRecordCount);
        Assert.True(page.HasNextPage);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Contains("SearchTerm=alpha%20%26%20beta", request.Uri.Query, StringComparison.Ordinal);
        Assert.Contains("Limit=40", request.Uri.Query, StringComparison.Ordinal);
        Assert.Contains("IncludeItemTypes=Movie,Series,Season,Episode", request.Uri.Query, StringComparison.Ordinal);
        Assert.Contains("SortBy=SortName", request.Uri.Query, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankSearchNeverSendsCredentials(string query)
    {
        using var handler = new Handler((_, _) => throw new InvalidOperationException("No request expected."));
        using var client = Client(handler);
        await Assert.ThrowsAsync<ArgumentException>(() => client.SearchAsync(Session, query, 0));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DetailsExposeCreditsRatingsTracksAndUserStateWithoutUiDependencies()
    {
        using var handler = new Handler((_, _) => Json("""
            {
              "Id":"movie","Name":"Example","Type":"Movie","ProductionYear":2025,
              "PremiereDate":"2025-02-03T00:00:00Z","RunTimeTicks":72000000000,
              "OfficialRating":"PG-13","Genres":["Drama"],"CommunityRating":8.2,"CriticRating":91,
              "Overview":"Synopsis","ImageTags":{"Primary":"tag"},"BackdropImageTags":["backdrop"],
              "People":[{"Id":"director","Name":"Director","Type":"Director"},{"Id":"actor","Name":"Actor","Role":"Lead","Type":"Actor","PrimaryImageTag":"photo"}],
              "MediaSources":[{"MediaStreams":[{"Index":0,"Type":"Video","Codec":"hevc","DisplayTitle":"4K HEVC","Width":3840,"Height":2160,"IsDefault":true}]}],
              "UserData":{"IsFavorite":true,"Played":false,"PlayedPercentage":25,"PlaybackPositionTicks":120}
            }
            """));
        using var client = Client(handler);

        var details = await client.GetItemDetailsAsync(Session, "movie");

        Assert.Equal("Example", details.Name);
        Assert.Equal(new DateOnly(2025, 2, 3), details.PremiereDate);
        Assert.Equal(["Drama"], details.Genres);
        Assert.Equal(["Community", "Critic"], details.Ratings.Select(rating => rating.Name));
        Assert.Equal(["Director", "Actor"], details.Credits.Select(credit => credit.CreditType));
        Assert.Equal("hevc", Assert.Single(details.Tracks).Codec);
        Assert.True(details.UserState.IsFavorite);
        Assert.False(details.UserState.IsPlayed);
        Assert.True(details.HasPrimaryImage);
        Assert.True(details.HasBackdrop);
    }

    [Theory]
    [InlineData(true, "POST")]
    [InlineData(false, "DELETE")]
    public async Task FavoriteMutationUsesUserScopeAndReturnsServerState(bool favorite, string method)
    {
        using var handler = new Handler((_, _) =>
            Json($$"""{"IsFavorite":{{favorite.ToString().ToLowerInvariant()}},"Played":false}"""));
        using var client = Client(handler);

        var state = await client.SetFavoriteAsync(Session, "movie/id", favorite);

        Assert.Equal(favorite, state.IsFavorite);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(method, request.Method.Method);
        Assert.Equal("/jellyfin/Users/user/FavoriteItems/movie%2Fid", request.Uri.AbsolutePath);
    }

    [Theory]
    [InlineData(true, "POST")]
    [InlineData(false, "DELETE")]
    public async Task WatchedMutationIsExplicitAndReturnsAuthoritativeProgress(bool played, string method)
    {
        using var handler = new Handler((_, _) =>
            Json($$"""{"IsFavorite":true,"Played":{{played.ToString().ToLowerInvariant()}},"PlaybackPositionTicks":0,"PlayedPercentage":0}"""));
        using var client = Client(handler);
        var state = await client.SetPlayedAsync(Session, "movie", played);
        Assert.Equal(played, state.IsPlayed);
        Assert.True(state.IsFavorite);
        Assert.Equal(0, state.PlaybackPositionTicks);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(method, request.Method.Method);
        Assert.Equal("/jellyfin/Users/user/PlayedItems/movie", request.Uri.AbsolutePath);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, MediaPreviewError.AccessDenied)]
    [InlineData(HttpStatusCode.Forbidden, MediaPreviewError.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, MediaPreviewError.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError, MediaPreviewError.UnexpectedStatus)]
    public async Task DetailAndWriteErrorsDoNotInventSuccessfulState(HttpStatusCode status, MediaPreviewError error)
    {
        using var handler = new Handler((_, _) => new(status));
        using var client = Client(handler);
        Assert.Equal(error, (await Assert.ThrowsAsync<MediaPreviewException>(
            () => client.GetItemDetailsAsync(Session, "movie"))).Error);
        Assert.Equal(error, (await Assert.ThrowsAsync<MediaPreviewException>(
            () => client.SetFavoriteAsync(Session, "movie", true))).Error);
        Assert.Equal(error, (await Assert.ThrowsAsync<MediaPreviewException>(
            () => client.SetPlayedAsync(Session, "movie", false))).Error);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("""{"Played":true}""")]
    [InlineData("""{"IsFavorite":true}""")]
    [InlineData("invalid")]
    public async Task IncompleteWriteResponsesRequireReconciliation(string json)
    {
        using var handler = new Handler((_, _) => Json(json));
        using var client = Client(handler);
        Assert.Equal(MediaPreviewError.InvalidResponse, (await Assert.ThrowsAsync<MediaPreviewException>(
            () => client.SetFavoriteAsync(Session, "movie", true))).Error);
    }

    [Fact]
    public async Task DetailsRetainRootTracksAndDistinctVersionsWithoutSourcesAndNeverWrite()
    {
        using var handler = new Handler((_, _) => Json("""
            {"Id":"movie","Name":"Movie","Type":"Movie","LocalTrailerCount":1,
             "RemoteTrailers":[{"Url":"https://example.com/trailer"}],
             "MediaStreams":[{"Type":"Audio","Index":1,"Codec":"aac","Language":"eng","Channels":2}]}
            """));
        using var client = Client(handler);
        var details = await client.GetItemDetailsAsync(Session, "movie");
        Assert.Equal("Audio", Assert.Single(details.Tracks).TrackType);
        Assert.False(details.HasUserState);
        Assert.Equal(1, details.LocalTrailerCount);
        Assert.True(details.HasRemoteTrailers);
        Assert.Equal(HttpMethod.Get, Assert.Single(handler.Requests).Method);
    }

    [Fact]
    public async Task TracksFromMultipleVersionsAreNotCollapsedByStreamIndex()
    {
        using var handler = new Handler((_, _) => Json("""
            {"Id":"movie","Name":"Movie","Type":"Movie","MediaSources":[
              {"MediaStreams":[{"Type":"Video","Index":0,"Codec":"hevc","Width":3840,"Height":2160}]},
              {"MediaStreams":[{"Type":"Video","Index":0,"Codec":"h264","Width":1920,"Height":1080}]}],
             "MediaStreams":[{"Type":"Video","Index":0,"Codec":"h264","Width":1920,"Height":1080}]}
            """));
        using var client = Client(handler);
        Assert.Equal(2, (await client.GetItemDetailsAsync(Session, "movie")).Tracks.Count);
    }

    [Fact]
    public async Task MismatchedItemIdentityCannotReceiveMutationsThroughDetails()
    {
        using var handler = new Handler((_, _) => Json("""{"Id":"other","Name":"Other","Type":"Movie"}"""));
        using var client = Client(handler);
        await Assert.ThrowsAsync<MediaPreviewException>(() => client.GetItemDetailsAsync(Session, "movie"));
    }

    [Fact]
    public async Task BackdropIsAuthenticatedCachedAndMissingArtworkIsExplicit()
    {
        using var handler = new Handler((request, _) => request.RequestUri!.AbsolutePath.Contains("missing", StringComparison.Ordinal)
            ? new(HttpStatusCode.NotFound)
            : new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
        using var client = Client(handler);
        Assert.Equal([1, 2, 3], await client.GetDetailBackdropAsync(Session, "movie"));
        Assert.Equal([1, 2, 3], await client.GetDetailBackdropAsync(Session, "movie"));
        Assert.Null(await client.GetDetailBackdropAsync(Session, "missing"));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("/jellyfin/Items/movie/Images/Backdrop/0", handler.Requests[0].Uri.AbsolutePath);
        Assert.All(handler.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static JellyfinMediaPreviewClient Client(HttpMessageHandler handler) =>
        new(handler, new JellyfinClientIdentity("Cindara", "Tests", "device", "1.0"));

    private sealed class Handler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(Uri Uri, HttpMethod Method)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(Session.AccessToken, request.Headers.GetValues("X-Emby-Token").Single());
            Assert.DoesNotContain(Session.AccessToken, request.RequestUri!.ToString(), StringComparison.Ordinal);
            Requests.Add((request.RequestUri, request.Method));
            return Task.FromResult(respond(request, cancellationToken));
        }
    }
}
