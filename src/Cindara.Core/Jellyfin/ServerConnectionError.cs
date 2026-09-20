namespace Cindara.Core.Jellyfin;

public enum ServerConnectionError
{
    InvalidAddress,
    Unreachable,
    TimedOut,
    AccessDenied,
    UnexpectedStatus,
    InvalidResponse,
}
