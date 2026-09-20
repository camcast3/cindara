using Cindara.Core.Authentication;

namespace Cindara.Core.Playback;

public sealed record PlaybackRequest(
    AuthenticatedSession Session,
    string ItemId,
    PlaybackCapabilities Capabilities);
