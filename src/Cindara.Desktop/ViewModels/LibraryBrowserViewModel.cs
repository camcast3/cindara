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
    private readonly LocalDiagnostics? _diagnostics;
    private bool _disposed;
    private MediaLibraryPage? _page;

    internal LibraryBrowserViewModel(
        IJellyfinMediaPreviewClient client,
        AuthenticatedSession session,
        IReadOnlyList<MediaLibrary> libraries,
        Func<MediaPreviewException, Task> onAccessDenied,
        LocalDiagnostics? diagnostics = null,
        Func<MediaPreviewItem, MediaPreviewCardViewModel>? createCard = null)
    {
        _client = client;
        _session = session;
        Libraries = libraries;
        _onAccessDenied = onAccessDenied;
        _diagnostics = diagnostics;
        _createCard = createCard ?? (item => new MediaPreviewCardViewModel(item));
    }

    public IReadOnlyList<MediaLibrary> Libraries { get; }
    public bool HasLibraries => Libraries.Count > 0;
    public bool HasPreviousPage => !IsLoading && _page?.StartIndex > 0;
    public bool HasNextPage => !IsLoading && _page?.HasNextPage is true;
    public int PreviousIndex => Math.Max(0, (_page?.StartIndex ?? 0) - MediaLibraryPage.PageSize);
    public int NextIndex => (_page?.StartIndex ?? 0) + Items.Count;
    public bool IsEmpty => !IsLoading && _page is { TotalRecordCount: 0 };
    public bool HasMessage => !string.IsNullOrEmpty(Message);
    public string PageDescription => _page is null ? string.Empty
        : Loc.Format("Library.Page", Items.Count == 0 ? 0 : _page.StartIndex + 1,
            Items.Count == 0 ? 0 : _page.StartIndex + Items.Count, _page.TotalRecordCount);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadPageCommand))]
    private MediaLibrary? _selectedLibrary;

    [ObservableProperty]
    private IReadOnlyList<MediaPreviewCardViewModel> _items = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string _message = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenLibraryCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadPageCommand))]
    private bool _isLoading;

    [ObservableProperty]
    private bool _canRetry;

    [ObservableProperty]
    private int _retryIndex;

    private bool CanOpenLibrary() => !_disposed && !IsLoading;
    private bool CanLoadPage() => CanOpenLibrary() && SelectedLibrary is not null;

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
        try
        {
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
            created.Clear();
            Message = string.Empty;
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
    }

    public void CancelLoading() => LoadPageCommand.Cancel();

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
        var previous = Items;
        Items = [];
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
    }
}
