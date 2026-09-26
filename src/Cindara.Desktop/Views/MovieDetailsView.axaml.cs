using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Cindara.Desktop.DesignSystem;
using Cindara.Desktop.ViewModels;

namespace Cindara.Desktop.Views;

public partial class MovieDetailsView : UserControl
{
    private Button? _focusedCast;
    private Control? _castReturnFocus;

    public MovieDetailsView()
    {
        InitializeComponent();
        SizeChanged += (_, args) => ApplyLayout(args.NewSize);
        MovieBody.SizeChanged += (_, _) => ApplyLayout(Bounds.Size);
        DataContextChanged += (_, _) => ResetPosition();
        LayoutUpdated += (_, _) => FitCopy();
    }

    public event EventHandler? BackRequested;
    public event EventHandler? InformationRequested;
    public event EventHandler? CreditsRequested;
    public event EventHandler<MovieCreditViewModel>? CastRequested;
    public Button BackAction => DetailsBack;

    public void ResetPosition()
    {
        CastScroll.Offset = default;
        _focusedCast = null;
        _castReturnFocus = null;
    }

    public bool TryMove(NavigationDirection direction)
    {
        var current = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        var cards = CastScroll.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Classes.Contains("cast-card")).ToArray();
        if (current is Button card && cards.Contains(card))
        {
            if (direction is NavigationDirection.Left or NavigationDirection.Right)
            {
                var next = direction == NavigationDirection.Right;
                if (FlowDirection == Avalonia.Media.FlowDirection.RightToLeft) next = !next;
                var index = Array.IndexOf(cards, card) + (next ? 1 : -1);
                if (index >= 0 && index < cards.Length) FocusCast(cards[index]);
                return true;
            }
            if (direction == NavigationDirection.Up)
            {
                var target = _castReturnFocus is { IsEffectivelyEnabled: true } ? _castReturnFocus : CreditsButton;
                target.Focus(NavigationMethod.Directional);
                return true;
            }
            return direction == NavigationDirection.Down;
        }
        if (direction == NavigationDirection.Down && cards.Length > 0
            && current?.GetVisualAncestors().Contains(MovieButtons) is true)
        {
            _castReturnFocus = current;
            FocusCast(_focusedCast is not null && cards.Contains(_focusedCast) ? _focusedCast : cards[0]);
            return true;
        }
        return false;
    }

    private static void FocusCast(Button card)
    {
        card.Focus(NavigationMethod.Directional);
        card.BringIntoView();
    }

    private void OnCastFocused(object? sender, RoutedEventArgs args)
    {
        if (sender is Button card)
        {
            _focusedCast = card;
            card.BringIntoView();
        }
    }

    private void OnCastSelected(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: MovieCreditViewModel credit })
            CastRequested?.Invoke(this, credit);
    }

    private void OnBack(object? sender, RoutedEventArgs args) => BackRequested?.Invoke(this, EventArgs.Empty);
    private void OnInformation(object? sender, RoutedEventArgs args) => InformationRequested?.Invoke(this, EventArgs.Empty);
    private void OnCredits(object? sender, RoutedEventArgs args) => CreditsRequested?.Invoke(this, EventArgs.Empty);

    private void FitCopy()
    {
        var compact = Bounds.Width < 1000 || Bounds.Height < 600;
        var largeCompactText = compact && (SynopsisPreview.FontSize > 16 || MovieButtons.DesiredSize.Height > 72);
        GenrePreview.IsVisible = !largeCompactText;
        DirectorPreview.IsVisible = !largeCompactText;
        var fixedCopy = MovieInformation.Children.Where(child => child != SynopsisPreview && child.IsVisible)
            .Sum(child => child.DesiredSize.Height) + MovieInformation.RowSpacing * 5;
        var textHeight = fixedCopy + SynopsisPreview.FontSize * 5;
        var bodyHeight = compact ? Math.Max(textHeight, MovieActions.DesiredSize.Height)
            : textHeight + MovieActions.DesiredSize.Height + MovieCopy.RowSpacing;
        var height = Math.Min(MovieHeading.Bounds.Height,
            bodyHeight + MovieButtons.DesiredSize.Height + MovieCopy.RowSpacing);
        if (double.IsNaN(MovieCopy.Height) || Math.Abs(MovieCopy.Height - height) > 0.5)
            MovieCopy.Height = height;
        var lineHeight = SynopsisPreview.FontSize * 1.4;
        var lines = (int)Math.Clamp(Math.Floor((MovieInformation.Bounds.Height - fixedCopy) / lineHeight), 0, 4);
        SynopsisPreview.LineHeight = lineHeight;
        SynopsisPreview.IsVisible = lines > 0;
        SynopsisPreview.MaxLines = Math.Max(1, lines);
    }

    private void ApplyLayout(Size size)
    {
        if (size.Width <= 0 || size.Height <= 0) return;
        var density = ResponsiveDensityProfile.Create(size.Width, size.Height);
        Resources["Cindara.Details.ReadingWidth"] = 1120 * density.TypeScale;
        var contentHeight = MovieBody.Bounds.Height > 0 ? MovieBody.Bounds.Height : size.Height - 96;
        var castHeight = Math.Clamp(contentHeight * 0.34, 120, 480);
        Resources["Cindara.Details.CastHeight"] = castHeight;
        Resources["Cindara.Details.CreditWidth"] = Math.Clamp(castHeight * 0.6, 90, 260);
        Resources["Cindara.Details.CreditHeight"] = Math.Max(60, castHeight - 56);
        var posterWidth = Math.Min(360 * density.CardScale,
            Math.Max(0, contentHeight - castHeight - 12) * 2 / 3);
        Resources["Cindara.Details.PosterWidth"] = posterWidth;
        Resources["Cindara.Details.PosterHeight"] = posterWidth * 1.5;
        var compact = size.Width < 1000 || size.Height < 600;
        MovieHeading.ColumnDefinitions = new ColumnDefinitions(compact ? "*" : "Auto,*");
        MovieHeading.ColumnSpacing = compact ? 0 : 32;
        MoviePoster.IsVisible = !compact;
        Grid.SetColumn(MovieCopy, compact ? 0 : 1);
        MovieCopy.ColumnDefinitions = new ColumnDefinitions(compact ? "3*,2*" : "*");
        MovieCopy.RowDefinitions = new RowDefinitions(compact ? "*,Auto" : "*,Auto,Auto");
        MovieCopy.ColumnSpacing = compact ? 16 : 0;
        Grid.SetColumn(MovieActions, compact ? 1 : 0);
        Grid.SetRow(MovieActions, compact ? 0 : 1);
        Grid.SetRow(MovieButtons, compact ? 1 : 2);
        Grid.SetColumnSpan(MovieButtons, compact ? 2 : 1);
        MediaSummary.ColumnDefinitions = new ColumnDefinitions(compact ? "*" : "*,2*,2*");
        MediaSummary.RowDefinitions = new RowDefinitions(compact ? "Auto,Auto,Auto" : "Auto");
        MediaSummary.RowSpacing = compact ? 8 : 0;
        Grid.SetColumn(AudioSummaryPanel, compact ? 0 : 1);
        Grid.SetRow(AudioSummaryPanel, compact ? 1 : 0);
        Grid.SetColumn(SubtitleSummaryPanel, compact ? 0 : 2);
        Grid.SetRow(SubtitleSummaryPanel, compact ? 2 : 0);
        VideoSummaryText.MaxLines = compact ? 1 : 2;
        AudioSummaryText.MaxLines = compact ? 1 : 2;
        SubtitleSummaryText.MaxLines = compact ? 1 : 2;
        PlaybackNotice.IsVisible = !compact;
        WatchedPreview.IsVisible = !compact;
    }
}
