namespace Cindara.Desktop.DesignSystem;

public sealed record ViewportProfile(double Scale, double SafeArea, double MinimumFocusTarget)
{
    private const double ReferenceWidth = 1920;
    private const double ReferenceHeight = 1080;
    private const double ReferenceSafeArea = 48;
    private const double ReferenceFocusTarget = 48;

    public static ViewportProfile Create(double width, double height)
    {
        if (!double.IsFinite(width) || width <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                width,
                "Viewport width must be finite and greater than zero.");
        }

        if (!double.IsFinite(height) || height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(height),
                height,
                "Viewport height must be finite and greater than zero.");
        }

        var scale = Math.Min(width / ReferenceWidth, height / ReferenceHeight);
        return new ViewportProfile(
            Scale: scale,
            SafeArea: ReferenceSafeArea * scale,
            MinimumFocusTarget: ReferenceFocusTarget * scale);
    }
}
