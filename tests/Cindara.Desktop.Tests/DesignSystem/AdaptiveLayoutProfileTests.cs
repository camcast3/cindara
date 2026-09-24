using Cindara.Desktop.DesignSystem;

namespace Cindara.Desktop.Tests.DesignSystem;

public sealed class AdaptiveLayoutProfileTests
{
    [Theory]
    [InlineData(720, 480, AdaptiveViewportClass.Compact, 16, 16, 1)]
    [InlineData(800, 600, AdaptiveViewportClass.Compact, 16, 16, 1)]
    [InlineData(1280, 720, AdaptiveViewportClass.Standard, 24, 24, 1)]
    [InlineData(1280, 800, AdaptiveViewportClass.Standard, 24, 24, 1)]
    [InlineData(1366, 768, AdaptiveViewportClass.Standard, 24, 24, 1)]
    [InlineData(1920, 1080, AdaptiveViewportClass.Wide, 32, 32, 1)]
    [InlineData(3840, 2160, AdaptiveViewportClass.TenFoot, 48, 40, 1.5)]
    public void ClassifiesRepresentativeLogicalViewports(
        double width,
        double height,
        AdaptiveViewportClass expectedClass,
        double expectedMargin,
        double expectedSpacing,
        double expectedUiScale)
    {
        var profile = AdaptiveLayoutProfile.Create(width, height);

        Assert.Equal(expectedClass, profile.ViewportClass);
        Assert.Equal(expectedMargin, profile.SafeMargin);
        Assert.Equal(expectedSpacing, profile.ContentSpacing);
        Assert.Equal(expectedUiScale, profile.UiScale);
    }

    [Fact]
    public void DisplayScalingUsesTheLogicalViewportInsteadOfPhysicalPixels()
    {
        var physical4KAt200Percent = AdaptiveLayoutProfile.Create(3840 / 2d, 2160 / 2d);

        Assert.Equal(AdaptiveViewportClass.Wide, physical4KAt200Percent.ViewportClass);
        Assert.Equal(32, physical4KAt200Percent.SafeMargin);
    }
}
