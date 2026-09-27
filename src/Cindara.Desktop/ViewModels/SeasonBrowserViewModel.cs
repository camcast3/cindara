using Avalonia.Media;
using Cindara.Core.Authentication;
using Cindara.Core.Diagnostics;
using Cindara.Core.Jellyfin;
using Cindara.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cindara.Desktop.ViewModels;

public sealed partial class SeasonBrowserViewModel : ObservableObject, IDisposable
{
    private readonly IJellyfinMediaPreviewClient _client;
    private readonly AuthenticatedSession _session;
    private readonly Func<MediaPreviewException, Task> _onAccessDenied;
    private readonly Func<byte[], PreviewImage> _decode;
    private readonly LocalDiagnostics? _diagnostics;
    private CancellationTokenSource? _load;
    private PreviewImage? _artwork;
    private long _generation;
    private string _seriesId = string.Empty;
    private string? _requestedSeason;
    private string? _requestedEpisode;
    private bool _disposed;

    internal SeasonBrowserViewModel(IJellyfinMediaPreviewClient client, AuthenticatedSession session,
        Func<MediaPreviewException, Task> onAccessDenied, LocalDiagnostics? diagnostics = null,
        Func<byte[], PreviewImage>? decode = null)
    {
        _client = client;
        _session = session;
        _onAccessDenied = onAccessDenied;
        _diagnostics = diagnostics;
        _decode = decode ?? PreviewImage.Decode;
    }

    public bool IsOpen { get; private set; }
    public bool IsLoading { get; private set; }
    public bool NeedsRetry { get; private set; }
    public string SeriesTitle { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public bool HasMessage => Message.Length > 0;
    public string ArtworkMessage { get; private set; } = string.Empty;
    public bool HasArtworkMessage => ArtworkMessage.Length > 0;
    public IReadOnlyList<MediaSeason> Seasons { get; private set; } = [];
    public IReadOnlyList<EpisodeCardViewModel> Episodes { get; private set; } = [];
    public MediaSeason? SelectedSeason { get; private set; }
    public EpisodeCardViewModel? SelectedEpisode { get; private set; }
    public string EpisodeTitle => SelectedEpisode?.Episode.Name ?? Loc.Get("Season.NoEpisodes");
    public string EpisodeNumber => SelectedEpisode is { } selected
        ? LocaleFormat.EpisodeNumber(selected.Episode.SeasonNumber, selected.Episode.EpisodeNumber) : string.Empty;
    public string Metadata => SelectedEpisode is { } selected
        ? string.Join(Loc.Get("Format.DetailSeparator"), new[]
        {
            selected.Episode.PremiereDate?.ToString("d", Loc.Culture),
            selected.Episode.RunTimeTicks is > 0
                ? LocaleFormat.Duration(TimeSpan.FromTicks(selected.Episode.RunTimeTicks.Value)) : null,
            selected.Episode.OfficialRating,
        }.Where(value => !string.IsNullOrWhiteSpace(value))) : string.Empty;
    public string Ratings => SelectedEpisode?.Episode.Ratings is { Count: > 0 } ratings
        ? string.Join(Loc.Get("Format.DetailSeparator"), ratings.Select(rating =>
            Loc.Format(rating.Name == "Critic" ? "Details.CriticRating" : "Details.CommunityRating",
                LocaleFormat.Number(rating.Value, rating.Name == "Critic" ? 0 : 1))))
        : Loc.Get("Details.NoRatings");
    public string Synopsis => string.IsNullOrWhiteSpace(SelectedEpisode?.Episode.Overview)
        ? Loc.Get("Details.NoSynopsis") : SelectedEpisode.Episode.Overview;
    public string Director => Loc.Format("Details.DirectorSummary",
        string.Join(Loc.Get("Format.DetailSeparator"), SelectedEpisode?.Episode.Credits
            .Where(credit => credit.CreditType == "Director").Select(credit => credit.Name) ?? [])
            is { Length: > 0 } names ? names : Loc.Get("Details.NotProvided"));
    public string Credits => string.Join(Loc.Get("Format.DetailSeparator"),
        SelectedEpisode?.Episode.Credits.Select(credit => string.IsNullOrWhiteSpace(credit.Role)
            ? credit.Name : Loc.Format("Details.CreditRole", credit.Name, credit.Role)) ?? [])
        is { Length: > 0 } names ? names : Loc.Get("Details.NoCredits");
    public IImage? Artwork => _artwork?.Source;
    public bool HasArtwork => Artwork is not null;

    public async Task OpenAsync(string seriesId, string seasonId, string title, string? episodeId = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(seriesId);
        ArgumentException.ThrowIfNullOrWhiteSpace(seasonId);
        Close();
        IsOpen = true;
        _seriesId = seriesId;
        _requestedSeason = seasonId;
        SeriesTitle = title;
        _requestedEpisode = episodeId;
        await LoadSeasonsAsync(seasonId);
    }

    private bool CanRetry() => IsOpen && !IsLoading && NeedsRetry;

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private Task RetryAsync() => Seasons.Count == 0
        ? LoadSeasonsAsync(_requestedSeason)
        : SelectSeasonAsync(SelectedSeason!.Id, _requestedEpisode);

    private async Task LoadSeasonsAsync(string? seasonId)
    {
        var (generation, token) = BeginLoad();
        try
        {
            var seasons = await _client.GetSeasonsAsync(_session, _seriesId, token);
            if (!Current(generation, token)) return;
            if (seasons.Select(season => season.Id).Distinct(StringComparer.Ordinal).Count() != seasons.Count)
                throw new MediaPreviewException(MediaPreviewError.InvalidResponse, "Duplicate season ids.");
            Seasons = seasons;
            Notify();
            if (seasons.Count == 0)
            {
                Finish(Loc.Get("Series.NoSeasons"));
                return;
            }
            var selected = seasons.FirstOrDefault(season => season.Id == seasonId);
            if (selected is null && seasonId is not null)
            {
                Finish(Loc.Get("Season.Unavailable"), retry: true);
                return;
            }
            await SelectSeasonAsync((selected ?? seasons[0]).Id, _requestedEpisode);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (CurrentGeneration(generation)) Finish(Loc.Get("Error.Preview.TimedOut"), retry: true);
        }
        catch (MediaPreviewException exception)
        {
            await HandleErrorAsync(exception, generation, token);
        }
    }

    public async Task SelectSeasonAsync(string seasonId, string? episodeId = null)
    {
        var season = Seasons.FirstOrDefault(candidate => candidate.Id == seasonId);
        if (season is null) return;
        var (generation, token) = BeginLoad();
        SelectedSeason = season;
        _requestedSeason = seasonId;
        _requestedEpisode = episodeId;
        Notify();
        try
        {
            var episodes = await _client.GetEpisodesAsync(_session, _seriesId, seasonId, token);
            if (!Current(generation, token)) return;
            if (episodes.Any(episode => episode.SeriesId != _seriesId || episode.SeasonId != seasonId)
                || episodes.Select(episode => episode.Id).Distinct(StringComparer.Ordinal).Count() != episodes.Count)
                throw new MediaPreviewException(MediaPreviewError.InvalidResponse, "Unexpected season episodes.");
            Episodes = episodes.Select(episode => new EpisodeCardViewModel(episode)).ToArray();
            SelectedEpisode = Episodes.FirstOrDefault(episode => episode.Episode.Id == episodeId)
                ?? (Episodes.Count > 0 ? Episodes[0] : null);
            Finish(episodeId is not null && SelectedEpisode?.Episode.Id != episodeId
                ? Loc.Get("Season.EpisodeUnavailable")
                : Episodes.Count == 0 ? Loc.Get("Season.NoEpisodes") : string.Empty);
            if (SelectedEpisode?.Episode.HasPrimaryImage is true)
            {
                try
                {
                    var bytes = await _client.GetLibraryArtworkAsync(_session, SelectedEpisode.Episode.Id, token);
                    if (!Current(generation, token)) return;
                    if (bytes is not null) SetArtwork(_decode(bytes));
                    else ArtworkMessage = Loc.Get("Season.ArtworkUnavailable");
                }
                catch (MediaPreviewException exception)
                {
                    if (!Current(generation, token)) return;
                    if (exception.Error == MediaPreviewError.InvalidResponse) _client.ClearImageCache();
                    _diagnostics?.Record(DiagnosticArea.Network, DiagnosticAction.LoadArtwork,
                        DiagnosticOutcome.Failed, DiagnosticLevel.Warning, exception);
                    ArtworkMessage = Loc.Get("Season.ArtworkUnavailable");
                    if (exception.Error == MediaPreviewError.AccessDenied) await _onAccessDenied(exception);
                }
                Notify();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (CurrentGeneration(generation)) Finish(Loc.Get("Error.Preview.TimedOut"), retry: true);
        }
        catch (MediaPreviewException exception)
        {
            await HandleErrorAsync(exception, generation, token);
        }
    }

    public async Task SelectEpisodeAsync(EpisodeCardViewModel episode)
    {
        if (!IsOpen || !Episodes.Contains(episode)) return;
        if (ReferenceEquals(SelectedEpisode, episode)) return;
        SelectedEpisode = episode;
        _requestedEpisode = episode.Episode.Id;
        _artwork?.Dispose();
        _artwork = null;
        ArtworkMessage = string.Empty;
        Notify();
        if (!episode.Episode.HasPrimaryImage) return;
        var (generation, token) = BeginArtworkLoad();
        try
        {
            var bytes = await _client.GetLibraryArtworkAsync(_session, episode.Episode.Id, token);
            if (!Current(generation, token)) return;
            if (bytes is not null) SetArtwork(_decode(bytes));
            else ArtworkMessage = Loc.Get("Season.ArtworkUnavailable");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (MediaPreviewException exception)
        {
            if (!Current(generation, token)) return;
            if (exception.Error == MediaPreviewError.InvalidResponse) _client.ClearImageCache();
            _diagnostics?.Record(DiagnosticArea.Network, DiagnosticAction.LoadArtwork,
                DiagnosticOutcome.Failed, DiagnosticLevel.Warning, exception);
            ArtworkMessage = Loc.Get("Season.ArtworkUnavailable");
            if (exception.Error == MediaPreviewError.AccessDenied) await _onAccessDenied(exception);
        }
        Notify();
    }

    private (long Generation, CancellationToken Token) BeginArtworkLoad()
    {
        _load?.Cancel();
        _load?.Dispose();
        _load = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return (++_generation, _load.Token);
    }

    private (long Generation, CancellationToken Token) BeginLoad()
    {
        var result = BeginArtworkLoad();
        IsLoading = true;
        NeedsRetry = false;
        Message = Loc.Get("Season.Loading");
        Episodes = [];
        SelectedEpisode = null;
        _artwork?.Dispose();
        _artwork = null;
        ArtworkMessage = string.Empty;
        Notify();
        return result;
    }

    private async Task HandleErrorAsync(MediaPreviewException exception, long generation, CancellationToken token)
    {
        if (!Current(generation, token)) return;
        _diagnostics?.Record(DiagnosticArea.Network, DiagnosticAction.LoadDetails,
            DiagnosticOutcome.Failed, DiagnosticLevel.Warning, exception);
        Finish(LocalizedErrors.Get(exception), retry: true);
        if (exception.Error == MediaPreviewError.AccessDenied) await _onAccessDenied(exception);
    }

    private void Finish(string message, bool retry = false)
    {
        IsLoading = false;
        NeedsRetry = retry;
        Message = message;
        Notify();
    }

    private bool CurrentGeneration(long generation) => IsOpen && !_disposed && generation == _generation;
    private bool Current(long generation, CancellationToken token) => CurrentGeneration(generation) && !token.IsCancellationRequested;
    private void SetArtwork(PreviewImage image)
    {
        _artwork?.Dispose();
        _artwork = image;
        Notify();
    }
    private void Notify()
    {
        OnPropertyChanged(string.Empty);
        RetryCommand.NotifyCanExecuteChanged();
    }

    public void Close()
    {
        ++_generation;
        _load?.Cancel();
        _load?.Dispose();
        _load = null;
        _artwork?.Dispose();
        _artwork = null;
        IsOpen = false;
        IsLoading = false;
        Seasons = [];
        Episodes = [];
        SelectedSeason = null;
        SelectedEpisode = null;
        NeedsRetry = false;
        Message = string.Empty;
        Notify();
    }

    public void Dispose()
    {
        _disposed = true;
        Close();
    }
}

public sealed class EpisodeCardViewModel(MediaEpisode episode)
{
    public MediaEpisode Episode { get; } = episode;
    public string Name => Episode.Name;
    public string Number => Episode.EpisodeNumber?.ToString(Loc.Culture) ?? Loc.Get("Format.Episode");
    public bool IsWatched => Episode.HasUserState && Episode.UserState.IsPlayed;
    public string Progress => !Episode.HasUserState ? Loc.Get("Season.ProgressUnknown")
        : IsWatched ? Loc.Get("Details.Watched")
        : Episode.UserState.PlayedPercentage is > 0 and < 100 ? Loc.Format("Season.Progress", Episode.UserState.PlayedPercentage)
        : Loc.Get("Details.Unwatched");
}
