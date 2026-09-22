using System.Xml.Linq;

namespace Cindara.Desktop.Tests.DesignSystem;

public sealed class GalleryNavigationMarkupTests
{
    private static readonly XNamespace Xaml = "https://github.com/avaloniaui";
    private static readonly XNamespace Names = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly string[] LibraryLabels = ["{loc:Tr Gallery.Shows}", "{loc:Tr Gallery.Movies}", "{loc:Tr Gallery.Anime}"];

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
    public void BothMediaCardTemplatesExposeTitleAndSubtitleToAssistiveTechnology()
    {
        var cards = LoadGallery().Descendants(Xaml + "Button")
            .Where(button => ((string?)button.Attribute("Classes"))?.Split(' ').Contains("card") is true)
            .ToArray();

        Assert.Equal(2, cards.Length);
        Assert.All(cards, card =>
        {
            Assert.Equal("{Binding Name}", (string?)card.Attribute("AutomationProperties.Name"));
            Assert.Equal("{Binding Subtitle}", (string?)card.Attribute("AutomationProperties.HelpText"));
        });
    }

    [Fact]
    public void SidebarLibraryOrderIsTvMoviesThenAnime()
    {
        var labels = LoadGallery().Descendants(Xaml + "Button")
            .Select(button => (string?)button.Attribute("AutomationProperties.Name"))
            .Where(name => LibraryLabels.Contains(name));

        Assert.Equal(LibraryLabels, labels);
    }

    [Fact]
    public void AnimeAndTvHaveSeparateVectorIconsAndLabels()
    {
        var buttons = LoadGallery().Descendants(Xaml + "Button").ToArray();
        var anime = Assert.Single(buttons,
            button => (string?)button.Attribute("AutomationProperties.Name") == "{loc:Tr Gallery.Anime}");
        var tv = Assert.Single(buttons,
            button => (string?)button.Attribute("AutomationProperties.Name") == "{loc:Tr Gallery.Shows}");
        var animePath = Assert.Single(anime.Elements(Xaml + "PathIcon")).Attribute("Data")?.Value;
        var tvPath = Assert.Single(tv.Elements(Xaml + "PathIcon")).Attribute("Data")?.Value;

        Assert.False(string.IsNullOrWhiteSpace(animePath));
        Assert.False(string.IsNullOrWhiteSpace(tvPath));
        Assert.NotEqual(animePath, tvPath);
        Assert.Equal("{loc:Tr Gallery.Anime}", (string?)anime.Attribute("ToolTip.Tip"));
        Assert.Equal("{loc:Tr Gallery.Shows}", (string?)tv.Attribute("ToolTip.Tip"));
    }

    private static XDocument LoadGallery()
    {
        using var source = typeof(GalleryNavigationMarkupTests).Assembly.GetManifestResourceStream(
            "Cindara.Tests.DesignGallery.axaml");
        Assert.NotNull(source);
        return XDocument.Load(source);
    }
}
