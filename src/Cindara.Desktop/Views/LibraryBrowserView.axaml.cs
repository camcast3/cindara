using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cindara.Desktop.Localization;
using Cindara.Desktop.ViewModels;

namespace Cindara.Desktop.Views;

public partial class LibraryBrowserView : UserControl
{
    private LibraryBrowserViewModel? _model;
    private Button? _focusedCard;
    private MediaPreviewCardViewModel? _focusedItem;
    private int _columns = 6;
    private (int Start, int End) _realizedRange = (-1, -1);
    private bool _pageFocusPending = true;
    private bool _rememberFocus;
    private Vector? _returnOffset;
    private bool _loadMoreQueued;

    public void SuspendFocusMemory()
    {
        _rememberFocus = false;
        _returnOffset = RowsScroll()?.Offset;
    }

    public void ResumeFocusMemory()
    {
        _rememberFocus = true;
        if (_returnOffset is { } offset)
        {
            if (RowsScroll() is { } scroll)
            {
                scroll.Offset = offset;
            }
            _returnOffset = null;
        }
    }

    public LibraryBrowserView()
    {
        InitializeComponent();
        BuildLetterChoices();
        SizeChanged += (_, args) => UpdateCardLayout(args.NewSize.Width);
        LayoutUpdated += (_, _) => UpdateArtworkWindowFromRealizedCards();
        DataContextChanged += (_, _) =>
        {
            if (_model is not null)
            {
                _model.PropertyChanged -= OnModelChanged;
            }

            _model = DataContext as LibraryBrowserViewModel;
            if (_model is not null)
            {
                _model.PropertyChanged += OnModelChanged;
                _model.SetColumnCount(_columns);
            }

            _focusedCard = null;
            _pageFocusPending = true;
            _returnOffset = null;
            _realizedRange = (-1, -1);
        };
    }

    public event EventHandler<MediaPreviewCardViewModel>? ItemRequested;
    public event EventHandler? FilterRequested;
    public event EventHandler? SortRequested;

    public Control InitialFocus => _model?.IsLoading is true ? this
        : _model?.CanRetry is true ? RetryLibraryLoading
        : !_pageFocusPending && _focusedCard is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } ? _focusedCard
        : Cards().FirstOrDefault() ?? (Control)FilterMenuButton;

    private Button[] Cards() => LibraryRows.GetVisualDescendants().OfType<Button>()
        .Where(button => button.Classes.Contains("card")).ToArray();

    private int Columns => _columns;

    private ScrollViewer? RowsScroll() =>
        LibraryRows.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    private void UpdateCardLayout(double width)
    {
        if (width <= 0)
        {
            return;
        }

        var compact = width < 900;
        Grid.SetColumn(LibraryItemCount, compact ? 0 : 1);
        Grid.SetRow(LibraryItemCount, compact ? 1 : 0);
        FilterMenuButton.MaxWidth = Math.Max(120, width - 12);
        SortMenuButton.MaxWidth = Math.Max(120, width - 12);

        var topLevel = TopLevel.GetTopLevel(this);
        var posterWidth = topLevel?.Resources["Cindara.Media.GridPosterWidth"] is double widthValue
            ? widthValue
            : 270;
        var gridSpacing = topLevel?.Resources["Cindara.Media.GridSpacing"] is double spacingValue
            ? spacingValue
            : 24;
        var availableWidth = Math.Max(posterWidth, width - 64);
        var columns = Math.Clamp(
            (int)Math.Floor((availableWidth + gridSpacing)
                / (posterWidth + gridSpacing)),
            2,
            14);
        if (_columns != columns)
        {
            _columns = columns;
            var focused = _focusedItem;
            var focusedIndex = focused is not null && _model is not null
                ? _model.Items.IndexOf(focused)
                : -1;
            _model?.SetColumnCount(columns);
            if (focusedIndex >= 0)
            {
                Dispatcher.UIThread.Post(
                    () => FocusItemAtIndex(focusedIndex),
                    DispatcherPriority.Loaded);
            }
        }
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(LibraryBrowserViewModel.Items))
        {
            _focusedCard = null;
            _focusedItem = null;
            _pageFocusPending = true;
            _returnOffset = null;
            _realizedRange = (-1, -1);
            if (RowsScroll() is { } scroll)
            {
                scroll.Offset = default;
            }
        }

        if (args.PropertyName == nameof(LibraryBrowserViewModel.IsLoading))
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (IsEffectivelyVisible && IsEffectivelyEnabled)
                {
                    UpdateLayout();
                    var target = InitialFocus;
                    if (target.Focus(NavigationMethod.Directional) && _model?.IsLoading is false)
                    {
                        _pageFocusPending = false;
                        _focusedCard = target.DataContext is MediaPreviewCardViewModel ? target as Button : null;
                    }
                }
            }, DispatcherPriority.Loaded);
        }

        if (args.PropertyName is nameof(LibraryBrowserViewModel.SelectedFilter)
            or nameof(LibraryBrowserViewModel.SelectedSortDirection)
            or nameof(LibraryBrowserViewModel.SelectedLetter))
        {
            UpdateSelections();
        }
    }

    public bool TryMove(NavigationDirection direction)
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        if (_model is null
            || focused?.DataContext is not MediaPreviewCardViewModel focusedItem)
        {
            return false;
        }

        var index = _model.Items.IndexOf(focusedItem);
        if (index < 0)
        {
            return false;
        }

        var rtl = FlowDirection == Avalonia.Media.FlowDirection.RightToLeft;
        var step = direction switch
        {
            NavigationDirection.Up => -Columns,
            NavigationDirection.Down => Columns,
            NavigationDirection.Left => rtl ? 1 : -1,
            NavigationDirection.Right => rtl ? -1 : 1,
            _ => 0,
        };
        if (step == -1 && index % Columns == 0)
        {
            return false;
        }

        if (step == 1 && (index % Columns == Columns - 1 || index == _model.Items.Count - 1))
        {
            ActiveLetterButton()?.Focus(NavigationMethod.Directional);
            return true;
        }

        var next = index + step;
        if (next < 0)
        {
            return FilterMenuButton.Focus(NavigationMethod.Directional);
        }

        if (next >= _model.Items.Count)
        {
            if (_model.CanRetryMore)
            {
                LibraryRows.ScrollIntoView(_model.Items.Count / Columns);
                Dispatcher.UIThread.Post(() =>
                    LibraryRows.GetVisualDescendants().OfType<Button>()
                        .FirstOrDefault(button => button.Name == "RetryLoadingMore"
                            && button.IsEffectivelyVisible)?.Focus(NavigationMethod.Directional),
                    DispatcherPriority.Loaded);
            }
            else if (_model.LoadMoreCommand.CanExecute(null))
            {
                _model.LoadMoreCommand.Execute(null);
            }

            return true;
        }

        return FocusItemAtIndex(next);
    }

    private bool FocusItemAtIndex(int index)
    {
        if (_model is null || index < 0 || index >= _model.Items.Count)
        {
            return false;
        }

        var item = _model.Items[index];
        if (LibraryRows.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(button => ReferenceEquals(button.DataContext, item)) is { } realized)
        {
            realized.Focus(NavigationMethod.Directional);
            realized.BringIntoView();
            return true;
        }

        LibraryRows.ScrollIntoView(index / Columns);
        Dispatcher.UIThread.Post(() =>
        {
            if (LibraryRows.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(button => ReferenceEquals(button.DataContext, item)) is { } button)
            {
                button.Focus(NavigationMethod.Directional);
                button.BringIntoView();
            }
        }, DispatcherPriority.Loaded);
        return true;
    }

    private void OnFilterMenuClicked(object? sender, RoutedEventArgs args) =>
        FilterRequested?.Invoke(this, EventArgs.Empty);

    private void OnSortMenuClicked(object? sender, RoutedEventArgs args) =>
        SortRequested?.Invoke(this, EventArgs.Empty);

    private async void OnLetterClicked(object? sender, RoutedEventArgs args)
    {
        if (_model is not null
            && sender is Button { Tag: string letter }
            && _model.SetLetterCommand.CanExecute(letter))
        {
            await _model.SetLetterCommand.ExecuteAsync(letter);
        }
    }

    private async void OnRetryMoreClicked(object? sender, RoutedEventArgs args)
    {
        if (_model?.RetryPageCommand.CanExecute(null) is true)
        {
            await _model.RetryPageCommand.ExecuteAsync(null);
        }
    }

    private void BuildLetterChoices()
    {
        AddLetter(Loc.Get("Library.Letter.All"), string.Empty);
        foreach (var letter in "ABCDEFGHIJKLMNOPQRSTUVWXYZ")
        {
            AddLetter(letter.ToString(), letter.ToString());
        }

        UpdateSelections();
    }

    private void AddLetter(string label, string value)
    {
        var button = new Button
        {
            Content = label,
            Tag = value,
            MinWidth = 48,
            MinHeight = 32,
        };
        AutomationProperties.SetName(button,
            string.IsNullOrEmpty(value) ? Loc.Get("Library.Letter.All") : value);
        button.Click += OnLetterClicked;
        LetterChoices.Children.Add(button);
    }

    private void UpdateSelections()
    {
        if (_model is null)
        {
            return;
        }

        foreach (var button in LetterChoices.Children.OfType<Button>())
        {
            var selected = _model.SelectedLetter?.ToString() == (string?)button.Tag
                || _model.SelectedLetter is null && string.IsNullOrEmpty((string?)button.Tag);
            SetSelected(button, selected);
        }
    }

    private Button? ActiveLetterButton()
    {
        var selected = _model?.SelectedLetter?.ToString() ?? string.Empty;
        return LetterChoices.Children.OfType<Button>()
            .FirstOrDefault(button => Equals(button.Tag, selected))
            ?? LetterChoices.Children.OfType<Button>().FirstOrDefault();
    }

    private static void SetSelected(Button button, bool selected)
    {
        button.Classes.Set("selected", selected);
        AutomationProperties.SetItemStatus(
            button, selected ? Loc.Get("State.Selected") : string.Empty);
    }

    private void OnCardFocused(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: MediaPreviewCardViewModel item })
        {
            _model!.SelectedItem = item;
            _focusedItem = item;
            var index = _model.Items.IndexOf(item);
            if (index >= 0)
            {
                _ = _model.SetArtworkWindowAsync(index, Columns);
                if (index >= Math.Max(0, _model.Items.Count - Columns)
                    && _model.LoadMoreCommand.CanExecute(null))
                {
                    _model.LoadMoreCommand.Execute(null);
                }
            }
        }

        if (_rememberFocus)
        {
            _focusedCard = sender as Button;
        }

        if (_rememberFocus && sender is Button { Parent: StackPanel panel })
        {
            panel.BringIntoView();
        }
    }

    private void UpdateArtworkWindowFromRealizedCards()
    {
        if (_model is null || _model.Items.Count == 0 || !IsEffectivelyVisible || !IsEffectivelyEnabled)
        {
            return;
        }

        if (!_loadMoreQueued && _model.LoadMoreCommand.CanExecute(null) && IsLoadBoundaryVisible())
        {
            _loadMoreQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                _loadMoreQueued = false;
                if (IsEffectivelyVisible && IsEffectivelyEnabled && _model?.LoadMoreCommand.CanExecute(null) is true
                    && IsLoadBoundaryVisible())
                {
                    _model.LoadMoreCommand.Execute(null);
                }
            }, DispatcherPriority.Background);
        }

        var indexes = Cards()
            .Select(button => button.DataContext)
            .OfType<MediaPreviewCardViewModel>()
            .Select(item => _model.Items.IndexOf(item))
            .Where(index => index >= 0)
            .ToArray();
        if (indexes.Length == 0)
        {
            return;
        }

        var range = (indexes.Min(), indexes.Max() + 1);
        if (_realizedRange != range)
        {
            _realizedRange = range;
            _ = _model.SetArtworkWindowAsync(
                range.Item1,
                Math.Max(Columns, range.Item2 - range.Item1));
        }
    }

    private bool IsLoadBoundaryVisible()
    {
        if (_model is null || _model.Items.Count == 0 || RowsScroll() is not { } scroll)
        {
            return false;
        }

        var lastLoadedRow = (_model.Items.Count - 1) / Columns;
        return LibraryRows.GetVisualDescendants().OfType<ListBoxItem>()
            .Any(container => container.DataContext is LibraryGridRowViewModel row
                && _model.Rows.IndexOf(row) >= lastLoadedRow
                && container.TranslatePoint(default, scroll) is { } position
                && position.Y < scroll.Viewport.Height
                && position.Y + container.Bounds.Height > 0);
    }

    private void OnCardClicked(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: MediaPreviewCardViewModel item })
        {
            ItemRequested?.Invoke(this, item);
        }
    }
}
