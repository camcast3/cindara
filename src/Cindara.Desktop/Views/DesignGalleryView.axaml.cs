using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cindara.Core.Jellyfin;
using Cindara.Desktop.Accessibility;
using Cindara.Desktop.DesignSystem;
using Cindara.Desktop.ViewModels;

namespace Cindara.Desktop.Views;

public partial class DesignGalleryView : UserControl
{
    private double _heroHeight = 420;
    private Button? _focusedCard;
    private Button? _libraryReturnFocus;
    private Button? _destinationReturnFocus;
    private bool _pinAfterLayout;
    private PresentationPreferences _preferences = new();
    private bool _rememberFocus;
    private DesignGalleryViewModel? _gallery;

    public event EventHandler? NavigationWidthChanged;
    public double NavigationWidth { get; private set; } = 96;
    public double UiScale { get; private set; } = 1;

    public void SuspendFocusMemory() => _rememberFocus = false;

    public DesignGalleryView()
    {
        InitializeComponent();
        if (HeroPanel.Parent is Grid content)
        {
            content.Children.Remove(HeroPanel);
            content.Children.Add(HeroPanel);
        }

        SizeChanged += OnSizeChanged;
        DataContextChanged += (_, _) =>
        {
            if (_gallery is not null)
            {
                _gallery.PropertyChanged -= OnGalleryChanged;
            }

            _gallery = DataContext as DesignGalleryViewModel;
            if (_gallery is not null)
            {
                _gallery.PropertyChanged += OnGalleryChanged;
            }

            HeroTextScroll.Offset = default;
            _focusedCard = null;
            _libraryReturnFocus = null;
            _rememberFocus = false;
        };
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

    private void OnGalleryChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(DesignGalleryViewModel.Libraries)
            or nameof(DesignGalleryViewModel.RecentlyAddedLibraries))
        {
            _focusedCard = null;
            MediaScrollViewer.Offset = default;
            HeroTextScroll.Offset = default;
            _pinAfterLayout = true;
        }
    }

    public void ApplyPreferences(PresentationPreferences preferences)
    {
        _preferences = preferences;
        HeroArtwork.IsVisible = !preferences.HighContrast;
        UpdateViewport(Bounds.Size);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs eventArgs) => UpdateViewport(eventArgs.NewSize);

    private void UpdateViewport(Size size)
    {
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        var profile = GalleryViewportProfile.Create(size.Width, size.Height);
        var scale = profile.CardScale;
        var heroScale = profile.HeroScale;
        var textScale = _preferences.TextScale;
        var heroHeight = profile.HeroHeight;
        var navigationWidth = profile.NavigationWidth;
        _heroHeight = heroHeight;
        var layoutScaleChanged = Math.Abs(UiScale - heroScale) > 0.001;
        UiScale = heroScale;
        if (Math.Abs(NavigationWidth - navigationWidth) > 0.1 || layoutScaleChanged)
        {
            NavigationWidth = navigationWidth;
            NavigationWidthChanged?.Invoke(this, EventArgs.Empty);
        }

        Resources["Gallery.NavigationWidth"] = navigationWidth;
        Resources["Gallery.NavigationPadding"] = new Thickness(10 * heroScale, 24 * heroScale);
        Resources["Gallery.LogoSize"] = 46 * heroScale;
        Resources["Gallery.LogoRadius"] = new CornerRadius(23 * heroScale);
        Resources["Gallery.LogoFontSize"] = 19 * heroScale * textScale;
        Resources["Gallery.NavigationButtonSize"] = Math.Max(48, 52 * heroScale);
        Resources["Gallery.NavigationIconSize"] = 22 * heroScale;
        Resources["Gallery.NavigationSpacing"] = 12 * heroScale;
        Resources["Gallery.ShortcutWidth"] = Math.Max(48, 74 * heroScale);
        Resources["Gallery.ShortcutTextWidth"] = Math.Max(42, 68 * heroScale);
        Resources["Gallery.NavigationTextSize"] = 14 * heroScale * textScale;
        Resources["Gallery.SectionHeadingSize"] = 24 * heroScale * textScale;
        Resources["Gallery.BodySize"] = 18 * heroScale * textScale;
        Resources["Gallery.HeroPanelMargin"] = new Thickness(
            52 * heroScale,
            30 * heroScale,
            40 * heroScale,
            44 * heroScale);
        Resources["Gallery.HeroHeight"] = heroHeight;
        Resources["Gallery.HeroContentWidth"] = profile.HeroContentWidth;
        Resources["Gallery.HeroHeaderSize"] = 18 * heroScale * textScale;
        Resources["Gallery.HeroTitleSize"] = 56 * heroScale * textScale;
        Resources["Gallery.HeroTitleLineHeight"] = 64 * heroScale * textScale;
        Resources["Gallery.HeroSubtitleSize"] = 28 * heroScale * textScale;
        Resources["Gallery.HeroBodySize"] = 22 * heroScale * textScale;
        Resources["Gallery.HeroBodyLineHeight"] = 30 * heroScale * textScale;
        Resources["Gallery.HeroTextMargin"] = new Thickness(0, 28 * heroScale, 0, 0);
        Resources["Gallery.HeroTextSpacing"] = new Thickness(0, 0, 0, 12 * heroScale);
        Resources["Gallery.ContinueWidth"] = 348 * scale;
        Resources["Gallery.ContinueHeight"] = 195.6 * scale;
        Resources["Gallery.PosterWidth"] = 187.2 * scale;
        Resources["Gallery.PosterHeight"] = 280.8 * scale;
        Resources["Gallery.CardTitleSize"] = 14 * heroScale * textScale;
        Resources["Gallery.CardCaptionSize"] = 12 * heroScale * textScale;
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
        if (_rememberFocus && sender is Button { DataContext: MediaPreviewCardViewModel item }
            && DataContext is DesignGalleryViewModel gallery)
        {
            if (!ReferenceEquals(gallery.Featured, item))
            {
                HeroTextScroll.Offset = default;
            }

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

    public event EventHandler? SettingsRequested;
    public event EventHandler? LibrariesRequested;
    public event EventHandler? SearchRequested;
    public event EventHandler? DownloadsRequested;
    public event EventHandler<MediaLibrary>? LibraryRequested;
    public event EventHandler<MediaPreviewCardViewModel>? ItemRequested;

    public Control HomeNavigation => SidebarHomeButton;

    public bool RestoreHomeFocus()
    {
        var destination = _destinationReturnFocus;
        _destinationReturnFocus = null;
        if (destination is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true }
            && destination.GetVisualAncestors().Contains(this)
            && destination.Focus(NavigationMethod.Directional))
        {
            _rememberFocus = true;
            destination.BringIntoView();
            return true;
        }

        var shortcut = _libraryReturnFocus;
        _libraryReturnFocus = null;
        if (shortcut is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true }
            && shortcut.GetVisualAncestors().Contains(this)
            && shortcut.Focus(NavigationMethod.Directional))
        {
            _rememberFocus = true;
            shortcut.BringIntoView();
            return true;
        }

        return FocusHomeContent();
    }

    public bool FocusHomeContent()
    {
        bool focused;
        if (_focusedCard is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true })
        {
            focused = _focusedCard.Focus(NavigationMethod.Directional);
        }
        else
        {
            var rows = GetMediaRows().ToArray();
            focused = rows.Length > 0 ? FocusMediaRow(rows[0]) : SidebarHomeButton.Focus(NavigationMethod.Directional);
        }

        _rememberFocus = true;
        return focused;
    }

    private void OnHomeClicked(object? sender, RoutedEventArgs args) => FocusHomeContent();

    private void OnSettingsClicked(object? sender, RoutedEventArgs args)
    {
        _destinationReturnFocus = sender as Button;
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnLibrariesClicked(object? sender, RoutedEventArgs args)
    {
        _destinationReturnFocus = sender as Button;
        LibrariesRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnSearchClicked(object? sender, RoutedEventArgs args)
    {
        _destinationReturnFocus = sender as Button;
        SearchRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnDownloadsClicked(object? sender, RoutedEventArgs args)
    {
        _destinationReturnFocus = sender as Button;
        DownloadsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnLibraryClicked(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: MediaLibrary library })
        {
            _libraryReturnFocus = (Button)sender;
            LibraryRequested?.Invoke(this, library);
        }
    }

    private void OnCardClicked(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: MediaPreviewCardViewModel item })
        {
            ItemRequested?.Invoke(this, item);
        }
    }

    public void ScrollDescription(bool forward)
    {
        var step = HeroTextScroll.Viewport.Height * (forward ? 1 : -1);
        HeroTextScroll.Offset = new Vector(0, Math.Clamp(HeroTextScroll.Offset.Y + step, 0,
            Math.Max(0, HeroTextScroll.Extent.Height - HeroTextScroll.Viewport.Height)));
    }

    public bool TryMoveGalleryFocus(NavigationDirection direction)
    {
        if (FlowDirection == Avalonia.Media.FlowDirection.RightToLeft)
        {
            direction = direction switch
            {
                NavigationDirection.Left => NavigationDirection.Right,
                NavigationDirection.Right => NavigationDirection.Left,
                _ => direction,
            };
        }

        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if (ReferenceEquals(focused, SidebarHomeButton) && direction == NavigationDirection.Right)
        {
            return FocusHomeContent();
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
            return SidebarHomeButton.Focus(NavigationMethod.Directional);
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
