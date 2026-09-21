using Cindara.Core.Jellyfin;
using Cindara.Desktop.ViewModels;

namespace Cindara.Desktop.Tests.ViewModels;

public sealed class DesignGalleryViewModelTests
{
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
