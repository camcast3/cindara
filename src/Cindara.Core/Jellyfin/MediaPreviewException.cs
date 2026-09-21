namespace Cindara.Core.Jellyfin;

public sealed class MediaPreviewException : Exception
{
    public MediaPreviewException(
        MediaPreviewError error,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
    }

    public MediaPreviewError Error { get; }
}
