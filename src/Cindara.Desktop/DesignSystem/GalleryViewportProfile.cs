namespace Cindara.Desktop.DesignSystem;

internal sealed record GalleryViewportProfile(
    double CardScale,
    double HeroScale,
    double HeroHeight,
    double HeroContentWidth,
    double NavigationWidth)
{
    private const double ReferenceNavigationWidth = 96;
    private const double ReferenceHeroHeight = 518.4;

    public static GalleryViewportProfile Create(double width, double height)
    {
        ViewportProfile.Create(width, height);
        var cardScale = Math.Clamp(width / 1600, 1, 1.55);
        // Short windows must still fit a full enlarged poster, its labels, and input hints.
        var availableHeroHeight = Math.Max(0, height - (280.8 * cardScale + 140));
        var heroHeight = Math.Min(Math.Clamp(height * 0.48, 420, 1080), availableHeroHeight);
        var heroScale = Math.Clamp(heroHeight / ReferenceHeroHeight, 0.7, 2);
        var navigationWidth = Math.Max(72, ReferenceNavigationWidth * heroScale);
        return new GalleryViewportProfile(
            CardScale: cardScale,
            HeroScale: heroScale,
            HeroHeight: heroHeight,
            HeroContentWidth: Math.Min(
                880 * heroScale,
                Math.Max(320, (width - navigationWidth) * 0.48)),
            NavigationWidth: navigationWidth);
    }
}
