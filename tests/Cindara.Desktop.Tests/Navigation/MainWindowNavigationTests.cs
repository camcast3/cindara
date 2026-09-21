using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cindara.Core.Authentication;
using Cindara.Core.Jellyfin;
using Cindara.Core.Models;
using Cindara.Desktop.Input;
using Cindara.Desktop.ViewModels;
using Cindara.Desktop.Views;

namespace Cindara.Desktop.Tests.Navigation;

public sealed class MainWindowNavigationTests
{
    private static readonly string[] Destinations = ["Libraries", "Search", "Downloads", "Settings"];

    [Fact]
    public Task DestinationRevisitRestoresContentInsteadOfAnotherScreensRailButton() => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.SignIn();
        fixture.Input.Press(ControllerAction.NavigateDown);
        var homeAction = Focused(fixture.Window);
        fixture.Input.Press(ControllerAction.Back);
        for (var i = 0; i < 4; i++)
        {
            fixture.Input.Press(ControllerAction.NavigateDown);
        }

        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();
        fixture.Input.Press(ControllerAction.NavigateRight);
        Assert.Equal("SwitchAccountButton", Focused(fixture.Window).Name);
        fixture.Input.Press(ControllerAction.Back);
        for (var i = 0; i < 4; i++)
        {
            fixture.Input.Press(ControllerAction.NavigateUp);
        }

        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();
        Assert.Same(homeAction, Focused(fixture.Window));
        fixture.Input.Press(ControllerAction.Back);
        for (var i = 0; i < 4; i++)
        {
            fixture.Input.Press(ControllerAction.NavigateDown);
        }

        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();
        Assert.Equal("SwitchAccountButton", Focused(fixture.Window).Name);
    });

    [Fact]
    public Task ExpandedRailNeverCoversContentFocusAndRemembersItsOrigin() => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.SignIn();
        var shell = fixture.Window.FindControl<ShellView>("Shell")!;
        var rail = shell.FindControl<Border>("NavigationRail")!;
        fixture.Input.Press(ControllerAction.NavigateDown);
        var launcher = Focused(fixture.Window);
        fixture.Input.Press(ControllerAction.Back);
        fixture.Flush();
        Assert.Equal(280, rail.Width);
        var point = rail.TranslatePoint(new Point(16, 16), fixture.Window)!.Value;
        fixture.Window.MouseMove(point, RawInputModifiers.None);
        fixture.Input.Press(ControllerAction.NavigateRight);
        fixture.Flush();
        Assert.Same(launcher, Focused(fixture.Window));
        var railRight = rail.TranslatePoint(new Point(rail.Bounds.Width, 0), fixture.Window)!.Value.X;
        var contentLeft = launcher.TranslatePoint(default, fixture.Window)!.Value.X;
        Assert.True(contentLeft > railRight);
        fixture.Window.MouseMove(new Point(0, 0), RawInputModifiers.None);
        fixture.Flush();
        Assert.Equal(88, rail.Width);
    });

    [Fact]
    public Task GalleryBackRestoresItsLauncherWithoutLeavingFullscreen() => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.SignIn();
        fixture.Input.Press(ControllerAction.NavigateDown);
        Assert.Equal("PreviewButton", Focused(fixture.Window).Name);
        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();
        Assert.True(fixture.Model.IsDesignGalleryVisible);
        Assert.Equal("HomeTabButton", Focused(fixture.Window).Name);
        fixture.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        fixture.Flush();
        Assert.False(fixture.Model.IsDesignGalleryVisible);
        Assert.Equal("PreviewButton", Focused(fixture.Window).Name);
        Assert.Equal(WindowState.FullScreen, fixture.Window.WindowState);
    });

    [Fact]
    public Task KeyboardArrowsAndMouseActivateTheSameShellDestinations() => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.SignIn();
        fixture.Window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
        Assert.Equal("HomeNavigation", Focused(fixture.Window).Name);
        fixture.Window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        fixture.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        fixture.Window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        fixture.Flush();
        var shell = fixture.Window.FindControl<ShellView>("Shell")!;
        Assert.Equal("Libraries", shell.Destination);
        var settings = shell.FindControl<Button>("SettingsNavigation")!;
        var point = settings.TranslatePoint(new Point(settings.Bounds.Width / 2, settings.Bounds.Height / 2), fixture.Window)!.Value;
        fixture.Window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        fixture.Window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        fixture.Flush();
        Assert.Equal("Settings", shell.Destination);
    });

    [Fact]
    public Task ControllerLayoutPromptsFollowTheActiveDeviceWithoutMovingFocus() => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.SignIn();
        var focused = Focused(fixture.Window);
        fixture.Input.Switch(ControllerLayout.Nintendo);
        Assert.Contains("[B]: select", fixture.Model.ControllerStatus, StringComparison.Ordinal);
        Assert.Contains("[A]: back", fixture.Model.ControllerStatus, StringComparison.Ordinal);
        fixture.Input.Switch(ControllerLayout.PlayStation);
        Assert.Contains("[Cross]: select", fixture.Model.ControllerStatus, StringComparison.Ordinal);
        Assert.Contains("[Circle]: back", fixture.Model.ControllerStatus, StringComparison.Ordinal);
        Assert.Same(focused, Focused(fixture.Window));
    });

    [Fact]
    public Task ModalInitialFocusDoesNotReuseThePreviouslyVisitedExitAction() => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.Click("WindowOptionsButton");
        fixture.Window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, null);
        Assert.Equal("Exit Cindara", Assert.IsType<Button>(Focused(fixture.Window)).Content);
        fixture.Input.Press(ControllerAction.Back);
        fixture.Click("WindowOptionsButton");
        Assert.Equal("Return to Cindara", Assert.IsType<Button>(Focused(fixture.Window)).Content);
    });

    [Fact]
    public Task SavedAccountPickerTrapsFocusAndRestoresSelection() => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        var window = fixture.Window;
        Assert.Equal("SavedAccountButton", Focused(window).Name);
        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();
        Assert.True(window.FindControl<Border>("ModalOverlay")!.IsVisible);
        fixture.Input.Press(ControllerAction.NavigateDown);
        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();
        Assert.Equal("second", fixture.Model.SelectedSavedSession!.UserId);
        Assert.False(window.FindControl<Border>("ModalOverlay")!.IsVisible);
        Assert.Equal("SavedAccountButton", Focused(window).Name);
        fixture.Input.Press(ControllerAction.NavigateDown);
        Assert.True(Equals("Continue", Assert.IsType<Button>(Focused(window)).Content), Describe(window));
        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();
        Assert.True(fixture.Model.IsAuthenticatedVisible);
        Assert.Equal("HomeHeader", Focused(window).Name);
    });

    [Fact]
    public Task ShellDestinationsAndSettingsAreControllerReachable() => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.SignIn();
        var shell = fixture.Window.FindControl<ShellView>("Shell")!;
        fixture.Input.Press(ControllerAction.Back);
        Assert.Equal("HomeNavigation", Focused(fixture.Window).Name);
        foreach (var destination in Destinations)
        {
            fixture.Input.Press(ControllerAction.NavigateDown);
            fixture.Input.Press(ControllerAction.Accept);
            fixture.Flush();
            Assert.Equal(destination, shell.Destination);
            Assert.Equal(destination == "Settings" ? "AccountCategory" : "ReturnHomeButton",
                Focused(fixture.Window).Name);
            if (destination != "Settings")
            {
                fixture.Input.Press(ControllerAction.Back);
            }
        }

        fixture.Input.Press(ControllerAction.NavigateRight);
        Assert.Equal("SwitchAccountButton", Focused(fixture.Window).Name);
        fixture.Input.Press(ControllerAction.NavigateLeft);
        Assert.Equal("AccountCategory", Focused(fixture.Window).Name);
        fixture.Input.Press(ControllerAction.NavigateDown);
        fixture.Input.Press(ControllerAction.NavigateRight);
        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();
        Assert.True(fixture.Window.FindControl<Border>("ModalOverlay")!.IsVisible);
    });

    [Fact]
    public Task WindowDialogTrapsTabAndBackAndKeepsFullscreenUntilExplicitChoice() => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.SignIn();
        fixture.Click("WindowOptionsButton");
        var modal = fixture.Window.FindControl<StackPanel>("ModalActions")!;
        Assert.Equal("Return to Cindara", Assert.IsType<Button>(Focused(fixture.Window)).Content);
        fixture.Window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, null);
        Assert.Equal("Exit Cindara", Assert.IsType<Button>(Focused(fixture.Window)).Content);
        fixture.Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        Assert.Equal("Return to Cindara", Assert.IsType<Button>(Focused(fixture.Window)).Content);
        fixture.Input.Press(ControllerAction.NavigateLeft);
        Assert.Contains(Focused(fixture.Window), modal.GetVisualDescendants());
        fixture.Input.Press(ControllerAction.Menu);
        Assert.Equal(WindowState.FullScreen, fixture.Window.WindowState);
        fixture.Input.Press(ControllerAction.Back);
        fixture.Flush();
        Assert.False(fixture.Window.FindControl<Border>("ModalOverlay")!.IsVisible);
        Assert.Equal("WindowOptionsButton", Focused(fixture.Window).Name);
        fixture.Click("WindowOptionsButton");
        fixture.Input.Press(ControllerAction.NavigateDown);
        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();
        Assert.Equal(WindowState.Normal, fixture.Window.WindowState);
        fixture.Window.KeyPress(Key.F11, RawInputModifiers.None, PhysicalKey.F11, null);
        Assert.Equal(WindowState.FullScreen, fixture.Window.WindowState);
    });

    [Fact]
    public Task ControllerKeyboardCommitsOrCancelsWithoutChangingFocusOwner() => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture(savedAccounts: false);
        var field = fixture.Window.FindControl<TextBox>("ServerAddressTextBox")!;
        Assert.Same(field, Focused(fixture.Window));
        field.Text = "https://";
        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();
        fixture.ClickContent("q");
        fixture.ClickContent("Shift");
        fixture.ClickContent("A");
        fixture.ClickContent("Backspace");
        fixture.ClickContent("Done");
        Assert.Equal("https://q", field.Text);
        Assert.Same(field, Focused(fixture.Window));
        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();
        fixture.ClickContent("Clear");
        fixture.Input.Press(ControllerAction.Back);
        Assert.Equal("https://q", field.Text);
        Assert.Empty(fixture.Window.FindControl<StackPanel>("ModalActions")!.Children);
    });

    [Fact]
    public Task BackgroundActionsDoNotActivateAndReconnectDoesNotMoveFocus() => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.SignIn();
        var focused = Focused(fixture.Window);
        fixture.Input.Connect(false);
        fixture.Input.Connect(true);
        Assert.Same(focused, Focused(fixture.Window));
        fixture.Window.DeactivateForTest();
        fixture.Flush();
        Assert.False(fixture.Window.IsActive);
        fixture.Input.Press(ControllerAction.Back);
        fixture.Input.Press(ControllerAction.Menu);
        fixture.Input.Press(ControllerAction.Accept);
        Assert.False(fixture.Window.FindControl<Border>("ModalOverlay")!.IsVisible);
        Assert.Equal(WindowState.FullScreen, fixture.Window.WindowState);
        Assert.False(fixture.Input.ApplicationActive);
    });

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(3840, 2160)]
    public Task ShellAndModalRemainInsideViewportAtTvSizes(int width, int height) => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.SignIn();
        fixture.Window.WindowState = WindowState.Normal;
        fixture.Window.Width = width;
        fixture.Window.Height = height;
        fixture.Flush();
        var shell = fixture.Window.FindControl<ShellView>("Shell")!;
        AssertInsideWindow(fixture.Window, shell.FindControl<Button>("HomeHeader")!);
        AssertInsideWindow(fixture.Window, shell.FindControl<Button>("SettingsNavigation")!);
        AssertInsideWindow(fixture.Window, fixture.Window.FindControl<Button>("WindowOptionsButton")!);
        Assert.Equal("HomeHeader", Focused(fixture.Window).Name);
        Capture(fixture.Window, $"shell-{width}x{height}");
        fixture.Input.Press(ControllerAction.Back);
        fixture.Flush();
        Capture(fixture.Window, $"rail-{width}x{height}");
        fixture.Input.Press(ControllerAction.NavigateRight);
        fixture.Click("WindowOptionsButton");
        AssertInsideWindow(fixture.Window, Focused(fixture.Window));
        Capture(fixture.Window, $"modal-{width}x{height}");
        fixture.Input.Press(ControllerAction.Back);
        shell.Navigate("Settings");
        fixture.Flush();
        Capture(fixture.Window, $"settings-{width}x{height}");
        fixture.Model.BackToSessionsCommand.Execute(null);
        fixture.Flush();
        Capture(fixture.Window, $"accounts-{width}x{height}");
        fixture.Model.AddServerCommand.Execute(null);
        fixture.Flush();
        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();
        Assert.True(fixture.Window.FindControl<Border>("ModalOverlay")!.IsVisible);
        AssertInsideWindow(fixture.Window, Focused(fixture.Window));
        Capture(fixture.Window, $"keyboard-{width}x{height}");
    });

    [Fact]
    public Task DpiMigrationKeepsLogicalLayoutAndFocusWithoutAResizeWorkaround() => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.SignIn();
        fixture.Window.WindowState = WindowState.Normal;
        fixture.Window.Width = 1920;
        fixture.Window.Height = 1080;
        fixture.Flush();
        var home = Focused(fixture.Window);
        var bounds = home.Bounds;
        fixture.Window.SetRenderScaling(2);
        fixture.Flush();
        Assert.Equal(2, fixture.Window.RenderScaling);
        Assert.InRange(Math.Abs(bounds.Width - home.Bounds.Width), 0, 1);
        Assert.Equal(bounds.Height, home.Bounds.Height);
        Assert.Same(home, Focused(fixture.Window));
        AssertInsideWindow(fixture.Window, home);
        Capture(fixture.Window, "shell-4k-200percent");
        fixture.Window.SetRenderScaling(1);
        fixture.Flush();
        Assert.Same(home, Focused(fixture.Window));
    });

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("CINDARA_NAV_CAPTURE");
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(Path.Combine(directory, $"{name}.png"), PngBitmapEncoderOptions.Default);
        }
    }

    private static void AssertInsideWindow(Window window, Control control)
    {
        Assert.True(control.IsEffectivelyVisible);
        var start = control.TranslatePoint(default, window)!.Value;
        var end = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), window)!.Value;
        Assert.InRange(start.X, 0, window.ClientSize.Width);
        Assert.InRange(start.Y, 0, window.ClientSize.Height);
        Assert.InRange(end.X, start.X + 1, window.ClientSize.Width);
        Assert.InRange(end.Y, start.Y + 1, window.ClientSize.Height);
    }

    private static Control Focused(Window window) => Assert.IsAssignableFrom<Control>(window.FocusManager!.GetFocusedElement());

    private sealed class ShellFixture : IDisposable
    {
        public ShellFixture(bool savedAccounts = true)
        {
            Model = new MainViewModel(new ServerClient(), new AuthenticationService(savedAccounts), new PreviewClient());
            Window = new TestMainWindow(Input) { DataContext = Model };
            Window.Show();
            Window.Activate();
            Flush();
        }

        public FakeController Input { get; } = new();
        public MainViewModel Model { get; }
        public TestMainWindow Window { get; }

        public void SignIn()
        {
            Model.UseSavedSessionCommand.Execute(null);
            Flush();
            Assert.True(Focused(Window).Name == "HomeHeader", Describe(Window));
        }

        public void Click(string name)
        {
            var button = Window.FindControl<Button>(name)!;
            button.Focus();
            ((IInvokeProvider)new ButtonAutomationPeer(button)).Invoke();
            Flush();
        }

        public void ClickContent(string text)
        {
            var button = Window.FindControl<StackPanel>("ModalActions")!.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, text));
            button.Focus();
            ((IInvokeProvider)new ButtonAutomationPeer(button)).Invoke();
            Flush();
        }

        public void Flush()
        {
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
        }

        public void Dispose()
        {
            Window.Close();
            Model.Dispose();
        }
    }

    private sealed class TestMainWindow(IControllerInputSource input) : MainWindow(input)
    {
        // The headless backend does not synthesize OS deactivation when another window opens.
        public void DeactivateForTest() => typeof(WindowBase)
            .GetMethod("HandleDeactivated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(this, null);
    }

    private static string Describe(Window window) => $"Focused: {Focused(window).Name}\n" + string.Join("\n",
        window.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible)
            .Select(button => $"{button.Name}/{button.Content}: enabled={button.IsEffectivelyEnabled} focusable={button.Focusable} bounds={button.Bounds} pos={button.TranslatePoint(default, window)}"));

    private sealed class FakeController : IControllerInputSource
    {
        public event EventHandler<ControllerActionEventArgs>? ActionPressed;
        public event EventHandler<ControllerConnectionEventArgs>? ConnectionChanged;
        public event EventHandler? ActiveControllerChanged;
        public ControllerInfo? ActiveController { get; private set; }
        public bool IsAvailable => true;
        public string? InitializationError => null;
        public int ConnectedGamepads { get; private set; } = 1;
        public bool ApplicationActive { get; private set; }
        public void SetApplicationActive(bool isActive) => ApplicationActive = isActive;
        public void Initialize() { }
        public void Poll() { }
        public void Dispose() { }
        public void Press(ControllerAction action)
        {
            ActionPressed?.Invoke(this, new ControllerActionEventArgs(1, action));
            Dispatcher.UIThread.RunJobs();
        }

        public void Connect(bool connected)
        {
            ConnectedGamepads = connected ? 1 : 0;
            ConnectionChanged?.Invoke(this, new ControllerConnectionEventArgs(1, "Test controller", connected));
        }

        public void Switch(ControllerLayout layout)
        {
            ActiveController = new ControllerInfo(1, "Test controller", layout);
            ActiveControllerChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static readonly ServerIdentity Server = new("server", new Uri("https://jellyfin.example"),
        "Test library", "10.11", "Linux");

    private sealed class ServerClient : IJellyfinServerClient
    {
        public Task<ServerIdentity> ConnectAsync(string address, CancellationToken cancellationToken = default) =>
            Task.FromResult(Server);
    }

    private sealed class PreviewClient : IJellyfinMediaPreviewClient
    {
        public Task<MediaPreviewHome> GetHomeAsync(AuthenticatedSession session, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MediaPreviewHome(null, [], []));
    }

    private sealed class AuthenticationService(bool savedAccounts) : IAuthenticationService
    {
        private readonly List<SessionProfile> _profiles = savedAccounts
            ? [new(Server, "first", "Viewer"), new(Server, "second", "Second viewer")] : [];
        public Task<IReadOnlyList<SessionProfile>> GetSavedSessionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SessionProfile>>(_profiles.ToArray());
        public Task<AuthenticatedSession> AuthenticateAsync(AuthenticationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuthenticatedSession(Server, "first", request.Username, "test-token"));
        public Task<AuthenticatedSession> RestoreAsync(SessionProfile profile, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuthenticatedSession(Server, profile.UserId, profile.Username, "test-token"));
        public Task LogoutAsync(AuthenticatedSession session, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InvalidateAsync(AuthenticatedSession session) => Task.CompletedTask;
        public Task RemoveAsync(SessionProfile profile, CancellationToken cancellationToken = default)
        {
            _profiles.Remove(profile);
            return Task.CompletedTask;
        }
    }
}
