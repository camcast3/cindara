namespace Cindara.Desktop.DesignSystem;

internal static class MediaRailLayout
{
    public static double GetPinnedOffset(
        double rowTop,
        double heroHeight,
        double viewportHeight,
        double extentHeight) =>
        Math.Clamp(rowTop - heroHeight, 0, Math.Max(0, extentHeight - viewportHeight));

    public static double GetTrailingSpace(
        double viewportHeight,
        double heroHeight,
        double remainingContentHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(viewportHeight);
        ArgumentOutOfRangeException.ThrowIfNegative(heroHeight);
        ArgumentOutOfRangeException.ThrowIfNegative(remainingContentHeight);
        if (!double.IsFinite(viewportHeight) || !double.IsFinite(heroHeight) || !double.IsFinite(remainingContentHeight))
        {
            throw new ArgumentOutOfRangeException(nameof(viewportHeight), "Rail dimensions must be finite.");
        }

        return Math.Max(0, viewportHeight - heroHeight - remainingContentHeight);
    }
}
