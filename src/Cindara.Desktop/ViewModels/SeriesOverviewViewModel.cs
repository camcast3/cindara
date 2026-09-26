using System.Collections.Concurrent;
using Avalonia.Media;
using Cindara.Core.Authentication;
using Cindara.Core.Diagnostics;
using Cindara.Core.Jellyfin;
using Cindara.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cindara.Desktop.ViewModels;

public sealed partial class SeriesOverviewViewModel : ObservableObject, IDisposable
{
    private readonly IJellyfinMediaPreviewClient _client;
    private readonly AuthenticatedSession _session;
    private readonly Func<MediaPreviewException, Task> _onAccessDenied;
    private readonly LocalDiagnostics? _diagnostics;
    private readonly Func<byte[], PreviewImage> _decode;
    private CancellationTokenSource? _cancellation;
    private string? _seriesId;
    private long _generation;
    private bool _disposed;

    internal SeriesOverviewViewModel(IJellyfinMediaPreviewClient client, AuthenticatedSession session,
        Func<MediaPreviewException, Task> onAccessDenied, LocalDiagnostics? diagnostics = null,
        Func<byte[], PreviewImage>? decode = null)
    {
        _client = client;
        _session = session;
        _onAccessDenied = onAccessDenied;
        _diagnostics = diagnostics;
        _decode = decode ?? PreviewImage.Decode;
        Summary = new MediaDetailsViewModel(client, session, onAccessDenied, diagnostics, decode, "Series");
    }

    public MediaDetailsViewModel Summary { get; }
    public IReadOnlyList<SeasonCardViewModel> Seasons { get; private set; } = [];
    public MediaItemDetails? Continuation { get; private set; }
    public bool IsOpen { get; private set; }
    public bool IsLoading { get; private set; }
    public bool HasSeasons => Seasons.Count > 0;
    public string Message { get; private set; } = string.Empty;
    public bool HasMessage => Message.Length > 0;
    public bool NeedsRetry { get; private set; }
    public string ArtworkMessage { get; private set; } = string.Empty;
    public bool HasArtworkMessage => ArtworkMessage.Length > 0;
    public string ContinuationLabel => IsLoading ? Loc.Get("Series.Loading")
        : NeedsRetry ? Loc.Get("Series.ContinuationUnknown")
        : Continuation is not { } episode ? Loc.Get("Series.NoContinuation")
        : Loc.Format(!episode.HasUserState ? "Series.UnknownEpisode" : episode.UserState.PlaybackPositionTicks is > 0
            ? "Series.ResumeEpisode" : "Series.NextEpisode", episode.Name);
    public string ContinuationMetadata => Continuation is { } episode
        ? string.Join(Loc.Get("Format.DetailSeparator"), new[]
        {
            LocaleFormat.EpisodeNumber(episode.SeasonNumber, episode.EpisodeNumber),
            episode.HasUserState && episode.RunTimeTicks is > 0
                && episode.UserState.PlaybackPositionTicks is >= 0
                && episode.UserState.PlaybackPositionTicks <= episode.RunTimeTicks
                ? Loc.Format("Series.Remaining", LocaleFormat.Duration(TimeSpan.FromTicks(
                    episode.RunTimeTicks.Value - episode.UserState.PlaybackPositionTicks.Value)))
                : Loc.Get("Series.RemainingUnknown"),
        }) : string.Empty;

    public async Task OpenAsync(string seriesId, string title)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Close();
        _seriesId = seriesId;
        IsOpen = true;
        await Task.WhenAll(Summary.OpenAsync(seriesId, title), LoadSeasonsAsync());
    }

    private bool CanRetry() => IsOpen && !IsLoading && NeedsRetry;

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private Task RetryAsync() => LoadSeasonsAsync();

    private async Task LoadSeasonsAsync()
    {
        _cancellation?.Cancel();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _cancellation = cancellation;
        var token = cancellation.Token;
        var generation = ++_generation;
        IsLoading = true;
        NeedsRetry = false;
        Message = Loc.Get("Series.Loading");
        ArtworkMessage = string.Empty;
        ClearSeasons();
        Continuation = null;
        Notify();
        using var operation = _diagnostics?.Begin(DiagnosticArea.Network, DiagnosticAction.LoadDetails);
        try
        {
            var seasonsTask = _client.GetSeasonsAsync(_session, _seriesId!, token);
            var continuationTask = _client.GetSeriesContinuationAsync(_session, _seriesId!, token);
            await Task.WhenAll(seasonsTask, continuationTask);
            if (!Current(generation, token)) return;
            var seasons = await seasonsTask;
            var continuation = await continuationTask;
            if (seasons.Select(season => season.Id).Distinct(StringComparer.Ordinal).Count() != seasons.Count
                || continuation is not null && (continuation.MediaType != "Episode" || continuation.SeriesId != _seriesId))
                throw new MediaPreviewException(MediaPreviewError.InvalidResponse, "Unexpected series children.");
            Seasons = seasons.Select(season => new SeasonCardViewModel(season)).ToArray();
            Continuation = continuation;
            IsLoading = false;
            Message = HasSeasons ? string.Empty : Loc.Get("Series.NoSeasons");
            Notify();
            operation?.Complete();
            await LoadArtworkAsync(generation, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            operation?.Fail(new OperationCanceledException(token));
            if (IsOpen && generation == _generation)
            {
                NeedsRetry = true;
                Message = Loc.Get("Error.Preview.TimedOut");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (IsOpen && generation == _generation)
            {
                ArtworkMessage = Loc.Get("Details.ArtworkUnavailable");
                Notify();
            }
            return;
        }
        catch (MediaPreviewException exception)
        {
            operation?.Fail(exception);
            if (Current(generation, token))
            {
                NeedsRetry = true;
                Message = LocalizedErrors.Get(exception);
                if (exception.Error == MediaPreviewError.AccessDenied) await _onAccessDenied(exception);
            }
        }
        finally
        {
            if (IsOpen && generation == _generation)
            {
                IsLoading = false;
                Notify();
            }
            if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
        }
    }

    private async Task LoadArtworkAsync(long generation, CancellationToken token)
    {
        var queue = new ConcurrentQueue<SeasonCardViewModel>(Seasons.Where(season => season.Season.HasPrimaryImage));
        async Task Worker()
        {
            while (queue.TryDequeue(out var card) && Current(generation, token))
            {
                try
                {
                    var bytes = await _client.GetLibraryArtworkAsync(_session, card.Season.Id, token);
                    if (!Current(generation, token)) return;
                    if (bytes is not null) card.SetImage(_decode(bytes));
                    else ArtworkMessage = Loc.Get("Details.ArtworkUnavailable");
                }
                catch (MediaPreviewException exception)
                {
                    if (!Current(generation, token)) return;
                    _diagnostics?.Record(DiagnosticArea.Network, DiagnosticAction.LoadArtwork,
                        DiagnosticOutcome.Failed, DiagnosticLevel.Warning, exception);
                    if (exception.Error == MediaPreviewError.InvalidResponse) _client.ClearImageCache();
                    ArtworkMessage = Loc.Get("Details.ArtworkUnavailable");
                    if (exception.Error == MediaPreviewError.AccessDenied)
                    {
                        await _onAccessDenied(exception);
                        return;
                    }
                }
                Notify();
            }
        }
        await Task.WhenAll(Enumerable.Range(0, Math.Min(4, queue.Count)).Select(_ => Worker()));
    }

    private bool Current(long generation, CancellationToken token) =>
        !_disposed && IsOpen && generation == _generation && !token.IsCancellationRequested;

    private void Notify()
    {
        OnPropertyChanged(string.Empty);
        RetryCommand.NotifyCanExecuteChanged();
    }

    private void ClearSeasons()
    {
        foreach (var season in Seasons) season.Dispose();
        Seasons = [];
    }

    public void Close()
    {
        ++_generation;
        _cancellation?.Cancel();
        IsOpen = false;
        IsLoading = false;
        NeedsRetry = false;
        Continuation = null;
        ClearSeasons();
        Summary.Close();
        Notify();
    }

    public void Dispose()
    {
        _disposed = true;
        Close();
        Summary.Dispose();
    }
}

public sealed class SeasonCardViewModel(MediaSeason season) : ObservableObject, IDisposable
{
    private PreviewImage? _image;
    public MediaSeason Season { get; } = season;
    public string Name => Season.Name;
    public bool IsWatched => Season.HasUserState && Season.UserState.IsPlayed;
    public bool IsUnwatched => Season.HasUserState && !Season.UserState.IsPlayed;
    public string WatchedState => Loc.Get(!Season.HasUserState ? "Details.StateUnknown"
        : IsWatched ? "Details.Watched" : "Details.Unwatched");
    public IImage? Image => _image?.Source;
    public bool HasImage => Image is not null;

    internal void SetImage(PreviewImage image)
    {
        _image?.Dispose();
        _image = image;
        OnPropertyChanged(nameof(Image));
        OnPropertyChanged(nameof(HasImage));
    }

    public void Dispose() => _image?.Dispose();
}
