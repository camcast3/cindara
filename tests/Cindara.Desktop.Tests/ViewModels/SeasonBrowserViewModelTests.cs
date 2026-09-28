using Cindara.Core.Authentication;
using Cindara.Core.Jellyfin;
using Cindara.Core.Models;
using Cindara.Desktop.Localization;
using Cindara.Desktop.Tests.Localization;
using Cindara.Desktop.ViewModels;

namespace Cindara.Desktop.Tests.ViewModels;

[Collection(LocalizationTestGroup.Name)]
public sealed class SeasonBrowserViewModelTests
{
    private static readonly AuthenticatedSession Session = new(
        new ServerIdentity("server", new Uri("https://media.example/"), "Media", "10.11", "Windows"),
        "user", "Viewer", "token");

    [Fact]
    public async Task BrowsesSpecialsWithSelectionMetadataAndNoWrites()
    {
        var client = new Client();
        using var model = Model(client);
        await model.OpenAsync("series", "specials", "Show", "episode-2");
        Assert.Equal(2, model.Seasons.Count);
        Assert.Equal("specials", model.SelectedSeason!.Id);
        Assert.Equal("episode-2", model.SelectedEpisode!.Episode.Id);
        Assert.Contains("Pilot 2", model.EpisodeTitle, StringComparison.Ordinal);
        Assert.Contains("PG", model.Metadata, StringComparison.Ordinal);
        Assert.Contains("Director", model.Director, StringComparison.Ordinal);
        Assert.Contains("8.5", model.Ratings, StringComparison.Ordinal);
        Assert.Equal("Synopsis 2", model.Synopsis);
        Assert.Equal("1080p H.264", model.Video);
        Assert.Equal(Loc.Get("Details.NotProvided"), model.Audio);
        Assert.True(model.HasEpisodeDetails);
        Assert.Equal(1, client.EpisodeDetailReads);
        Assert.Equal("Specials", model.SeasonTitle);
        Assert.Equal("Season synopsis", model.SeasonOverview);
        Assert.Equal(2, model.EpisodeCredits.Count);
        Assert.Equal(Loc.Format("Season.EpisodeCount", 2), model.EpisodeCount);
        Assert.Equal(Loc.Get("Season.ProgressUnknown"), model.Episodes[0].Progress);
        Assert.Equal(Loc.Format("Season.Progress", 45), model.Episodes[1].Progress);
        Assert.False(model.Episodes[0].IsUnwatched);
        Assert.False(model.Episodes[0].IsInProgress);
        Assert.True(model.Episodes[1].IsInProgress);
        Assert.False(model.Episodes[1].IsUnwatched);
        Assert.NotNull(model.Episodes[1].ProgressArc);
        await model.SelectEpisodeAsync(model.Episodes[0]);
        Assert.Equal("episode-1", model.SelectedEpisode!.Episode.Id);
        Assert.Equal(2, client.EpisodeDetailReads);
        await model.SelectEpisodeAsync(model.Episodes[1]);
        Assert.Equal(2, client.EpisodeDetailReads);
        Assert.Equal(0, client.Writes);
        model.Close();
        Assert.Empty(model.Episodes);
    }

    [Fact]
    public void EpisodeCardsUseTheSameThreeIconStatesWithoutGuessingUnknownState()
    {
        var episode = Episode("episode", "season-1", 1);
        using var unwatched = new EpisodeCardViewModel(episode with
        {
            HasUserState = true,
            UserState = new(false, false, 0, null),
        });
        using var watched = new EpisodeCardViewModel(episode with
        {
            HasUserState = true,
            UserState = new(false, true, 100, null),
        });
        using var unknown = new EpisodeCardViewModel(episode with { HasUserState = false });
        Assert.True(unwatched.IsUnwatched);
        Assert.False(unwatched.IsInProgress);
        Assert.True(watched.IsWatched);
        Assert.False(watched.IsUnwatched);
        Assert.Null(watched.ProgressArc);
        Assert.False(unknown.IsUnwatched);
        Assert.False(unknown.IsInProgress);
        Assert.Null(unknown.ProgressArc);
        Assert.Equal(Loc.Get("Season.ProgressUnknown"), unknown.Progress);
    }

    [Fact]
    public async Task EmptySeasonAndUnavailableSeasonAreExplicit()
    {
        var client = new Client { Empty = true };
        using var model = Model(client);
        await model.OpenAsync("series", "specials", "Show");
        Assert.Empty(model.Episodes);
        Assert.Equal(Loc.Get("Season.NoEpisodes"), model.Message);
        await model.OpenAsync("series", "missing", "Show");
        Assert.True(model.NeedsRetry);
        Assert.Equal(Loc.Get("Season.Unavailable"), model.Message);
        Assert.Equal(0, client.Writes);
    }

    [Fact]
    public async Task OldSeasonResponseCannotReplaceActiveSeason()
    {
        var client = new Client { Gate = new() };
        using var model = Model(client);
        var old = model.OpenAsync("series", "specials", "Show");
        client.Gate = null;
        await model.OpenAsync("series", "season-1", "Show");
        client.OldGate!.SetResult([Episode("old", "specials", 1)]);
        await old;
        Assert.Equal("season-1", model.SelectedSeason!.Id);
        Assert.Equal("season-1", model.SelectedEpisode!.Episode.SeasonId);
        Assert.Equal(0, client.Writes);
    }

    [Fact]
    public async Task SeasonPosterCreditsAndEpisodeStillsAreDecodedAndDisposed()
    {
        var client = new Client { WithImages = true };
        var decoder = new TestPreviewImageDecoder();
        using var model = new SeasonBrowserViewModel(client, Session, _ => Task.CompletedTask,
            decode: decoder.Decode);
        await model.OpenAsync("series", "specials", "Show");
        Assert.True(model.HasSeasonPoster);
        Assert.All(model.Episodes, episode => Assert.True(episode.HasImage));
        Assert.True(model.EpisodeCredits[0].HasImage);
        Assert.Equal(2, client.ThumbnailReads);
        await model.SelectSeasonAsync("season-1");
        Assert.All(decoder.Resources.Take(4), resource => Assert.Equal(1, resource.DisposeCount));
        model.Close();
        Assert.All(decoder.Resources, resource => Assert.Equal(1, resource.DisposeCount));
        Assert.Equal(0, client.Writes);
    }

    [Fact]
    public async Task EpisodeCreditsFollowSelectionAndShowAnExplicitEmptyState()
    {
        var client = new Client { NoSecondEpisodeCredits = true };
        using var model = Model(client);
        await model.OpenAsync("series", "specials", "Show");
        Assert.Equal("Actor 1", model.EpisodeCredits[0].Name);
        await model.SelectEpisodeAsync(model.Episodes[1]);
        Assert.Empty(model.EpisodeCredits);
        Assert.False(model.HasEpisodeCredits);
        await model.SelectEpisodeAsync(model.Episodes[0]);
        Assert.Equal("Actor 1", model.EpisodeCredits[0].Name);
        Assert.Equal(0, client.SeriesDetailReads);
        Assert.Equal(0, client.Writes);
        Assert.Equal(0, client.Writes);
    }

    [Fact]
    public async Task LongEpisodeCreditsHaveBoundedPreviewAndCompleteReadBelow()
    {
        var client = new Client { ManyEpisodeCredits = true };
        using var model = Model(client);
        await model.OpenAsync("series", "season-1", "Show");
        Assert.Contains("Credit 1", model.CreditsPreview, StringComparison.Ordinal);
        Assert.DoesNotContain("Credit 8", model.CreditsPreview, StringComparison.Ordinal);
        Assert.Contains("Credit 8", model.Credits, StringComparison.Ordinal);
        Assert.True(model.HasSelectedEpisode);
    }

    [Fact]
    public async Task LateEpisodeDetailsCannotReplaceNewSelectionOrClosedSeason()
    {
        var client = new Client { EpisodeGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var model = Model(client);
        await model.OpenAsync("series", "specials", "Show");
        var pending = model.SelectEpisodeAsync(model.Episodes[1]);
        Assert.True(model.IsEpisodeDetailsLoading);
        Assert.Null(model.SelectedEpisodeDetails);
        await model.SelectEpisodeAsync(model.Episodes[0]);
        client.EpisodeGate.SetResult(client.Details("episode-2"));
        await pending;
        Assert.Equal("episode-1", model.SelectedEpisodeDetails!.Id);
        Assert.Equal(string.Empty, model.EpisodeDetailsMessage);

        client.EpisodeGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var closing = model.SelectEpisodeAsync(model.Episodes[1]);
        model.Close();
        client.EpisodeGate.SetResult(client.Details("episode-2"));
        await closing;
        Assert.Null(model.SelectedEpisodeDetails);
        Assert.False(model.IsOpen);
        Assert.Equal(0, client.Writes);
    }

    [Fact]
    public async Task EpisodeDetailErrorsRemainVisibleAndAllowFocusRetry()
    {
        var client = new Client
        {
            EpisodeDetailsError = MediaPreviewError.Forbidden,
            NoSecondEpisodeCredits = true,
        };
        using var model = Model(client);
        await model.OpenAsync("series", "specials", "Show");
        await model.SelectEpisodeAsync(model.Episodes[1]);
        Assert.False(model.HasEpisodeDetails);
        Assert.True(model.HasEpisodeDetailsMessage);
        Assert.False(model.IsEpisodeDetailsLoading);
        Assert.Equal(model.EpisodeDetailsMessage, model.EpisodeCreditsStatus);
        client.EpisodeDetailsError = null;
        await model.SelectEpisodeAsync(model.Episodes[1]);
        Assert.Equal("episode-2", model.SelectedEpisodeDetails!.Id);
        Assert.False(model.HasEpisodeDetailsMessage);
        Assert.Equal(Loc.Get("Details.NoCredits"), model.EpisodeCreditsStatus);
        Assert.Equal(0, client.Writes);
    }

    [Fact]
    public async Task InvalidEpisodeParentIsRejectedWithoutCachingAndMissingTracksAreExplicit()
    {
        var client = new Client { InvalidEpisodeParent = true };
        using var model = Model(client);
        await model.OpenAsync("series", "specials", "Show");
        await model.SelectEpisodeAsync(model.Episodes[1]);
        Assert.False(model.HasEpisodeDetails);
        Assert.True(model.HasEpisodeDetailsMessage);
        client.InvalidEpisodeParent = false;
        client.NoEpisodeTracks = true;
        await model.SelectEpisodeAsync(model.Episodes[1]);
        Assert.True(model.HasEpisodeDetails);
        Assert.Equal(Loc.Get("Details.NotProvided"), model.Video);
        Assert.Equal(Loc.Get("Details.NotProvided"), model.Audio);
        Assert.Equal(Loc.Get("Details.NotProvided"), model.Subtitles);
        Assert.Equal(0, client.Writes);
    }

    [Theory]
    [InlineData(MediaPreviewError.Forbidden)]
    [InlineData(MediaPreviewError.NotFound)]
    [InlineData(MediaPreviewError.AccessDenied)]
    public async Task UnavailableEpisodesAllowRetryWithoutWrites(MediaPreviewError error)
    {
        var client = new Client { Error = error };
        var rejected = 0;
        using var model = new SeasonBrowserViewModel(client, Session, _ => { rejected++; return Task.CompletedTask; });
        await model.OpenAsync("series", "specials", "Show");
        Assert.True(model.NeedsRetry);
        Assert.Empty(model.Episodes);
        Assert.Equal(error == MediaPreviewError.AccessDenied ? 1 : 0, rejected);
        client.Error = null;
        await model.RetryCommand.ExecuteAsync(null);
        Assert.Equal(2, model.Episodes.Count);
        Assert.Equal(0, client.Writes);
    }

    private static SeasonBrowserViewModel Model(Client client) =>
        new(client, Session, _ => Task.CompletedTask);

    private static MediaEpisode Episode(string id, string season, int number) =>
        new(id, $"Pilot {number}", "series", season, season == "specials" ? 0 : 1,
            number, new DateOnly(2026, 1, 2), TimeSpan.FromMinutes(22).Ticks, "PG",
            $"Synopsis {number}", [new("director", "Director", null, "Director", null)],
            new(false, false, number == 2 ? 45 : null, null), false,
            [new("Community", 8.5)], number == 2);

    private sealed class Client : IJellyfinMediaPreviewClient
    {
        public bool Empty { get; set; }
        public int Writes { get; private set; }
        public MediaPreviewError? Error { get; set; }
        public bool WithImages { get; set; }
        public bool NoSecondEpisodeCredits { get; set; }
        public bool ManyEpisodeCredits { get; set; }
        public int ThumbnailReads { get; private set; }
        public int EpisodeDetailReads { get; private set; }
        public int SeriesDetailReads { get; private set; }
        public TaskCompletionSource<MediaItemDetails>? EpisodeGate { get; set; }
        public MediaPreviewError? EpisodeDetailsError { get; set; }
        public bool InvalidEpisodeParent { get; set; }
        public bool NoEpisodeTracks { get; set; }
        public TaskCompletionSource<IReadOnlyList<MediaEpisode>>? Gate
        {
            get => _gate;
            set { if (value is not null) OldGate = value; _gate = value; }
        }
        private TaskCompletionSource<IReadOnlyList<MediaEpisode>>? _gate;
        public TaskCompletionSource<IReadOnlyList<MediaEpisode>>? OldGate { get; private set; }
        public Task<IReadOnlyList<MediaSeason>> GetSeasonsAsync(AuthenticatedSession session,
            string seriesId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MediaSeason>>(
            [
                new("specials", "Specials", 0, new(false, false, null, null), false),
                new("season-1", "Season 1", 1, new(false, false, null, null), false),
            ]);
        public Task<MediaItemDetails> GetItemDetailsAsync(AuthenticatedSession session, string itemId,
            CancellationToken cancellationToken = default)
        {
            var episode = itemId.StartsWith("episode-", StringComparison.Ordinal);
            if (episode) EpisodeDetailReads++;
            if (itemId == "series") SeriesDetailReads++;
            if (itemId == "episode-2" && EpisodeGate is { } gate) return gate.Task;
            if (itemId == "episode-2" && EpisodeDetailsError is { } error)
                return Task.FromException<MediaItemDetails>(new MediaPreviewException(error, "episode details unavailable"));
            return Task.FromResult(Details(itemId));
        }
        public MediaItemDetails Details(string itemId)
        {
            var episode = itemId.StartsWith("episode-", StringComparison.Ordinal);
            return new MediaItemDetails(
                itemId, itemId, episode ? "Episode" : itemId == "series" ? "Series" : "Season",
                episode && itemId == "episode-2" && InvalidEpisodeParent ? "wrong-series" : "series",
                "Show", episode ? itemId == "episode-3" ? "season-1" : "specials" : itemId,
                null, null, 2026, null,
                null, "PG", [], [], "Season synopsis",
                episode && ManyEpisodeCredits
                    ? Enumerable.Range(1, 8)
                        .Select(index => new MediaCredit($"credit-{index}", $"Credit {index}", null, "Actor", null))
                        .ToArray()
                    : episode && itemId == "episode-2" && NoSecondEpisodeCredits ? []
                    : [new("actor", episode ? itemId == "episode-1" ? "Actor 1" : "Actor 2" : "Actor",
                           "Role", "Actor", WithImages ? "image" : null),
                       new("director", "Director", null, "Director", null)],
                episode && !NoEpisodeTracks ? [new MediaTrackInfo("Video", "h264", "1080p H.264", null, 1920, 1080,
                    null, true, false)] : [], new(false, false, null, null), WithImages, false);
        }
        public Task<IReadOnlyList<MediaEpisode>> GetEpisodesAsync(AuthenticatedSession session,
            string seriesId, string seasonId, CancellationToken cancellationToken = default) =>
            Gate?.Task ?? (Error is { } error
                ? Task.FromException<IReadOnlyList<MediaEpisode>>(new MediaPreviewException(error, "error"))
                : Task.FromResult<IReadOnlyList<MediaEpisode>>(Empty ? [] : seasonId == "specials"
                    ? [WithArt(Episode("episode-1", seasonId, 1)), WithArt(Episode("episode-2", seasonId, 2))]
                    : [WithArt(Episode("episode-3", seasonId, 3))]));
        private MediaEpisode WithArt(MediaEpisode episode) =>
            episode with
            {
                HasPrimaryImage = WithImages,
                Credits = NoSecondEpisodeCredits && episode.Id == "episode-2" ? []
                    : ManyEpisodeCredits ? Enumerable.Range(1, 8)
                    .Select(index => new MediaCredit($"credit-{index}", $"Credit {index}", null, "Actor", null))
                    .ToArray() : episode.Credits,
            };
        public Task<MediaUserState> SetFavoriteAsync(AuthenticatedSession session, string itemId,
            bool isFavorite, CancellationToken cancellationToken = default)
        {
            Writes++;
            throw new InvalidOperationException("Browsing cannot write.");
        }
        public Task<MediaUserState> SetPlayedAsync(AuthenticatedSession session, string itemId,
            bool isPlayed, CancellationToken cancellationToken = default)
        {
            Writes++;
            throw new InvalidOperationException("Browsing cannot write.");
        }
        public void ClearImageCache() { }
        public Task<byte[]?> GetLibraryArtworkAsync(AuthenticatedSession session, string itemId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<byte[]?>(WithImages ? [1, 2, 3] : null);
        public Task<byte[]?> GetEpisodeThumbnailAsync(AuthenticatedSession session, string itemId,
            CancellationToken cancellationToken = default)
        {
            ThumbnailReads++;
            return Task.FromResult<byte[]?>(WithImages ? [1, 2, 3] : null);
        }
        public Task<MediaPreviewHome> GetHomeAsync(AuthenticatedSession session,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaLibraryPage> GetLibraryPageAsync(AuthenticatedSession session, MediaLibrary library,
            int startIndex, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
