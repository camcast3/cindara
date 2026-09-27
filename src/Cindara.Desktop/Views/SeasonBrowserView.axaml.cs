using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Cindara.Core.Jellyfin;
using Cindara.Desktop.Navigation;
using Cindara.Desktop.ViewModels;

namespace Cindara.Desktop.Views;

public partial class SeasonBrowserView : UserControl
{
    private Button? _lastEpisode;
    private Button? _lastSeason;

    public SeasonBrowserView() => InitializeComponent();

    public Button BackAction => BrowserBack;
    public event EventHandler? BackRequested;
    public event EventHandler<MediaSeason>? SeasonRequested;
    public event EventHandler<EpisodeCardViewModel>? EpisodeRequested;

    public void ResetPosition()
    {
        _lastSeason = null;
        _lastEpisode = null;
        SeasonScroll.Offset = default;
        EpisodeScroll.Offset = default;
        EpisodeDetailsScroll.Offset = default;
    }

    public void ResetEpisodePosition()
    {
        _lastEpisode = null;
        EpisodeScroll.Offset = default;
    }

    public void FocusSelectedEpisode(string? episodeId = null)
    {
        var cards = EpisodeScroll.GetVisualDescendants().OfType<Button>().ToArray();
        var target = cards.FirstOrDefault(card => card.DataContext is EpisodeCardViewModel episode
            && episode.Episode.Id == episodeId) ?? cards.FirstOrDefault();
        target?.Focus(NavigationMethod.Directional);
    }

    public bool TryMove(NavigationDirection direction)
    {
        var focus = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        var seasons = SeasonScroll.GetVisualDescendants().OfType<Button>().ToArray();
        var episodes = EpisodeScroll.GetVisualDescendants().OfType<Button>().ToArray();
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
            return false;
        }
        if (direction is NavigationDirection.Left or NavigationDirection.Right)
        {
            var forward = direction == NavigationDirection.Right;
            if (FlowDirection == Avalonia.Media.FlowDirection.RightToLeft) forward = !forward;
            var index = Array.IndexOf(row, focus) + (forward ? 1 : -1);
            if (index >= 0 && index < row.Length) row[index].Focus(NavigationMethod.Directional);
            return true;
        }
        if (direction == NavigationDirection.Up)
        {
            (ReferenceEquals(row, episodes) ? _lastSeason ?? seasons.FirstOrDefault() : BrowserBack)
                ?.Focus(NavigationMethod.Directional);
            return true;
        }
        if (direction == NavigationDirection.Down)
        {
            if (ReferenceEquals(row, seasons))
                (_lastEpisode is not null && episodes.Contains(_lastEpisode) ? _lastEpisode : episodes.FirstOrDefault())
                    ?.Focus(NavigationMethod.Directional);
            else ReadBelow.Focus(NavigationMethod.Directional);
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
    private void ScrollDetails(int direction) =>
        EpisodeDetailsScroll.Offset = new Vector(0, Math.Clamp(
            EpisodeDetailsScroll.Offset.Y + direction * EpisodeDetailsScroll.Viewport.Height * 0.8,
            0, Math.Max(0, EpisodeDetailsScroll.Extent.Height - EpisodeDetailsScroll.Viewport.Height)));
}
