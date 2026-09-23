namespace Cindara.Desktop.DesignSystem;

internal sealed record GalleryViewportProfile(double CardScale, double HeroScale, double HeroHeight)
{
    public static GalleryViewportProfile Create(double width, double height)
    {
        var reference = ViewportProfile.Create(width, height);
        var cardScale = Math.Clamp(width / 1600, 1, 1.55);
        // Short windows must still fit a full enlarged poster, its labels, and input hints.
        var availableHeroHeight = Math.Max(0, height - (280.8 * cardScale + 140));
        return new GalleryViewportProfile(
            CardScale: cardScale,
            HeroScale: Math.Clamp(reference.Scale, 1, 2),
            HeroHeight: Math.Min(Math.Clamp(height * 0.48, 420, 1080), availableHeroHeight));
    }
}
