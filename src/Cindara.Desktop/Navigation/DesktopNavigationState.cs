namespace Cindara.Desktop.Navigation;

public enum PrimaryDestination
{
    Dashboard,
    Libraries,
    Favorites,
}

public enum DesktopPage
{
    Dashboard,
    LibraryLanding,
    LibraryResults,
    Favorites,
    Search,
    Settings,
}

public sealed class DesktopNavigationState
{
    private readonly Dictionary<PrimaryDestination, List<DesktopPage>> _stacks = [];

    public DesktopNavigationState() => Reset();

    public PrimaryDestination Primary { get; private set; }

    public DesktopPage Page => _stacks[Primary][^1];

    public string FocusKey => $"{Primary}:{Page}";

    public IReadOnlyList<DesktopPage> Stack(PrimaryDestination destination) =>
        _stacks[destination].AsReadOnly();

    public void Select(PrimaryDestination destination)
    {
        if (!Enum.IsDefined(destination))
            throw new ArgumentOutOfRangeException(nameof(destination));

        if (Primary == destination)
        {
            var stack = _stacks[destination];
            stack.RemoveRange(1, stack.Count - 1);
        }
        Primary = destination;
    }

    public void Push(DesktopPage page)
    {
        if (page is not (DesktopPage.LibraryResults or DesktopPage.Search or DesktopPage.Settings))
            throw new ArgumentOutOfRangeException(nameof(page), "Primary pages must be selected, not pushed.");

        var stack = _stacks[Primary];
        var existing = stack.IndexOf(page);
        if (existing >= 0)
            stack.RemoveRange(existing + 1, stack.Count - existing - 1);
        else
            stack.Add(page);
    }

    public void GoHome()
    {
        Primary = PrimaryDestination.Dashboard;
        var stack = _stacks[Primary];
        stack.RemoveRange(1, stack.Count - 1);
    }

    public bool Back()
    {
        var stack = _stacks[Primary];
        if (stack.Count > 1)
        {
            stack.RemoveAt(stack.Count - 1);
            return true;
        }

        if (Primary == PrimaryDestination.Dashboard)
            return false;

        var dashboard = _stacks[PrimaryDestination.Dashboard];
        dashboard.RemoveRange(1, dashboard.Count - 1);
        Primary = PrimaryDestination.Dashboard;
        return true;
    }

    public void Reset()
    {
        _stacks.Clear();
        _stacks.Add(PrimaryDestination.Dashboard, [DesktopPage.Dashboard]);
        _stacks.Add(PrimaryDestination.Libraries, [DesktopPage.LibraryLanding]);
        _stacks.Add(PrimaryDestination.Favorites, [DesktopPage.Favorites]);
        Primary = PrimaryDestination.Dashboard;
    }
}
