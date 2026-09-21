using Avalonia.Media;
using Avalonia.Media.Imaging;
using Cindara.Core.Jellyfin;

namespace Cindara.Desktop.ViewModels;

internal sealed class PreviewImage(IImage source, IDisposable resource) : IDisposable
{
    private IDisposable? _resource = resource;

    public IImage Source { get; } = source;

    public static PreviewImage Decode(byte[] data) => Decode(data, stream =>
    {
        var bitmap = new Bitmap(stream);
        return new PreviewImage(bitmap, bitmap);
    });

    internal static PreviewImage Decode(byte[] data, Func<Stream, PreviewImage> decode)
    {
        using var stream = new MemoryStream(data, writable: false);
        try
        {
            return decode(stream);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or InvalidDataException)
        {
            throw new MediaPreviewException(
                MediaPreviewError.InvalidResponse,
                "Jellyfin returned artwork that could not be decoded. Try refreshing the preview.",
                exception);
        }
    }

    public void Dispose()
    {
        _resource?.Dispose();
        _resource = null;
    }
}
