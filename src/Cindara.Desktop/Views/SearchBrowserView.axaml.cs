using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cindara.Desktop.ViewModels;

namespace Cindara.Desktop.Views;

public partial class SearchBrowserView : UserControl
{
    private SearchBrowserViewModel? _model;
    private MediaPreviewCardViewModel? _focusedItem;
    private int _columns = 6;
    private int? _pendingFocusIndex;
    private (int Start, int End) _realizedRange = (-1, -1);
    private Vector? _returnOffset;
    private bool _rememberFocus;
    private bool _loadMoreQueued;

    public SearchBrowserView()
    {
        InitializeComponent();
        SizeChanged += (_, args) => UpdateAdaptiveLayout(args.NewSize);
        LayoutUpdated += (_, _) => UpdateVisibleItems();
        DataContextChanged += (_, _) =>
        {
            if (_model is not null)
                _model.PropertyChanged -= OnModelChanged;
            _model = DataContext as SearchBrowserViewModel;
            if (_model is not null)
            {
                _model.PropertyChanged += OnModelChanged;
                _model.SetColumnCount(_columns);
            }
            _focusedItem = null;
            _pendingFocusIndex = null;
            _realizedRange = (-1, -1);
            _returnOffset = null;
        };
    }

    public event EventHandler<MediaPreviewCardViewModel>? ItemRequested;
    public event EventHandler<TextBox>? KeyboardRequested;

    public Control InitialFocus => _model?.IsLoading is true ? CancelSearch
        : _model?.CanRetry is true ? RetrySearch
        : _focusedItem is not null && FindCard(_focusedItem) is { IsEffectivelyVisible: true } card
            ? card : SearchTextBox;

    private ScrollViewer? RowsScroll() =>
        SearchRows.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    private Button[] Cards() => SearchRows.GetVisualDescendants().OfType<Button>()
        .Where(button => button.Classes.Contains("search-card")).ToArray();

    private Button? FindCard(MediaPreviewCardViewModel item) =>
        Cards().FirstOrDefault(button => ReferenceEquals(button.DataContext, item));

    public bool TryActivateTextBox(TextBox textBox)
    {
        if (!ReferenceEquals(textBox, SearchTextBox))
            return false;
        KeyboardRequested?.Invoke(this, textBox);
        return true;
    }

    public void SuspendFocusMemory()
    {
        _rememberFocus = false;
        _returnOffset = RowsScroll()?.Offset;
    }

    public void ResumeFocusMemory()
    {
        _rememberFocus = true;
        if (_returnOffset is { } offset && RowsScroll() is { } scroll)
            scroll.Offset = offset;
        _returnOffset = null;
    }

    public bool TryMove(NavigationDirection direction)
    {
        if (_model is null || TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement()
            is not Button { DataContext: MediaPreviewCardViewModel item })
            return false;
        var index = _model.Items.IndexOf(item);
        if (index < 0)
            return false;
        var rtl = FlowDirection == Avalonia.Media.FlowDirection.RightToLeft;
        var step = direction switch
        {
            NavigationDirection.Up => -_columns,
            NavigationDirection.Down => _columns,
            NavigationDirection.Left => rtl ? 1 : -1,
            NavigationDirection.Right => rtl ? -1 : 1,
            _ => 0,
        };
        if (step == -1 && index % _columns == 0)
            return false;
        if (step == 1 && (index % _columns == _columns - 1 || index == _model.Items.Count - 1))
            return true;
        var next = index + step;
        if (next < 0)
            return SearchTextBox.Focus(NavigationMethod.Directional);
        if (next >= _model.Items.Count)
        {
            if (_model.CanRetryMore)
                RetryMore.Focus(NavigationMethod.Directional);
            else if (_model.IsLoadingMore)
                _pendingFocusIndex = next;
            else if (_model.LoadMoreCommand.CanExecute(null))
            {
                _pendingFocusIndex = next;
                _model.LoadMoreCommand.Execute(null);
            }
            return true;
        }
        return FocusItemAtIndex(next);
    }

    private bool FocusItemAtIndex(int index)
    {
        if (_model is null || index >= _model.Items.Count || index < 0)
            return false;
        var item = _model.Items[index];
        if (FindCard(item) is { } card)
        {
            card.Focus(NavigationMethod.Directional);
            card.BringIntoView();
            return true;
        }
        SearchRows.ScrollIntoView(index / _columns);
        Dispatcher.UIThread.Post(() =>
        {
            if (FindCard(item) is { } realized)
            {
                realized.Focus(NavigationMethod.Directional);
                realized.BringIntoView();
            }
        }, DispatcherPriority.Loaded);
        return true;
    }

    private void UpdateAdaptiveLayout(Size size)
    {
        if (size.Width <= 0 || size.Height <= 0)
            return;
        SearchEntry.Width = Math.Min(840, size.Width);
        var stackEntry = size.Width < 900;
        SearchEntry.ColumnDefinitions = stackEntry
            ? new ColumnDefinitions("*") : new ColumnDefinitions("*,Auto");
        SearchEntry.RowDefinitions = stackEntry
            ? new RowDefinitions("Auto,Auto") : new RowDefinitions("Auto");
        Grid.SetColumn(SearchKeyboardButton, stackEntry ? 0 : 1);
        Grid.SetRow(SearchKeyboardButton, stackEntry ? 1 : 0);
        SearchKeyboardButton.HorizontalAlignment =
            stackEntry ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        var topLevel = TopLevel.GetTopLevel(this);
        var posterWidth = topLevel?.Resources["Cindara.Media.GridPosterWidth"] is double width ? width : 270;
        var spacing = topLevel?.Resources["Cindara.Media.GridSpacing"] is double value ? value : 24;
        var columns = Math.Clamp((int)Math.Floor((size.Width - 64 + spacing) / (posterWidth + spacing)), 2, 14);
        if (_columns != columns)
        {
            _columns = columns;
            var focusedIndex = _focusedItem is not null && _model is not null
                ? _model.Items.IndexOf(_focusedItem) : -1;
            _model?.SetColumnCount(columns);
            if (focusedIndex >= 0)
                Dispatcher.UIThread.Post(() => FocusItemAtIndex(focusedIndex), DispatcherPriority.Loaded);
        }
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SearchBrowserViewModel.Query))
        {
            _focusedItem = null;
            _pendingFocusIndex = null;
            _realizedRange = (-1, -1);
            _returnOffset = null;
            if (RowsScroll() is { } scroll)
                scroll.Offset = default;
        }
        if (args.PropertyName == nameof(SearchBrowserViewModel.IsLoading)
            && _model?.IsLoading is false)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (IsEffectivelyVisible && IsEffectivelyEnabled && _focusedItem is null)
                    InitialFocus.Focus(NavigationMethod.Directional);
            }, DispatcherPriority.Loaded);
        }
        if (args.PropertyName == nameof(SearchBrowserViewModel.IsLoadingMore)
            && _model?.IsLoadingMore is false && _pendingFocusIndex is { } next)
        {
            _pendingFocusIndex = null;
            Dispatcher.UIThread.Post(() =>
            {
                if (IsEffectivelyVisible && _model is not null)
                {
                    if (next < _model.Items.Count)
                        FocusItemAtIndex(next);
                    else if (_model.CanRetryMore)
                        RetryMore.Focus(NavigationMethod.Directional);
                }
            }, DispatcherPriority.Loaded);
        }
    }

    private void UpdateVisibleItems()
    {
        if (_model is null || !IsEffectivelyVisible || !IsEffectivelyEnabled || _model.Items.Count == 0)
            return;
        var scroll = RowsScroll();
        if (!_loadMoreQueued && scroll is not null && _model.LoadMoreCommand.CanExecute(null)
            && SearchRows.GetVisualDescendants().OfType<ListBoxItem>()
                .Any(row => row.DataContext is LibraryGridRowViewModel grid
                    && _model.Rows.IndexOf(grid) >= (_model.Items.Count - 1) / _columns
                    && row.TranslatePoint(default, scroll) is { } point
                    && point.Y < scroll.Viewport.Height && point.Y + row.Bounds.Height > 0))
        {
            _loadMoreQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                _loadMoreQueued = false;
                if (IsEffectivelyVisible && _model?.LoadMoreCommand.CanExecute(null) is true)
                    _model.LoadMoreCommand.Execute(null);
            }, DispatcherPriority.Background);
        }
        var indexes = Cards().Select(card => card.DataContext).OfType<MediaPreviewCardViewModel>()
            .Select(item => _model.Items.IndexOf(item)).Where(index => index >= 0).ToArray();
        if (indexes.Length == 0)
            return;
        var range = (indexes.Min(), indexes.Max() + 1);
        if (_realizedRange != range)
        {
            _realizedRange = range;
            _ = _model.SetArtworkWindowAsync(range.Item1, Math.Max(_columns, range.Item2 - range.Item1));
        }
    }

    private void OnKeyboardClicked(object? sender, RoutedEventArgs args) =>
        KeyboardRequested?.Invoke(this, SearchTextBox);

    private void OnCancelClicked(object? sender, RoutedEventArgs args) => _model?.CancelLoading();

    private void OnCardFocused(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { DataContext: MediaPreviewCardViewModel item } button || _model is null)
            return;
        if (_rememberFocus)
        {
            _focusedItem = item;
            button.BringIntoView();
        }
        var index = _model.Items.IndexOf(item);
        if (index >= 0)
        {
            _ = _model.SetArtworkWindowAsync(index, _columns);
            if (index >= Math.Max(0, _model.Items.Count - _columns)
                && _model.LoadMoreCommand.CanExecute(null))
                _model.LoadMoreCommand.Execute(null);
        }
    }

    private void OnCardClicked(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: MediaPreviewCardViewModel item })
            ItemRequested?.Invoke(this, item);
    }
}
