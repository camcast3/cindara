using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cindara.Desktop.ViewModels;

namespace Cindara.Desktop.Views;

public partial class SearchBrowserView : UserControl
{
    private SearchBrowserViewModel? _model;
    private Button? _focusedCard;
    private Vector? _returnOffset;
    private bool _rememberFocus;

    public SearchBrowserView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_model is not null)
            {
                _model.PropertyChanged -= OnModelChanged;
            }

            _model = DataContext as SearchBrowserViewModel;
            if (_model is not null)
            {
                _model.PropertyChanged += OnModelChanged;
            }

            _focusedCard = null;
            _returnOffset = null;
            SearchScroll.Offset = default;
        };
    }

    public event EventHandler<MediaPreviewCardViewModel>? ItemRequested;
    public event EventHandler<TextBox>? KeyboardRequested;

    public Control InitialFocus => _model?.IsLoading is true ? CancelSearch
        : _model?.CanRetry is true ? RetrySearch
        : _focusedCard is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } ? _focusedCard
        : SearchTextBox;

    public void SuspendFocusMemory()
    {
        _rememberFocus = false;
        _returnOffset = SearchScroll.Offset;
    }

    public void ResumeFocusMemory()
    {
        _rememberFocus = true;
        if (_returnOffset is { } offset)
        {
            SearchScroll.Offset = offset;
            _returnOffset = null;
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
            NavigationDirection.Up => -5,
            NavigationDirection.Down => 5,
            NavigationDirection.Left => rtl ? 1 : -1,
            NavigationDirection.Right => rtl ? -1 : 1,
            _ => 0,
        };
        if (step == -1 && index % 5 == 0)
        {
            return false;
        }

        if (step == 1 && (index % 5 == 4 || index == cards.Length - 1))
        {
            return true;
        }

        var next = index + step;
        if (next < 0)
        {
            return SearchTextBox.Focus(NavigationMethod.Directional);
        }

        if (next >= cards.Length)
        {
            var action = NextSearchPage.IsEffectivelyEnabled ? NextSearchPage : PreviousSearchPage;
            if (action.IsEffectivelyEnabled)
            {
                action.Focus(NavigationMethod.Directional);
            }

            return true;
        }

        return cards[next].Focus(NavigationMethod.Directional);
    }

    private Button[] Cards() => SearchGroups.GetVisualDescendants().OfType<Button>()
        .Where(button => button.Classes.Contains("search-card")).ToArray();

    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SearchBrowserViewModel.Groups))
        {
            _focusedCard = null;
            _returnOffset = null;
            SearchScroll.Offset = default;
        }

        if (args.PropertyName == nameof(SearchBrowserViewModel.IsLoading))
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (IsEffectivelyVisible && IsEffectivelyEnabled)
                {
                    UpdateLayout();
                    InitialFocus.Focus(NavigationMethod.Directional);
                }
            }, DispatcherPriority.Loaded);
        }
    }

    private void OnKeyboardClicked(object? sender, RoutedEventArgs args) =>
        KeyboardRequested?.Invoke(this, SearchTextBox);

    private void OnCancelClicked(object? sender, RoutedEventArgs args) => _model?.CancelLoading();

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
