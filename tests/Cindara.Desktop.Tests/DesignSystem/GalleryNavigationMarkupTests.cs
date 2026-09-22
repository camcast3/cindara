using System.Xml.Linq;

namespace Cindara.Desktop.Tests.DesignSystem;

public sealed class GalleryNavigationMarkupTests
{
    private static readonly XNamespace Xaml = "https://github.com/avaloniaui";
    private static readonly XNamespace Names = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void HomeHasOneNavigationActionAndNoSpeculativeTopTabs()
    {
        var gallery = LoadGallery();
        Assert.DoesNotContain(gallery.Descendants(),
            element => (string?)element.Attribute(Names + "Name") == "TopNavigationPanel");
        var home = Assert.Single(gallery.Descendants(Xaml + "Button"),
            button => (string?)button.Attribute("AutomationProperties.Name") == "{loc:Tr Nav.Home}");
        Assert.Equal("SidebarHomeButton", (string?)home.Attribute(Names + "Name"));
        Assert.Equal("OnHomeClicked", (string?)home.Attribute("Click"));
    }

    [Fact]
    public void AllMediaCardTemplatesExposeTitleAndSubtitleToAssistiveTechnology()
    {
        var cards = LoadGallery().Descendants(Xaml + "Button")
            .Where(button => (string?)button.Attribute("Click") == "OnCardClicked")
            .ToArray();

        Assert.Equal(3, cards.Length);
        Assert.All(cards, card =>
        {
            Assert.Equal("{Binding Name}", (string?)card.Attribute("AutomationProperties.Name"));
            Assert.Equal("{Binding Subtitle}", (string?)card.Attribute("AutomationProperties.HelpText"));
        });
    }

    [Fact]
    public void LibrariesUseRealServerEntriesInsteadOfSpeculativeCategoryShortcuts()
    {
        var gallery = LoadGallery();
        var libraries = Assert.Single(gallery.Descendants(Xaml + "ItemsControl"),
            element => (string?)element.Attribute("ItemsSource") == "{Binding Libraries}");
        Assert.Equal("OnLibraryClicked", (string?)Assert.Single(libraries.Descendants(Xaml + "Button")).Attribute("Click"));
        Assert.Contains(gallery.Descendants(Xaml + "Button"),
            element => (string?)element.Attribute("Click") == "OnLibrariesClicked");
        Assert.DoesNotContain(gallery.Descendants(Xaml + "Button"),
            element => (string?)element.Attribute("AutomationProperties.HelpText") == "{loc:Tr Gallery.PreviewOnly}");
    }

    private static XDocument LoadGallery()
    {
        using var source = typeof(GalleryNavigationMarkupTests).Assembly.GetManifestResourceStream(
            "Cindara.Tests.DesignGallery.axaml");
        Assert.NotNull(source);
        return XDocument.Load(source);
    }
}
