namespace Cindara.Core.Jellyfin;

public sealed class ServerConnectionException : Exception
{
    public ServerConnectionException(
        ServerConnectionError error,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
    }

    public ServerConnectionError Error { get; }
}
