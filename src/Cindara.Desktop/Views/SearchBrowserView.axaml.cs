using System.ComponentModel;
using Avalonia;
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
    private const double ReferenceCardWidth = 180;
    private SearchBrowserViewModel? _model;
    private Button? _focusedCard;
    private Vector? _returnOffset;
    private bool _rememberFocus;

    public SearchBrowserView()
    {
        Resources["Search.CardWidth"] = ReferenceCardWidth;
        Resources["Search.CardHeight"] = ReferenceCardWidth * 1.5;
        InitializeComponent();
        SizeChanged += (_, args) => UpdateAdaptiveLayout(args.NewSize);
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
        : _focusedCard is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true }
            ? _focusedCard
            : SearchTextBox;

    public bool TryActivateTextBox(TextBox textBox)
    {
        if (!ReferenceEquals(textBox, SearchTextBox))
        {
            return false;
        }

        KeyboardRequested?.Invoke(this, textBox);
        return true;
    }

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

    private int Columns =>
        SearchCards.GetVisualDescendants().OfType<UniformGrid>().FirstOrDefault()?.Columns ?? 1;

    private Button[] Cards() => SearchCards.GetVisualDescendants().OfType<Button>()
        .Where(button => button.Classes.Contains("search-card")).ToArray();

    private void UpdateAdaptiveLayout(Size size)
    {
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        var stackEntry = size.Width < 900;
        SearchEntry.ColumnDefinitions = stackEntry
            ? new ColumnDefinitions("*")
            : new ColumnDefinitions("*,Auto");
        SearchEntry.RowDefinitions = stackEntry
            ? new RowDefinitions("Auto,Auto")
            : new RowDefinitions("Auto");
        Grid.SetColumn(SearchKeyboardButton, stackEntry ? 0 : 1);
        Grid.SetRow(SearchKeyboardButton, stackEntry ? 1 : 0);
        SearchKeyboardButton.HorizontalAlignment =
            stackEntry ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;

        var columns = size.Width switch
        {
            < 560 => 2,
            < 800 => 3,
            < 1100 => 5,
            < 1500 => 7,
            < 2200 => 8,
            _ => 9,
        };
        var maximumCardWidth = size.Width >= 2200 ? ReferenceCardWidth * 1.3 : ReferenceCardWidth;
        var cardWidth = Math.Min(maximumCardWidth, Math.Max(112, (size.Width / columns) - 14));
        Resources["Search.CardWidth"] = cardWidth;
        Resources["Search.CardHeight"] = cardWidth * 1.5;
        if (SearchCards.GetVisualDescendants().OfType<UniformGrid>().FirstOrDefault() is { } grid)
        {
            grid.Columns = columns;
        }
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SearchBrowserViewModel.Items))
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
        if (_rememberFocus && sender is Button button)
        {
            _focusedCard = button;
            button.BringIntoView();
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
