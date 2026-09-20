namespace Cindara.Desktop.DesignSystem;

public sealed record ViewportProfile(double Scale, double SafeArea, double MinimumFocusTarget)
{
    private const double ReferenceWidth = 1920;
    private const double ReferenceHeight = 1080;
    private const double ReferenceSafeArea = 48;
    private const double ReferenceFocusTarget = 48;

    public static ViewportProfile Create(double width, double height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var scale = Math.Min(width / ReferenceWidth, height / ReferenceHeight);
        return new ViewportProfile(
            Scale: scale,
            SafeArea: ReferenceSafeArea * scale,
            MinimumFocusTarget: ReferenceFocusTarget * scale);
    }
}
