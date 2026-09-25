using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cindara.Core.Jellyfin;
using Cindara.Desktop.Localization;
using Cindara.Desktop.ViewModels;

namespace Cindara.Desktop.Views;

public partial class LibraryBrowserView : UserControl
{
    private const double ReferenceMaximumCardWidth = 270;
    private const double GridSpacing = 24;
    private LibraryBrowserViewModel? _model;
    private Button? _focusedCard;
    private MediaPreviewCardViewModel? _focusedItem;
    private int _columns = 6;
    private (int Start, int End) _realizedRange = (-1, -1);
    private bool _pageFocusPending = true;
    private bool _rememberFocus;
    private Vector? _returnOffset;

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
        Resources["Library.CardWidth"] = ReferenceMaximumCardWidth;
        Resources["Library.CardHeight"] = ReferenceMaximumCardWidth * 1.5;
        Resources["Library.GridSpacing"] = GridSpacing;
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

    public Control InitialFocus => _model?.IsLoading is true ? CancelLibraryLoading
        : _model?.CanRetry is true ? RetryLibraryLoading
        : !_pageFocusPending && _focusedCard is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } ? _focusedCard
        : Cards().FirstOrDefault() ?? Choices().FirstOrDefault() ?? (Control)this;

    private Button[] Cards() => LibraryRows.GetVisualDescendants().OfType<Button>()
        .Where(button => button.Classes.Contains("card")).ToArray();

    private Button[] Choices() => LibraryChoices.GetVisualDescendants().OfType<Button>().ToArray();

    private int Columns => _columns;

    private ScrollViewer? RowsScroll() =>
        LibraryRows.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    private void UpdateCardLayout(double width)
    {
        if (width <= 0)
        {
            return;
        }

        var availableWidth = Math.Max(280, width - 64);
        var columns = Math.Clamp(
            (int)Math.Floor((availableWidth + GridSpacing)
                / (ReferenceMaximumCardWidth + GridSpacing)),
            2,
            14);
        var cardWidth = Math.Min(
            ReferenceMaximumCardWidth,
            Math.Max(132, (availableWidth - (GridSpacing * (columns - 1))) / columns));
        Resources["Library.CardWidth"] = cardWidth;
        Resources["Library.CardHeight"] = cardWidth * 1.5;
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
            return Choices().FirstOrDefault()?.Focus(NavigationMethod.Directional) is true;
        }

        if (next >= _model.Items.Count)
        {
            if (_model.CanRetryMore
                && LibraryRows.GetVisualDescendants().OfType<Button>()
                    .FirstOrDefault(button => button.Name == "RetryLoadingMore") is { } retry)
            {
                retry.Focus(NavigationMethod.Directional);
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

    private async void OnLibraryClicked(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: MediaLibrary library } && _model?.OpenLibraryCommand.CanExecute(library) is true)
        {
            await _model.OpenLibraryCommand.ExecuteAsync(library);
        }
    }

    private void OnCancelLoadingClicked(object? sender, RoutedEventArgs args) =>
        _model?.CancelLoading();

    private async void OnFilterClicked(object? sender, RoutedEventArgs args)
    {
        if (_model is not null
            && sender is Button { Tag: string value }
            && Enum.TryParse<MediaLibraryFilter>(value, out var filter)
            && _model.SetFilterCommand.CanExecute(filter))
        {
            await _model.SetFilterCommand.ExecuteAsync(filter);
        }
    }

    private async void OnSortClicked(object? sender, RoutedEventArgs args)
    {
        if (_model is not null
            && sender is Button { Tag: string value }
            && Enum.TryParse<MediaLibrarySortDirection>(value, out var direction)
            && _model.SetSortDirectionCommand.CanExecute(direction))
        {
            await _model.SetSortDirectionCommand.ExecuteAsync(direction);
        }
    }

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

        SetSelected(FilterAllButton, _model.SelectedFilter == MediaLibraryFilter.All);
        SetSelected(FilterUnwatchedButton, _model.SelectedFilter == MediaLibraryFilter.Unwatched);
        SetSelected(FilterFavoritesButton, _model.SelectedFilter == MediaLibraryFilter.Favorites);
        SetSelected(SortAscendingButton,
            _model.SelectedSortDirection == MediaLibrarySortDirection.Ascending);
        SetSelected(SortDescendingButton,
            _model.SelectedSortDirection == MediaLibrarySortDirection.Descending);
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
        if (_model is null || _model.Items.Count == 0)
        {
            return;
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

    private void OnCardClicked(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: MediaPreviewCardViewModel item })
        {
            ItemRequested?.Invoke(this, item);
        }
    }
}
