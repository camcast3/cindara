using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Cindara.Core.Jellyfin;
using Cindara.Desktop.DesignSystem;
using Cindara.Desktop.Navigation;
using Cindara.Desktop.ViewModels;

namespace Cindara.Desktop.Views;

public partial class SeasonBrowserView : UserControl
{
    private Button? _lastEpisode;
    private Button? _lastSeason;
    private int _columns = 4;

    public SeasonBrowserView()
    {
        InitializeComponent();
        SizeChanged += (_, args) => ApplyLayout(args.NewSize);
    }

    public Button BackAction => BrowserBack;
    public event EventHandler? BackRequested;
    public event EventHandler<MediaSeason>? SeasonRequested;
    public event EventHandler<EpisodeCardViewModel>? EpisodeRequested;

    public void ResetPosition()
    {
        _lastSeason = null;
        _lastEpisode = null;
        SeasonScroll.Offset = default;
        EpisodeDetailsScroll.Offset = default;
    }

    public void ResetEpisodePosition()
    {
        _lastEpisode = null;
        EpisodeDetailsScroll.Offset = default;
    }

    public void FocusSelectedEpisode(string? episodeId = null)
    {
        var cards = EpisodeGrid.GetVisualDescendants().OfType<Button>().ToArray();
        var target = cards.FirstOrDefault(card => card.DataContext is EpisodeCardViewModel episode
            && episode.Episode.Id == episodeId) ?? cards.FirstOrDefault();
        target?.Focus(NavigationMethod.Directional);
    }

    public bool TryMove(NavigationDirection direction)
    {
        var focus = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        var seasons = SeasonScroll.GetVisualDescendants().OfType<Button>().ToArray();
        var episodes = EpisodeGrid.GetVisualDescendants().OfType<Button>().ToArray();
        var row = seasons.Contains(focus) ? seasons : episodes.Contains(focus) ? episodes : null;
        if (row is null)
        {
            if (direction == NavigationDirection.Up && focus == ReadBelow)
            {
                (_lastEpisode is not null && episodes.Contains(_lastEpisode) ? _lastEpisode : episodes.FirstOrDefault())
                    ?.Focus(NavigationMethod.Directional);
                return true;
            }
            if (direction == NavigationDirection.Down && focus == BrowserBack)
            {
                (seasons.FirstOrDefault(card => card.DataContext is MediaSeason season
                    && season.Id == (DataContext as SeasonBrowserViewModel)?.SelectedSeason?.Id)
                    ?? seasons.FirstOrDefault())?.Focus(NavigationMethod.Directional);
                return true;
            }
            if (direction == NavigationDirection.Down && focus is Button
                && (focus == ReadBelow || focus == CreditsLeft || focus == CreditsRight))
            {
                (episodes.FirstOrDefault(card => card.DataContext is EpisodeCardViewModel episode
                    && ReferenceEquals(episode, (DataContext as SeasonBrowserViewModel)?.SelectedEpisode))
                    ?? episodes.FirstOrDefault())?.Focus(NavigationMethod.Directional);
                return true;
            }
            return false;
        }
        if (direction is NavigationDirection.Left or NavigationDirection.Right)
        {
            var forward = direction == NavigationDirection.Right;
            if (FlowDirection == Avalonia.Media.FlowDirection.RightToLeft) forward = !forward;
            var index = Array.IndexOf(row, focus) + (forward ? 1 : -1);
            if (ReferenceEquals(row, episodes) && index >= 0 && index < row.Length
                && index / _columns != Array.IndexOf(row, focus) / _columns) return true;
            if (index >= 0 && index < row.Length) row[index].Focus(NavigationMethod.Directional);
            return true;
        }
        if (direction == NavigationDirection.Up)
        {
            if (ReferenceEquals(row, episodes) && Array.IndexOf(episodes, focus) >= _columns)
                episodes[Array.IndexOf(episodes, focus) - _columns].Focus(NavigationMethod.Directional);
            else (ReferenceEquals(row, episodes) ? _lastSeason ?? seasons.FirstOrDefault() : BrowserBack)
                    ?.Focus(NavigationMethod.Directional);
            return true;
        }
        if (direction == NavigationDirection.Down)
        {
            if (ReferenceEquals(row, seasons))
                (_lastEpisode is not null && episodes.Contains(_lastEpisode) ? _lastEpisode : episodes.FirstOrDefault())
                    ?.Focus(NavigationMethod.Directional);
            else
            {
                var next = Array.IndexOf(episodes, focus) + _columns;
                if (next < episodes.Length) episodes[next].Focus(NavigationMethod.Directional);
                else ReadBelow.Focus(NavigationMethod.Directional);
            }
            return true;
        }
        return false;
    }

    private void OnSeason(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: MediaSeason season }) SeasonRequested?.Invoke(this, season);
    }
    private void OnEpisode(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: EpisodeCardViewModel episode }) EpisodeRequested?.Invoke(this, episode);
    }
    private void OnSeasonFocus(object? sender, RoutedEventArgs args)
    {
        if (sender is Button card)
        {
            _lastSeason = card;
            card.BringIntoView();
        }
    }
    private void OnEpisodeFocus(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: EpisodeCardViewModel episode } card)
        {
            _lastEpisode = card;
            card.BringIntoView();
            EpisodeRequested?.Invoke(this, episode);
        }
    }
    private void OnBack(object? sender, RoutedEventArgs args) => BackRequested?.Invoke(this, EventArgs.Empty);
    private void OnReadAbove(object? sender, RoutedEventArgs args) => ScrollDetails(-1);
    private void OnReadBelow(object? sender, RoutedEventArgs args) => ScrollDetails(1);
    private void OnCreditsLeft(object? sender, RoutedEventArgs args) => ScrollCredits(-1);
    private void OnCreditsRight(object? sender, RoutedEventArgs args) => ScrollCredits(1);
    private void ScrollCredits(int direction) =>
        CreditsScroll.Offset = new Vector(Math.Clamp(
            CreditsScroll.Offset.X + direction * CreditsScroll.Viewport.Width * 0.8,
            0, Math.Max(0, CreditsScroll.Extent.Width - CreditsScroll.Viewport.Width)), 0);
    private void ScrollDetails(int direction) =>
        EpisodeDetailsScroll.Offset = new Vector(0, Math.Clamp(
            EpisodeDetailsScroll.Offset.Y + direction * EpisodeDetailsScroll.Viewport.Height * 0.8,
            0, Math.Max(0, EpisodeDetailsScroll.Extent.Height - EpisodeDetailsScroll.Viewport.Height)));

    private void ApplyLayout(Size size)
    {
        if (size.Width <= 0 || size.Height <= 0) return;
        var margin = AdaptiveLayoutProfile.Create(size.Width, size.Height).SafeMargin;
        var width = size.Width - 2 * margin;
        _columns = width >= 1800 ? 7 : width >= 1250 ? 5 : width >= 900 ? 4 : width >= 600 ? 3 : 2;
        var cardWidth = Math.Max(120, (width - 24) / _columns - 16);
        Resources["Cindara.Season.EpisodeWidth"] = cardWidth;
        Resources["Cindara.Season.ImageHeight"] = cardWidth * 9 / 16;
        var compact = width < 900;
        SeasonPoster.IsVisible = !compact;
        SeasonHero.ColumnDefinitions = new ColumnDefinitions(compact ? "*" : "Auto,*,*");
        SeasonHero.RowDefinitions = new RowDefinitions(compact ? "Auto,Auto" : "Auto");
        Grid.SetColumn(SeasonSummary, compact ? 0 : 1);
        Grid.SetColumn(SelectedDetails, compact ? 0 : 2);
        Grid.SetRow(SelectedDetails, compact ? 1 : 0);
    }
}
