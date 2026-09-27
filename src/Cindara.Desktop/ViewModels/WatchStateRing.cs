namespace Cindara.Desktop.ViewModels;

internal static class WatchStateRing
{
    public static string? FromPercentage(double? percentage)
    {
        if (percentage is not > 0 or >= 100 || !double.IsFinite(percentage.Value))
            return null;

        var angle = -Math.PI / 2 + 2 * Math.PI * percentage.Value / 100;
        var x = 10 + 8 * Math.Cos(angle);
        var y = 10 + 8 * Math.Sin(angle);
        var largeArc = percentage > 50 ? 1 : 0;
        return FormattableString.Invariant($"M 10,2 A 8,8 0 {largeArc},1 {x},{y}");
    }
}
