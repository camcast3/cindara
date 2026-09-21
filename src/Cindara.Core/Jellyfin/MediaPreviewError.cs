namespace Cindara.Core.Jellyfin;

public enum MediaPreviewError
{
    AccessDenied,
    Network,
    TimedOut,
    InvalidResponse,
    UnexpectedStatus,
    InsecureConnection,
}
