using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cindara.Core.Jellyfin;
using Cindara.Desktop.ViewModels;

namespace Cindara.Desktop.Views;

public partial class LibraryBrowserView : UserControl
{
    private const double ReferenceMaximumCardWidth = 247.2;
    private LibraryBrowserViewModel? _model;
    private Button? _focusedCard;
    private bool _pageFocusPending = true;
    private bool _rememberFocus;
    private Vector? _returnOffset;

    public void SuspendFocusMemory()
    {
        _rememberFocus = false;
        _returnOffset = LibraryScroll.Offset;
    }

    public void ResumeFocusMemory()
    {
        _rememberFocus = true;
        if (_returnOffset is { } offset)
        {
            LibraryScroll.Offset = offset;
            _returnOffset = null;
        }
    }

    public LibraryBrowserView()
    {
        Resources["Library.CardWidth"] = ReferenceMaximumCardWidth;
        Resources["Library.CardHeight"] = ReferenceMaximumCardWidth * 1.5;
        InitializeComponent();
        SizeChanged += (_, args) => UpdateCardLayout(args.NewSize.Width);
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
            }

            _focusedCard = null;
            _pageFocusPending = true;
            _returnOffset = null;
            LibraryScroll.Offset = default;
        };
    }

    public event EventHandler<MediaPreviewCardViewModel>? ItemRequested;

    public Control InitialFocus => _model?.IsLoading is true ? CancelLibraryLoading
        : _model?.CanRetry is true ? RetryLibraryLoading
        : !_pageFocusPending && _focusedCard is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } ? _focusedCard
        : Cards().FirstOrDefault() ?? Choices().FirstOrDefault() ?? (Control)this;

    private Button[] Cards() => LibraryCards.GetVisualDescendants().OfType<Button>()
        .Where(button => button.Classes.Contains("card")).ToArray();

    private Button[] Choices() => LibraryChoices.GetVisualDescendants().OfType<Button>().ToArray();

    private int Columns => LibraryCards.GetVisualDescendants().OfType<UniformGrid>().FirstOrDefault()?.Columns ?? 1;

    private void UpdateCardLayout(double width)
    {
        if (width <= 0)
        {
            return;
        }

        var columns = width switch
        {
            < 560 => 2,
            < 820 => 3,
            < 1080 => 4,
            _ => 5,
        };
        var maximumCardWidth = width >= 2200
            ? ReferenceMaximumCardWidth * 1.5
            : ReferenceMaximumCardWidth;
        var cardWidth = Math.Min(maximumCardWidth, Math.Max(120, (width / columns) - 16));
        Resources["Library.CardWidth"] = cardWidth;
        Resources["Library.CardHeight"] = cardWidth * 1.5;
        if (LibraryCards.GetVisualDescendants().OfType<UniformGrid>().FirstOrDefault() is { } grid)
        {
            grid.Columns = columns;
        }
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(LibraryBrowserViewModel.Items))
        {
            _focusedCard = null;
            _pageFocusPending = true;
            _returnOffset = null;
            LibraryScroll.Offset = default;
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
    }

    public bool TryMove(NavigationDirection direction)
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        var cards = Cards();
        var index = Array.FindIndex(cards, card => ReferenceEquals(card, focused));
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

        if (step == 1 && (index % Columns == Columns - 1 || index == cards.Length - 1))
        {
            return true;
        }

        var next = index + step;
        if (next < 0)
        {
            return Choices().FirstOrDefault()?.Focus(NavigationMethod.Directional) is true;
        }

        if (next >= cards.Length)
        {
            var action = NextLibraryPage.IsEffectivelyEnabled ? NextLibraryPage : PreviousLibraryPage;
            if (action.IsEffectivelyEnabled)
            {
                action.Focus(NavigationMethod.Directional);
            }

            return true;
        }

        return cards[next].Focus(NavigationMethod.Directional);
    }

    private async void OnLibraryClicked(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: MediaLibrary library } && _model?.OpenLibraryCommand.CanExecute(library) is true)
        {
            await _model.OpenLibraryCommand.ExecuteAsync(library);
        }
    }

    private void OnCardFocused(object? sender, RoutedEventArgs args)
    {
        if (_rememberFocus)
        {
            _focusedCard = sender as Button;
        }

        if (_rememberFocus && sender is Button { Parent: StackPanel panel })
        {
            panel.BringIntoView();
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
