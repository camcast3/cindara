using System.Collections.Concurrent;
using Cindara.Core.Authentication;
using Cindara.Core.Diagnostics;
using Cindara.Core.Jellyfin;
using Cindara.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cindara.Desktop.ViewModels;

public sealed partial class LibraryBrowserViewModel : ObservableObject, IDisposable
{
    private readonly IJellyfinMediaPreviewClient _client;
    private readonly AuthenticatedSession _session;
    private readonly Func<MediaPreviewException, Task> _onAccessDenied;
    private readonly Func<MediaPreviewItem, MediaPreviewCardViewModel> _createCard;
    private readonly Func<byte[], PreviewImage> _decodeArtwork;
    private readonly LocalDiagnostics? _diagnostics;
    private bool _disposed;
    private MediaLibraryPage? _page;

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
    public bool HasPreviousPage => !IsLoading && _page?.StartIndex > 0;
    public bool HasNextPage => !IsLoading && _page?.HasNextPage is true;
    public int PreviousIndex => Math.Max(0, (_page?.StartIndex ?? 0) - MediaLibraryPage.PageSize);
    public int NextIndex => (_page?.StartIndex ?? 0) + Items.Count;
    public bool IsEmpty => !IsLoading && _page is { TotalRecordCount: 0 };
    public bool HasMessage => !string.IsNullOrEmpty(Message);
    public bool HasArtworkMessage => !string.IsNullOrEmpty(ArtworkMessage);
    public string PageDescription => _page is null ? string.Empty
        : Loc.Format("Library.Page", Items.Count == 0 ? 0 : _page.StartIndex + 1,
            Items.Count == 0 ? 0 : _page.StartIndex + Items.Count, _page.TotalRecordCount);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadPageCommand))]
    private MediaLibrary? _selectedLibrary;

    [ObservableProperty]
    private IReadOnlyList<MediaPreviewCardViewModel> _items = [];

    [ObservableProperty]
    private MediaPreviewCardViewModel? _selectedItem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string _message = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenLibraryCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadArtworkCommand))]
    private bool _isLoading;

    [ObservableProperty]
    private bool _canRetry;

    [ObservableProperty]
    private int _retryIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasArtworkMessage))]
    private string _artworkMessage = string.Empty;

    [ObservableProperty]
    private bool _canRetryArtwork;

    private bool CanOpenLibrary() => !_disposed && !IsLoading;
    private bool CanLoadPage() => CanOpenLibrary() && SelectedLibrary is not null;
    private bool CanLoadArtwork() => CanOpenLibrary() && _page is not null
        && _page.Items.Zip(Items).Any(pair => pair.First.ArtworkItemId is not null && !pair.Second.HasArtwork);

    [RelayCommand(CanExecute = nameof(CanOpenLibrary))]
    private async Task OpenLibraryAsync(MediaLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        if (!Libraries.Contains(library))
        {
            throw new ArgumentException("The library does not belong to this account.", nameof(library));
        }

        if (SelectedLibrary == library && _page is not null)
        {
            return;
        }

        ClearPage();
        SelectedLibrary = library;
        await LoadPageCommand.ExecuteAsync(0);
    }

    [RelayCommand(CanExecute = nameof(CanLoadPage), IncludeCancelCommand = true)]
    private async Task LoadPageAsync(int startIndex, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
        if (SelectedLibrary is not { } library)
        {
            throw new InvalidOperationException("Choose a library before loading a page.");
        }

        IsLoading = true;
        CanRetry = false;
        RetryIndex = startIndex;
        Message = Loc.Get("Library.Loading");
        NotifyPageChanged();
        using var operation = _diagnostics?.Begin(DiagnosticArea.Network, DiagnosticAction.LoadLibrary);
        var created = new List<MediaPreviewCardViewModel>();
        var loaded = false;
        try
        {
            LoadArtworkCommand.Cancel();
            if (LoadArtworkCommand.ExecutionTask is { } previousArtwork)
            {
                await previousArtwork;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var page = await _client.GetLibraryPageAsync(_session, library, startIndex, cancellationToken);
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
            ClearPage();
            _page = page;
            Items = cards;
            SelectedItem = cards.FirstOrDefault();
            created.Clear();
            Message = string.Empty;
            loaded = true;
            operation?.Complete();
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            operation?.Fail(exception);
            Message = Loc.Get("Library.Canceled");
            CanRetry = true;
        }
        catch (MediaPreviewException exception)
        {
            operation?.Fail(exception);
            Message = LocalizedErrors.Get(exception);
            CanRetry = true;
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
            NotifyPageChanged();
        }

        if (loaded && LoadArtworkCommand.CanExecute(null))
        {
            LoadArtworkCommand.Execute(null);
        }
    }

    [RelayCommand(CanExecute = nameof(CanLoadArtwork), IncludeCancelCommand = true)]
    private async Task LoadArtworkAsync(CancellationToken cancellationToken)
    {
        if (_page is null)
        {
            throw new InvalidOperationException("Load a library page before loading artwork.");
        }

        var pending = _page.Items.Zip(Items)
            .Where(pair => pair.First.ArtworkItemId is not null && !pair.Second.HasArtwork).ToArray();
        CanRetryArtwork = false;
        ArtworkMessage = Loc.Get("Library.LoadingArtwork");
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
                CanRetryArtwork = pending.Any(pair => !pair.Second.HasArtwork);
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
        LoadArtworkCommand.Cancel();
    }

    private void NotifyPageChanged()
    {
        OnPropertyChanged(nameof(HasPreviousPage));
        OnPropertyChanged(nameof(HasNextPage));
        OnPropertyChanged(nameof(PreviousIndex));
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
        SelectedItem = null;
        _page = null;
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
    }
}
