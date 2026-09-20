using Cindara.Desktop.DesignSystem;

namespace Cindara.Desktop.Tests.DesignSystem;

public sealed class ViewportProfileTests
{
    [Theory]
    [InlineData(1920, 1080, 1, 48, 48)]
    [InlineData(3840, 2160, 2, 96, 96)]
    [InlineData(1280, 720, 0.6666666666666666, 32, 32)]
    public void CreateScalesFromThe1080PReference(
        double width,
        double height,
        double expectedScale,
        double expectedSafeArea,
        double expectedFocusTarget)
    {
        var profile = ViewportProfile.Create(width, height);

        Assert.Equal(expectedScale, profile.Scale, precision: 10);
        Assert.Equal(expectedSafeArea, profile.SafeArea, precision: 10);
        Assert.Equal(expectedFocusTarget, profile.MinimumFocusTarget, precision: 10);
    }

    [Theory]
    [InlineData(0, 1080)]
    [InlineData(1920, 0)]
    [InlineData(-1, 1080)]
    [InlineData(double.NaN, 1080)]
    [InlineData(1920, double.NaN)]
    [InlineData(double.PositiveInfinity, 1080)]
    [InlineData(1920, double.NegativeInfinity)]
    public void CreateRejectsInvalidDimensions(double width, double height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ViewportProfile.Create(width, height));
    }
}
