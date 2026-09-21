using Cindara.Desktop.Input;

namespace Cindara.Desktop.Tests.Input;

public sealed class ControllerBackNavigationTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void GalleryTakesPriorityOverFullscreen(bool galleryVisible, bool fullscreen)
    {
        Assert.Equal(ControllerBackDestination.Account,
            ControllerBackNavigation.Resolve(galleryVisible, fullscreen));
    }

    [Fact]
    public void FullscreenEscapeStillWorksOutsideGallery()
    {
        Assert.Equal(ControllerBackDestination.Windowed, ControllerBackNavigation.Resolve(false, true));
    }

    [Fact]
    public void BackOutsideGalleryAndFullscreenDoesNothing()
    {
        Assert.Equal(ControllerBackDestination.None, ControllerBackNavigation.Resolve(false, false));
    }
}
