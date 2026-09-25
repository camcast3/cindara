using Cindara.Desktop.DesignSystem;

namespace Cindara.Desktop.Tests.DesignSystem;

public sealed class ResponsiveDensityProfileTests
{
    [Theory]
    [InlineData(1280, 720, 1, 0.7, 270, 405, 24, 48)]
    [InlineData(1920, 1080, 1.2, 1, 324, 486, 28.8, 56)]
    [InlineData(3440, 1400, 1.55, 1.2962962962962963, 418.5, 627.75, 37.2, 72.5925925925926)]
    [InlineData(3840, 2160, 1.55, 2, 418.5, 627.75, 37.2, 112)]
    public void GridTypographyAndActionsFollowHomeDensity(
        double width,
        double height,
        double cardScale,
        double typeScale,
        double posterWidth,
        double posterHeight,
        double spacing,
        double actionSize)
    {
        var profile = ResponsiveDensityProfile.Create(width, height);

        Assert.Equal(cardScale, profile.CardScale, precision: 6);
        Assert.Equal(typeScale, profile.TypeScale, precision: 6);
        Assert.Equal(posterWidth, profile.GridPosterWidth, precision: 6);
        Assert.Equal(posterHeight, profile.GridPosterHeight, precision: 6);
        Assert.Equal(spacing, profile.GridSpacing, precision: 6);
        Assert.Equal(actionSize, profile.NavigationActionSize, precision: 6);
    }
}
