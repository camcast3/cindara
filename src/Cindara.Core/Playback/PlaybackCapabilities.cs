namespace Cindara.Core.Playback;

public sealed record PlaybackCapabilities(
    IReadOnlySet<string> DirectPlayContainers,
    IReadOnlySet<string> VideoCodecs,
    IReadOnlySet<string> AudioCodecs,
    bool SupportsHls);
