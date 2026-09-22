using Avalonia.Media;
using Cindara.Core.Jellyfin;
using Cindara.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cindara.Desktop.ViewModels;

public sealed class DesignGalleryViewModel : ObservableObject, IDisposable
{
    private readonly MediaPreviewCardViewModel? _initialFeatured;
    private MediaPreviewCardViewModel? _featured;
    private bool _disposed;

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
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(item);
        Featured = item;
    }

    public static DesignGalleryViewModel Create(MediaPreviewHome home) =>
        Create(home, PreviewImage.Decode);

    internal static DesignGalleryViewModel Create(MediaPreviewHome home, Func<byte[], PreviewImage> decode)
    {
        var createdCards = new List<MediaPreviewCardViewModel>();
        var completed = false;
        MediaPreviewCardViewModel CreateCard(MediaPreviewItem item)
        {
            var card = new MediaPreviewCardViewModel(item, decode);
            createdCards.Add(card);
            return card;
        }

        try
        {
            var gallery = new DesignGalleryViewModel(
                home.Featured is null ? null : CreateCard(home.Featured),
                home.ContinueWatching.Select(CreateCard).ToArray(),
                home.RecentlyAddedLibraries.Select(rail => new MediaPreviewRailViewModel(
                    rail.LibraryName is { } libraryName
                        ? Loc.Format("Format.RecentlyAdded", libraryName)
                        : rail.Title,
                    rail.Items.Select(CreateCard).ToArray())).ToArray());
            completed = true;
            return gallery;
        }
        finally
        {
            if (!completed)
            {
                foreach (var card in createdCards)
                {
                    card.Dispose();
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
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
    internal MediaPreviewRailViewModel(string title, IReadOnlyList<MediaPreviewCardViewModel> items)
    {
        Title = title;
        Items = items;
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
    private readonly PreviewImage? _artwork;
    private readonly PreviewImage? _backdrop;

    public MediaPreviewCardViewModel(MediaPreviewItem item)
        : this(item, PreviewImage.Decode)
    {
    }

    internal MediaPreviewCardViewModel(MediaPreviewItem item, Func<byte[], PreviewImage> decode)
    {
        Name = item.Name;
        HeroName = item.HeroName ?? item.Name;
        Overview = item.Overview ?? string.Empty;
        if (item.Metadata is { } metadata)
        {
            Subtitle = LocaleFormat.Subtitle(
                metadata.Name, item.MediaType, metadata.SeriesName, metadata.SeasonNumber,
                metadata.EpisodeNumber, metadata.ProductionYear, metadata.PreferSeriesTitle);
            HeroSubtitle = LocaleFormat.Subtitle(
                metadata.Name, item.MediaType, metadata.SeriesName, metadata.SeasonNumber,
                metadata.EpisodeNumber, metadata.ProductionYear, preferSeriesTitle: true);
            Details = LocaleFormat.Details(metadata.ProductionYear, metadata.RunTimeTicks, metadata.OfficialRating);
        }
        else
        {
            Subtitle = item.Subtitle;
            HeroSubtitle = item.HeroSubtitle ?? item.Subtitle;
            Details = item.Details;
        }

        PlaybackProgress = item.PlaybackProgress ?? 0;
        var completed = false;
        try
        {
            _artwork = item.Artwork is null ? null : decode(item.Artwork);
            _backdrop = item.Backdrop is null ? null : decode(item.Backdrop);
            completed = true;
        }
        finally
        {
            if (!completed)
            {
                _artwork?.Dispose();
                _backdrop?.Dispose();
            }
        }
    }

    public string Name { get; }

    public string Subtitle { get; }

    public string HeroName { get; }

    public string HeroSubtitle { get; }

    public string Overview { get; }

    public string Details { get; }

    public double PlaybackProgress { get; }

    public bool HasPlaybackProgress => PlaybackProgress > 0;

    public IImage? Artwork => _artwork?.Source;

    public IImage? Backdrop => _backdrop?.Source;

    public void Dispose()
    {
        _artwork?.Dispose();
        _backdrop?.Dispose();
    }
}
