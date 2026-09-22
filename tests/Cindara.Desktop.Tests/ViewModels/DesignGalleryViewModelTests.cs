using Cindara.Core.Jellyfin;
using Cindara.Desktop.Tests.Localization;
using Cindara.Desktop.ViewModels;

namespace Cindara.Desktop.Tests.ViewModels;

[Collection(LocalizationTestGroup.Name)]
public sealed class DesignGalleryViewModelTests
{
    [Theory]
    [InlineData("en", "Northstar · S2 E3", "S2 E3 · Homecoming", "2026  ·  1h 5m  ·  TV-14",
        "Recently Added in TV {1}")]
    [InlineData("fr", "Northstar · S2 E3", "S2 E3 · Homecoming", "2026  ·  1h 5m  ·  TV-14",
        "Recently Added in TV {1}")]
    public void RawMetadataIsLocalizedForCardsHeroAndLibraryHeadings(
        string culture, string subtitle, string heroSubtitle, string details, string railTitle)
    {
        using var scope = new CultureScope(culture);
        var item = CreateItem("episode", "Homecoming", "Northstar", "legacy subtitle") with
        {
            Metadata = new MediaPreviewMetadata("Homecoming", "Northstar", 2, 3, 2026,
                TimeSpan.FromMinutes(65).Ticks, "TV-14", false),
        };
        using var gallery = DesignGalleryViewModel.Create(new MediaPreviewHome(item, [item],
            [new MediaPreviewRail("tv", "unformatted title", [item], LibraryName: "TV {1}")]));

        var card = Assert.Single(gallery.ContinueWatching);
        Assert.Equal("Homecoming", card.Name);
        Assert.Equal("Northstar", card.HeroName);
        Assert.Equal("Episode overview", card.Overview);
        Assert.Equal(subtitle, card.Subtitle);
        Assert.Equal(heroSubtitle, card.HeroSubtitle);
        Assert.Equal(details, card.Details);
        Assert.Equal(railTitle, Assert.Single(gallery.RecentlyAddedLibraries).Title);
        gallery.SelectFeatured(card);
        Assert.Equal(heroSubtitle, gallery.Featured?.HeroSubtitle);
    }

    [Fact]
    public void LibraryCardsAndFeaturedItemsUseSeriesTitleSubtitleLayout()
    {
        using var scope = new CultureScope("fr");
        var item = CreateItem("episode", "Northstar", "Northstar", "legacy subtitle") with
        {
            Metadata = new MediaPreviewMetadata("Homecoming", "Northstar", 2, 3, null, null, null, true),
        };
        using var card = new MediaPreviewCardViewModel(item);

        Assert.Equal("S2 E3 · Homecoming", card.Subtitle);
        Assert.Equal(card.Subtitle, card.HeroSubtitle);
        Assert.Empty(card.Details);
    }

    [Theory]
    [InlineData("qps-ploc")]
    [InlineData("qps-plocm")]
    public void PseudoMetadataPreservesAllServerSuppliedText(string culture)
    {
        using var scope = new CultureScope(culture);
        var item = CreateItem("episode", "Homecoming", "Northstar", "legacy subtitle") with
        {
            Metadata = new MediaPreviewMetadata("Homecoming", "Northstar", 2, 3, 2026,
                TimeSpan.FromMinutes(65).Ticks, "TV-14", false),
        };
        using var gallery = DesignGalleryViewModel.Create(new MediaPreviewHome(item, [item],
            [new MediaPreviewRail("tv", "unformatted title", [item], LibraryName: "My library")]));

        var card = Assert.Single(gallery.ContinueWatching);
        Assert.Equal("Homecoming", card.Name);
        Assert.Equal("Northstar", card.HeroName);
        Assert.Equal("Episode overview", card.Overview);
        Assert.Contains("Northstar", card.Subtitle, StringComparison.Ordinal);
        Assert.Contains("Homecoming", card.HeroSubtitle, StringComparison.Ordinal);
        Assert.Contains("TV-14", card.Details, StringComparison.Ordinal);
        var railTitle = Assert.Single(gallery.RecentlyAddedLibraries).Title;
        Assert.Contains("My library", railTitle, StringComparison.Ordinal);
        Assert.DoesNotContain("Recently Added", railTitle, StringComparison.Ordinal);
    }

    [Fact]
    public void BackdropFailureDisposesArtworkDecodedForTheSameCard()
    {
        var decoder = new TestPreviewImageDecoder { FailOnCall = 2 };
        var item = CreateItem("episode", "Pilot", "Northstar", "S1 E1") with
        {
            Artwork = [1],
            Backdrop = [2],
        };

        var exception = Assert.Throws<MediaPreviewException>(
            () => new MediaPreviewCardViewModel(item, decoder.Decode));

        Assert.Equal(MediaPreviewError.InvalidResponse, exception.Error);
        Assert.Equal(1, Assert.Single(decoder.Resources).DisposeCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void GalleryFailureDisposesAllPreviouslyDecodedImages(int failOnCall)
    {
        var decoder = new TestPreviewImageDecoder { FailOnCall = failOnCall };
        var item = CreateItem("episode", "Pilot", "Northstar", "S1 E1") with
        {
            Artwork = [1],
            Backdrop = [2],
        };
        var home = new MediaPreviewHome(item, [item],
            [new MediaPreviewRail("tv", "TV", [item]), new MediaPreviewRail("anime", "Anime", [item])]);

        var exception = Assert.Throws<MediaPreviewException>(() => DesignGalleryViewModel.Create(home, decoder.Decode));

        Assert.Equal(MediaPreviewError.InvalidResponse, exception.Error);
        Assert.Equal(failOnCall - 1, decoder.Resources.Count);
        Assert.All(decoder.Resources, resource => Assert.Equal(1, resource.DisposeCount));
    }

    [Fact]
    public void SuccessfulGalleryOwnsAllImagesUntilDisposed()
    {
        var decoder = new TestPreviewImageDecoder();
        var item = CreateItem("episode", "Pilot", "Northstar", "S1 E1") with
        {
            Artwork = [1],
            Backdrop = [2],
        };
        var home = new MediaPreviewHome(item, [item], [new MediaPreviewRail("tv", "TV", [item])]);
        var gallery = DesignGalleryViewModel.Create(home, decoder.Decode);
        gallery.SelectFeatured(gallery.ContinueWatching[0]);
        Assert.Equal(6, decoder.Resources.Count);
        Assert.All(decoder.Resources, resource => Assert.Equal(0, resource.DisposeCount));

        gallery.Dispose();
        gallery.Dispose();

        Assert.All(decoder.Resources, resource => Assert.Equal(1, resource.DisposeCount));
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("unsupported")]
    [InlineData("corrupt")]
    public void KnownDecodeErrorsAreMappedAndInputStreamsAreClosed(string error)
    {
        Exception failure = error switch
        {
            "invalid" => new ArgumentException("Cannot decode"),
            "unsupported" => new NotSupportedException("Unsupported image"),
            _ => new InvalidDataException("Corrupt image"),
        };
        Stream? input = null;

        var exception = Assert.Throws<MediaPreviewException>(() => PreviewImage.Decode([1, 2, 3], stream =>
        {
            input = stream;
            throw failure;
        }));

        Assert.Equal(MediaPreviewError.InvalidResponse, exception.Error);
        Assert.Same(failure, exception.InnerException);
        Assert.NotNull(input);
        Assert.False(input.CanRead);
    }

    [Fact]
    public void UnexpectedDecodeFailuresPropagateButStillReleasePartialImages()
    {
        var failure = new InvalidOperationException("Renderer unavailable.");
        var decoder = new TestPreviewImageDecoder { FailOnCall = 2, Failure = failure };
        var item = CreateItem("episode", "Pilot", "Northstar", "S1 E1") with
        {
            Artwork = [1],
            Backdrop = [2],
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => new MediaPreviewCardViewModel(item, decoder.Decode));

        Assert.Same(failure, exception);
        Assert.Equal(1, Assert.Single(decoder.Resources).DisposeCount);
    }

    [Fact]
    public void SelectingContinueWatchingItemUpdatesHeroContent()
    {
        var first = CreateItem("episode-1", "Pilot", "Northstar", "S1 E1");
        var second = CreateItem("episode-2", "Homecoming", "Northstar", "S1 E2");
        using var viewModel = DesignGalleryViewModel.Create(
            new MediaPreviewHome(first, [first, second], []));

        viewModel.SelectFeatured(viewModel.ContinueWatching[1]);

        Assert.Equal("Northstar", viewModel.Featured?.HeroName);
        Assert.Equal("S1 E2", viewModel.Featured?.HeroSubtitle);
        Assert.Equal("Homecoming", viewModel.Featured?.Name);
    }

    private static MediaPreviewItem CreateItem(
        string id,
        string name,
        string heroName,
        string heroSubtitle) =>
        new(
            id,
            name,
            $"{heroName} · {heroSubtitle}",
            "Episode",
            Artwork: null,
            Backdrop: null,
            Overview: "Episode overview",
            Details: "42m",
            PlaybackProgress: 50,
            heroName,
            heroSubtitle);
}
