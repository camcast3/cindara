using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cindara.Desktop.DesignSystem;
using Cindara.Desktop.ViewModels;

namespace Cindara.Desktop.Views;

public partial class DesignGalleryView : UserControl
{
    private double _heroHeight = 420;
    private Button? _focusedCard;
    private Button? _lastHeaderTab;
    private bool _pinAfterLayout;

    public DesignGalleryView()
    {
        InitializeComponent();
        if (HeroPanel.Parent is Grid content)
        {
            content.Children.Remove(HeroPanel);
            content.Children.Add(HeroPanel);
        }

        SizeChanged += OnSizeChanged;
        HeroPanel.SizeChanged += (_, _) =>
        {
            UpdateHeroArtwork();
            _pinAfterLayout = true;
        };
        MediaRowsPanel.SizeChanged += (_, _) => _pinAfterLayout = true;
        MediaScrollViewer.LayoutUpdated += OnMediaLayoutUpdated;
        AddHandler(KeyDownEvent, OnGalleryKeyDown, RoutingStrategies.Tunnel);
        HeroBackdrop.PropertyChanged += (_, args) =>
        {
            if (args.Property == Image.SourceProperty)
            {
                UpdateHeroArtwork();
            }
        };
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs eventArgs)
    {
        var scale = Math.Clamp(eventArgs.NewSize.Width / 1600, 1, 1.55);
        var heroScale = Math.Clamp(
            ViewportProfile.Create(eventArgs.NewSize.Width, eventArgs.NewSize.Height).Scale,
            1,
            2);
        var heroHeight = Math.Clamp(eventArgs.NewSize.Height * 0.48, 420, 1080);
        _heroHeight = heroHeight;
        Resources["Gallery.HeroHeight"] = heroHeight;
        Resources["Gallery.HeroContentWidth"] = Math.Min(
            880 * heroScale,
            eventArgs.NewSize.Width * 0.46);
        Resources["Gallery.HeroHeaderSize"] = 18 * heroScale;
        Resources["Gallery.HeroTitleSize"] = 56 * heroScale;
        Resources["Gallery.HeroTitleLineHeight"] = 64 * heroScale;
        Resources["Gallery.HeroSubtitleSize"] = 28 * heroScale;
        Resources["Gallery.HeroBodySize"] = 22 * heroScale;
        Resources["Gallery.HeroBodyLineHeight"] = 30 * heroScale;
        Resources["Gallery.HeroTextMargin"] = new Thickness(0, 28 * heroScale, 0, 0);
        Resources["Gallery.HeroTextSpacing"] = new Thickness(0, 0, 0, 12 * heroScale);
        Resources["Gallery.ContinueWidth"] = 290 * scale;
        Resources["Gallery.ContinueHeight"] = 163 * scale;
        Resources["Gallery.PosterWidth"] = 156 * scale;
        Resources["Gallery.PosterHeight"] = 234 * scale;
        Resources["Gallery.CardTitleSize"] = 14 * scale;
        Resources["Gallery.CardCaptionSize"] = 12 * scale;
        Resources["Gallery.ItemSpacing"] = 16 * scale;
    }

    private void UpdateHeroArtwork()
    {
        if (HeroPanel.Bounds.Width <= 0 || HeroPanel.Bounds.Height <= 0)
        {
            return;
        }

        var imageSize = HeroBackdrop.Source?.Size ?? new Size(16, 9);
        var layout = HeroArtworkLayout.Create(
            HeroPanel.Bounds.Width,
            HeroPanel.Bounds.Height,
            imageSize.Width / imageSize.Height);
        Resources["Gallery.HeroArtworkWidth"] = layout.Width;
        Resources["Gallery.HeroArtworkHeight"] = layout.Height;
        Resources["Gallery.HeroArtworkVisibleHeight"] = layout.VisibleHeight;
    }

    private void OnMediaCardFocused(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { DataContext: MediaPreviewCardViewModel item }
            && DataContext is DesignGalleryViewModel gallery)
        {
            gallery.SelectFeatured(item);
            var card = (Button)sender;
            _focusedCard = card;
            Dispatcher.UIThread.Post(
                () => PinFocusedRow(card),
                DispatcherPriority.Loaded);
        }
    }

    private void OnGalleryKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        var direction = eventArgs.Key switch
        {
            Key.Up => NavigationDirection.Up,
            Key.Down => NavigationDirection.Down,
            Key.Left => NavigationDirection.Left,
            Key.Right => NavigationDirection.Right,
            _ => (NavigationDirection?)null,
        };
        if (direction is { } navigationDirection && TryMoveGalleryFocus(navigationDirection))
        {
            eventArgs.Handled = true;
        }
    }

    private void OnHeaderFocused(object? sender, RoutedEventArgs eventArgs)
    {
        if (eventArgs.Source is Button tab && tab.Classes.Contains("header-tab"))
        {
            _lastHeaderTab = tab;
        }
    }

    public bool FocusTopNavigation() =>
        (_lastHeaderTab ?? HomeTabButton).Focus(NavigationMethod.Directional);

    public bool TryMoveGalleryFocus(NavigationDirection direction)
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        var tabs = TopNavigationPanel.Children.OfType<Button>().ToArray();
        var tabIndex = Array.FindIndex(tabs, tab => ReferenceEquals(tab, focused));
        if (tabIndex >= 0)
        {
            switch (direction)
            {
                case NavigationDirection.Left:
                    return tabIndex == 0
                        ? SidebarHomeButton.Focus(NavigationMethod.Directional)
                        : tabs[tabIndex - 1].Focus(NavigationMethod.Directional);
                case NavigationDirection.Right:
                    return tabIndex == tabs.Length - 1
                        || tabs[tabIndex + 1].Focus(NavigationMethod.Directional);
                case NavigationDirection.Up:
                    return true;
                case NavigationDirection.Down:
                    var rows = GetMediaRows().ToArray();
                    if (rows.Length == 0)
                    {
                        return true;
                    }

                    var previousRow = _focusedCard?.GetVisualAncestors().OfType<ItemsControl>()
                        .FirstOrDefault(control => control.Classes.Contains("media-row"));
                    var rowIndex = Array.FindIndex(rows, row => ReferenceEquals(row.Control, previousRow));
                    return FocusMediaRow(rows[Math.Max(0, rowIndex)]);
            }
        }

        if (ReferenceEquals(focused, SidebarHomeButton) && direction == NavigationDirection.Right)
        {
            return FocusTopNavigation();
        }

        return TryMoveMediaRowFocus(direction);
    }

    private IEnumerable<(ItemsControl Control, Button FirstCard)> GetMediaRows()
    {
        foreach (var control in MediaRowsPanel.GetVisualDescendants().OfType<ItemsControl>()
            .Where(control => control.Classes.Contains("media-row") && control.IsEffectivelyVisible))
        {
            var firstCard = control.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(button => button.Classes.Contains("card")
                    && button.IsEffectivelyVisible && button.IsEnabled && button.Focusable);
            if (firstCard is not null)
            {
                yield return (control, firstCard);
            }
        }
    }

    private static bool FocusMediaRow((ItemsControl Control, Button FirstCard) row)
    {
        var horizontalScroll = row.Control.GetVisualAncestors().OfType<ScrollViewer>().First();
        horizontalScroll.Offset = new Vector(0, horizontalScroll.Offset.Y);
        return row.FirstCard.Focus(NavigationMethod.Directional);
    }

    private bool TryMoveMediaRowFocus(NavigationDirection direction)
    {
        if (direction is not (NavigationDirection.Up or NavigationDirection.Down)
            || TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Button currentCard
            || !currentCard.Classes.Contains("card"))
        {
            return false;
        }

        var currentRow = currentCard.GetVisualAncestors()
            .OfType<ItemsControl>()
            .FirstOrDefault(control => control.Classes.Contains("media-row"));
        var rows = GetMediaRows().ToArray();
        var rowIndex = Array.FindIndex(rows, row => ReferenceEquals(row.Control, currentRow));
        if (rowIndex < 0)
        {
            return false;
        }

        var targetIndex = rowIndex + (direction == NavigationDirection.Down ? 1 : -1);
        if (targetIndex < 0)
        {
            return FocusTopNavigation();
        }

        if (targetIndex >= rows.Length)
        {
            return true;
        }

        return FocusMediaRow(rows[targetIndex]);
    }

    private void OnMediaLayoutUpdated(object? sender, EventArgs eventArgs)
    {
        var lastCard = MediaRowsPanel.GetVisualDescendants()
            .OfType<Button>()
            .LastOrDefault(button => button.Classes.Contains("card") && button.IsEffectivelyVisible);
        var lastRowPosition = lastCard is null
            ? null
            : GetMediaRail(lastCard).TranslatePoint(default, MediaRowsPanel);
        var remainingContentHeight = lastRowPosition is { } position
            ? Math.Max(0, MediaRowsPanel.Bounds.Height - position.Y + MediaRowsPanel.Margin.Bottom)
            : 0;
        var trailingSpace = lastRowPosition is null
            ? 0
            : MediaRailLayout.GetTrailingSpace(
                MediaScrollViewer.Viewport.Height,
                _heroHeight,
                remainingContentHeight);

        // Reserve enough space to pin the final row's heading, not just its artwork.
        if (Math.Abs(MediaScrollTail.Height - trailingSpace) > 0.1)
        {
            MediaScrollTail.Height = trailingSpace;
            _pinAfterLayout = true;
            return;
        }

        if (_pinAfterLayout)
        {
            _pinAfterLayout = false;
            if (_focusedCard is { IsFocused: true } card)
            {
                PinFocusedRow(card);
            }
        }
    }

    private static StackPanel GetMediaRail(Button card) =>
        card.GetVisualAncestors().OfType<StackPanel>()
            .First(panel => panel.Classes.Contains("media-rail"));

    private void OnMediaBringIntoViewRequested(object? sender, RequestBringIntoViewEventArgs eventArgs)
    {
        // Child scroll viewers have already handled horizontal visibility; we own vertical positioning.
        eventArgs.Handled = true;
    }

    private void PinFocusedRow(Button card)
    {
        if (!card.IsFocused)
        {
            return;
        }

        var position = GetMediaRail(card).TranslatePoint(default, MediaScrollContent);
        if (position is null)
        {
            return;
        }

        var offset = MediaRailLayout.GetPinnedOffset(
            position.Value.Y,
            _heroHeight,
            MediaScrollViewer.Viewport.Height,
            MediaScrollViewer.Extent.Height);
        MediaScrollViewer.Offset = new Vector(MediaScrollViewer.Offset.X, offset);
    }

}
