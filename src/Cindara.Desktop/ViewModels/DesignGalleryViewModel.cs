using Avalonia.Media;
using Cindara.Core.Jellyfin;
using Cindara.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cindara.Desktop.ViewModels;

public sealed class DesignGalleryViewModel : ObservableObject, IDisposable
{
    private readonly MediaPreviewCardViewModel? _initialFeatured;
    private MediaPreviewCardViewModel? _featured;
    private readonly IReadOnlyList<MediaPreviewRailViewModel> _allRails;
    private readonly IReadOnlyList<MediaLibrary> _allLibraries;
    private IReadOnlyList<MediaPreviewRailViewModel> _recentlyAddedLibraries;
    private IReadOnlyList<MediaLibrary> _libraries;
    private IReadOnlyList<MediaLibrary> _sidebarLibraries = [];
    private string _libraryLayoutWarning = string.Empty;
    private bool _disposed;

    private DesignGalleryViewModel(
        MediaPreviewCardViewModel? featured,
        IReadOnlyList<MediaPreviewCardViewModel> continueWatching,
        IReadOnlyList<MediaPreviewRailViewModel> recentlyAddedLibraries,
        IReadOnlyList<MediaLibrary> libraries)
    {
        _initialFeatured = featured;
        _featured = featured;
        ContinueWatching = continueWatching;
        _allRails = recentlyAddedLibraries;
        _recentlyAddedLibraries = recentlyAddedLibraries;
        _allLibraries = libraries;
        _libraries = libraries;
        _sidebarLibraries = libraries.Where(library => library.IsSupportedVideoLibrary).ToArray();
    }

    public MediaPreviewCardViewModel? Featured
    {
        get => _featured;
        private set => SetProperty(ref _featured, value);
    }

    public IReadOnlyList<MediaPreviewCardViewModel> ContinueWatching { get; }

    public IReadOnlyList<MediaPreviewRailViewModel> RecentlyAddedLibraries => _recentlyAddedLibraries;

    public bool HasContinueWatching => ContinueWatching.Count > 0;
    public IReadOnlyList<MediaLibrary> Libraries => _libraries;
    public IReadOnlyList<MediaLibrary> SidebarLibraries => _sidebarLibraries;
    public bool HasLibraries => Libraries.Count > 0;
    public string LibraryLayoutWarning => _libraryLayoutWarning;
    public bool HasLibraryLayoutWarning => !string.IsNullOrEmpty(LibraryLayoutWarning);
    public bool IsEmpty => !HasContinueWatching && RecentlyAddedLibraries.All(rail => rail.Items.Count == 0);

    public void ApplyLibraryLayout(IReadOnlyList<string> sidebarIds, IReadOnlyList<string> homeIds, string warning)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var sidebar = sidebarIds.Select(id => _allLibraries.FirstOrDefault(library => library.Id == id)).OfType<MediaLibrary>().ToArray();
        if (!SidebarLibraries.Select(library => library.Id).SequenceEqual(sidebar.Select(library => library.Id)))
        {
            _sidebarLibraries = sidebar;
            OnPropertyChanged(nameof(SidebarLibraries));
        }

        var libraries = homeIds.Select(id => _allLibraries.FirstOrDefault(library => library.Id == id)).OfType<MediaLibrary>().ToArray();
        var rails = homeIds.Select(id => _allRails.FirstOrDefault(rail => rail.LibraryId == id)).OfType<MediaPreviewRailViewModel>().ToArray();
        if (!Libraries.Select(library => library.Id).SequenceEqual(libraries.Select(library => library.Id))
            || !RecentlyAddedLibraries.Select(rail => rail.LibraryId).SequenceEqual(rails.Select(rail => rail.LibraryId)))
        {
            _libraries = libraries;
            _recentlyAddedLibraries = rails;
            Featured = ContinueWatching.Count > 0 ? ContinueWatching[0]
                : rails.SelectMany(rail => rail.Items).FirstOrDefault();
            OnPropertyChanged(nameof(Libraries));
            OnPropertyChanged(nameof(HasLibraries));
            OnPropertyChanged(nameof(RecentlyAddedLibraries));
            OnPropertyChanged(nameof(IsEmpty));
        }

        if (SetProperty(ref _libraryLayoutWarning, warning, nameof(LibraryLayoutWarning)))
        {
            OnPropertyChanged(nameof(HasLibraryLayoutWarning));
        }
    }

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
                    rail.Items.Select(CreateCard).ToArray(), rail.LibraryId)).ToArray(),
                home.Libraries);
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

        foreach (var rail in _allRails)
        {
            rail.Dispose();
        }
    }
}

public sealed class MediaPreviewRailViewModel : IDisposable
{
    internal MediaPreviewRailViewModel(string title, IReadOnlyList<MediaPreviewCardViewModel> items, string? libraryId = null)
    {
        Title = title;
        Items = items;
        LibraryId = libraryId;
    }

    public string Title { get; }
    public string? LibraryId { get; }

    public IReadOnlyList<MediaPreviewCardViewModel> Items { get; }

    public void Dispose()
    {
        foreach (var item in Items)
        {
            item.Dispose();
        }
    }
}

public sealed class MediaPreviewCardViewModel : ObservableObject, IDisposable
{
    private PreviewImage? _artwork;
    private readonly PreviewImage? _backdrop;
    private bool _isArtworkLoading;

    public MediaPreviewCardViewModel(MediaPreviewItem item)
        : this(item, PreviewImage.Decode)
    {
    }

    internal MediaPreviewCardViewModel(MediaPreviewItem item, Func<byte[], PreviewImage> decode)
    {
        Id = item.Id;
        Name = item.Name;
        MediaType = item.MediaType;
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
    public string Id { get; }
    public string MediaType { get; }

    public string Subtitle { get; }

    public string HeroName { get; }

    public string HeroSubtitle { get; }

    public string Overview { get; }

    public string Details { get; }

    public double PlaybackProgress { get; }

    public bool HasPlaybackProgress => PlaybackProgress > 0;

    public IImage? Artwork => _artwork?.Source;
    public bool HasArtwork => Artwork is not null;
    public bool IsArtworkLoading
    {
        get => _isArtworkLoading;
        internal set
        {
            if (SetProperty(ref _isArtworkLoading, value))
            {
                OnPropertyChanged(nameof(ArtworkPlaceholder));
                OnPropertyChanged(nameof(ShowArtworkPlaceholder));
                OnPropertyChanged(nameof(MetadataOpacity));
            }
        }
    }
    public string ArtworkPlaceholder =>
        HasArtwork ? string.Empty : Loc.Get("Library.ArtworkUnavailable");
    public bool ShowArtworkPlaceholder => !HasArtwork && !IsArtworkLoading;
    public double MetadataOpacity => IsArtworkLoading ? 0 : 1;

    public IImage? Backdrop => _backdrop?.Source;

    internal void SetArtwork(PreviewImage? artwork)
    {
        _artwork?.Dispose();
        _artwork = artwork;
        IsArtworkLoading = false;
        OnPropertyChanged(nameof(Artwork));
        OnPropertyChanged(nameof(HasArtwork));
        OnPropertyChanged(nameof(ShowArtworkPlaceholder));
        OnPropertyChanged(nameof(MetadataOpacity));
    }

    public void Dispose()
    {
        _artwork?.Dispose();
        _backdrop?.Dispose();
    }
}
