using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cindara.Desktop.Localization;
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
        Resources["Search.HeroPosterWidth"] = 120d;
        Resources["Search.HeroPosterHeight"] = 180d;
        InitializeComponent();
        BuildKeyboard();
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
            SearchViewportScroll.Offset = default;
        };
    }

    public event EventHandler<MediaPreviewCardViewModel>? ItemRequested;

    public Control InitialFocus => _model?.IsLoading is true ? CancelSearch
        : _model?.CanRetry is true ? RetrySearch
        : _focusedCard is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } ? _focusedCard
        : SearchTextBox;

    public bool IsKeyboardOpen => InlineKeyboard.IsVisible;

    public bool TryActivateTextBox(TextBox textBox)
    {
        if (!ReferenceEquals(textBox, SearchTextBox))
        {
            return false;
        }

        SetKeyboardVisible(true);
        KeyboardKeys.Children.OfType<Button>().First().Focus(NavigationMethod.Directional);
        return true;
    }

    public void SuspendFocusMemory()
    {
        _rememberFocus = false;
        _returnOffset = SearchViewportScroll.Offset;
    }

    public void ResumeFocusMemory()
    {
        _rememberFocus = true;
        if (_returnOffset is { } offset)
        {
            SearchViewportScroll.Offset = offset;
            _returnOffset = null;
        }
    }

    public bool TryGoBack()
    {
        if (!InlineKeyboard.IsVisible)
        {
            return false;
        }

        SetKeyboardVisible(false);
        SearchTextBox.Focus(NavigationMethod.Directional);
        return true;
    }

    public bool TryMove(NavigationDirection direction)
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        var candidates = this.GetVisualDescendants()
            .OfType<Control>()
            .Where(control => control is Button or TextBox
                && control.Focusable && control.IsEffectivelyVisible && control.IsEffectivelyEnabled
                && control.Bounds.Width > 0 && control.Bounds.Height > 0)
            .ToArray();
        if (focused is null || !candidates.Contains(focused))
        {
            return false;
        }

        var rtl = FlowDirection == Avalonia.Media.FlowDirection.RightToLeft;
        if (rtl)
        {
            direction = direction switch
            {
                NavigationDirection.Left => NavigationDirection.Right,
                NavigationDirection.Right => NavigationDirection.Left,
                _ => direction,
            };
        }

        var origin = BoundsInView(focused);
        var target = candidates.Where(candidate => candidate != focused)
            .Select(candidate => (Control: candidate, Bounds: BoundsInView(candidate)))
            .Where(candidate => IsInDirection(origin, candidate.Bounds, direction))
            .OrderBy(candidate => OrthogonalGap(origin, candidate.Bounds, direction) > 0)
            .ThenBy(candidate => PrimaryGap(origin, candidate.Bounds, direction))
            .ThenBy(candidate => Distance(origin, candidate.Bounds))
            .Select(candidate => candidate.Control)
            .FirstOrDefault();
        if (target is null)
        {
            return false;
        }

        var moved = target.Focus(NavigationMethod.Directional);
        target.BringIntoView();
        return moved;
    }

    private void BuildKeyboard()
    {
        foreach (var character in "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789")
        {
            var value = character.ToString();
            var key = new Button
            {
                Content = value,
                Margin = new Thickness(2),
                MinWidth = 48,
                MinHeight = 48,
            };
            AutomationProperties.SetName(key, value);
            key.Click += (_, _) => InsertText(value);
            KeyboardKeys.Children.Add(key);
        }

        AddKeyboardAction(Loc.Get("Keyboard.Backspace"), Backspace);
        AddKeyboardAction(Loc.Get("Keyboard.Space"), () => InsertText(" "));
        AddKeyboardAction(Loc.Get("Keyboard.Clear"), () => SearchTextBox.Text = string.Empty);
        AddKeyboardAction(Loc.Get("Keyboard.Done"), () =>
        {
            SetKeyboardVisible(false);
            (_focusedCard as Control ?? SearchTextBox).Focus(NavigationMethod.Directional);
        });
    }

    private void AddKeyboardAction(string text, Action action)
    {
        var button = new Button
        {
            Content = text,
            Margin = new Thickness(2),
            MinHeight = 48,
        };
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => action();
        KeyboardActions.Children.Add(button);
    }

    private void InsertText(string value)
    {
        var text = SearchTextBox.Text ?? string.Empty;
        var start = Math.Min(SearchTextBox.SelectionStart, SearchTextBox.SelectionEnd);
        var length = Math.Abs(SearchTextBox.SelectionEnd - SearchTextBox.SelectionStart);
        if (start < 0 || start > text.Length)
        {
            start = text.Length;
            length = 0;
        }

        length = Math.Min(length, text.Length - start);
        var available = Math.Max(0, SearchTextBox.MaxLength - (text.Length - length));
        var insertion = value.Length <= available ? value : value[..available];
        SearchTextBox.Text = text.Remove(start, length).Insert(start, insertion);
        SearchTextBox.CaretIndex = start + insertion.Length;
        SearchTextBox.SelectionStart = SearchTextBox.SelectionEnd = SearchTextBox.CaretIndex;
    }

    private void Backspace()
    {
        if (SearchTextBox.SelectionStart == SearchTextBox.SelectionEnd && SearchTextBox.CaretIndex > 0)
        {
            SearchTextBox.SelectionStart = SearchTextBox.CaretIndex - 1;
            SearchTextBox.SelectionEnd = SearchTextBox.CaretIndex;
        }

        InsertText(string.Empty);
    }

    private void SetKeyboardVisible(bool visible)
    {
        InlineKeyboard.IsVisible = visible;
        SearchKeyboardButton.Classes.Set("selected", visible);
        AutomationProperties.SetItemStatus(
            SearchKeyboardButton, visible ? Loc.Get("State.Selected") : string.Empty);
        UpdateAdaptiveLayout(Bounds.Size);
    }

    private void UpdateAdaptiveLayout(Size size)
    {
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        var compact = size.Width < 960;
        SearchEntry.ColumnDefinitions = compact
            ? new ColumnDefinitions("*")
            : new ColumnDefinitions("*,Auto");
        Grid.SetColumn(SearchKeyboardButton, compact ? 0 : 1);
        Grid.SetRow(SearchKeyboardButton, compact ? 1 : 0);
        SearchEntry.RowDefinitions = compact
            ? new RowDefinitions("Auto,Auto")
            : new RowDefinitions("Auto");
        SearchKeyboardButton.HorizontalAlignment = compact ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;

        var stackBody = compact || InlineKeyboard.IsVisible && size.Width < 1280;
        SearchBody.ColumnDefinitions = stackBody
            ? new ColumnDefinitions("*")
            : new ColumnDefinitions("Auto,*");
        SearchBody.RowDefinitions = stackBody
            ? new RowDefinitions("Auto,Auto")
            : new RowDefinitions("Auto");
        Grid.SetColumn(SearchResults, stackBody ? 0 : 1);
        Grid.SetRow(SearchResults, stackBody ? 1 : 0);
        InlineKeyboard.Width = stackBody ? double.NaN : Math.Min(360, size.Width * 0.34);
        InlineKeyboard.HorizontalAlignment = stackBody ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;

        var cardWidth = Math.Clamp(size.Width / (compact ? 3.3 : 6.5), 120, ReferenceCardWidth);
        Resources["Search.CardWidth"] = cardWidth;
        Resources["Search.CardHeight"] = cardWidth * 1.5;
        Resources["Search.HeroPosterWidth"] = Math.Clamp(cardWidth * 0.65, 84, 120);
        Resources["Search.HeroPosterHeight"] = Math.Clamp(cardWidth * 0.65, 84, 120) * 1.5;
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SearchBrowserViewModel.Groups))
        {
            _focusedCard = null;
            _returnOffset = null;
            SearchViewportScroll.Offset = default;
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

    private void OnKeyboardClicked(object? sender, RoutedEventArgs args)
    {
        SetKeyboardVisible(!InlineKeyboard.IsVisible);
        if (InlineKeyboard.IsVisible)
        {
            KeyboardKeys.Children.OfType<Button>().First().Focus(NavigationMethod.Directional);
        }
        else
        {
            SearchTextBox.Focus(NavigationMethod.Directional);
        }
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs args) => _model?.CancelLoading();

    private void OnCardFocused(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: MediaPreviewCardViewModel item } button)
        {
            _model!.SelectedItem = item;
            if (_rememberFocus)
            {
                _focusedCard = button;
            }

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

    private Rect BoundsInView(Control control)
    {
        var start = control.TranslatePoint(default, this) ?? default;
        var end = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), this) ?? start;
        return new Rect(start, end);
    }

    private static double Distance(Rect origin, Rect target) =>
        Math.Pow(origin.Center.X - target.Center.X, 2) + Math.Pow(origin.Center.Y - target.Center.Y, 2);

    private static bool IsInDirection(Rect origin, Rect target, NavigationDirection direction) => direction switch
    {
        NavigationDirection.Left => target.Center.X < origin.Center.X - 1,
        NavigationDirection.Right => target.Center.X > origin.Center.X + 1,
        NavigationDirection.Up => target.Center.Y < origin.Center.Y - 1,
        NavigationDirection.Down => target.Center.Y > origin.Center.Y + 1,
        _ => false,
    };

    private static double OrthogonalGap(Rect origin, Rect target, NavigationDirection direction) =>
        direction is NavigationDirection.Left or NavigationDirection.Right
            ? Math.Max(0, Math.Max(origin.Top - target.Bottom, target.Top - origin.Bottom))
            : Math.Max(0, Math.Max(origin.Left - target.Right, target.Left - origin.Right));

    private static double PrimaryGap(Rect origin, Rect target, NavigationDirection direction) => direction switch
    {
        NavigationDirection.Left => Math.Max(0, origin.Left - target.Right),
        NavigationDirection.Right => Math.Max(0, target.Left - origin.Right),
        NavigationDirection.Up => Math.Max(0, origin.Top - target.Bottom),
        NavigationDirection.Down => Math.Max(0, target.Top - origin.Bottom),
        _ => double.MaxValue,
    };
}
