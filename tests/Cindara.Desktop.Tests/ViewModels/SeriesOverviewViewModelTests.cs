using Cindara.Core.Authentication;
using Cindara.Core.Jellyfin;
using Cindara.Core.Models;
using Cindara.Desktop.Localization;
using Cindara.Desktop.Tests.Localization;
using Cindara.Desktop.ViewModels;

namespace Cindara.Desktop.Tests.ViewModels;

[Collection(LocalizationTestGroup.Name)]
public sealed class SeriesOverviewViewModelTests
{
    private static readonly AuthenticatedSession Session = new(
        new ServerIdentity("server", new Uri("https://media.example/"), "Media", "10.11", "Windows"),
        "user", "Viewer", "token");

    [Fact]
    public async Task OverviewReadsSeriesSeasonsAndRemainingTimeWithoutWrites()
    {
        var client = new Client();
        using var model = Model(client);
        await model.OpenAsync("series", "Card");
        Assert.Equal("Series", model.Summary.Title);
        Assert.Equal(2, model.Seasons.Count);
        Assert.True(model.Seasons[0].IsWatched);
        Assert.True(model.Seasons[1].IsUnwatched);
        Assert.Contains("Episode", model.ContinuationLabel, StringComparison.Ordinal);
        Assert.Contains("15m", model.ContinuationMetadata, StringComparison.Ordinal);
        Assert.False(model.NeedsRetry);
        Assert.False(model.IsLoading);
        Assert.Equal(0, client.Writes);
        model.Close();
        Assert.Empty(model.Seasons);
        Assert.Null(model.Continuation);
        Assert.Null(model.Summary.Details);
    }

    [Theory]
    [InlineData(MediaPreviewError.NotFound)]
    [InlineData(MediaPreviewError.Forbidden)]
    [InlineData(MediaPreviewError.Network)]
    [InlineData(MediaPreviewError.AccessDenied)]
    public async Task FailedChildrenAreExplicitAndRetryReadsWithoutWriting(MediaPreviewError error)
    {
        var rejected = 0;
        var client = new Client { Error = error };
        using var model = new SeriesOverviewViewModel(client, Session, _ => { rejected++; return Task.CompletedTask; });
        await model.OpenAsync("series", "Card");
        Assert.True(model.Summary.HasDetails);
        Assert.Empty(model.Seasons);
        Assert.Null(model.Continuation);
        Assert.True(model.NeedsRetry);
        Assert.Equal(error == MediaPreviewError.AccessDenied ? 1 : 0, rejected);
        Assert.Equal(LocalizedErrors.Get(new MediaPreviewException(error, "failure")), model.Message);
        client.Error = null;
        await model.RetryCommand.ExecuteAsync(null);
        Assert.Equal(2, model.Seasons.Count);
        Assert.False(model.NeedsRetry);
        Assert.Equal(0, client.Writes);
    }

    [Fact]
    public async Task CanceledLateResponsesNeverReplaceAnotherSeries()
    {
        var gate = new TaskCompletionSource<IReadOnlyList<MediaSeason>>();
        var client = new Client { Gate = gate };
        using var model = Model(client);
        var first = model.OpenAsync("old", "Old");
        model.Close();
        client.Gate = null;
        await model.OpenAsync("series", "Current");
        gate.SetResult([new("old-season", "Old season", 1, new(false, false, null, null), false)]);
        await first;
        Assert.Equal("series", model.Summary.Details!.Id);
        Assert.Equal(["season-1", "season-2"], model.Seasons.Select(season => season.Season.Id));
        Assert.Equal("series", model.Continuation!.SeriesId);
        Assert.False(model.IsLoading);
    }

    [Fact]
    public async Task EmptyAndUnknownStatesDoNotImplyUnwatchedOrKnownProgress()
    {
        var client = new Client { Empty = true };
        using var model = Model(client);
        await model.OpenAsync("series", "Card");
        Assert.Empty(model.Seasons);
        Assert.Equal(Loc.Get("Series.NoSeasons"), model.Message);
        Assert.Equal(Loc.Get("Series.NoContinuation"), model.ContinuationLabel);
        var card = new SeasonCardViewModel(new("unknown", "Unknown", null, new(false, false, null, null), false, false));
        Assert.False(card.IsWatched);
        Assert.False(card.IsUnwatched);
        Assert.Equal(Loc.Get("Details.StateUnknown"), card.WatchedState);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1L)]
    [InlineData(18000000001L)]
    public async Task MissingOrInvalidPositionDoesNotInventRemainingTime(long? position)
    {
        var client = new Client { Position = position };
        using var model = Model(client);
        await model.OpenAsync("series", "Card");
        Assert.Contains(Loc.Get("Series.RemainingUnknown"), model.ContinuationMetadata, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WrongSeriesContinuationFailsClosed()
    {
        var client = new Client { WrongSeries = true };
        using var model = Model(client);
        await model.OpenAsync("series", "Card");
        Assert.True(model.NeedsRetry);
        Assert.Empty(model.Seasons);
        Assert.Null(model.Continuation);
    }

    private static SeriesOverviewViewModel Model(Client client) => new(client, Session, _ => Task.CompletedTask);

    private sealed class Client : IJellyfinMediaPreviewClient
    {
        public bool Empty { get; set; }
        public bool WrongSeries { get; set; }
        public long? Position { get; set; } = TimeSpan.FromMinutes(15).Ticks;
        public int Writes { get; private set; }
        public MediaPreviewError? Error { get; set; }
        public TaskCompletionSource<IReadOnlyList<MediaSeason>>? Gate { get; set; }
        public Task<MediaItemDetails> GetItemDetailsAsync(AuthenticatedSession session, string itemId,
            CancellationToken cancellationToken = default) => Task.FromResult(Details(itemId, "Series", null));
        public Task<IReadOnlyList<MediaSeason>> GetSeasonsAsync(AuthenticatedSession session, string seriesId,
            CancellationToken cancellationToken = default) => Gate?.Task
            ?? (Error is { } error ? Task.FromException<IReadOnlyList<MediaSeason>>(new MediaPreviewException(error, "failure"))
                : Task.FromResult<IReadOnlyList<MediaSeason>>(Empty ? [] :
                [
                    new("season-1", "Season 1", 1, new(false, true, 100, 0), false),
                    new("season-2", "Season 2", 2, new(false, false, 0, 0), false),
                ]));
        public Task<MediaItemDetails?> GetSeriesContinuationAsync(AuthenticatedSession session, string seriesId,
            CancellationToken cancellationToken = default) => Task.FromResult<MediaItemDetails?>(Empty ? null :
                Details("episode", "Episode", WrongSeries ? "wrong" : seriesId)
                with
                { UserState = new(false, false, 50, Position) });
        private static MediaItemDetails Details(string id, string type, string? seriesId) =>
            new(id, type, type, seriesId, null, "season-2", 2, 3, 2026, null, TimeSpan.FromMinutes(30).Ticks,
                "PG", [], [], "Synopsis", [], [], new(false, false, 0, 0), false, false);
        public Task<MediaUserState> SetFavoriteAsync(AuthenticatedSession session, string itemId, bool isFavorite,
            CancellationToken cancellationToken = default)
        {
            Writes++;
            throw new InvalidOperationException("Browsing must not write.");
        }
        public Task<MediaUserState> SetPlayedAsync(AuthenticatedSession session, string itemId, bool isPlayed,
            CancellationToken cancellationToken = default)
        {
            Writes++;
            throw new InvalidOperationException("Browsing must not write.");
        }
        public void ClearImageCache() { }
        public Task<byte[]?> GetLibraryArtworkAsync(AuthenticatedSession session, string itemId,
            CancellationToken cancellationToken = default) => Task.FromResult<byte[]?>(null);
        public Task<MediaPreviewHome> GetHomeAsync(AuthenticatedSession session,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaLibraryPage> GetLibraryPageAsync(AuthenticatedSession session, MediaLibrary library, int startIndex,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
