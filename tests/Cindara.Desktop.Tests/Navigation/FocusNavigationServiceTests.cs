using Avalonia.Controls;
using Avalonia.Input;
using Cindara.Desktop.Navigation;

namespace Cindara.Desktop.Tests.Navigation;

public sealed class FocusNavigationServiceTests
{
    [Fact]
    public Task DirectionalMovementUsesPhysicalDirectionInMirroredScopes() => TestAppBuilder.Run(() =>
    {
        var first = ButtonAt(0, 0);
        var second = ButtonAt(0, 0);
        var scope = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            FlowDirection = Avalonia.Media.FlowDirection.RightToLeft,
            Children = { first, second },
        };
        var window = Show(scope);
        try
        {
            var navigation = new FocusNavigationService(window);
            navigation.SetScope(scope, first);
            navigation.Move(NavigationDirection.Left);
            Assert.Same(second, window.FocusManager!.GetFocusedElement());
            navigation.Move(NavigationDirection.Right);
            Assert.Same(first, window.FocusManager.GetFocusedElement());
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task SpatialMovesPreferAlignedTargetsAndUseStableOrder() => TestAppBuilder.Run(() =>
    {
        var origin = ButtonAt(0, 0);
        var diagonal = ButtonAt(70, 70);
        var aligned = ButtonAt(200, 0);
        var disabled = ButtonAt(80, 0);
        disabled.IsEnabled = false;
        var hidden = ButtonAt(100, 0);
        hidden.IsVisible = false;
        var scope = new Canvas { Children = { origin, diagonal, aligned, disabled, hidden } };
        var window = Show(scope);
        try
        {
            var navigation = new FocusNavigationService(window);
            navigation.SetScope(scope, origin);
            Assert.Same(origin, window.FocusManager!.GetFocusedElement());
            navigation.Move(NavigationDirection.Right);
            Assert.Same(aligned, window.FocusManager.GetFocusedElement());
            navigation.Move(NavigationDirection.Down);
            Assert.Same(diagonal, window.FocusManager.GetFocusedElement());
            navigation.Move(NavigationDirection.Down);
            Assert.Same(diagonal, window.FocusManager.GetFocusedElement());
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task RemovedOrDisabledFocusRecoversToNearestSurvivor() => TestAppBuilder.Run(() =>
    {
        var first = ButtonAt(0, 0);
        var middle = ButtonAt(0, 80);
        var last = ButtonAt(0, 180);
        var scope = new Canvas { Children = { first, middle, last } };
        var window = Show(scope);
        try
        {
            var navigation = new FocusNavigationService(window);
            navigation.SetScope(scope, middle);
            scope.Children.Remove(middle);
            navigation.EnsureFocus();
            Assert.Same(first, window.FocusManager!.GetFocusedElement());
            first.IsEnabled = false;
            navigation.EnsureFocus();
            Assert.Same(last, window.FocusManager.GetFocusedElement());
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task ModalTrapsDirectionalAndTabNavigationThenRestoresScreenMemory() => TestAppBuilder.Run(() =>
    {
        var first = ButtonAt(0, 0);
        var second = ButtonAt(0, 80);
        var page = new Canvas { Children = { first, second } };
        var cancel = ButtonAt(300, 0);
        var confirm = ButtonAt(300, 80);
        var modal = new Canvas { Children = { cancel, confirm } };
        var window = Show(new Grid { Children = { page, modal } });
        try
        {
            var navigation = new FocusNavigationService(window);
            navigation.SetScope(page, second);
            navigation.SetScope(modal, cancel);
            navigation.Move(NavigationDirection.Left);
            Assert.Same(cancel, window.FocusManager!.GetFocusedElement());
            navigation.Move(NavigationDirection.Previous);
            Assert.Same(confirm, window.FocusManager.GetFocusedElement());
            navigation.Move(NavigationDirection.Next);
            Assert.Same(cancel, window.FocusManager.GetFocusedElement());
            navigation.SetScope(page, first);
            Assert.Same(second, window.FocusManager.GetFocusedElement());
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task ReplacedViewRestoresNearestFocusAndEmptyViewCanRecoverAfterLoading() => TestAppBuilder.Run(() =>
    {
        var scope = new Canvas();
        var window = Show(scope);
        try
        {
            var navigation = new FocusNavigationService(window);
            navigation.SetScope(scope);
            Assert.False(navigation.Move(NavigationDirection.Down));
            var button = ButtonAt(0, 0);
            scope.Children.Add(button);
            window.UpdateLayout();
            navigation.EnsureFocus();
            Assert.Same(button, window.FocusManager!.GetFocusedElement());
            navigation.Reset();
            var replacement = ButtonAt(0, 80);
            scope.Children.Add(replacement);
            window.UpdateLayout();
            navigation.SetScope(scope, replacement);
            Assert.Same(replacement, window.FocusManager.GetFocusedElement());
        }
        finally
        {
            window.Close();
        }
    });

    private static Button ButtonAt(double x, double y)
    {
        var button = new Button { Content = "Action", Width = 64, Height = 48 };
        Canvas.SetLeft(button, x);
        Canvas.SetTop(button, y);
        return button;
    }

    private static Window Show(Control content)
    {
        var window = new Window { Content = content, Width = 800, Height = 600 };
        window.Show();
        window.UpdateLayout();
        return window;
    }
}
