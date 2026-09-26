using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Cindara.Desktop.DesignSystem;
using Cindara.Desktop.ViewModels;

namespace Cindara.Desktop.Views;

public partial class SeriesOverviewView : UserControl
{
    private Button? _lastSeason;
    private Control? _seasonReturn;

    public SeriesOverviewView()
    {
        InitializeComponent();
        SizeChanged += (_, args) => ApplyLayout(args.NewSize);
    }

    public Button BackAction => SeriesBack;
    public event EventHandler? BackRequested;
    public event EventHandler? InformationRequested;
    public event EventHandler? CreditsRequested;
    public event EventHandler<SeasonCardViewModel>? SeasonRequested;

    public void ResetPosition()
    {
        SeasonScroll.Offset = default;
        OverviewCopy.Offset = default;
        _lastSeason = null;
        _seasonReturn = null;
    }

    public bool TryMove(NavigationDirection direction)
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        var cards = SeasonScroll.GetVisualDescendants().OfType<Button>().ToArray();
        if (focused is Button card && cards.Contains(card))
        {
            if (direction is NavigationDirection.Left or NavigationDirection.Right)
            {
                var forward = direction == NavigationDirection.Right;
                if (FlowDirection == Avalonia.Media.FlowDirection.RightToLeft) forward = !forward;
                var index = Array.IndexOf(cards, card) + (forward ? 1 : -1);
                if (index >= 0 && index < cards.Length) cards[index].Focus(NavigationMethod.Directional);
                return true;
            }
            if (direction == NavigationDirection.Up)
            {
                (_seasonReturn is { IsEffectivelyEnabled: true } ? _seasonReturn : InformationButton)
                    .Focus(NavigationMethod.Directional);
                return true;
            }
            return direction == NavigationDirection.Down;
        }
        if (direction == NavigationDirection.Down && cards.Length > 0
            && focused?.GetVisualAncestors().Contains(OverviewActions) is true)
        {
            _seasonReturn = focused;
            (_lastSeason is not null && cards.Contains(_lastSeason) ? _lastSeason : cards[0])
                .Focus(NavigationMethod.Directional);
            return true;
        }
        return false;
    }

    private void ApplyLayout(Size size)
    {
        if (size.Width <= 0 || size.Height <= 0) return;
        var density = ResponsiveDensityProfile.Create(size.Width, size.Height);
        var height = Math.Clamp(size.Height * 0.26, 100, 405 * density.CardScale);
        Resources["Cindara.Series.SeasonHeight"] = height;
        Resources["Cindara.Series.SeasonWidth"] = height * 2 / 3;
        var posterWidth = Math.Min(280 * density.CardScale, size.Height * 0.28);
        Resources["Cindara.Series.PosterWidth"] = posterWidth;
        Resources["Cindara.Series.PosterHeight"] = posterWidth * 1.5;
        var compact = size.Width < 1000 || size.Height < 600;
        SeriesPoster.IsVisible = !compact;
        OverviewHeading.ColumnDefinitions = new ColumnDefinitions(compact ? "*" : "Auto,*");
        Grid.SetColumn(OverviewCopy, compact ? 0 : 1);
    }

    private void OnSeasonFocus(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button card) return;
        _lastSeason = card;
        card.BringIntoView();
    }

    private void OnSeason(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: SeasonCardViewModel season }) SeasonRequested?.Invoke(this, season);
    }
    private void OnBack(object? sender, RoutedEventArgs args) => BackRequested?.Invoke(this, EventArgs.Empty);
    private void OnInformation(object? sender, RoutedEventArgs args) => InformationRequested?.Invoke(this, EventArgs.Empty);
    private void OnCredits(object? sender, RoutedEventArgs args) => CreditsRequested?.Invoke(this, EventArgs.Empty);
}
