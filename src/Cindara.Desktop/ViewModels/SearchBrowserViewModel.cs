using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Cindara.Core.Authentication;
using Cindara.Core.Diagnostics;
using Cindara.Core.Jellyfin;
using Cindara.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cindara.Desktop.ViewModels;

public sealed partial class SearchBrowserViewModel : ObservableObject, IDisposable
{
    private readonly IJellyfinMediaPreviewClient _client;
    private readonly AuthenticatedSession _session;
    private readonly Func<MediaPreviewException, Task> _onAccessDenied;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<MediaPreviewItem, MediaPreviewCardViewModel> _createCard;
    private readonly Func<byte[], PreviewImage> _decodeArtwork;
    private readonly LocalDiagnostics? _diagnostics;
    private readonly List<MediaPreviewItem> _sources = [];
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _artworkCancellation;
    private long _generation;
    private long _artworkGeneration;
    private int _totalRecordCount;
    private int _columns = 6;
    private (int Start, int End) _artworkWindow;
    private bool _hasSearched;
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

    public ObservableCollection<MediaPreviewCardViewModel> Items { get; } = [];
    public ObservableCollection<LibraryGridRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string _message = Loc.Get("Search.Prompt");

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    private bool _isLoadingMore;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    private bool _canRetry;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    private bool _canRetryMore;

    [ObservableProperty]
    private bool _canRetryArtwork;

    public bool HasMessage => !string.IsNullOrEmpty(Message);
    public bool IsEmpty => _hasSearched && !IsLoading && Items.Count == 0 && _totalRecordCount == 0;
    public bool HasMore => !IsLoading && !IsLoadingMore && !CanRetryMore
        && Items.Count < _totalRecordCount;
    public string CountDescription => !_hasSearched ? string.Empty
        : Loc.Format("Search.Loaded", Items.Count, _totalRecordCount);

    partial void OnQueryChanged(string value)
    {
        CancelPendingSearch();
        Interlocked.Increment(ref _generation);
        CancelArtwork();
        ClearResults();
        CanRetry = false;
        CanRetryMore = false;
        IsLoading = false;
        IsLoadingMore = false;
        Message = string.IsNullOrWhiteSpace(value) ? Loc.Get("Search.Prompt") : Loc.Get("Search.Waiting");
        NotifyResultsChanged();
        if (!string.IsNullOrWhiteSpace(value) && !_disposed)
        {
            var cancellation = new CancellationTokenSource();
            _searchCancellation = cancellation;
            _ = DebounceAsync(Volatile.Read(ref _generation), cancellation.Token);
        }
    }

    private async Task DebounceAsync(long generation, CancellationToken token)
    {
        try
        {
            await _delay(TimeSpan.FromMilliseconds(350), token);
            await SearchAsync(0, generation, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private bool CanLoadMore() => !_disposed && HasMore;
    private bool CanRetrySearch() => !_disposed && (CanRetry || CanRetryMore)
        && !IsLoading && !IsLoadingMore;

    [RelayCommand]
    private Task RefreshAsync() => StartSearchAsync(0);

    [RelayCommand(CanExecute = nameof(CanLoadMore))]
    private Task LoadMoreAsync() => StartSearchAsync(Items.Count);

    [RelayCommand(CanExecute = nameof(CanRetrySearch))]
    private Task RetryAsync() => StartSearchAsync(CanRetryMore ? Items.Count : 0);

    private async Task StartSearchAsync(int startIndex)
    {
        if (_disposed || string.IsNullOrWhiteSpace(Query))
            return;
        CancelPendingSearch();
        var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        await SearchAsync(startIndex, Interlocked.Increment(ref _generation), cancellation.Token);
    }

    private async Task SearchAsync(int startIndex, long generation, CancellationToken token)
    {
        var query = Query.Trim();
        if (_disposed || string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        var append = startIndex > 0;
        IsLoading = !append;
        IsLoadingMore = append;
        CanRetry = false;
        CanRetryMore = false;
        if (!append)
        {
            Message = Loc.Get("Search.Loading");
        }
        NotifyResultsChanged();
        using var operation = _diagnostics?.Begin(DiagnosticArea.Network, DiagnosticAction.LoadLibrary);
        var created = new List<MediaPreviewCardViewModel>();
        try
        {
            var page = await _client.SearchAsync(_session, query, startIndex, token);
            token.ThrowIfCancellationRequested();
            if (_disposed || generation != Volatile.Read(ref _generation))
            {
                return;
            }

            if (page.StartIndex != startIndex || page.TotalRecordCount < startIndex + page.Items.Count
                || page.Items.Count > MediaSearchPage.PageSize
                || page.Items.Count == 0 && startIndex < page.TotalRecordCount
                || page.Items.Any(item => item.MediaType is not ("Movie" or "Series")))
            {
                throw new MediaPreviewException(MediaPreviewError.InvalidResponse, "Invalid search results.");
            }

            foreach (var item in page.Items)
            {
                token.ThrowIfCancellationRequested();
                created.Add(_createCard(item));
            }
            token.ThrowIfCancellationRequested();
            if (_disposed || generation != Volatile.Read(ref _generation))
            {
                return;
            }

            if (!append)
            {
                CancelArtwork();
                ClearResults();
            }
            foreach (var card in created)
            {
                Items.Add(card);
            }
            _sources.AddRange(page.Items);
            created.Clear();
            _totalRecordCount = page.TotalRecordCount;
            _hasSearched = true;
            Message = Items.Count == 0 ? Loc.Get("Search.Empty") : string.Empty;
            RebuildRows();
            operation?.Complete();
        }
        catch (OperationCanceledException exception) when (token.IsCancellationRequested)
        {
            operation?.Fail(exception);
        }
        catch (MediaPreviewException exception)
        {
            operation?.Fail(exception);
            if (!_disposed && generation == Volatile.Read(ref _generation) && !token.IsCancellationRequested)
            {
                Message = append ? string.Empty : LocalizedErrors.Get(exception);
                CanRetry = !append;
                CanRetryMore = append;
                if (exception.Error == MediaPreviewError.AccessDenied)
                {
                    await _onAccessDenied(exception);
                }
            }
        }
        finally
        {
            foreach (var card in created)
            {
                card.Dispose();
            }
            if (!_disposed && generation == Volatile.Read(ref _generation))
            {
                IsLoading = false;
                IsLoadingMore = false;
                RebuildRows();
                NotifyResultsChanged();
            }
        }
    }

    public void SetColumnCount(int columns)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        if (_columns != columns)
        {
            _columns = columns;
            RebuildRows();
        }
    }

    private void RebuildRows()
    {
        var chunks = Items.Chunk(_columns).ToArray();
        while (Rows.Count > chunks.Length)
        {
            Rows.RemoveAt(Rows.Count - 1);
        }
        for (var index = 0; index < chunks.Length; index++)
        {
            if (index == Rows.Count)
            {
                Rows.Add(new LibraryGridRowViewModel(chunks[index]));
            }
            else if (!Rows[index].Items.SequenceEqual(chunks[index].Take(Rows[index].Items.Count)))
            {
                Rows[index] = new LibraryGridRowViewModel(chunks[index]);
            }
            else
            {
                var row = Rows[index].Items;
                while (row.Count > chunks[index].Length)
                {
                    row.RemoveAt(row.Count - 1);
                }
                for (var column = row.Count; column < chunks[index].Length; column++)
                {
                    row.Add(chunks[index][column]);
                }
            }
            Rows[index].SetPlaceholderCount(0);
        }
        NotifyResultsChanged();
    }

    public Task SetArtworkWindowAsync(int focusedIndex, int columns)
    {
        if (_disposed || Items.Count == 0)
        {
            return Task.CompletedTask;
        }
        var start = Math.Max(0, focusedIndex - columns * 2);
        var end = Math.Min(Items.Count, focusedIndex + columns * 4);
        if (_artworkWindow == (start, end))
        {
            return Task.CompletedTask;
        }
        return StartArtworkWindowAsync(start, end);
    }

    private Task StartArtworkWindowAsync(int start, int end)
    {
        CancelArtwork();
        _artworkWindow = (start, end);
        CanRetryArtwork = false;
        for (var index = 0; index < Items.Count; index++)
        {
            if ((index < start || index >= end) && Items[index].HasArtwork)
            {
                Items[index].SetArtwork(null);
            }
        }
        var cancellation = new CancellationTokenSource();
        _artworkCancellation = cancellation;
        return LoadArtworkAsync(start, end, Volatile.Read(ref _generation),
            Volatile.Read(ref _artworkGeneration), cancellation.Token);
    }

    [RelayCommand]
    private Task RetryArtworkAsync() => StartArtworkWindowAsync(
        _artworkWindow.Start, _artworkWindow.End);

    private async Task LoadArtworkAsync(int start, int end, long generation, long artworkGeneration,
        CancellationToken token)
    {
        var pending = Enumerable.Range(start, end - start)
            .Where(index => _sources[index].ArtworkItemId is not null && !Items[index].HasArtwork)
            .Select(index => (Source: _sources[index], Card: Items[index])).ToArray();
        foreach (var (_, card) in pending)
        {
            card.IsArtworkLoading = true;
        }
        var requests = new ConcurrentDictionary<string, Lazy<Task<byte[]?>>>(StringComparer.Ordinal);
        var next = -1;
        try
        {
            await Task.WhenAll(Enumerable.Range(0, Math.Min(6, pending.Length)).Select(_ => WorkerAsync()));
            if (!token.IsCancellationRequested && !_disposed && generation == Volatile.Read(ref _generation)
                && artworkGeneration == Volatile.Read(ref _artworkGeneration))
            {
                CanRetryArtwork = pending.Any(pair => !pair.Card.HasArtwork);
            }
        }
        finally
        {
            foreach (var (_, card) in pending)
            {
                card.IsArtworkLoading = false;
            }
        }

        async Task WorkerAsync()
        {
            while (!token.IsCancellationRequested && !_disposed && generation == Volatile.Read(ref _generation)
                && artworkGeneration == Volatile.Read(ref _artworkGeneration))
            {
                var index = Interlocked.Increment(ref next);
                if (index >= pending.Length)
                {
                    return;
                }
                var (source, card) = pending[index];
                PreviewImage? decoded = null;
                try
                {
                    var bytes = await requests.GetOrAdd(source.ArtworkItemId!,
                        id => new Lazy<Task<byte[]?>>(() => _client.GetLibraryArtworkAsync(_session, id, token))).Value;
                    token.ThrowIfCancellationRequested();
                    if (bytes is not null)
                    {
                        decoded = await Task.Run(() => _decodeArtwork(bytes), token);
                    }
                    if (!token.IsCancellationRequested && !_disposed
                        && generation == Volatile.Read(ref _generation)
                        && artworkGeneration == Volatile.Read(ref _artworkGeneration) && Items.Contains(card))
                    {
                        card.SetArtwork(decoded);
                        decoded = null;
                    }
                    if (bytes is null)
                    {
                        _diagnostics?.Record(DiagnosticArea.Network, DiagnosticAction.LoadArtwork,
                            DiagnosticOutcome.Unavailable, DiagnosticLevel.Warning);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (MediaPreviewException exception)
                {
                    _diagnostics?.Record(DiagnosticArea.Network, DiagnosticAction.LoadArtwork,
                        DiagnosticOutcome.Failed, DiagnosticLevel.Warning, exception);
                    if (exception.Error == MediaPreviewError.InvalidResponse)
                    {
                        _client.ClearImageCache();
                    }
                    if (exception.Error == MediaPreviewError.AccessDenied && !token.IsCancellationRequested
                        && !_disposed && generation == Volatile.Read(ref _generation))
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

    public void CancelLoading()
    {
        var loading = IsLoading || IsLoadingMore;
        CancelPendingSearch();
        Interlocked.Increment(ref _generation);
        if (loading)
        {
            CanRetry = IsLoading;
            CanRetryMore = IsLoadingMore;
            IsLoading = false;
            IsLoadingMore = false;
            Message = CanRetry ? Loc.Get("Search.Canceled") : string.Empty;
            NotifyResultsChanged();
        }
    }

    private void CancelPendingSearch()
    {
        var cancellation = Interlocked.Exchange(ref _searchCancellation, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private void CancelArtwork()
    {
        Interlocked.Increment(ref _artworkGeneration);
        var cancellation = Interlocked.Exchange(ref _artworkCancellation, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
        _artworkWindow = default;
    }

    private void ClearResults()
    {
        foreach (var item in Items)
        {
            item.Dispose();
        }
        Items.Clear();
        Rows.Clear();
        _sources.Clear();
        _totalRecordCount = 0;
        _hasSearched = false;
        CanRetryArtwork = false;
    }

    private void NotifyResultsChanged()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(CountDescription));
        LoadMoreCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        _disposed = true;
        Interlocked.Increment(ref _generation);
        CancelPendingSearch();
        CancelArtwork();
        ClearResults();
    }
}
