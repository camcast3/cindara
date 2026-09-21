namespace Cindara.Desktop.DesignSystem;

internal sealed record HeroArtworkLayout(double Width, double Height, double VisibleHeight)
{
    public static HeroArtworkLayout Create(
        double heroWidth,
        double heroHeight,
        double imageAspectRatio = 16.0 / 9)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heroWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heroHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageAspectRatio);
        if (!double.IsFinite(heroWidth) || !double.IsFinite(heroHeight) || !double.IsFinite(imageAspectRatio))
        {
            throw new ArgumentOutOfRangeException(nameof(heroWidth), "Artwork dimensions must be finite.");
        }

        // Size from the viewport, not the banner height, so the fade always starts near mid-screen.
        var width = heroWidth * 0.6;
        var height = width / imageAspectRatio;
        return new HeroArtworkLayout(width, height, Math.Min(height, heroHeight));
    }
}
