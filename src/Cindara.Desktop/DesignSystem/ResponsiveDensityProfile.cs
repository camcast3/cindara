namespace Cindara.Desktop.DesignSystem;

internal sealed record ResponsiveDensityProfile(
    double CardScale,
    double TypeScale,
    double GridPosterWidth,
    double GridPosterHeight,
    double GridSpacing,
    double NavigationActionSize)
{
    private const double ReferenceGridPosterWidth = 270;
    private const double ReferenceGridSpacing = 24;
    private const double ReferenceNavigationActionSize = 56;

    public static ResponsiveDensityProfile Create(double width, double height)
    {
        var gallery = GalleryViewportProfile.Create(width, height);
        return new(
            gallery.CardScale,
            gallery.HeroScale,
            ReferenceGridPosterWidth * gallery.CardScale,
            ReferenceGridPosterWidth * 1.5 * gallery.CardScale,
            ReferenceGridSpacing * gallery.CardScale,
            Math.Max(48, ReferenceNavigationActionSize * gallery.HeroScale));
    }
}
