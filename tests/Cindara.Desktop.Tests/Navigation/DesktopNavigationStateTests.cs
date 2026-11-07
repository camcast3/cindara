using Cindara.Desktop.Navigation;

namespace Cindara.Desktop.Tests.Navigation;

public sealed class DesktopNavigationStateTests
{
    [Fact]
    public void PrimaryRootsRetainIndependentStacksAndReselectionReturnsToRoot()
    {
        var state = new DesktopNavigationState();
        state.Push(DesktopPage.Search);
        state.Select(PrimaryDestination.Libraries);
        Assert.Equal(DesktopPage.LibraryLanding, state.Page);
        state.Push(DesktopPage.LibraryResults);
        state.Push(DesktopPage.Settings);
        state.Select(PrimaryDestination.Dashboard);
        Assert.Equal(DesktopPage.Search, state.Page);
        state.Select(PrimaryDestination.Libraries);
        Assert.Equal(DesktopPage.Settings, state.Page);
        state.Select(PrimaryDestination.Libraries);
        Assert.Equal(DesktopPage.LibraryLanding, state.Page);
        Assert.Equal([DesktopPage.Dashboard, DesktopPage.Search], state.Stack(PrimaryDestination.Dashboard));
    }

    [Fact]
    public void ReopeningSecondaryRoutePopsToItWithoutDuplicatingEntries()
    {
        var state = new DesktopNavigationState();
        state.Push(DesktopPage.Search);
        state.Push(DesktopPage.Search);
        state.Push(DesktopPage.Settings);
        state.Push(DesktopPage.Search);
        Assert.Equal([DesktopPage.Dashboard, DesktopPage.Search], state.Stack(PrimaryDestination.Dashboard));
        Assert.True(state.Back());
        Assert.Equal(DesktopPage.Dashboard, state.Page);
        Assert.False(state.Back());
    }

    [Fact]
    public void BackUnwindsSecondaryRoutesThenReturnsToDashboardRoot()
    {
        var state = new DesktopNavigationState();
        state.Push(DesktopPage.Search);
        state.Select(PrimaryDestination.Libraries);
        state.Push(DesktopPage.LibraryResults);
        state.Push(DesktopPage.Settings);
        Assert.True(state.Back());
        Assert.Equal(DesktopPage.LibraryResults, state.Page);
        Assert.True(state.Back());
        Assert.Equal(DesktopPage.LibraryLanding, state.Page);
        Assert.True(state.Back());
        Assert.Equal(PrimaryDestination.Dashboard, state.Primary);
        Assert.Equal(DesktopPage.Dashboard, state.Page);
        Assert.False(state.Back());
    }

    [Fact]
    public void FocusKeysArePrimaryScopedAndResetClearsEveryStack()
    {
        var state = new DesktopNavigationState();
        state.Push(DesktopPage.Settings);
        var dashboardKey = state.FocusKey;
        state.Select(PrimaryDestination.Libraries);
        state.Push(DesktopPage.Settings);
        Assert.NotEqual(dashboardKey, state.FocusKey);
        state.Reset();
        Assert.Equal(PrimaryDestination.Dashboard, state.Primary);
        Assert.Equal([DesktopPage.Dashboard], state.Stack(PrimaryDestination.Dashboard));
        Assert.Equal([DesktopPage.LibraryLanding], state.Stack(PrimaryDestination.Libraries));
        Assert.Equal([DesktopPage.Favorites], state.Stack(PrimaryDestination.Favorites));
    }

    [Theory]
    [InlineData(DesktopPage.Dashboard)]
    [InlineData(DesktopPage.LibraryLanding)]
    [InlineData(DesktopPage.Favorites)]
    [InlineData((DesktopPage)999)]
    public void PrimaryAndUnknownPagesCannotBePushed(DesktopPage page)
    {
        var state = new DesktopNavigationState();
        Assert.Throws<ArgumentOutOfRangeException>(() => state.Push(page));
        Assert.Equal(DesktopPage.Dashboard, state.Page);
    }

    [Fact]
    public void UnknownPrimaryCannotChangeCurrentRoute()
    {
        var state = new DesktopNavigationState();
        Assert.Throws<ArgumentOutOfRangeException>(() => state.Select((PrimaryDestination)999));
        Assert.Equal(PrimaryDestination.Dashboard, state.Primary);
    }
}
