using Avalonia.Media.Imaging;
using Cindara.Core.Jellyfin;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cindara.Desktop.ViewModels;

public sealed class DesignGalleryViewModel : ObservableObject, IDisposable
{
    private readonly MediaPreviewCardViewModel? _initialFeatured;
    private MediaPreviewCardViewModel? _featured;

    private DesignGalleryViewModel(
        MediaPreviewCardViewModel? featured,
        IReadOnlyList<MediaPreviewCardViewModel> continueWatching,
        IReadOnlyList<MediaPreviewRailViewModel> recentlyAddedLibraries)
    {
        _initialFeatured = featured;
        _featured = featured;
        ContinueWatching = continueWatching;
        RecentlyAddedLibraries = recentlyAddedLibraries;
    }

    public MediaPreviewCardViewModel? Featured
    {
        get => _featured;
        private set => SetProperty(ref _featured, value);
    }

    public IReadOnlyList<MediaPreviewCardViewModel> ContinueWatching { get; }

    public IReadOnlyList<MediaPreviewRailViewModel> RecentlyAddedLibraries { get; }

    public bool HasContinueWatching => ContinueWatching.Count > 0;

    public void SelectFeatured(MediaPreviewCardViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Featured = item;
    }

    public static DesignGalleryViewModel Create(MediaPreviewHome home) =>
        new(
            home.Featured is null ? null : new MediaPreviewCardViewModel(home.Featured),
            home.ContinueWatching.Select(item => new MediaPreviewCardViewModel(item)).ToArray(),
            home.RecentlyAddedLibraries
                .Select(rail => new MediaPreviewRailViewModel(rail))
                .ToArray());

    public void Dispose()
    {
        _initialFeatured?.Dispose();
        foreach (var item in ContinueWatching)
        {
            item.Dispose();
        }

        foreach (var rail in RecentlyAddedLibraries)
        {
            rail.Dispose();
        }
    }
}

public sealed class MediaPreviewRailViewModel : IDisposable
{
    public MediaPreviewRailViewModel(MediaPreviewRail rail)
    {
        Title = rail.Title;
        Items = rail.Items.Select(item => new MediaPreviewCardViewModel(item)).ToArray();
    }

    public string Title { get; }

    public IReadOnlyList<MediaPreviewCardViewModel> Items { get; }

    public void Dispose()
    {
        foreach (var item in Items)
        {
            item.Dispose();
        }
    }
}

public sealed class MediaPreviewCardViewModel : IDisposable
{
    public MediaPreviewCardViewModel(MediaPreviewItem item)
    {
        Name = item.Name;
        Subtitle = item.Subtitle;
        HeroName = item.HeroName ?? item.Name;
        HeroSubtitle = item.HeroSubtitle ?? item.Subtitle;
        Overview = item.Overview ?? string.Empty;
        Details = item.Details;
        PlaybackProgress = item.PlaybackProgress ?? 0;
        Artwork = item.Artwork is null ? null : new Bitmap(new MemoryStream(item.Artwork));
        Backdrop = item.Backdrop is null ? null : new Bitmap(new MemoryStream(item.Backdrop));
    }

    public string Name { get; }

    public string Subtitle { get; }

    public string HeroName { get; }

    public string HeroSubtitle { get; }

    public string Overview { get; }

    public string Details { get; }

    public double PlaybackProgress { get; }

    public bool HasPlaybackProgress => PlaybackProgress > 0;

    public Bitmap? Artwork { get; }

    public Bitmap? Backdrop { get; }

    public void Dispose()
    {
        Artwork?.Dispose();
        Backdrop?.Dispose();
    }
}
