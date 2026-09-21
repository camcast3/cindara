using Avalonia.Media;
using Cindara.Desktop.ViewModels;

namespace Cindara.Desktop.Tests.ViewModels;

internal sealed class TestPreviewImageDecoder
{
    private int _calls;

    public int? FailOnCall { get; set; }

    public Exception Failure { get; set; } = new ArgumentException("Invalid encoded artwork.");

    public List<TrackedImageResource> Resources { get; } = [];

    public PreviewImage Decode(byte[] data) => PreviewImage.Decode(data, _ =>
    {
        if (++_calls == FailOnCall)
        {
            throw Failure;
        }

        var resource = new TrackedImageResource();
        Resources.Add(resource);
        return new PreviewImage(new DrawingImage(), resource);
    });
}

internal sealed class TrackedImageResource : IDisposable
{
    public int DisposeCount { get; private set; }

    public void Dispose() => DisposeCount++;
}
