using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cindara.Desktop.DesignSystem;
using Cindara.Desktop.Localization;
using Cindara.Desktop.ViewModels;

namespace Cindara.Desktop.Views;

public partial class MovieCreditsView : UserControl
{
    public MovieCreditsView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ApplyLayout();
        CreditsScroll.ScrollChanged += (_, _) => UpdateActions();
    }

    public event EventHandler? BackRequested;
    public Button BackAction => CreditsBack;
    public int Columns { get; private set; } = 1;

    public void ShowCredits(MediaDetailsViewModel details, MovieCreditViewModel? selected)
    {
        DataContext = details;
        CreditEntries.ItemsSource = details.Credits.Select(credit => new MovieCreditEntry(credit, credit == selected)).ToArray();
        EmptyCredits.IsVisible = details.Credits.Count == 0;
        CreditsCount.Text = Loc.Format("Details.CreditCount", details.Credits.Count);
        Dispatcher.UIThread.Post(() =>
        {
            ApplyLayout();
            if (selected is not null)
            {
                CreditEntries.GetVisualDescendants().OfType<Border>()
                    .FirstOrDefault(border => border.Classes.Contains("credit-entry")
                        && border.DataContext is MovieCreditEntry { IsSelected: true })?.BringIntoView();
            }
        }, DispatcherPriority.Loaded);
    }

    private void ApplyLayout()
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var density = ResponsiveDensityProfile.Create(Bounds.Width, Bounds.Height);
        var textScale = Math.Max(1, CreditsCount.FontSize / (14 * density.TypeScale));
        Columns = Math.Max(1, (int)(Bounds.Width / (340 * density.TypeScale * textScale)));
        if (CreditEntries.GetVisualDescendants().OfType<UniformGrid>().FirstOrDefault() is { } grid)
            grid.Columns = Columns;
        Resources["Cindara.Credits.ImageWidth"] = 72 * density.CardScale;
        Resources["Cindara.Credits.ImageHeight"] = 96 * density.CardScale;
    }

    public bool HandleKey(Key key)
    {
        if (key is not (Key.PageUp or Key.PageDown)) return false;
        Scroll(key == Key.PageDown);
        return true;
    }

    private void Scroll(bool next) =>
        CreditsScroll.Offset = new Vector(0, Math.Clamp(
            CreditsScroll.Offset.Y + (next ? 1 : -1) * CreditsScroll.Viewport.Height * 0.85,
            0, Math.Max(0, CreditsScroll.Extent.Height - CreditsScroll.Viewport.Height)));

    private void UpdateActions()
    {
        PreviousCredits.IsEnabled = CreditsScroll.Offset.Y > 1;
        NextCredits.IsEnabled = CreditsScroll.Offset.Y < CreditsScroll.Extent.Height - CreditsScroll.Viewport.Height - 1;
    }

    private void OnBack(object? sender, RoutedEventArgs args) => BackRequested?.Invoke(this, EventArgs.Empty);
    private void OnPrevious(object? sender, RoutedEventArgs args) => Scroll(false);
    private void OnNext(object? sender, RoutedEventArgs args) => Scroll(true);
}

public sealed record MovieCreditEntry(MovieCreditViewModel Credit, bool IsSelected);
