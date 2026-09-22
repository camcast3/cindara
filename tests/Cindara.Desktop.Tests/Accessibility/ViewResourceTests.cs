using System.Xml.Linq;
using Cindara.Desktop.Localization;

namespace Cindara.Desktop.Tests.Accessibility;

public sealed class ViewResourceTests
{
    [Theory]
    [InlineData("MainWindow")]
    [InlineData("ShellView")]
    [InlineData("DesignGallery")]
    [InlineData("LibraryBrowserView")]
    public void VisibleStringsAndAccessibleLabelsUseResolvableResources(string view)
    {
        using var stream = typeof(ViewResourceTests).Assembly.GetManifestResourceStream($"Cindara.Tests.{view}.axaml");
        Assert.NotNull(stream);
        var document = XDocument.Load(stream);
        var attributes = document.Descendants().Attributes().Where(attribute =>
            attribute.Name.LocalName is "Text" or "Content" or "Title" or "PlaceholderText"
                or "AutomationProperties.Name" or "AutomationProperties.HelpText" or "AutomationProperties.ItemStatus" or "ToolTip.Tip");
        Assert.NotEmpty(attributes);
        foreach (var attribute in attributes)
        {
            Assert.StartsWith("{", attribute.Value, StringComparison.Ordinal);
            if (attribute.Value.StartsWith("{loc:Tr ", StringComparison.Ordinal))
            {
                var key = attribute.Value[8..^1];
                Assert.NotEqual($"[{key}]", Loc.Get(key));
            }
        }
    }
}
