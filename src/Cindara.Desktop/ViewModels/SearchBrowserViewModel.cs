using System.Collections.Concurrent;
using Cindara.Core.Authentication;
using Cindara.Core.Diagnostics;
using Cindara.Core.Jellyfin;
using Cindara.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cindara.Desktop.ViewModels;

public sealed partial class SearchBrowserViewModel : ObservableObject, IDisposable
{
    private static readonly string[] TypeOrder = ["Movie", "Series", "Season", "Episode"];
    private readonly IJellyfinMediaPreviewClient _client;
    private readonly AuthenticatedSession _session;
    private readonly Func<MediaPreviewException, Task> _onAccessDenied;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<MediaPreviewItem, MediaPreviewCardViewModel> _createCard;
    private readonly Func<byte[], PreviewImage> _decodeArtwork;
    private readonly LocalDiagnostics? _diagnostics;
    private CancellationTokenSource? _searchCancellation;
    private MediaSearchPage? _page;
    private long _generation;
    private bool _disposed;

    internal SearchBrowserViewModel(
        IJellyfinMediaPreviewClient client,
        AuthenticatedSession session,
        Func<MediaPreviewException, Task> onAccessDenied,
        LocalDiagnostics? diagnostics = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<MediaPreviewItem, MediaPreviewCardViewModel>? createCard = null,
        Func<byte[], PreviewImage>? decodeArtwork = null)
    {
        _client = client;
        _session = session;
        _onAccessDenied = onAccessDenied;
        _diagnostics = diagnostics;
        _delay = delay ?? Task.Delay;
        _createCard = createCard ?? (item => new MediaPreviewCardViewModel(item));
        _decodeArtwork = decodeArtwork ?? PreviewImage.Decode;
    }

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResults))]
    private IReadOnlyList<SearchResultGroupViewModel> _groups = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string _message = Loc.Get("Search.Prompt");

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadPageCommand))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasPreviousPage))]
    [NotifyPropertyChangedFor(nameof(HasNextPage))]
    private bool _isLoading;

    [ObservableProperty]
    private bool _canRetry;

    [ObservableProperty]
    private int _retryIndex;

    public bool HasMessage => !string.IsNullOrEmpty(Message);
    public bool HasResults => Groups.Count > 0;
    public bool IsEmpty => !IsLoading && _page is { TotalRecordCount: 0 };
    public bool HasPreviousPage => !IsLoading && _page?.StartIndex > 0;
    public bool HasNextPage => !IsLoading && _page?.HasNextPage is true;
    public int PreviousIndex => Math.Max(0, (_page?.StartIndex ?? 0) - MediaSearchPage.PageSize);
    public int NextIndex => (_page?.StartIndex ?? 0) + Groups.Sum(group => group.Items.Count);
    public string PageDescription => _page is null ? string.Empty
        : Loc.Format("Search.Page", _page.Items.Count == 0 ? 0 : _page.StartIndex + 1,
            _page.Items.Count == 0 ? 0 : _page.StartIndex + _page.Items.Count, _page.TotalRecordCount);

    partial void OnQueryChanged(string value)
    {
        CancelPendingSearch();
        var generation = Interlocked.Increment(ref _generation);
        CanRetry = false;
        RetryIndex = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            ClearResults();
            Message = Loc.Get("Search.Prompt");
            NotifyPageChanged();
            return;
        }

        ClearResults();
        Message = Loc.Get("Search.Waiting");
        var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        _ = DebounceAsync(generation, cancellation.Token);
    }

    private async Task DebounceAsync(long generation, CancellationToken cancellationToken)
    {
        try
        {
            await _delay(TimeSpan.FromMilliseconds(350), cancellationToken);
            await SearchAsync(0, generation, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private bool CanLoadPage() => !_disposed && !IsLoading && !string.IsNullOrWhiteSpace(Query);

    [RelayCommand(CanExecute = nameof(CanLoadPage))]
    private async Task LoadPageAsync(int startIndex)
    {
        CancelPendingSearch();
        var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        var generation = Interlocked.Increment(ref _generation);
        await SearchAsync(startIndex, generation, cancellation.Token);
    }

    private async Task SearchAsync(int startIndex, long generation, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
        var query = Query.Trim();
        IsLoading = true;
        CanRetry = false;
        RetryIndex = startIndex;
        Message = Loc.Get("Search.Loading");
        NotifyPageChanged();
        using var operation = _diagnostics?.Begin(DiagnosticArea.Network, DiagnosticAction.LoadLibrary);
        try
        {
            var page = await _client.SearchAsync(_session, query, startIndex, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed || generation != Volatile.Read(ref _generation)
                || !string.Equals(query, Query.Trim(), StringComparison.Ordinal))
            {
                return;
            }

            var sources = page.Items.ToDictionary(item => item.Id, StringComparer.Ordinal);
            var cards = page.Items.Select(_createCard).ToArray();
            var groups = TypeOrder
                .Select(type => new SearchResultGroupViewModel(
                    Loc.Get($"Search.Group.{type}"),
                    cards.Where(card => sources[card.Id].MediaType == type).ToArray()))
                .Where(group => group.Items.Count > 0)
                .ToArray();
            ClearResults();
            _page = page;
            Groups = groups;
            Message = page.TotalRecordCount == 0 ? Loc.Get("Search.Empty") : string.Empty;
            operation?.Complete();
            NotifyPageChanged();
            _ = LoadArtworkAsync(sources, cards, generation, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            operation?.Fail(new OperationCanceledException(cancellationToken));
        }
        catch (MediaPreviewException exception)
        {
            operation?.Fail(exception);
            if (!_disposed && generation == Volatile.Read(ref _generation))
            {
                Message = LocalizedErrors.Get(exception);
                CanRetry = true;
                if (exception.Error == MediaPreviewError.AccessDenied)
                {
                    await _onAccessDenied(exception);
                }
            }
        }
        finally
        {
            if (!_disposed && generation == Volatile.Read(ref _generation))
            {
                IsLoading = false;
                NotifyPageChanged();
            }
        }
    }

    private async Task LoadArtworkAsync(
        Dictionary<string, MediaPreviewItem> sources,
        IReadOnlyList<MediaPreviewCardViewModel> cards,
        long generation,
        CancellationToken cancellationToken)
    {
        var pending = cards.Where(card => sources[card.Id].ArtworkItemId is not null).ToArray();
        foreach (var card in pending)
        {
            card.IsArtworkLoading = true;
        }

        var requests = new ConcurrentDictionary<string, Lazy<Task<byte[]?>>>(StringComparer.Ordinal);
        var next = -1;
        try
        {
            await Task.WhenAll(Enumerable.Range(0, Math.Min(6, pending.Length)).Select(_ => WorkerAsync()));
        }
        finally
        {
            foreach (var card in pending)
            {
                card.IsArtworkLoading = false;
            }
        }

        async Task WorkerAsync()
        {
            while (!cancellationToken.IsCancellationRequested && !_disposed
                   && generation == Volatile.Read(ref _generation))
            {
                var index = Interlocked.Increment(ref next);
                if (index >= pending.Length)
                {
                    return;
                }

                var card = pending[index];
                PreviewImage? decoded = null;
                try
                {
                    var itemId = sources[card.Id].ArtworkItemId!;
                    var bytes = await requests.GetOrAdd(itemId,
                        id => new Lazy<Task<byte[]?>>(
                            () => _client.GetLibraryArtworkAsync(_session, id, cancellationToken))).Value;
                    if (bytes is not null)
                    {
                        decoded = await Task.Run(() => _decodeArtwork(bytes), cancellationToken);
                    }

                    if (!_disposed && generation == Volatile.Read(ref _generation) && Contains(card))
                    {
                        card.SetArtwork(decoded);
                        decoded = null;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (MediaPreviewException exception)
                {
                    _diagnostics?.Record(DiagnosticArea.Network, DiagnosticAction.LoadArtwork,
                        DiagnosticOutcome.Failed, DiagnosticLevel.Warning, exception);
                    if (exception.Error == MediaPreviewError.AccessDenied && !_disposed
                        && generation == Volatile.Read(ref _generation))
                    {
                        CancelPendingSearch();
                        await _onAccessDenied(exception);
                        return;
                    }
                }
                finally
                {
                    decoded?.Dispose();
                    card.IsArtworkLoading = false;
                }
            }
        }
    }

    private bool Contains(MediaPreviewCardViewModel card) =>
        Groups.Any(group => group.Items.Contains(card));

    public void CancelLoading()
    {
        CancelPendingSearch();
        if (IsLoading)
        {
            IsLoading = false;
            Message = Loc.Get("Search.Canceled");
            CanRetry = true;
            NotifyPageChanged();
        }
    }

    private void CancelPendingSearch()
    {
        var cancellation = Interlocked.Exchange(ref _searchCancellation, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private void NotifyPageChanged()
    {
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasPreviousPage));
        OnPropertyChanged(nameof(HasNextPage));
        OnPropertyChanged(nameof(PreviousIndex));
        OnPropertyChanged(nameof(NextIndex));
        OnPropertyChanged(nameof(PageDescription));
    }

    private void ClearResults()
    {
        var previous = Groups;
        Groups = [];
        _page = null;
        foreach (var group in previous)
        {
            group.Dispose();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        Interlocked.Increment(ref _generation);
        CancelPendingSearch();
        ClearResults();
        LoadPageCommand.NotifyCanExecuteChanged();
    }
}

public sealed record SearchResultGroupViewModel(
    string Title,
    IReadOnlyList<MediaPreviewCardViewModel> Items) : IDisposable
{
    public void Dispose()
    {
        foreach (var item in Items)
        {
            item.Dispose();
        }
    }
}
