namespace Cindara.Core.Authentication;

public sealed class AuthenticationException : Exception
{
    public AuthenticationException(
        AuthenticationError error,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
    }

    public AuthenticationError Error { get; }
}
