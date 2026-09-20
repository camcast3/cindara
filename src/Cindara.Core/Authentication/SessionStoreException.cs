namespace Cindara.Core.Authentication;

public sealed class SessionStoreException : Exception
{
    public SessionStoreException(
        SessionStoreError error,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
    }

    public SessionStoreError Error { get; }
}
