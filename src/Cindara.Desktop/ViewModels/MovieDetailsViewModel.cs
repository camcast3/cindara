using System.Collections.Concurrent;
using Avalonia.Media;
using Cindara.Core.Authentication;
using Cindara.Core.Diagnostics;
using Cindara.Core.Jellyfin;
using Cindara.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cindara.Desktop.ViewModels;

public sealed partial class MovieDetailsViewModel : ObservableObject, IDisposable
{
    private readonly IJellyfinMediaPreviewClient _client;
    private readonly AuthenticatedSession _session;
    private readonly Func<MediaPreviewException, Task> _onAccessDenied;
    private readonly LocalDiagnostics? _diagnostics;
    private readonly Func<byte[], PreviewImage> _decode;
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _saveCancellation;
    private PreviewImage? _poster;
    private PreviewImage? _backdrop;
    private long _generation;
    private bool _disposed;
    private bool _stateKnown;
    private string? _itemId;

    internal MovieDetailsViewModel(
        IJellyfinMediaPreviewClient client,
        AuthenticatedSession session,
        Func<MediaPreviewException, Task> onAccessDenied,
        LocalDiagnostics? diagnostics = null,
        Func<byte[], PreviewImage>? decode = null)
    {
        _client = client;
        _session = session;
        _onAccessDenied = onAccessDenied;
        _diagnostics = diagnostics;
        _decode = decode ?? PreviewImage.Decode;
    }

    public event Action<string, MediaUserState>? UserStateChanged;
    public MediaItemDetails? Details { get; private set; }
    public IReadOnlyList<MovieCreditViewModel> Cast { get; private set; } = [];
    public IReadOnlyList<MovieCreditViewModel> Credits { get; private set; } = [];
    public string Title { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public string ArtworkMessage { get; private set; } = string.Empty;
    public bool IsOpen { get; private set; }
    public bool IsLoading { get; private set; }
    public bool IsSaving { get; private set; }
    public bool HasKnownUserState => _stateKnown;
    public bool IsWatched => _stateKnown && Details?.UserState.IsPlayed is true;
    public bool IsUnwatched => _stateKnown && Details?.UserState.IsPlayed is false;
    public bool HasDetails => Details is not null;
    public bool HasMessage => Message.Length > 0;
    public bool NeedsRetry => HasMessage && !IsLoading && !IsSaving && !_stateKnown;
    public bool HasArtworkMessage => ArtworkMessage.Length > 0;
    public bool HasCast => Cast.Count > 0;
    public bool CanClose => !IsSaving;
    public IImage? Poster => _poster?.Source;
    public IImage? Backdrop => _backdrop?.Source;
    public bool HasPoster => Poster is not null;
    public string Metadata => Details is { } item
        ? LocaleFormat.Details(item.ProductionYear, item.RunTimeTicks, item.OfficialRating) : string.Empty;
    public string Genres => string.Join(Loc.Get("Format.DetailSeparator"), Details?.Genres ?? []);
    public string Overview => string.IsNullOrWhiteSpace(Details?.Overview)
        ? Loc.Get("Details.NoSynopsis") : Details.Overview;
    public string Directors => CreditNames("Director");
    public string DirectorSummary => Loc.Format("Details.DirectorSummary", Directors);
    public string Crew => string.Join(Loc.Get("Format.DetailSeparator"),
        (Details?.Credits ?? []).Where(credit => credit.CreditType is not ("Actor" or "GuestStar" or "Director"))
        .Select(credit => string.IsNullOrWhiteSpace(credit.Role)
            ? credit.Name : Loc.Format("Details.CreditRole", credit.Name, credit.Role)).Distinct());
    public bool HasCrew => Crew.Length > 0;
    public string Ratings => Details?.Ratings.Count > 0
        ? string.Join(Loc.Get("Format.DetailSeparator"), Details.Ratings.Select(rating =>
            Loc.Format(rating.Name == "Critic" ? "Details.CriticRating" : "Details.CommunityRating",
                LocaleFormat.Number(rating.Value, rating.Name == "Critic" ? 0 : 1))))
        : Loc.Get("Details.NoRatings");
    public string Video => TrackDescription("Video");
    public string Audio => TrackDescription("Audio");
    public string Subtitles => TrackDescription("Subtitle");
    public string Trailers => Details is { } item
        ? Loc.Get(item.LocalTrailerCount > 0 || item.HasRemoteTrailers
            ? "Details.TrailersAvailable" : "Details.NoTrailers")
        : string.Empty;
    public string FavoriteLabel => Loc.Get(Details?.UserState.IsFavorite is true
        ? "Details.RemoveFavorite" : "Details.AddFavorite");
    public string WatchedLabel => Loc.Get(Details?.UserState.IsPlayed is true
        ? "Details.MarkUnwatched" : "Details.MarkWatched");
    public string WatchedState => !_stateKnown ? Loc.Get("Details.StateUnknown")
        : Loc.Get(Details?.UserState.IsPlayed is true ? "Details.Watched" : "Details.Unwatched");
    public string PlaybackState => !_stateKnown ? Loc.Get("Details.StateUnknown")
        : Details?.UserState.PlaybackPositionTicks is { } position && position > 0
            ? Loc.Format("Details.ResumeState", LocaleFormat.Duration(TimeSpan.FromTicks(position)))
            : Loc.Get("Details.PlayState");

    private string CreditNames(string type) =>
        string.Join(Loc.Get("Format.DetailSeparator"),
            (Details?.Credits ?? []).Where(credit => credit.CreditType == type).Select(credit => credit.Name)) is { Length: > 0 } names
            ? names : Loc.Get("Details.NotProvided");

    private string TrackDescription(string type)
    {
        var tracks = (Details?.Tracks ?? []).Where(track => track.TrackType == type).ToArray();
        if (tracks.Length == 0) return Loc.Get("Details.NotProvided");
        var track = tracks.FirstOrDefault(track => track.IsDefault) ?? tracks[0];
        if (type == "Subtitle" && !tracks.Any(track => track.IsDefault))
            return Loc.Format("Details.TrackCount", tracks.Length);

        string description;
        if (!string.IsNullOrWhiteSpace(track.DisplayTitle))
        {
            description = track.DisplayTitle;
        }
        else
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(track.Codec)) parts.Add(track.Codec.ToUpperInvariant());
            if (!string.IsNullOrWhiteSpace(track.Language)) parts.Add(track.Language);
            if (track.Width is > 0 && track.Height is > 0)
                parts.Add(Loc.Format("Details.Resolution", track.Width, track.Height));
            if (track.Channels is > 0) parts.Add(Loc.Format("Details.Channels", track.Channels));
            description = parts.Count > 0 ? string.Join(Loc.Get("Format.DetailSeparator"), parts)
                : Loc.Get("Details.NotProvided");
        }
        return tracks.Length > 1
            ? Loc.Format("Details.MoreTracks", description, tracks.Length - 1) : description;
    }

    public async Task OpenAsync(string itemId, string title)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        if (IsSaving) return;
        Close();
        _itemId = itemId;
        Title = title;
        IsOpen = true;
        await LoadAsync();
    }

    private bool CanRefresh() => !_disposed && IsOpen && !IsLoading && !IsSaving;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        using var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        var token = cancellation.Token;
        var generation = ++_generation;
        var itemId = _itemId!;
        IsLoading = true;
        _stateKnown = false;
        Message = Loc.Get("Details.Loading");
        NotifyState();
        using var operation = _diagnostics?.Begin(DiagnosticArea.Network, DiagnosticAction.LoadDetails);
        try
        {
            var details = await _client.GetItemDetailsAsync(_session, itemId, token);
            if (!IsCurrent(generation, token)) return;
            if (details.Id != itemId || details.MediaType != "Movie")
                throw new MediaPreviewException(MediaPreviewError.InvalidResponse, "Unexpected movie details.");
            ClearImages();
            Details = details;
            Title = details.Name;
            _stateKnown = details.HasUserState;
            Credits = details.Credits.Select(credit => new MovieCreditViewModel(credit)).ToArray();
            Cast = Credits.Where(credit => credit.Credit.CreditType is "Actor" or "GuestStar").ToArray();
            Message = _stateKnown ? string.Empty : Loc.Get("Details.StateUnknown");
            ArtworkMessage = string.Empty;
            IsLoading = false;
            NotifyState();
            if (_stateKnown) UserStateChanged?.Invoke(details.Id, details.UserState);
            operation?.Complete();
            await LoadArtworkAsync(details, generation, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            operation?.Fail(new OperationCanceledException(token));
        }
        catch (MediaPreviewException exception)
        {
            operation?.Fail(exception);
            if (IsCurrent(generation, token))
            {
                Message = LocalizedErrors.Get(exception);
                if (exception.Error == MediaPreviewError.AccessDenied) await _onAccessDenied(exception);
            }
        }
        finally
        {
            if (IsCurrent(generation, token))
            {
                IsLoading = false;
                NotifyState();
            }
            if (ReferenceEquals(_loadCancellation, cancellation)) _loadCancellation = null;
        }
    }

    private async Task LoadArtworkAsync(MediaItemDetails details, long generation, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var jobs = new ConcurrentQueue<(Func<Task<byte[]?>> Fetch, Action<PreviewImage> Apply)>();
        if (details.HasPrimaryImage)
            jobs.Enqueue((() => _client.GetLibraryArtworkAsync(_session, details.Id, deadline.Token), image => _poster = image));
        if (details.HasBackdrop)
            jobs.Enqueue((() => _client.GetDetailBackdropAsync(_session, details.Id, deadline.Token), image => _backdrop = image));
        foreach (var person in Cast.Where(person => person.Credit.ImageTag is not null))
            jobs.Enqueue((() => _client.GetLibraryArtworkAsync(_session, person.Credit.Id, deadline.Token), person.SetImage));

        async Task Worker()
        {
            while (jobs.TryDequeue(out var job) && IsCurrent(generation, token))
            {
                try
                {
                    var bytes = await job.Fetch();
                    if (!IsCurrent(generation, token)) return;
                    if (bytes is null)
                    {
                        ArtworkMessage = Loc.Get("Details.ArtworkUnavailable");
                    }
                    else
                    {
                        var image = _decode(bytes);
                        job.Apply(image);
                    }
                    NotifyState();
                }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested)
                {
                    if (IsCurrent(generation, token))
                    {
                        ArtworkMessage = Loc.Get("Details.ArtworkUnavailable");
                        NotifyState();
                    }
                    return;
                }
                catch (MediaPreviewException exception)
                {
                    _diagnostics?.Record(DiagnosticArea.Network, DiagnosticAction.LoadArtwork,
                        DiagnosticOutcome.Failed, DiagnosticLevel.Warning, exception);
                    if (!IsCurrent(generation, token)) return;
                    if (exception.Error == MediaPreviewError.InvalidResponse) _client.ClearImageCache();
                    ArtworkMessage = Loc.Get("Details.ArtworkUnavailable");
                    NotifyState();
                    if (exception.Error == MediaPreviewError.AccessDenied)
                    {
                        await _onAccessDenied(exception);
                        return;
                    }
                }
            }
        }

        await Task.WhenAll(Enumerable.Range(0, Math.Min(4, jobs.Count)).Select(_ => Worker()));
    }

    private bool CanChangeState() => !_disposed && IsOpen && _stateKnown && Details is not null
        && !IsLoading && !IsSaving;

    [RelayCommand(CanExecute = nameof(CanChangeState))]
    private Task ToggleFavoriteAsync() => SaveAsync(favorite: true, !Details!.UserState.IsFavorite);

    [RelayCommand(CanExecute = nameof(CanChangeState))]
    private Task ToggleWatchedAsync() => SaveAsync(favorite: false, !Details!.UserState.IsPlayed);

    private async Task SaveAsync(bool favorite, bool target)
    {
        if (IsSaving || Details is not { } details || !_stateKnown) return;
        var generation = _generation;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _saveCancellation = cancellation;
        IsSaving = true;
        Message = string.Empty;
        NotifyState();
        using var operation = _diagnostics?.Begin(DiagnosticArea.Network,
            favorite ? DiagnosticAction.UpdateFavorite : DiagnosticAction.UpdateWatched);
        try
        {
            var state = favorite
                ? await _client.SetFavoriteAsync(_session, details.Id, target, cancellation.Token)
                : await _client.SetPlayedAsync(_session, details.Id, target, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!IsCurrent(generation, cancellation.Token)) return;
            Details = details with { UserState = state };
            Message = string.Empty;
            UserStateChanged?.Invoke(details.Id, state);
            operation?.Complete();
        }
        catch (Exception exception) when (exception is MediaPreviewException
            || exception is OperationCanceledException && cancellation.IsCancellationRequested)
        {
            operation?.Fail(exception);
            if (!_disposed && IsOpen && generation == _generation)
            {
                // A lost response can follow a committed write. Never retry by toggling stale state.
                _stateKnown = false;
                Message = Loc.Format("Details.SaveUncertain", exception is MediaPreviewException media
                    ? LocalizedErrors.Get(media) : Loc.Get("Error.Preview.TimedOut"));
                if (exception is MediaPreviewException { Error: MediaPreviewError.AccessDenied } denied)
                    await _onAccessDenied(denied);
            }
        }
        finally
        {
            if (ReferenceEquals(_saveCancellation, cancellation)) _saveCancellation = null;
            IsSaving = false;
            NotifyState();
        }
    }

    private bool IsCurrent(long generation, CancellationToken token) =>
        !_disposed && IsOpen && generation == _generation && !token.IsCancellationRequested;

    private void NotifyState()
    {
        OnPropertyChanged(string.Empty);
        RefreshCommand.NotifyCanExecuteChanged();
        ToggleFavoriteCommand.NotifyCanExecuteChanged();
        ToggleWatchedCommand.NotifyCanExecuteChanged();
    }

    public void Close()
    {
        ++_generation;
        _loadCancellation?.Cancel();
        _saveCancellation?.Cancel();
        IsOpen = false;
        IsLoading = false;
        _stateKnown = false;
        Details = null;
        ClearImages();
        NotifyState();
    }

    private void ClearImages()
    {
        _poster?.Dispose();
        _poster = null;
        _backdrop?.Dispose();
        _backdrop = null;
        foreach (var person in Credits) person.Dispose();
        Credits = [];
        Cast = [];
    }

    public void Dispose()
    {
        _disposed = true;
        Close();
    }
}

public sealed class MovieCreditViewModel(MediaCredit credit) : ObservableObject, IDisposable
{
    private PreviewImage? _image;
    public MediaCredit Credit { get; } = credit;
    public string Name => Credit.Name;
    public string Role => Credit.Role ?? string.Empty;
    public string Kind => Credit.CreditType switch
    {
        "Actor" or "GuestStar" => Loc.Get("Details.Cast"),
        "Director" => Loc.Get("Details.Director"),
        _ => Credit.CreditType,
    };
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
