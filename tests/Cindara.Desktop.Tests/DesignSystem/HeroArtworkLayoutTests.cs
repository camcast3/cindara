using Cindara.Desktop.DesignSystem;

namespace Cindara.Desktop.Tests.DesignSystem;

public sealed class HeroArtworkLayoutTests
{
    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    [InlineData(3440, 1440)]
    [InlineData(3840, 2160)]
    public void ResizingKeepsArtworkNearMidpointWithoutChangingItsAspect(double width, double height)
    {
        var heroWidth = width - 74;
        var heroHeight = Math.Clamp(height * 0.48, 420, 1080);
        var layout = HeroArtworkLayout.Create(heroWidth, heroHeight);

        Assert.Equal(heroWidth * 0.4, heroWidth - layout.Width, precision: 8);
        Assert.Equal(16.0 / 9, layout.Width / layout.Height, precision: 8);
        Assert.Equal(Math.Min(layout.Height, heroHeight), layout.VisibleHeight);
        Assert.InRange(layout.VisibleHeight, 0, heroHeight);
    }

    [Theory]
    [InlineData(2.4)]
    [InlineData(4.0 / 3)]
    [InlineData(2.0 / 3)]
    public void ImageSpecificAspectPreservesFramingAndKeepsTheFadeInsideTheBanner(double aspect)
    {
        var layout = HeroArtworkLayout.Create(3366, 691.2, aspect);

        Assert.Equal(2019.6, layout.Width, precision: 8);
        Assert.Equal(aspect, layout.Width / layout.Height, precision: 8);
        Assert.Equal(Math.Min(layout.Height, 691.2), layout.VisibleHeight);
    }

    [Theory]
    [InlineData(0, 420, 1)]
    [InlineData(1206, 0, 1)]
    [InlineData(1206, 420, 0)]
    [InlineData(double.NaN, 420, 1)]
    [InlineData(1206, double.PositiveInfinity, 1)]
    [InlineData(1206, 420, double.NaN)]
    public void RejectsInvalidDimensions(double width, double height, double aspect)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HeroArtworkLayout.Create(width, height, aspect));
    }
}
