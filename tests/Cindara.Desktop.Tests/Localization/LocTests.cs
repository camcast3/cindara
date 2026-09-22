using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text;
using Cindara.Desktop.Localization;

namespace Cindara.Desktop.Tests.Localization;

[Collection(LocalizationTestGroup.Name)]
public sealed class LocTests
{
    [Theory]
    [InlineData("en", "Checking server...")]
    [InlineData("en-US", "Checking server...")]
    [InlineData("fr", "Checking server...")]
    [InlineData("fr-CA", "Checking server...")]
    [InlineData("de-DE", "Checking server...")]
    public void ResourcesUseCultureParentsAndNeutralFallback(string culture, string expected)
    {
        using var scope = new CultureScope(culture);

        Assert.Equal(expected, Loc.Get("Status.CheckingServer"));
        Assert.Equal("Options", Loc.Get("Controller.Options"));
        Assert.Equal($"[{nameof(ResourcesUseCultureParentsAndNeutralFallback)}]",
            Loc.Get(nameof(ResourcesUseCultureParentsAndNeutralFallback)));
    }

    [Fact]
    public void InvalidCultureFallsBackToEnglish()
    {
        using var scope = new CultureScope("not_a_locale!");

        Assert.Equal("en", Loc.Culture.Name);
        Assert.Equal("Checking server...", Loc.Get("Status.CheckingServer"));
        Assert.False(Loc.IsRightToLeft);
    }

    [Fact]
    public void UnsupportedUiLanguagesUseEnglishAcrossAllResourceCatalogs()
    {
        using var scope = new CultureScope("fr-CA");
        Assert.Equal("Home", Loc.Get("Nav.Home"));
        Assert.Equal("Your media. Your space.", Loc.Get("Home.Tagline"));
        Assert.Equal("Checking server...", Loc.Get("Status.CheckingServer"));
    }

    [Fact]
    public void UnspecifiedCultureUsesCurrentCultureAndConfiguresNewThreads()
    {
        using var scope = new CultureScope("fr-CA");
        Loc.Configure(null);

        Assert.Equal("fr-CA", Loc.Culture.Name);
        Assert.Equal(Loc.Culture, CultureInfo.CurrentCulture);
        Assert.Equal(Loc.Culture, CultureInfo.CurrentUICulture);
        Assert.Equal(Loc.Culture, CultureInfo.DefaultThreadCurrentCulture);
        Assert.Equal(Loc.Culture, CultureInfo.DefaultThreadCurrentUICulture);
    }

    [Theory]
    [InlineData("en-US", false)]
    [InlineData("fr-FR", false)]
    [InlineData("ar-SA", false)]
    [InlineData("he-IL", false)]
    [InlineData("qps-ploc", false)]
    [InlineData("qps-plocm", true)]
    public void DirectionReflectsSelectedCulture(string culture, bool rightToLeft)
    {
        using var scope = new CultureScope(culture);

        Assert.Equal(rightToLeft, Loc.IsRightToLeft);
    }

    [Theory]
    [InlineData("qps-ploc")]
    [InlineData("qps-plocm")]
    public void PseudoLocalesExpandResourcesWithoutChangingSubstitutions(string culture)
    {
        using var scope = new CultureScope(culture);
        const string serverName = "User {0} — 日本語 & <library>";

        var template = Loc.Get("Status.Connected");
        var message = Loc.Format("Status.Connected", serverName);

        Assert.True(Loc.IsPseudoLocalized);
        Assert.Contains("{0}", template, StringComparison.Ordinal);
        Assert.Contains("Çöńńéçţéð", template, StringComparison.Ordinal);
        Assert.True(template.Length > "Connected to {0}. Sign in with your Jellyfin account.".Length);
        Assert.Contains(serverName, message, StringComparison.Ordinal);
        Assert.Equal("[unknown]", Loc.Get("unknown"));
        Assert.Equal(culture == "qps-plocm", message.StartsWith('\u2067'));
        Assert.Equal(culture == "qps-plocm", message.EndsWith('\u2069'));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PseudoFormattingPreservesEscapedBracesAlignmentAndFormatSpecifiers(bool rightToLeft)
    {
        const string template = "Amount: {0,12:N2}, {{literal}}, day {1:yyyy-MM-dd}; name {2}";
        var pseudo = Loc.PseudoLocalize(template, rightToLeft);

        Assert.Contains("{0,12:N2}", pseudo, StringComparison.Ordinal);
        Assert.Contains("{1:yyyy-MM-dd}", pseudo, StringComparison.Ordinal);
        Assert.Contains("{{", pseudo, StringComparison.Ordinal);
        Assert.Contains("}}", pseudo, StringComparison.Ordinal);
        var formatted = string.Format(CultureInfo.GetCultureInfo("en-US"), pseudo,
            1234.5, new DateTime(2026, 9, 22), "unchanged");
        Assert.Contains("1,234.50", formatted, StringComparison.Ordinal);
        Assert.Contains("2026-09-22", formatted, StringComparison.Ordinal);
        Assert.Contains("unchanged", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void ResourceCatalogsAndTheirPseudoVersionsHaveValidCompositeFormats()
    {
        using var scope = new CultureScope("en");
        var catalogs = typeof(Loc).Assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".resources", StringComparison.Ordinal)
                && name.StartsWith("Cindara.Desktop.Localization.", StringComparison.Ordinal));
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var catalog in catalogs)
        {
            using var stream = typeof(Loc).Assembly.GetManifestResourceStream(catalog)!;
            using var reader = new ResourceReader(stream);
            foreach (DictionaryEntry resource in reader)
            {
                var key = Assert.IsType<string>(resource.Key);
                var value = Assert.IsType<string>(resource.Value);
                Assert.True(keys.Add(key), $"Duplicate resource key: {key}");
                Assert.Equal(value, Loc.Get(key));
                var format = CompositeFormat.Parse(value);
                Assert.Equal(format.MinimumArgumentCount,
                    CompositeFormat.Parse(Loc.PseudoLocalize(value, false)).MinimumArgumentCount);
                Assert.Equal(format.MinimumArgumentCount,
                    CompositeFormat.Parse(Loc.PseudoLocalize(value, true)).MinimumArgumentCount);
            }
        }

        Assert.NotEmpty(keys);
    }

    [Fact]
    public void MarkupExtensionUsesTheSameResourceLookup()
    {
        using var scope = new CultureScope("fr");

        Assert.Equal(Loc.Get("Status.CheckingServer"),
            new TrExtension("Status.CheckingServer").ProvideValue(null!));
        Assert.Equal(Loc.Get("Status.CheckingServer"),
            new TrExtension { Key = "Status.CheckingServer" }.ProvideValue(null!));
    }
}
