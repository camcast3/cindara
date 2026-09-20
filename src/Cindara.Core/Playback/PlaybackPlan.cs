namespace Cindara.Core.Playback;

public sealed record PlaybackPlan(
    PlaybackMethod Method,
    Uri MediaUri,
    string? Container,
    string? VideoCodec,
    string? AudioCodec);
