using Cindara.Desktop.DesignSystem;

namespace Cindara.Desktop.Tests.DesignSystem;

public sealed class GalleryViewportProfileTests
{
    [Theory]
    [InlineData(1280, 720, 1, 1, 299.2, 568.32)]
    [InlineData(1920, 1080, 1.2, 1, 518.4, 875.52)]
    [InlineData(3440, 1400, 1.55, 1.2962962962962963, 672, 1140.7407407407406)]
    [InlineData(3840, 2160, 1.55, 2, 1036.8, 1760)]
    public void PreservesDensityAndReservesSpaceForEnlargedPosters(
        double width,
        double height,
        double cards,
        double hero,
        double heroHeight,
        double heroContentWidth)
    {
        var profile = GalleryViewportProfile.Create(width, height);

        Assert.Equal(cards, profile.CardScale, precision: 8);
        Assert.Equal(hero, profile.HeroScale, precision: 8);
        Assert.Equal(heroHeight, profile.HeroHeight, precision: 8);
        Assert.Equal(heroContentWidth, profile.HeroContentWidth, precision: 8);
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
