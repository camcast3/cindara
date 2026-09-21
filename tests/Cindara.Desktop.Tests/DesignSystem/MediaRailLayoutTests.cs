using Cindara.Desktop.DesignSystem;

namespace Cindara.Desktop.Tests.DesignSystem;

public sealed class MediaRailLayoutTests
{
    [Theory]
    [InlineData(720, 420, 340, 0)]
    [InlineData(1080, 518.4, 332, 229.6)]
    [InlineData(1440, 691.2, 460, 288.8)]
    [InlineData(2160, 1036.8, 460, 663.2)]
    [InlineData(1440, 691.2, 310, 438.8)]
    public void TailMakesTheFinalRowReachTheSameHeroBoundary(
        double viewportHeight,
        double heroHeight,
        double remainingContentHeight,
        double expectedSpace)
    {
        var space = MediaRailLayout.GetTrailingSpace(viewportHeight, heroHeight, remainingContentHeight);

        Assert.Equal(expectedSpace, space, precision: 8);
        foreach (var precedingRowCount in new[] { 0, 1, 3 })
        {
            var cardTop = heroHeight + 38 + precedingRowCount * 420;
            var extent = cardTop + remainingContentHeight + space;
            var maximumOffset = Math.Max(0, extent - viewportHeight);
            var offset = Math.Clamp(cardTop - heroHeight, 0, maximumOffset);

            Assert.Equal(heroHeight, cardTop - offset, precision: 8);
        }
    }

    [Fact]
    public void ResizingRecomputesTailInsteadOfAccumulatingPadding()
    {
        var original = MediaRailLayout.GetTrailingSpace(1440, 691.2, 460);
        var larger = MediaRailLayout.GetTrailingSpace(2160, 1036.8, 460);
        var restored = MediaRailLayout.GetTrailingSpace(1440, 691.2, 460);

        Assert.True(larger > original);
        Assert.Equal(original, restored);
    }

    [Fact]
    public void PinningUsesContentCoordinatesWhenNavigationChangesDuringMotion()
    {
        const double heroHeight = 691.2;
        const double viewportHeight = 1440;
        const double extentHeight = 3200;
        var rowOffsets = new[] { 0.0, 377, 864, 1351, 864, 377, 0 };

        foreach (var rowOffset in rowOffsets)
        {
            Assert.Equal(rowOffset, MediaRailLayout.GetPinnedOffset(
                heroHeight + rowOffset, heroHeight, viewportHeight, extentHeight), precision: 8);
        }
    }

    [Theory]
    [InlineData(420, 420, 720, 1400, 0)]
    [InlineData(100, 420, 720, 1400, 0)]
    [InlineData(2000, 420, 720, 1400, 680)]
    [InlineData(420, 420, 1080, 720, 0)]
    public void PinnedOffsetStaysInsideScrollRange(
        double rowTop, double heroHeight, double viewport, double extent, double expected)
    {
        Assert.Equal(expected, MediaRailLayout.GetPinnedOffset(rowTop, heroHeight, viewport, extent));
    }

    [Fact]
    public void FinalRowPinsItsHeadingAndKeepsItsCardsBelowIt()
    {
        const double viewportHeight = 1440;
        const double heroHeight = 691.2;
        const double headingAndGap = 39;
        const double cardCaptionsAndMargin = 460;
        var remaining = headingAndGap + cardCaptionsAndMargin;
        var tail = MediaRailLayout.GetTrailingSpace(viewportHeight, heroHeight, remaining);
        var rowTop = heroHeight + 1400;
        var extent = rowTop + remaining + tail;
        var offset = Math.Clamp(rowTop - heroHeight, 0, extent - viewportHeight);

        Assert.Equal(heroHeight, rowTop - offset, precision: 8);
        Assert.Equal(heroHeight + headingAndGap, rowTop + headingAndGap - offset, precision: 8);
    }

    [Theory]
    [InlineData(-1, 420, 340)]
    [InlineData(720, -1, 340)]
    [InlineData(720, 420, -1)]
    [InlineData(double.NaN, 420, 340)]
    [InlineData(720, double.PositiveInfinity, 340)]
    [InlineData(720, 420, double.NaN)]
    public void RejectsInvalidDimensions(double viewport, double hero, double remaining)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MediaRailLayout.GetTrailingSpace(viewport, hero, remaining));
    }
}
