namespace Cindara.Desktop.DesignSystem;

public enum AdaptiveViewportClass
{
    Compact,
    Standard,
    Wide,
    TenFoot,
}

public sealed record AdaptiveLayoutProfile(
    AdaptiveViewportClass ViewportClass,
    double SafeMargin,
    double ContentSpacing,
    double UiScale)
{
    public const double CompactBreakpoint = 960;
    public const double WideBreakpoint = 1440;
    public const double TenFootBreakpoint = 2560;

    public static AdaptiveLayoutProfile Create(double width, double height)
    {
        ViewportProfile.Create(width, height);
        var shortestSide = Math.Min(width, height);
        if (width < CompactBreakpoint || shortestSide < 600)
        {
            return new(AdaptiveViewportClass.Compact, 16, 16, 1);
        }

        if (width < WideBreakpoint)
        {
            return new(AdaptiveViewportClass.Standard, 24, 24, 1);
        }

        if (width < TenFootBreakpoint)
        {
            return new(AdaptiveViewportClass.Wide, 32, 32, 1);
        }

        return new(AdaptiveViewportClass.TenFoot, 48, 40, 1.5);
    }
}
