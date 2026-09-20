namespace Cindara.Core.Playback;

public interface IPlaybackNegotiator
{
    Task<PlaybackPlan> NegotiateAsync(
        PlaybackRequest request,
        CancellationToken cancellationToken = default);
}
