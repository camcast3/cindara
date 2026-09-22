using System.Globalization;
using System.Resources;
using System.Text;

namespace Cindara.Desktop.Localization;

/// <summary>Resource lookup configured once during application startup.</summary>
public static class Loc
{
    private static readonly ResourceManager Strings = new(
        "Cindara.Desktop.Localization.Strings", typeof(Loc).Assembly);
    private static readonly ResourceManager Views = new(
        "Cindara.Desktop.Localization.Views", typeof(Loc).Assembly);
    private static CultureInfo _culture = CultureInfo.CurrentCulture;
    private static bool _pseudo;
    private static bool _pseudoRightToLeft;

    public static CultureInfo Culture => _culture;

    public static bool IsPseudoLocalized => _pseudo;

    public static bool IsRightToLeft => _pseudoRightToLeft || Culture.TextInfo.IsRightToLeft;

    /// <summary>Selects the startup culture; existing views are not retranslated.</summary>
    public static void Configure(string? cultureName)
    {
        _pseudo = string.Equals(cultureName, "qps-ploc", StringComparison.OrdinalIgnoreCase)
            || string.Equals(cultureName, "qps-plocm", StringComparison.OrdinalIgnoreCase);
        _pseudoRightToLeft = string.Equals(cultureName, "qps-plocm", StringComparison.OrdinalIgnoreCase);
        try
        {
            _culture = string.IsNullOrWhiteSpace(cultureName)
                ? CultureInfo.CurrentCulture
                : CultureInfo.GetCultureInfo(cultureName);
        }
        catch (CultureNotFoundException)
        {
            _culture = CultureInfo.GetCultureInfo("en");
        }

        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _culture;
        CultureInfo.DefaultThreadCurrentCulture = _culture;
        CultureInfo.DefaultThreadCurrentUICulture = _culture;
    }

    public static string Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var culture = _pseudo ? CultureInfo.InvariantCulture : Culture;
        var value = Strings.GetString(key, culture) ?? Views.GetString(key, culture);
        if (value is null)
        {
            return $"[{key}]";
        }

        return _pseudo ? PseudoLocalize(value, _pseudoRightToLeft) : value;
    }

    public static string Format(string key, params object[] args) =>
        string.Format(Culture, Get(key), args);

    internal static string PseudoLocalize(string value, bool rightToLeft)
    {
        const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        const string accents = "ÅƁÇÐÉƑĜĤÎĴĶĻḾŃÖÞQŔŠŢÛṼŴẊÝŽåƀçðéƒĝĥîĵķļḿńöþqŕšţûṽŵẋýž";
        var result = new StringBuilder(value.Length * 2);
        result.Append(rightToLeft ? "\u2067[!! " : "[!! ");
        var literalLetters = 0;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character is '{' or '}' && index + 1 < value.Length && value[index + 1] == character)
            {
                result.Append(character, 2);
                index++;
                continue;
            }

            // Composite-format placeholders, including alignment and format specifiers, are opaque.
            if (character == '{')
            {
                var end = value.IndexOf('}', index + 1);
                if (end >= 0)
                {
                    result.Append(value, index, end - index + 1);
                    index = end;
                    continue;
                }
            }

            var letter = letters.IndexOf(character);
            result.Append(letter >= 0 ? accents[letter] : character);
            if (letter >= 0)
            {
                literalLetters++;
            }
        }

        result.Append('~', (literalLetters + 2) / 3);
        result.Append(rightToLeft ? " !!]\u2069" : " !!]");
        return result.ToString();
    }
}
