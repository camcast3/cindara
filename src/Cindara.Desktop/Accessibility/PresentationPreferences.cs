namespace Cindara.Desktop.Accessibility;

public sealed record PresentationPreferences
{
    private readonly double _textScale;

    public PresentationPreferences(double TextScale = 1, bool HighContrast = false, bool ReducedMotion = false)
    {
        this.TextScale = TextScale;
        this.HighContrast = HighContrast;
        this.ReducedMotion = ReducedMotion;
    }

    public double TextScale
    {
        get => _textScale;
        init
        {
            if (value is not (1 or 1.25 or 1.5))
            {
                throw new ArgumentOutOfRangeException(nameof(TextScale), value, "Supported text scales are 1, 1.25, and 1.5.");
            }

            _textScale = value;
        }
    }

    public bool HighContrast { get; init; }
    public bool ReducedMotion { get; init; }
}
