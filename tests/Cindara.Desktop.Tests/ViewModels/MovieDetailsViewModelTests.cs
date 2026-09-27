using Cindara.Core.Authentication;
using Cindara.Core.Diagnostics;
using Cindara.Core.Jellyfin;
using Cindara.Core.Models;
using Cindara.Desktop.Localization;
using Cindara.Desktop.Tests.Localization;
using Cindara.Desktop.ViewModels;

namespace Cindara.Desktop.Tests.ViewModels;

[Collection(LocalizationTestGroup.Name)]
public sealed class MovieDetailsViewModelTests
{
    private static readonly AuthenticatedSession Session = new(
        new ServerIdentity("server", new Uri("https://media.example/"), "Media", "10.11", "Windows"),
        "user", "Viewer", "token");

    [Fact]
    public async Task ReadingMetadataAndCreditsDoesNotWriteOrChangeProgress()
    {
        var client = new Client();
        using var model = Model(client);
        await model.OpenAsync("movie", "Card");
        Assert.Equal("Full movie", model.Title);
        Assert.Contains("2025", model.Metadata, StringComparison.Ordinal);
        Assert.Equal("Director", model.Directors);
        Assert.Equal("Actor", Assert.Single(model.Cast).Name);
        Assert.Equal("Lead", model.Cast[0].Role);
        Assert.Contains("8.2", model.Ratings, StringComparison.Ordinal);
        Assert.Contains("HEVC", model.Video, StringComparison.Ordinal);
        Assert.Contains("eng", model.Audio, StringComparison.Ordinal);
        Assert.Equal(Loc.Format("Details.TrackCount", 1), model.Subtitles);
        Assert.Contains("30m", model.PlaybackState, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromMinutes(30).Ticks, model.Details!.UserState.PlaybackPositionTicks);
        Assert.Equal(0, client.Writes);
        model.Close();
        Assert.Equal(0, client.Writes);
        Assert.Empty(model.Cast);
        Assert.Null(model.Details);
    }

    [Fact]
    public async Task WritesAreSerializedNonOptimisticAndUseServerResponse()
    {
        var client = new Client { WriteGate = new() };
        using var model = Model(client);
        await model.OpenAsync("movie", "Card");
        var save = model.ToggleFavoriteCommand.ExecuteAsync(null);
        Assert.True(model.IsSaving);
        Assert.False(model.CanClose);
        Assert.False(model.Details!.UserState.IsFavorite);
        Assert.False(model.ToggleFavoriteCommand.CanExecute(null));
        Assert.False(model.ToggleWatchedCommand.CanExecute(null));
        Assert.False(model.RefreshCommand.CanExecute(null));
        Assert.Equal(1, client.Writes);
        var authoritative = new MediaUserState(true, true, 100, 0);
        client.WriteGate.SetResult(authoritative);
        await save;
        Assert.Equal(authoritative, model.Details.UserState);
        Assert.Equal(Loc.Get("Details.RemoveFavorite"), model.FavoriteLabel);
        Assert.True(model.CanClose);
    }

    [Fact]
    public async Task WatchedTogglesDirectlyAndShowsOnlyAuthoritativeState()
    {
        var client = new Client();
        using var model = Model(client);
        await model.OpenAsync("movie", "Card");
        Assert.True(model.IsUnwatched);
        Assert.False(model.IsWatched);
        await model.ToggleWatchedCommand.ExecuteAsync(null);
        Assert.Equal(1, client.Writes);
        Assert.True(model.Details!.UserState.IsPlayed);
        Assert.True(model.IsWatched);
        Assert.False(model.IsUnwatched);
        Assert.False(model.HasMessage);
        Assert.Equal(0, model.Details.UserState.PlaybackPositionTicks);
        await model.ToggleWatchedCommand.ExecuteAsync(null);
        Assert.False(model.Details.UserState.IsPlayed);
        Assert.True(model.IsUnwatched);
        Assert.False(model.HasMessage);
    }

    [Fact]
    public async Task SuccessfulChangesAreLoggedWithoutCustomerFacingStatusBanners()
    {
        var directory = Directory.CreateTempSubdirectory("cindara-movie-diagnostics-");
        try
        {
            var diagnostics = new LocalDiagnostics(directory.FullName);
            var client = new Client { WriteGate = new() };
            using var model = new MovieDetailsViewModel(client, Session, _ => Task.CompletedTask, diagnostics);
            await model.OpenAsync("movie", "Card");
            var save = model.ToggleWatchedCommand.ExecuteAsync(null);
            Assert.True(model.IsSaving);
            Assert.True(model.IsUnwatched);
            Assert.False(model.IsWatched);
            Assert.False(model.HasMessage);
            Assert.False(model.ToggleFavoriteCommand.CanExecute(null));
            Assert.False(model.ToggleWatchedCommand.CanExecute(null));
            client.WriteGate.SetResult(new(false, true, 100, 0));
            await save;
            Assert.True(model.IsWatched);
            Assert.False(model.HasMessage);
            client.WriteGate = null;
            await model.ToggleFavoriteCommand.ExecuteAsync(null);
            Assert.False(model.HasMessage);
            var entries = diagnostics.Snapshot();
            Assert.Contains(entries, entry => entry.Action == DiagnosticAction.UpdateWatched
                && entry.Outcome == DiagnosticOutcome.Completed);
            Assert.Contains(entries, entry => entry.Action == DiagnosticAction.UpdateFavorite
                && entry.Outcome == DiagnosticOutcome.Completed);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(MediaPreviewError.Network)]
    [InlineData(MediaPreviewError.TimedOut)]
    [InlineData(MediaPreviewError.InvalidResponse)]
    [InlineData(MediaPreviewError.Forbidden)]
    public async Task UncertainWriteRequiresFreshReadInsteadOfBlindToggle(MediaPreviewError error)
    {
        var client = new Client { WriteError = error };
        var rejected = 0;
        using var model = new MovieDetailsViewModel(client, Session, _ => { rejected++; return Task.CompletedTask; });
        await model.OpenAsync("movie", "Card");
        Assert.False(model.NeedsRetry);
        await model.ToggleFavoriteCommand.ExecuteAsync(null);
        Assert.Equal(1, client.Writes);
        Assert.False(model.Details!.UserState.IsFavorite);
        Assert.Contains(LocalizedErrors.Get(new MediaPreviewException(error, "error")), model.Message, StringComparison.Ordinal);
        Assert.False(model.ToggleFavoriteCommand.CanExecute(null));
        Assert.False(model.ToggleWatchedCommand.CanExecute(null));
        Assert.False(model.HasKnownUserState);
        Assert.False(model.IsWatched);
        Assert.False(model.IsUnwatched);
        Assert.True(model.NeedsRetry);
        Assert.Equal(0, rejected);
        client.Value = client.Value with { UserState = new(true, false, 25, TimeSpan.FromMinutes(30).Ticks) };
        client.WriteError = null;
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(1, client.Writes);
        Assert.True(model.Details.UserState.IsFavorite);
        Assert.Equal(Loc.Get("Details.RemoveFavorite"), model.FavoriteLabel);
        Assert.True(model.ToggleFavoriteCommand.CanExecute(null));
        Assert.False(model.NeedsRetry);
    }

    [Fact]
    public async Task ExpiredSessionUsesExistingReauthenticationPath()
    {
        var client = new Client { WriteError = MediaPreviewError.AccessDenied };
        var rejected = 0;
        using var model = new MovieDetailsViewModel(client, Session, _ => { rejected++; return Task.CompletedTask; });
        await model.OpenAsync("movie", "Card");
        await model.ToggleFavoriteCommand.ExecuteAsync(null);
        Assert.Equal(1, rejected);
        Assert.False(model.ToggleFavoriteCommand.CanExecute(null));
    }

    [Fact]
    public async Task LateResponseAfterBackOrAnotherMovieCannotReplaceCurrentDetails()
    {
        var old = new TaskCompletionSource<MediaItemDetails>();
        var client = new Client { DetailGate = old };
        using var model = Model(client);
        var loading = model.OpenAsync("old", "Old");
        model.Close();
        client.DetailGate = null;
        await model.OpenAsync("movie", "Current");
        old.SetResult(client.Value with { Id = "old", Name = "Late" });
        await loading;
        Assert.Equal("Full movie", model.Title);
        Assert.Equal("movie", model.Details!.Id);
        Assert.False(model.IsLoading);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("qps-ploc")]
    [InlineData("qps-plocm")]
    public async Task MissingMetadataIsHonestAndUnknownUserStateDisablesWrites(string culture)
    {
        using var scope = new CultureScope(culture);
        var client = new Client();
        client.Value = client.Value with
        {
            Overview = null,
            Credits = [],
            Tracks = [],
            Ratings = [],
            HasUserState = false,
            HasPrimaryImage = false,
            HasBackdrop = false,
        };
        using var model = Model(client);
        await model.OpenAsync("movie", "Card");
        Assert.Equal(Loc.Get("Details.NoSynopsis"), model.Overview);
        Assert.Equal(Loc.Get("Details.NotProvided"), model.Directors);
        Assert.Equal(Loc.Get("Details.NotProvided"), model.Video);
        Assert.Equal(Loc.Get("Details.NoRatings"), model.Ratings);
        Assert.Equal(Loc.Get("Details.StateUnknown"), model.Message);
        Assert.False(model.ToggleFavoriteCommand.CanExecute(null));
        Assert.False(model.ToggleWatchedCommand.CanExecute(null));
        Assert.False(model.HasKnownUserState);
    }

    [Theory]
    [InlineData(MediaPreviewError.NotFound)]
    [InlineData(MediaPreviewError.Forbidden)]
    [InlineData(MediaPreviewError.Network)]
    public async Task DetailErrorsOfferRefreshWithoutFakeContent(MediaPreviewError error)
    {
        var client = new Client { ReadError = error };
        using var model = Model(client);
        await model.OpenAsync("movie", "Card");
        Assert.False(model.HasDetails);
        Assert.True(model.RefreshCommand.CanExecute(null));
        Assert.Equal(LocalizedErrors.Get(new MediaPreviewException(error, "test")), model.Message);
        client.ReadError = null;
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.True(model.HasDetails);
    }

    [Fact]
    public async Task CorruptArtworkDoesNotHideMetadataAndRefreshRetries()
    {
        var client = new Client { Artwork = [1], Value = Details() with { HasPrimaryImage = true } };
        var attempts = 0;
        using var model = new MovieDetailsViewModel(client, Session, _ => Task.CompletedTask,
            decode: _ =>
            {
                attempts++;
                throw new MediaPreviewException(MediaPreviewError.InvalidResponse, "image");
            });
        await model.OpenAsync("movie", "Card");
        Assert.True(model.HasDetails);
        Assert.True(model.HasArtworkMessage);
        Assert.Equal(1, client.CacheClears);
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(2, attempts);
    }

    private static MovieDetailsViewModel Model(Client client) => new(client, Session, _ => Task.CompletedTask);

    [Fact]
    public async Task TrackSummariesStayCompactWithoutRepeatingDefaultsOrListingEveryLanguage()
    {
        var client = new Client();
        client.Value = client.Value with
        {
            Tracks = [
                new("Video", "av1", "4K AV1 SDR", null, 3840, 2160, null, true, false),
                new("Audio", "eac3", "English - Dolby Atmos - Default", "eng", null, null, 6, true, false),
                .. Enumerable.Range(0, 30).Select(index =>
                    new MediaTrackInfo("Subtitle", "srt", index == 0 ? "English - Default - SUBRIP" : $"Language {index}",
                        null, null, null, null, index == 0, false)),
            ],
        };
        using var model = Model(client);
        await model.OpenAsync("movie", "Card");
        Assert.Equal("4K AV1 SDR", model.Video);
        Assert.Equal("English - Dolby Atmos - Default", model.Audio);
        Assert.Equal(Loc.Format("Details.MoreTracks", "English - Default - SUBRIP", 29), model.Subtitles);
        Assert.DoesNotContain(Environment.NewLine, model.Subtitles, StringComparison.Ordinal);
        Assert.DoesNotContain("Language 1", model.Subtitles, StringComparison.Ordinal);
    }

    private static MediaItemDetails Details() => new(
        "movie", "Full movie", "Movie", null, null, null, null, null, 2025, null,
        TimeSpan.FromHours(2).Ticks, "PG-13", ["Drama"], [new("Community", 8.2)], "Full synopsis.",
        [new("director", "Director", null, "Director", null), new("actor", "Actor", "Lead", "Actor", null)],
        [new("Video", "hevc", null, null, 3840, 2160, null, true, false),
            new("Audio", "aac", null, "eng", null, null, 2, true, false),
            new("Subtitle", "srt", null, "spa", null, null, null, false, true)],
        new(false, false, 25, TimeSpan.FromMinutes(30).Ticks), false, false);

    private sealed class Client : IJellyfinMediaPreviewClient
    {
        public MediaItemDetails Value { get; set; } = Details();
        public TaskCompletionSource<MediaItemDetails>? DetailGate { get; set; }
        public TaskCompletionSource<MediaUserState>? WriteGate { get; set; }
        public MediaPreviewError? WriteError { get; set; }
        public MediaPreviewError? ReadError { get; set; }
        public byte[]? Artwork { get; set; }
        public int Writes { get; private set; }
        public int CacheClears { get; private set; }
        public Task<MediaItemDetails> GetItemDetailsAsync(AuthenticatedSession session, string itemId,
            CancellationToken cancellationToken = default) => ReadError is { } error
                ? Task.FromException<MediaItemDetails>(new MediaPreviewException(error, "read"))
                : DetailGate?.Task ?? Task.FromResult(Value with { Id = itemId });
        public Task<MediaUserState> SetFavoriteAsync(AuthenticatedSession session, string itemId, bool isFavorite,
            CancellationToken cancellationToken = default) => Write(Value.UserState with { IsFavorite = isFavorite });
        public Task<MediaUserState> SetPlayedAsync(AuthenticatedSession session, string itemId, bool isPlayed,
            CancellationToken cancellationToken = default) => Write(Value.UserState with
            { IsPlayed = isPlayed, PlayedPercentage = isPlayed ? 100 : 0, PlaybackPositionTicks = 0 });
        private Task<MediaUserState> Write(MediaUserState state)
        {
            Writes++;
            if (WriteError is { } error) throw new MediaPreviewException(error, "write");
            Value = Value with { UserState = state };
            return WriteGate?.Task ?? Task.FromResult(state);
        }
        public void ClearImageCache() => CacheClears++;
        public Task<byte[]?> GetLibraryArtworkAsync(AuthenticatedSession session, string itemId,
            CancellationToken cancellationToken = default) => Task.FromResult(Artwork);
        public Task<MediaPreviewHome> GetHomeAsync(AuthenticatedSession session, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<MediaLibraryPage> GetLibraryPageAsync(AuthenticatedSession session, MediaLibrary library,
            int startIndex, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
