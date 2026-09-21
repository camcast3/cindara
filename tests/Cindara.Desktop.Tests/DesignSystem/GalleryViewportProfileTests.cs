using Cindara.Desktop.DesignSystem;

namespace Cindara.Desktop.Tests.DesignSystem;

public sealed class GalleryViewportProfileTests
{
    [Theory]
    [InlineData(1280, 720, 1, 1, 420)]
    [InlineData(1920, 1080, 1.2, 1, 518.4)]
    [InlineData(3440, 1440, 1.55, 1.3333333333333333, 691.2)]
    [InlineData(3840, 2160, 1.55, 2, 1036.8)]
    public void PreservesApprovedPreviewDensity(
        double width, double height, double cards, double hero, double heroHeight)
    {
        var profile = GalleryViewportProfile.Create(width, height);

        Assert.Equal(cards, profile.CardScale, precision: 8);
        Assert.Equal(hero, profile.HeroScale, precision: 8);
        Assert.Equal(heroHeight, profile.HeroHeight, precision: 8);
    }

    [Theory]
    [InlineData(0, 1080)]
    [InlineData(1920, double.NaN)]
    [InlineData(double.PositiveInfinity, 1080)]
    public void RejectsInvalidViewports(double width, double height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GalleryViewportProfile.Create(width, height));
    }
}
