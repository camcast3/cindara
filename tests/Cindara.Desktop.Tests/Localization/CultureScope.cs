using System.Globalization;
using Cindara.Desktop.Localization;

namespace Cindara.Desktop.Tests.Localization;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LocalizationTestGroup
{
    public const string Name = "Localization";
}

internal sealed class CultureScope : IDisposable
{
    private readonly string _locale = Loc.Culture.Name;
    private readonly CultureInfo _current = CultureInfo.CurrentCulture;
    private readonly CultureInfo _currentUi = CultureInfo.CurrentUICulture;
    private readonly CultureInfo? _default = CultureInfo.DefaultThreadCurrentCulture;
    private readonly CultureInfo? _defaultUi = CultureInfo.DefaultThreadCurrentUICulture;

    public CultureScope(string culture)
    {
        Loc.Configure(culture);
    }

    public void Dispose()
    {
        Loc.Configure(_locale);
        CultureInfo.CurrentCulture = _current;
        CultureInfo.CurrentUICulture = _currentUi;
        CultureInfo.DefaultThreadCurrentCulture = _default;
        CultureInfo.DefaultThreadCurrentUICulture = _defaultUi;
    }
}
