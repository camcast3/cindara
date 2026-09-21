namespace Cindara.Desktop.DesignSystem;

internal sealed record GalleryViewportProfile(double CardScale, double HeroScale, double HeroHeight)
{
    public static GalleryViewportProfile Create(double width, double height)
    {
        var reference = ViewportProfile.Create(width, height);
        return new GalleryViewportProfile(
            CardScale: Math.Clamp(width / 1600, 1, 1.55),
            HeroScale: Math.Clamp(reference.Scale, 1, 2),
            HeroHeight: Math.Clamp(height * 0.48, 420, 1080));
    }
}
