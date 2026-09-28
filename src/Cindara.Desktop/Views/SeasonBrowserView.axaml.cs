using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Cindara.Desktop.DesignSystem;
using Cindara.Desktop.Navigation;
using Cindara.Desktop.ViewModels;

namespace Cindara.Desktop.Views;

public partial class SeasonBrowserView : UserControl
{
    private Control? _focusedRow;

    public SeasonBrowserView()
    {
        InitializeComponent();
        SizeChanged += (_, args) => ApplyLayout(args.NewSize);
        BrowserScroll.LayoutUpdated += (_, _) => PinFocusedRow();
    }

    public Button BackAction => BrowserBack;
    public event EventHandler? BackRequested;
    public event EventHandler<EpisodeCardViewModel>? EpisodeRequested;

    public void ResetPosition()
    {
        _focusedRow = null;
        BrowserScroll.Offset = default;
        EpisodeScroll.Offset = default;
        CreditsScroll.Offset = default;
    }

    public void FocusSelectedEpisode(string? episodeId = null)
    {
        var cards = EpisodeCards();
        var target = cards.FirstOrDefault(card => card.DataContext is EpisodeCardViewModel episode
            && episode.Episode.Id == episodeId) ?? cards.FirstOrDefault();
        target?.Focus(NavigationMethod.Directional);
    }

    public bool TryMove(NavigationDirection direction)
    {
        var focus = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        var episodes = EpisodeCards();
        var credits = CastRail.GetVisualDescendants().OfType<StackPanel>()
            .Where(panel => panel.DataContext is MovieCreditViewModel && panel.Focusable).ToArray();
        var row = episodes.Contains(focus) ? episodes.Cast<Control>().ToArray()
            : credits.Contains(focus) ? credits.Cast<Control>().ToArray() : null;
        if (row is null)
        {
            if (direction == NavigationDirection.Down && focus == BrowserBack)
            {
                EpisodeScroll.Offset = default;
                episodes.FirstOrDefault()?.Focus(NavigationMethod.Directional);
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
            if (episodes.Contains(focus))
            {
                _focusedRow = null;
                BrowserBack.Focus(NavigationMethod.Directional);
                BrowserScroll.Offset = default;
            }
            else
            {
                EpisodeScroll.Offset = default;
                episodes.FirstOrDefault()?.Focus(NavigationMethod.Directional);
            }
            return true;
        }
        if (direction == NavigationDirection.Down)
        {
            if (episodes.Contains(focus))
            {
                CreditsScroll.Offset = default;
                credits.FirstOrDefault()?.Focus(NavigationMethod.Directional);
            }
            return true;
        }
        return false;
    }

    private Button[] EpisodeCards() => EpisodeRail.GetVisualDescendants().OfType<Button>()
        .Where(button => button.Classes.Contains("episode-card")).ToArray();

    private void OnEpisode(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: EpisodeCardViewModel episode }) EpisodeRequested?.Invoke(this, episode);
    }

    private void OnEpisodeFocus(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: EpisodeCardViewModel episode } card)
        {
            _focusedRow = EpisodeSection;
            card.BringIntoView();
            PinFocusedRow();
            EpisodeRequested?.Invoke(this, episode);
        }
    }

    private void OnCreditFocus(object? sender, RoutedEventArgs args)
    {
        if (sender is StackPanel card)
        {
            _focusedRow = CastSection;
            card.BringIntoView();
            PinFocusedRow();
        }
    }

    private void OnRowBringIntoViewRequested(object? sender, RequestBringIntoViewEventArgs args)
    {
        if (args.Source is Visual visual
            && (ReferenceEquals(visual, EpisodeSection) || visual.GetVisualAncestors().Contains(EpisodeSection)
                || ReferenceEquals(visual, CastSection) || visual.GetVisualAncestors().Contains(CastSection)))
            args.Handled = true;
    }

    private void PinFocusedRow()
    {
        if (_focusedRow is null || !_focusedRow.IsEffectivelyVisible) return;
        var position = _focusedRow.TranslatePoint(default, BrowserContent);
        if (position is null) return;
        var offset = Math.Clamp(position.Value.Y, 0,
            Math.Max(0, BrowserScroll.Extent.Height - BrowserScroll.Viewport.Height));
        if (Math.Abs(BrowserScroll.Offset.Y - offset) > 0.5)
            BrowserScroll.Offset = new Vector(BrowserScroll.Offset.X, offset);
    }

    private void OnBack(object? sender, RoutedEventArgs args) => BackRequested?.Invoke(this, EventArgs.Empty);

    private void ApplyLayout(Size size)
    {
        if (size.Width <= 0 || size.Height <= 0) return;
        var density = ResponsiveDensityProfile.Create(size.Width, size.Height);
        var margin = AdaptiveLayoutProfile.Create(size.Width, size.Height).SafeMargin;
        var width = size.Width - 2 * margin;
        var cardWidth = Math.Min(348 * Math.Clamp(size.Width / 1600, 1, 1.55),
            Math.Max(160, width - 16));
        Resources["Cindara.Season.EpisodeWidth"] = cardWidth;
        Resources["Cindara.Season.ImageHeight"] = cardWidth * 9 / 16;
        Resources["Cindara.Season.EpisodeSectionHeight"] = cardWidth * 9 / 16 + 110;
        var creditWidth = Math.Clamp(size.Height * 0.2, 96, 260);
        Resources["Cindara.Season.CastHeight"] = creditWidth + 100;
        Resources["Cindara.Season.CreditWidth"] = creditWidth;
        Resources["Cindara.Season.CreditImageHeight"] = creditWidth;
        Resources["Cindara.Season.ReadingWidth"] = 1120 * density.TypeScale;

        var compact = width < 900 || size.Height < 600;
        SeasonPoster.IsVisible = !compact;
        if (!compact)
        {
            var posterWidth = Math.Clamp(320 * density.CardScale, 220, 440);
            SeasonPoster.Width = posterWidth;
            SeasonPoster.Height = posterWidth * 1.5;
        }
        SeasonHero.ColumnDefinitions = new ColumnDefinitions(compact ? "*" : "Auto,*");
        Grid.SetColumn(EpisodeInformation, compact ? 0 : 1);
        MediaSummary.ColumnDefinitions = new ColumnDefinitions(compact ? "*" : "*,2*,2*");
        MediaSummary.RowDefinitions = new RowDefinitions(compact ? "Auto,Auto,Auto" : "Auto");
        Grid.SetColumn(AudioSummaryPanel, compact ? 0 : 1);
        Grid.SetRow(AudioSummaryPanel, compact ? 1 : 0);
        Grid.SetColumn(SubtitleSummaryPanel, compact ? 0 : 2);
        Grid.SetRow(SubtitleSummaryPanel, compact ? 2 : 0);
    }
}
