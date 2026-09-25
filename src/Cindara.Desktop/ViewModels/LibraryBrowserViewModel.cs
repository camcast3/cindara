using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Cindara.Core.Authentication;
using Cindara.Core.Diagnostics;
using Cindara.Core.Jellyfin;
using Cindara.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cindara.Desktop.ViewModels;

public sealed partial class LibraryBrowserViewModel : ObservableObject, IDisposable
{
    private const int PlaceholderBufferSize = 60;
    private readonly IJellyfinMediaPreviewClient _client;
    private readonly AuthenticatedSession _session;
    private readonly Func<MediaPreviewException, Task> _onAccessDenied;
    private readonly Func<MediaPreviewItem, MediaPreviewCardViewModel> _createCard;
    private readonly Func<byte[], PreviewImage> _decodeArtwork;
    private readonly LocalDiagnostics? _diagnostics;
    private readonly List<MediaPreviewItem> _sources = [];
    private bool _disposed;
    private MediaLibraryQuery _activeQuery = new();
    private MediaLibraryQuery? _retryQuery;
    private bool _retryAppend;
    private int _totalRecordCount;
    private int _columnCount = 6;
    private (int Start, int End) _artworkWindow;
    private long _artworkWindowGeneration;

    internal LibraryBrowserViewModel(
        IJellyfinMediaPreviewClient client,
        AuthenticatedSession session,
        IReadOnlyList<MediaLibrary> libraries,
        Func<MediaPreviewException, Task> onAccessDenied,
        LocalDiagnostics? diagnostics = null,
        Func<MediaPreviewItem, MediaPreviewCardViewModel>? createCard = null,
        Func<byte[], PreviewImage>? decodeArtwork = null)
    {
        _client = client;
        _session = session;
        Libraries = libraries.Where(library => library.IsSupportedVideoLibrary).ToArray();
        _onAccessDenied = onAccessDenied;
        _diagnostics = diagnostics;
        _createCard = createCard ?? (item => new MediaPreviewCardViewModel(item));
        _decodeArtwork = decodeArtwork ?? PreviewImage.Decode;
    }

    public IReadOnlyList<MediaLibrary> Libraries { get; }
    public bool HasLibraries => Libraries.Count > 0;
    public bool HasMore => !IsLoadingMore && Items.Count < _totalRecordCount;
    public int NextIndex => Items.Count;
    public bool IsEmpty => !IsLoading && _totalRecordCount == 0 && SelectedLibrary is not null;
    public bool HasMessage => !string.IsNullOrEmpty(Message);
    public bool HasArtworkError => CanRetryArtwork && !string.IsNullOrEmpty(ArtworkMessage);
    public string PageDescription => SelectedLibrary is null
        ? string.Empty
        : Loc.Format("Library.Loaded", Items.Count, _totalRecordCount);
    public bool IsAnyLoading => IsLoading || IsLoadingMore;
    public int ColumnCount => _columnCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadPageCommand))]
    private MediaLibrary? _selectedLibrary;

    [ObservableProperty]
    private ObservableCollection<MediaPreviewCardViewModel> _items = [];

    public ObservableCollection<LibraryGridRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    private MediaPreviewCardViewModel? _selectedItem;

    [ObservableProperty]
    private MediaLibraryFilter _selectedFilter;

    [ObservableProperty]
    private MediaLibrarySortDirection _selectedSortDirection;

    [ObservableProperty]
    private char? _selectedLetter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string _message = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenLibraryCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadArtworkCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetFilterCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetSortDirectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetLetterCommand))]
    [NotifyCanExecuteChangedFor(nameof(RetryPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    [NotifyPropertyChangedFor(nameof(IsAnyLoading))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenLibraryCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetFilterCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetSortDirectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetLetterCommand))]
    [NotifyCanExecuteChangedFor(nameof(RetryPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    [NotifyPropertyChangedFor(nameof(IsAnyLoading))]
    [NotifyPropertyChangedFor(nameof(HasMore))]
    private bool _isLoadingMore;

    [ObservableProperty]
    private bool _canRetry;

    [ObservableProperty]
    private int _retryIndex;

    [ObservableProperty]
    private bool _canRetryMore;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasArtworkError))]
    private string _artworkMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasArtworkError))]
    private bool _canRetryArtwork;

    private bool CanOpenLibrary() => !_disposed && !IsAnyLoading;
    private bool CanLoadPage() => CanOpenLibrary() && SelectedLibrary is not null;
    private bool CanLoadMore() => CanLoadPage() && HasMore && !CanRetryMore;
    private bool CanLoadArtwork() => !_disposed && _sources.Count > 0
        && Enumerable.Range(_artworkWindow.Start, Math.Max(0, _artworkWindow.End - _artworkWindow.Start))
            .Any(index => index < _sources.Count
                && _sources[index].ArtworkItemId is not null
                && !Items[index].HasArtwork);

    [RelayCommand(CanExecute = nameof(CanOpenLibrary))]
    private async Task OpenLibraryAsync(MediaLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        if (!Libraries.Contains(library))
        {
            throw new ArgumentException("The library does not belong to this account.", nameof(library));
        }

        if (SelectedLibrary == library && Items.Count > 0)
        {
            return;
        }

        ClearPage();
        SetActiveQuery(new());
        SelectedLibrary = library;
        await LoadPageCommand.ExecuteAsync(0);
    }

    [RelayCommand(CanExecute = nameof(CanLoadPage), IncludeCancelCommand = true)]
    private async Task LoadPageAsync(int startIndex, CancellationToken cancellationToken)
    {
        await LoadQueryAsync(
            _activeQuery with { StartIndex = startIndex },
            append: startIndex > 0,
            cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanLoadMore))]
    private async Task LoadMoreAsync(CancellationToken cancellationToken)
    {
        await LoadQueryAsync(
            _activeQuery with { StartIndex = Items.Count },
            append: true,
            cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanLoadPage))]
    private async Task SetFilterAsync(MediaLibraryFilter filter, CancellationToken cancellationToken)
    {
        await LoadQueryAsync(
            _activeQuery with { StartIndex = 0, Filter = filter },
            append: false,
            cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanLoadPage))]
    private async Task SetSortDirectionAsync(
        MediaLibrarySortDirection sortDirection,
        CancellationToken cancellationToken)
    {
        await LoadQueryAsync(
            _activeQuery with { StartIndex = 0, SortDirection = sortDirection },
            append: false,
            cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanLoadPage))]
    private async Task SetLetterAsync(string? letter, CancellationToken cancellationToken)
    {
        char? startsWith = string.IsNullOrEmpty(letter) ? null : char.ToUpperInvariant(letter[0]);
        await LoadQueryAsync(
            _activeQuery with { StartIndex = 0, StartsWith = startsWith },
            append: false,
            cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanLoadPage))]
    private async Task RetryPageAsync(CancellationToken cancellationToken)
    {
        if (_retryQuery is null)
        {
            return;
        }

        await LoadQueryAsync(_retryQuery, _retryAppend, cancellationToken);
    }

    private async Task LoadQueryAsync(
        MediaLibraryQuery query,
        bool append,
        CancellationToken cancellationToken)
    {
        query.Validate();
        if (SelectedLibrary is not { } library)
        {
            throw new InvalidOperationException("Choose a library before loading a page.");
        }

        IsLoading = !append;
        IsLoadingMore = append;
        CanRetry = false;
        CanRetryMore = false;
        RetryIndex = query.StartIndex;
        _retryQuery = query;
        _retryAppend = append;
        Message = string.Empty;
        RebuildRows();
        NotifyPageChanged();
        using var operation = _diagnostics?.Begin(DiagnosticArea.Network, DiagnosticAction.LoadLibrary);
        var created = new List<MediaPreviewCardViewModel>();
        var loaded = false;
        var loadedCount = 0;
        try
        {
            LoadArtworkCommand.Cancel();
            if (LoadArtworkCommand.ExecutionTask is { } previousArtwork)
            {
                await previousArtwork;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var page = await _client.GetLibraryPageAsync(_session, library, query, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed)
            {
                return;
            }

            await Task.Run(() =>
            {
                foreach (var item in page.Items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    created.Add(_createCard(item));
                }
            }, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed)
            {
                return;
            }

            var cards = created.ToArray();
            loadedCount = cards.Length;
            if (!append)
            {
                ClearPage();
                Items = [];
            }

            foreach (var card in cards)
            {
                Items.Add(card);
            }

            _sources.AddRange(page.Items);
            _totalRecordCount = page.TotalRecordCount;
            SelectedItem ??= cards.FirstOrDefault();
            SetActiveQuery(query with { StartIndex = 0 });
            _retryQuery = null;
            created.Clear();
            Message = string.Empty;
            loaded = true;
            operation?.Complete();
        }

        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            operation?.Fail(exception);
            Message = append ? string.Empty : Loc.Get("Library.Canceled");
            CanRetry = !append;
            CanRetryMore = append;
        }
        catch (MediaPreviewException exception)
        {
            operation?.Fail(exception);
            Message = append ? string.Empty : LocalizedErrors.Get(exception);
            CanRetry = !append;
            CanRetryMore = append;
            if (exception.Error == MediaPreviewError.AccessDenied && !_disposed && !cancellationToken.IsCancellationRequested)
            {
                await _onAccessDenied(exception);
            }
        }
        finally
        {
            foreach (var card in created)
            {
                card.Dispose();
            }

            IsLoading = false;
            IsLoadingMore = false;
            RebuildRows();
            NotifyPageChanged();
        }

        if (loaded)
        {
            var focusIndex = Math.Max(0, Items.Count - loadedCount);
            await SetArtworkWindowAsync(focusIndex, _columnCount);
        }
    }

    private void SetActiveQuery(MediaLibraryQuery query)
    {
        _activeQuery = query;
        SelectedFilter = query.Filter;
        SelectedSortDirection = query.SortDirection;
        SelectedLetter = query.StartsWith;
    }

    public void SetColumnCount(int columns)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        if (_columnCount != columns)
        {
            _columnCount = columns;
            RebuildRows();
        }
    }

    public async Task SetArtworkWindowAsync(int focusedIndex, int columns)
    {
        if (Items.Count == 0)
        {
            return;
        }

        var start = Math.Max(0, focusedIndex - (columns * 2));
        var end = Math.Min(Items.Count, focusedIndex + (columns * 4));
        if (_artworkWindow == (start, end))
        {
            return;
        }

        var generation = Interlocked.Increment(ref _artworkWindowGeneration);
        _artworkWindow = (start, end);
        for (var index = 0; index < Items.Count; index++)
        {
            if ((index < start || index >= end) && Items[index].HasArtwork)
            {
                Items[index].SetArtwork(null);
            }
        }

        var previous = LoadArtworkCommand.ExecutionTask;
        LoadArtworkCommand.Cancel();
        if (previous is not null)
        {
            await previous;
        }

        if (!_disposed
            && generation == Volatile.Read(ref _artworkWindowGeneration)
            && LoadArtworkCommand.CanExecute(null))
        {
            LoadArtworkCommand.Execute(null);
        }
    }

    private void RebuildRows()
    {
        if (_disposed)
        {
            return;
        }

        var desiredItems = Items
            .Select((item, index) => (item, index))
            .Chunk(_columnCount)
            .Select(chunk => chunk.Select(entry => entry.item).ToArray())
            .ToList();
        var remaining = Items.Count == 0
            ? IsLoading ? MediaLibraryPage.PageSize : 0
            : Math.Clamp(_totalRecordCount - Items.Count, 0, PlaceholderBufferSize);
        if (CanRetryMore)
        {
            remaining = Math.Max(1, remaining);
        }

        if (desiredItems.Count == 0 && remaining > 0)
        {
            desiredItems.Add([]);
        }

        var placeholderCounts = Enumerable.Repeat(0, desiredItems.Count).ToList();
        var placeholderRowIndex = Math.Max(0, desiredItems.Count - 1);
        while (remaining > 0)
        {
            if (placeholderRowIndex >= desiredItems.Count)
            {
                desiredItems.Add([]);
                placeholderCounts.Add(0);
            }

            var capacity = _columnCount - desiredItems[placeholderRowIndex].Length;
            var count = Math.Min(remaining, capacity);
            placeholderCounts[placeholderRowIndex] = count;
            remaining -= count;
            placeholderRowIndex++;
        }

        while (Rows.Count > desiredItems.Count)
        {
            Rows.RemoveAt(Rows.Count - 1);
        }

        for (var rowIndex = 0; rowIndex < desiredItems.Count; rowIndex++)
        {
            var rowItems = desiredItems[rowIndex];
            if (rowIndex >= Rows.Count)
            {
                Rows.Add(new(rowItems));
            }
            else if (!Rows[rowIndex].Items.SequenceEqual(
                         rowItems.Take(Rows[rowIndex].Items.Count)))
            {
                Rows[rowIndex] = new(rowItems);
            }
            else
            {
                while (Rows[rowIndex].Items.Count > rowItems.Length)
                {
                    Rows[rowIndex].Items.RemoveAt(Rows[rowIndex].Items.Count - 1);
                }

                for (var itemIndex = Rows[rowIndex].Items.Count;
                     itemIndex < rowItems.Length;
                     itemIndex++)
                {
                    Rows[rowIndex].Items.Add(rowItems[itemIndex]);
                }
            }

            Rows[rowIndex].HasRetry = CanRetryMore && rowIndex == Items.Count / _columnCount;
            Rows[rowIndex].SetPlaceholderCount(placeholderCounts[rowIndex]);
        }

        NotifyPageChanged();
    }

    [RelayCommand(CanExecute = nameof(CanLoadArtwork), IncludeCancelCommand = true)]
    private async Task LoadArtworkAsync(CancellationToken cancellationToken)
    {
        if (_sources.Count == 0)
        {
            throw new InvalidOperationException("Load a library page before loading artwork.");
        }

        var pending = Enumerable.Range(
                _artworkWindow.Start,
                Math.Max(0, _artworkWindow.End - _artworkWindow.Start))
            .Where(index => index < _sources.Count && index < Items.Count)
            .Select(index => (Source: _sources[index], Card: Items[index]))
            .Where(pair => pair.Source.ArtworkItemId is not null && !pair.Card.HasArtwork)
            .ToArray();
        CanRetryArtwork = false;
        ArtworkMessage = string.Empty;
        foreach (var (_, card) in pending)
        {
            card.IsArtworkLoading = true;
        }

        using var operation = _diagnostics?.Begin(DiagnosticArea.Network, DiagnosticAction.LoadArtwork);
        var requests = new ConcurrentDictionary<string, Lazy<Task<byte[]?>>>(StringComparer.Ordinal);
        var next = -1;
        var failures = 0;
        MediaPreviewException? rejectedSession = null;
        try
        {
            await Task.WhenAll(Enumerable.Range(0, Math.Min(6, pending.Length)).Select(_ => LoadWorkerAsync()));
            if (rejectedSession is { } rejection && !_disposed)
            {
                await _onAccessDenied(rejection);
            }

            if (!_disposed)
            {
                CanRetryArtwork = pending.Any(pair => !pair.Card.HasArtwork);
                ArtworkMessage = CanRetryArtwork
                    ? Loc.Get(cancellationToken.IsCancellationRequested ? "Library.ArtworkCanceled" : "Library.ArtworkFailed")
                    : string.Empty;
            }

            if (failures > 0 || cancellationToken.IsCancellationRequested)
            {
                _diagnostics?.Record(DiagnosticArea.Network, DiagnosticAction.LoadArtwork,
                    cancellationToken.IsCancellationRequested ? DiagnosticOutcome.Canceled : DiagnosticOutcome.Failed,
                    DiagnosticLevel.Warning);
            }
            else
            {
                operation?.Complete();
            }
        }
        finally
        {
            foreach (var (_, card) in pending)
            {
                card.IsArtworkLoading = false;
            }
        }

        async Task LoadWorkerAsync()
        {
            while (!cancellationToken.IsCancellationRequested && !_disposed)
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
                        id => new Lazy<Task<byte[]?>>(() => _client.GetLibraryArtworkAsync(_session, id, cancellationToken))).Value;
                    cancellationToken.ThrowIfCancellationRequested();
                    if (bytes is not null)
                    {
                        try
                        {
                            decoded = await Task.Run(() => _decodeArtwork(bytes), cancellationToken);
                        }
                        catch (MediaPreviewException exception) when (exception.Error == MediaPreviewError.InvalidResponse)
                        {
                            if (!_disposed && !cancellationToken.IsCancellationRequested)
                            {
                                _client.ClearImageCache();
                            }

                            throw;
                        }
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    else
                    {
                        Interlocked.Increment(ref failures);
                        _diagnostics?.Record(DiagnosticArea.Network, DiagnosticAction.LoadArtwork,
                            DiagnosticOutcome.Unavailable, DiagnosticLevel.Warning);
                    }

                    if (!_disposed && Items.Contains(card))
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
                    Interlocked.Increment(ref failures);
                    operation?.Fail(exception);
                    if (exception.Error == MediaPreviewError.AccessDenied
                        && !cancellationToken.IsCancellationRequested && !_disposed)
                    {
                        Interlocked.CompareExchange(ref rejectedSession, exception, null);
                        LoadArtworkCommand.Cancel();
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
        LoadPageCommand.Cancel();
        SetFilterCommand.Cancel();
        SetSortDirectionCommand.Cancel();
        SetLetterCommand.Cancel();
        RetryPageCommand.Cancel();
        LoadMoreCommand.Cancel();
        LoadArtworkCommand.Cancel();
    }

    private void NotifyPageChanged()
    {
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(NextIndex));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(PageDescription));
    }

    private void ClearPage()
    {
        LoadArtworkCommand.Cancel();
        CanRetryArtwork = false;
        ArtworkMessage = string.Empty;
        var previous = Items;
        Items = [];
        Rows.Clear();
        _sources.Clear();
        _totalRecordCount = 0;
        _artworkWindow = default;
        Interlocked.Increment(ref _artworkWindowGeneration);
        CanRetryMore = false;
        SelectedItem = null;
        foreach (var card in previous)
        {
            card.Dispose();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        CancelLoading();
        ClearPage();
        OpenLibraryCommand.NotifyCanExecuteChanged();
        LoadPageCommand.NotifyCanExecuteChanged();
        LoadArtworkCommand.NotifyCanExecuteChanged();
        SetFilterCommand.NotifyCanExecuteChanged();
        SetSortDirectionCommand.NotifyCanExecuteChanged();
        SetLetterCommand.NotifyCanExecuteChanged();
        RetryPageCommand.NotifyCanExecuteChanged();
        LoadMoreCommand.NotifyCanExecuteChanged();
    }
}

public sealed partial class LibraryGridRowViewModel : ObservableObject
{
    public LibraryGridRowViewModel(IEnumerable<MediaPreviewCardViewModel> items)
    {
        foreach (var item in items)
        {
            Items.Add(item);
        }
    }

    public ObservableCollection<MediaPreviewCardViewModel> Items { get; } = [];
    public ObservableCollection<int> PlaceholderSlots { get; } = [];
    public ObservableCollection<LibraryGridSlotViewModel> Slots { get; } = [];

    [ObservableProperty]
    private bool _hasRetry;

    public void SetPlaceholderCount(int count)
    {
        while (PlaceholderSlots.Count > count)
        {
            PlaceholderSlots.RemoveAt(PlaceholderSlots.Count - 1);
        }

        while (PlaceholderSlots.Count < count)
        {
            PlaceholderSlots.Add(PlaceholderSlots.Count);
        }

        var desiredCount = Items.Count + count;
        while (Slots.Count > desiredCount)
        {
            Slots.RemoveAt(Slots.Count - 1);
        }

        for (var index = 0; index < desiredCount; index++)
        {
            if (index >= Slots.Count)
            {
                Slots.Add(new());
            }

            Slots[index].Item = index < Items.Count ? Items[index] : null;
            Slots[index].IsRetry = HasRetry && index == Items.Count;
        }
    }
}

public sealed partial class LibraryGridSlotViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasItem))]
    [NotifyPropertyChangedFor(nameof(IsPlaceholder))]
    private MediaPreviewCardViewModel? _item;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaceholder))]
    private bool _isRetry;

    public bool HasItem => Item is not null;
    public bool IsPlaceholder => Item is null && !IsRetry;
}
