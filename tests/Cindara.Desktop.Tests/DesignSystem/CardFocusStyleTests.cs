using System.Xml.Linq;

namespace Cindara.Desktop.Tests.DesignSystem;

public sealed class CardFocusStyleTests
{
    [Fact]
    public void CardUsesOneRoundedFocusIndicatorForEveryInputMethod()
    {
        using var source = typeof(CardFocusStyleTests).Assembly.GetManifestResourceStream(
            "Cindara.Tests.CardStyles.axaml");
        Assert.NotNull(source);
        var document = XDocument.Load(source);
        XNamespace xaml = "https://github.com/avaloniaui";
        var styles = document.Root!.Elements(xaml + "Style").ToArray();
        var cardStyle = Assert.Single(styles,
            style => (string?)style.Attribute("Selector") == "Button.card");
        var adorner = Assert.Single(cardStyle.Elements(xaml + "Setter"),
            setter => (string?)setter.Attribute("Property") == "FocusAdorner");
        Assert.Equal("{x:Null}", (string?)adorner.Attribute("Value"));

        var focusStyle = Assert.Single(styles,
            style => (string?)style.Attribute("Selector") == "Button.card:focus /template/ ContentPresenter");
        var border = Assert.Single(focusStyle.Elements(xaml + "Setter"),
            setter => (string?)setter.Attribute("Property") == "BorderThickness");
        Assert.Equal("4", (string?)border.Attribute("Value"));
    }
}
