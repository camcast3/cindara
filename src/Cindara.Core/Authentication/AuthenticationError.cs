namespace Cindara.Core.Authentication;

public enum AuthenticationError
{
    InvalidCredentials,
    InsecureConnection,
    RevokedSession,
    SecureStorageUnavailable,
    Network,
    InvalidResponse,
    UnexpectedStatus,
}
