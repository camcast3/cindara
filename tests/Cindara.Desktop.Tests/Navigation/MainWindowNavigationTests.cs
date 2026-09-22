using Avalonia;
using Avalonia.Automation;
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
using Cindara.Core.Diagnostics;
using Cindara.Core.Jellyfin;
using Cindara.Core.Models;
using Cindara.Desktop.Accessibility;
using Cindara.Desktop.Input;
using Cindara.Desktop.Localization;
using Cindara.Desktop.Tests.Localization;
using Cindara.Desktop.ViewModels;
using Cindara.Desktop.Views;

namespace Cindara.Desktop.Tests.Navigation;

[Collection(LocalizationTestGroup.Name)]
public sealed class MainWindowNavigationTests
{
    private static readonly string[] Destinations = ["Libraries", "Search", "Downloads"];

    [Theory]
    [InlineData(false, "en", 1280)]
    [InlineData(true, "en", 1280)]
    [InlineData(false, "qps-plocm", 720)]
    public Task DiagnosticsPreviewIsOptInFocusTrappedAndReachableWithoutChangingSettings(
        bool signedIn, string cultureName, int width) => TestAppBuilder.Run(() =>
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cindara-ui-diagnostics-{Guid.NewGuid():N}");
        try
        {
            using var culture = new CultureScope(cultureName);
            var diagnostics = new LocalDiagnostics(directory);
            diagnostics.Record(DiagnosticArea.Controller, DiagnosticAction.InitializeController,
                DiagnosticOutcome.Failed, DiagnosticLevel.Error, new DllNotFoundException("private-path"));
            using var fixture = new ShellFixture(diagnostics: diagnostics);
            fixture.Window.WindowState = WindowState.Normal;
            fixture.Window.Width = width;
            fixture.Window.Height = width * 9 / 16;
            if (signedIn)
            {
                fixture.SignIn();
                fixture.OpenSettings();
                Assert.Equal(3, fixture.Shell.FindControl<StackPanel>("SettingsActions")!.Children.OfType<Button>().Count());
                fixture.Input.Press(ControllerAction.NavigateDown);
                fixture.Input.Press(ControllerAction.NavigateDown);
                fixture.Input.Press(ControllerAction.NavigateDown);
                Assert.Equal("DiagnosticsButton", Focused(fixture.Window).Name);
            }

            fixture.Click("DiagnosticsButton");
            Assert.True(fixture.IsModalVisible);
            Assert.False(fixture.Window.FindControl<Grid>("MainSurface")!.IsEnabled);
            AssertInsideWindow(fixture.Window, Focused(fixture.Window));
            Assert.Contains(fixture.Modal.Children.OfType<TextBlock>(),
                block => block.Text!.Contains("SDL3", StringComparison.Ordinal));
            Assert.DoesNotContain(fixture.Modal.Children.OfType<TextBlock>(),
                block => block.Text!.Contains("private-path", StringComparison.Ordinal));
            fixture.ClickContent(Loc.Get("Diagnostics.RecentErrors"));
            Assert.Contains(fixture.Modal.Children.OfType<TextBlock>(),
                block => block.Text!.Contains("MissingNativeLibrary", StringComparison.Ordinal));
            fixture.ClickContent(Loc.Get("Action.Back"));
            fixture.ClickContent(Loc.Get("Diagnostics.Preview"));
            Assert.Equal(5, fixture.Modal.Children.OfType<Button>().Count());
            Assert.Contains(fixture.Modal.Children.OfType<TextBlock>(),
                block => block.Text!.Contains("support-bundles", StringComparison.Ordinal));
            var file = fixture.Modal.Children.OfType<Button>().First();
            Assert.Contains("environment.json", (string)file.Content!, StringComparison.Ordinal);
            fixture.Click(file);
            Assert.Contains(fixture.Modal.Children.OfType<TextBlock>(),
                block => block.Text!.Contains("RuntimeVersion", StringComparison.Ordinal));
            fixture.Key(Key.Tab, RawInputModifiers.Shift);
            Assert.Contains(Focused(fixture.Window), fixture.Modal.Children);
            AssertInsideWindow(fixture.Window, Focused(fixture.Window));
            fixture.ClickContent(Loc.Get("Action.Back"));
            fixture.ClickContent(Loc.Get("Action.Cancel"));
            fixture.Input.Press(ControllerAction.Back);
            Assert.False(fixture.IsModalVisible);
            Assert.Equal("DiagnosticsButton", Focused(fixture.Window).Name);
            Assert.DoesNotContain(diagnostics.Snapshot(), entry => entry.Action == DiagnosticAction.ExportBundle);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    });

    [Fact]
    public Task DiagnosticsKeepsFocusWhenUnderlyingHomeLoadingFinishes() => TestAppBuilder.Run(() =>
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cindara-loading-diagnostics-{Guid.NewGuid():N}");
        try
        {
            var diagnostics = new LocalDiagnostics(directory);
            using var fixture = new ShellFixture(diagnostics: diagnostics);
            fixture.Preview.Pause = true;
            fixture.SignIn(waitForHome: false);
            fixture.Click("DiagnosticsButton");
            fixture.Model.ShowDesignGalleryCommand.Cancel();
            fixture.Flush();
            Assert.True(fixture.IsModalVisible);
            Assert.Contains(Focused(fixture.Window), fixture.Modal.Children);
            Assert.False(fixture.Window.FindControl<Grid>("MainSurface")!.IsEnabled);
            fixture.Input.Press(ControllerAction.Back);
            Assert.Equal("DiagnosticsButton", Focused(fixture.Window).Name);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    });

    [Fact]
    public Task LoginOffersOnlyEnglishLanguageAndTrapsKeyboardFocus() => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture(false);
        fixture.Window.FindControl<Button>("LanguageButton")!.Focus();
        fixture.Key(Key.Enter);
        Assert.Equal(Loc.Get("Language.English"), Assert.IsType<Button>(Focused(fixture.Window)).Content);
        Assert.Equal(Loc.Get("State.Selected"), AutomationProperties.GetItemStatus(Focused(fixture.Window)));
        var options = fixture.Modal.Children.OfType<Button>().Select(button => button.Content).ToArray();
        Assert.Equal(new object[] { Loc.Get("Language.English"), Loc.Get("Action.Back") }, options);
        fixture.Key(Key.Tab, RawInputModifiers.Shift);
        Assert.Equal(Loc.Get("Action.Back"), Assert.IsType<Button>(Focused(fixture.Window)).Content);
        fixture.Key(Key.Tab);
        fixture.Key(Key.Enter);
        Assert.False(fixture.IsModalVisible);
        Assert.Equal("LanguageButton", Focused(fixture.Window).Name);
        Assert.Null(fixture.Window.FindControl<Button>("WindowOptionsButton"));
        Assert.Equal(new PresentationPreferences(), fixture.Window.Preferences);
    });

    [Fact]
    public Task SignInOpensMediaHomeWithoutAnIntermediateLauncherOrDuplicateHome() => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.SignIn();
        Assert.True(fixture.Model.IsDesignGalleryVisible);
        Assert.Equal(1, fixture.Preview.Calls);
        Assert.IsType<MediaPreviewCardViewModel>(Focused(fixture.Window).DataContext);
        var gallery = fixture.Gallery;
        Assert.Single(gallery.GetVisualDescendants().OfType<Button>(),
            button => AutomationProperties.GetName(button) == Loc.Get("Nav.Home"));
        Assert.Null(gallery.FindControl<Control>("TopNavigationPanel"));
        Assert.Null(fixture.Window.FindControl<Button>("GalleryBackButton"));
        Assert.False(fixture.Window.FindControl<Button>("LanguageButton")!.IsEffectivelyVisible);
        fixture.Input.Press(ControllerAction.Back);
        Assert.True(fixture.Model.IsDesignGalleryVisible);
        Assert.Equal("SidebarHomeButton", Focused(fixture.Window).Name);
        fixture.Input.Press(ControllerAction.NavigateRight);
        Assert.IsType<MediaPreviewCardViewModel>(Focused(fixture.Window).DataContext);
    });

    [Fact]
    public Task SettingsHasExactlyThreeActionsAndHomeReturnDoesNotReload() => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.SignIn();
        var homeFocus = Focused(fixture.Window);
        fixture.OpenSettings();
        Assert.Equal("Settings", fixture.Shell.Destination);
        Assert.Equal("SettingsLanguageButton", Focused(fixture.Window).Name);
        var actions = fixture.Shell.FindControl<StackPanel>("SettingsActions")!.Children.OfType<Button>().ToArray();
        Assert.Equal(new[] { Loc.Get("Language.Selection"), Loc.Get("Action.Exit"), Loc.Get("Nav.BackHome") },
            actions.Select(button => button.Content));
        fixture.Input.Press(ControllerAction.Accept);
        Assert.True(fixture.IsModalVisible);
        Assert.Equal(Loc.Get("Language.English"), Assert.IsType<Button>(Focused(fixture.Window)).Content);
        fixture.Input.Press(ControllerAction.Back);
        Assert.Equal("SettingsLanguageButton", Focused(fixture.Window).Name);
        fixture.Input.Press(ControllerAction.NavigateDown);
        Assert.Equal("ExitButton", Focused(fixture.Window).Name);
        fixture.Input.Press(ControllerAction.NavigateDown);
        Assert.Equal("BackHomeButton", Focused(fixture.Window).Name);
        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();
        Assert.True(fixture.Model.IsDesignGalleryVisible);
        Assert.Equal(1, fixture.Preview.Calls);
        Assert.Same(homeFocus, Focused(fixture.Window));
    });

    [Theory]
    [InlineData("Home")]
    [InlineData("Libraries")]
    public Task ReenteringSettingsStartsOnLanguageWithoutDiscardingInPageFocus(string destination) => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.SignIn();
        fixture.OpenSettings();
        fixture.Input.Press(ControllerAction.NavigateDown);
        Assert.Equal("ExitButton", Focused(fixture.Window).Name);
        fixture.Input.Press(ControllerAction.NavigateLeft);
        Assert.Equal("SettingsNavigation", Focused(fixture.Window).Name);
        fixture.Input.Press(ControllerAction.NavigateRight);
        Assert.Equal("ExitButton", Focused(fixture.Window).Name);

        if (destination == "Home")
        {
            fixture.Input.Press(ControllerAction.NavigateDown);
            fixture.Input.Press(ControllerAction.Accept);
            fixture.Flush();
            fixture.OpenSettings();
        }
        else
        {
            fixture.Click(fixture.Shell.FindControl<Button>("LibrariesNavigation")!);
            fixture.Click(fixture.Shell.FindControl<Button>("SettingsNavigation")!);
        }

        Assert.Equal("SettingsLanguageButton", Focused(fixture.Window).Name);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task LoadingCancelIsInitiallyFocusedAndReachableFromRailUsingOnlyController(bool rtl) => TestAppBuilder.Run(() =>
    {
        using var culture = new CultureScope(rtl ? "qps-plocm" : "en");
        using var fixture = new ShellFixture();
        fixture.Preview.Pause = true;
        fixture.SignIn(waitForHome: false);
        Assert.Equal("CancelLoadingButton", Focused(fixture.Window).Name);

        fixture.Input.Press(rtl ? ControllerAction.NavigateRight : ControllerAction.NavigateLeft);
        for (var index = 0; index < 4; index++)
        {
            fixture.Input.Press(ControllerAction.NavigateUp);
        }

        Assert.Equal("HomeNavigation", Focused(fixture.Window).Name);
        fixture.Input.Press(rtl ? ControllerAction.NavigateLeft : ControllerAction.NavigateRight);
        Assert.Equal("CancelLoadingButton", Focused(fixture.Window).Name);
        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();
        Assert.False(fixture.Model.IsBusy);
        Assert.Equal(Loc.Get("Status.PreviewCanceled"), fixture.Model.StatusMessage);
        fixture.Input.Press(rtl ? ControllerAction.NavigateLeft : ControllerAction.NavigateRight);
        Assert.Equal("RetryHomeButton", Focused(fixture.Window).Name);

        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();
        Assert.Equal("CancelLoadingButton", Focused(fixture.Window).Name);
        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();
        Assert.False(fixture.Model.IsBusy);
    });

    [Fact]
    public Task LoadingCanBeCanceledAndRetriedWithoutAutomaticRetryLoop() => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.Preview.Pause = true;
        fixture.SignIn(waitForHome: false);
        Assert.True(fixture.Model.IsBusy);
        Assert.True(fixture.Window.FindControl<Button>("CancelLoadingButton")!.IsEffectivelyVisible);
        fixture.Click("CancelLoadingButton");
        Assert.False(fixture.Model.IsBusy);
        Assert.False(fixture.Model.IsDesignGalleryVisible);
        Assert.Equal(Loc.Get("Status.PreviewCanceled"), fixture.Model.StatusMessage);
        fixture.Flush();
        Assert.Equal(1, fixture.Preview.Calls);
        fixture.Preview.Pause = false;
        fixture.Click(fixture.Shell.FindControl<Button>("RetryHomeButton")!);
        Assert.True(fixture.Model.IsDesignGalleryVisible);
        Assert.Equal(2, fixture.Preview.Calls);
    });

    [Fact]
    public Task NavigatingToSettingsCancelsPendingHomeInsteadOfJumpingBack() => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.Preview.Pause = true;
        fixture.SignIn(waitForHome: false);
        fixture.Shell.Navigate("Settings");
        fixture.Flush();
        Assert.False(fixture.Model.IsBusy);
        Assert.False(fixture.Model.IsDesignGalleryVisible);
        Assert.Equal("Settings", fixture.Shell.Destination);
        Assert.Equal(1, fixture.Preview.Calls);
    });

    [Fact]
    public Task HomeFailureKeepsAccountAndExposesRetry() => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.Preview.Error = MediaPreviewError.TimedOut;
        fixture.SignIn(waitForHome: false);
        Assert.True(fixture.Model.IsAuthenticatedVisible);
        Assert.False(fixture.Model.IsBusy);
        Assert.Equal(Loc.Get("Error.Preview.TimedOut"), fixture.Model.StatusMessage);
        Assert.True(fixture.Shell.FindControl<Button>("RetryHomeButton")!.IsEffectivelyVisible);
        fixture.Preview.Error = null;
        fixture.Click(fixture.Shell.FindControl<Button>("RetryHomeButton")!);
        Assert.True(fixture.Model.IsDesignGalleryVisible);
    });

    [Fact]
    public Task EmptyHomeStillHasReachableSettingsAndOneHomeAction() => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.Preview.Empty = true;
        fixture.SignIn();
        Assert.Equal("SidebarHomeButton", Focused(fixture.Window).Name);
        fixture.OpenSettings();
        Assert.Equal("SettingsLanguageButton", Focused(fixture.Window).Name);
    });

    [Fact]
    public Task NavigationDestinationsRemainAvailableAndDoNotAddBackToHomeButtons() => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.SignIn();
        fixture.OpenSettings();
        foreach (var destination in Destinations)
        {
            fixture.Click(fixture.Shell.FindControl<Button>($"{destination}Navigation")!);
            Assert.Equal(destination, fixture.Shell.Destination);
            Assert.Equal($"{destination}Navigation", Focused(fixture.Window).Name);
            Assert.Null(fixture.Shell.FindControl<Button>("ReturnHomeButton"));
        }

        fixture.Click(fixture.Shell.FindControl<Button>("HomeNavigation")!);
        Assert.True(fixture.Model.IsDesignGalleryVisible);
    });

    [Fact]
    public Task LanguageDialogTrapsFocusAndRestoresItsSettingsLauncher() => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.SignIn();
        fixture.OpenSettings();
        var launcher = Assert.IsType<Button>(Focused(fixture.Window));
        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();
        Assert.Equal(Loc.Get("Language.English"), Assert.IsType<Button>(Focused(fixture.Window)).Content);
        fixture.Key(Key.Tab, RawInputModifiers.Shift);
        Assert.Equal(Loc.Get("Action.Back"), Assert.IsType<Button>(Focused(fixture.Window)).Content);
        fixture.Key(Key.Tab);
        Assert.Equal(Loc.Get("Language.English"), Assert.IsType<Button>(Focused(fixture.Window)).Content);
        fixture.Input.Press(ControllerAction.Menu);
        Assert.Equal(WindowState.FullScreen, fixture.Window.WindowState);
        fixture.Input.Press(ControllerAction.Back);
        Assert.Same(launcher, Focused(fixture.Window));
        fixture.Key(Key.F11);
        Assert.Equal(WindowState.Normal, fixture.Window.WindowState);
        fixture.Key(Key.F11);
        Assert.Equal(WindowState.FullScreen, fixture.Window.WindowState);
    });

    [Fact]
    public Task SettingsExitClosesTheWindow() => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.SignIn();
        fixture.OpenSettings();
        var closed = false;
        fixture.Window.Closed += (_, _) => closed = true;
        fixture.Input.Press(ControllerAction.NavigateDown);
        Assert.Equal("ExitButton", Focused(fixture.Window).Name);
        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();
        Assert.True(closed);
    });

    [Fact]
    public Task SettingsButtonLabelsStayCenteredWithinUniformActions() => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.SignIn();
        fixture.OpenSettings();
        var buttons = fixture.Shell.FindControl<StackPanel>("SettingsActions")!.Children.OfType<Button>().ToArray();
        foreach (var button in buttons)
        {
            Assert.Equal(buttons[0].Bounds.Size, button.Bounds.Size);
            Assert.Equal(Avalonia.Layout.VerticalAlignment.Center, button.VerticalContentAlignment);
            Assert.Equal(Avalonia.Layout.HorizontalAlignment.Center, button.HorizontalContentAlignment);
            var text = Assert.Single(button.GetVisualDescendants().OfType<TextBlock>());
            var center = text.TranslatePoint(new Point(text.Bounds.Width / 2, text.Bounds.Height / 2), button)!.Value;
            Assert.InRange(Math.Abs(center.Y - button.Bounds.Height / 2), 0, 1);
            Assert.InRange(Math.Abs(center.X - button.Bounds.Width / 2), 0, 1);
        }
    });

    [Fact]
    public Task SavedAccountPickerTrapsFocusAndSwitchingAccountsClearsPreviousHome() => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();
        fixture.Input.Press(ControllerAction.NavigateDown);
        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();
        Assert.Equal("second", fixture.Model.SelectedSavedSession!.UserId);
        Assert.Equal(LocaleFormat.SessionDisplayName(fixture.Model.SelectedSavedSession),
            fixture.Window.FindControl<Button>("SavedAccountButton")!.Content);
        Assert.Equal("SavedAccountButton", Focused(fixture.Window).Name);
        fixture.SignIn();
        var previous = fixture.Model.DesignGallery;
        fixture.Model.BackToSessionsCommand.Execute(null);
        fixture.Flush();
        Assert.True(fixture.Model.AreSavedSessionsVisible);
        Assert.Null(fixture.Model.DesignGallery);
        fixture.SignIn();
        Assert.NotSame(previous, fixture.Model.DesignGallery);
        Assert.Equal(2, fixture.Preview.Calls);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ServerEntryOffersSavedAccountsOnlyWhenTheyExist(bool savedAccounts) => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture(savedAccounts);
        if (savedAccounts)
        {
            fixture.Model.AddServerCommand.Execute(null);
            fixture.Flush();
        }

        var button = fixture.Window.FindControl<Button>("BackToSavedAccountsButton")!;
        Assert.Equal(savedAccounts, button.IsEffectivelyVisible);
        if (savedAccounts)
        {
            fixture.Click(button);
            Assert.Equal("SavedAccountButton", Focused(fixture.Window).Name);
        }
        else
        {
            fixture.Input.Press(ControllerAction.NavigateDown);
            Assert.Equal("LanguageButton", Focused(fixture.Window).Name);
        }
    });

    [Fact]
    public Task ControllerKeyboardCommitsOrCancelsWithoutChangingFocusOwner() => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture(false);
        var field = fixture.Window.FindControl<TextBox>("ServerAddressTextBox")!;
        field.Text = "https://";
        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();
        fixture.ClickContent("q");
        fixture.ClickContent(Loc.Get("Keyboard.Shift"));
        fixture.ClickContent("A");
        fixture.ClickContent(Loc.Get("Keyboard.Backspace"));
        fixture.ClickContent(Loc.Get("Keyboard.Done"));
        Assert.Equal("https://q", field.Text);
        Assert.Same(field, Focused(fixture.Window));
        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();
        fixture.ClickContent(Loc.Get("Keyboard.Clear"));
        fixture.Input.Press(ControllerAction.Back);
        Assert.Equal("https://q", field.Text);
        Assert.Empty(fixture.Modal.Children);
    });

    [Fact]
    public Task ControllerChangesAndBackgroundActionsDoNotStealFocus() => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.SignIn();
        var focused = Focused(fixture.Window);
        fixture.Input.Switch(ControllerLayout.Nintendo);
        Assert.Contains("[B]: select", fixture.Model.ControllerStatus, StringComparison.Ordinal);
        fixture.Input.Switch(ControllerLayout.PlayStation);
        Assert.Contains("[Cross]: select", fixture.Model.ControllerStatus, StringComparison.Ordinal);
        fixture.Input.Connect(false);
        fixture.Input.Connect(true);
        Assert.Same(focused, Focused(fixture.Window));
        fixture.Window.DeactivateForTest();
        fixture.Flush();
        fixture.Input.Press(ControllerAction.Back);
        fixture.Input.Press(ControllerAction.Menu);
        Assert.False(fixture.Input.ApplicationActive);
        Assert.Same(focused, Focused(fixture.Window));
        Assert.True(fixture.Model.IsDesignGalleryVisible);
        Assert.Equal(WindowState.FullScreen, fixture.Window.WindowState);
    });

    [Fact]
    public Task AuthenticationControlsExposeNamesRolesAndMaskedPassword() => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture(false);
        var server = fixture.Window.FindControl<TextBox>("ServerAddressTextBox")!;
        Assert.Equal(Loc.Get("Auth.ServerAddress"), new TextBoxAutomationPeer(server).GetName());
        fixture.Model.ServerAddress = Server.BaseUri.ToString();
        fixture.Model.ConnectCommand.Execute(null);
        fixture.Flush();
        var password = fixture.Window.GetVisualDescendants().OfType<TextBox>()
            .Single(field => field.IsEffectivelyVisible && field.PasswordChar != default);
        Assert.Equal('*', password.PasswordChar);
        Assert.Equal(AutomationControlType.Edit, new TextBoxAutomationPeer(password).GetAutomationControlType());
        foreach (var button in fixture.Window.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible))
        {
            var peer = new ButtonAutomationPeer(button);
            Assert.False(string.IsNullOrWhiteSpace(peer.GetName()));
            Assert.Equal(AutomationControlType.Button, peer.GetAutomationControlType());
        }
    });

    [Theory]
    [InlineData("qps-ploc", false)]
    [InlineData("qps-plocm", true)]
    public Task DeveloperLargeTextAndPseudoModesKeepHomeAndSettingsReachable(string locale, bool rtl) => TestAppBuilder.Run(() =>
    {
        using var culture = new CultureScope(locale);
        using var fixture = new ShellFixture(preferences: new PresentationPreferences(1.5));
        fixture.SignIn();
        Assert.Equal(rtl ? Avalonia.Media.FlowDirection.RightToLeft : Avalonia.Media.FlowDirection.LeftToRight,
            fixture.Window.FlowDirection);
        fixture.OpenSettings();
        Assert.Equal("SettingsLanguageButton", Focused(fixture.Window).Name);
        AssertInsideWindow(fixture.Window, Focused(fixture.Window));
        fixture.Input.Press(rtl ? ControllerAction.NavigateRight : ControllerAction.NavigateLeft);
        Assert.Equal("SettingsNavigation", Focused(fixture.Window).Name);
        fixture.Input.Press(rtl ? ControllerAction.NavigateLeft : ControllerAction.NavigateRight);
        Assert.Equal("SettingsLanguageButton", Focused(fixture.Window).Name);
        AssertInsideWindow(fixture.Window, Focused(fixture.Window));
        Capture(fixture.Window, $"settings-{locale}");
    });

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(3840, 2160)]
    public Task HomeSettingsAndDialogsFitTvViewports(int width, int height) => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.Window.WindowState = WindowState.Normal;
        fixture.Window.Width = width;
        fixture.Window.Height = height;
        fixture.SignIn();
        AssertInsideWindow(fixture.Window, Focused(fixture.Window));
        Capture(fixture.Window, $"media-home-{width}");
        fixture.OpenSettings();
        AssertInsideWindow(fixture.Window, Focused(fixture.Window));
        Capture(fixture.Window, $"in-app-settings-{width}");
        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();
        AssertInsideWindow(fixture.Window, Focused(fixture.Window));
        Capture(fixture.Window, $"language-options-{width}");
    });

    [Fact]
    public Task DpiMigrationPreservesLogicalSettingsLayoutAndFocus() => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.SignIn();
        fixture.OpenSettings();
        var focused = Focused(fixture.Window);
        var size = focused.Bounds.Size;
        fixture.Window.SetRenderScaling(2);
        fixture.Flush();
        Assert.InRange(Math.Abs(size.Width - focused.Bounds.Width), 0, 1);
        Assert.InRange(Math.Abs(size.Height - focused.Bounds.Height), 0, 1);
        Assert.Same(focused, Focused(fixture.Window));
        AssertInsideWindow(fixture.Window, focused);
    });

    [Fact]
    public Task EnlargedGalleryDescriptionRemainsScrollableWithNoHeaderTabs() => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture(preferences: new PresentationPreferences(1.5, true, true));
        fixture.Preview.LongDescription = true;
        fixture.SignIn();
        var description = fixture.Gallery.FindControl<ScrollViewer>("HeroTextScroll")!;
        Assert.True(description.Extent.Height > description.Viewport.Height);
        fixture.Key(Key.PageDown);
        Assert.True(description.Offset.Y > 0);
        fixture.Key(Key.PageUp);
        Assert.Equal(0, description.Offset.Y);
        Assert.False(fixture.Gallery.FindControl<Border>("HeroArtwork")!.IsVisible);
    });

    private static Control Focused(Window window) =>
        Assert.IsAssignableFrom<Control>(window.FocusManager!.GetFocusedElement());

    private static void AssertInsideWindow(Window window, Control control)
    {
        Assert.True(control.IsEffectivelyVisible);
        var start = control.TranslatePoint(default, window)!.Value;
        var end = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), window)!.Value;
        Assert.InRange(start.X, 0, window.ClientSize.Width);
        Assert.InRange(start.Y, 0, window.ClientSize.Height);
        Assert.InRange(end.X, 0, window.ClientSize.Width);
        Assert.InRange(end.Y, 0, window.ClientSize.Height);
        Assert.True(Math.Abs(end.X - start.X) > 0);
        Assert.True(Math.Abs(end.Y - start.Y) > 0);
    }

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

    private sealed class ShellFixture : IDisposable
    {
        public ShellFixture(bool savedAccounts = true, PresentationPreferences? preferences = null,
            LocalDiagnostics? diagnostics = null)
        {
            Model = new MainViewModel(new ServerClient(), new AuthenticationService(savedAccounts), Preview);
            Window = new TestMainWindow(Input, preferences, diagnostics) { DataContext = Model };
            Window.Show();
            Window.Activate();
            Flush();
        }

        public FakeController Input { get; } = new();
        public PreviewClient Preview { get; } = new();
        public MainViewModel Model { get; }
        public TestMainWindow Window { get; }
        public ShellView Shell => Window.FindControl<ShellView>("Shell")!;
        public DesignGalleryView Gallery => Window.FindControl<DesignGalleryView>("GalleryView")!;
        public StackPanel Modal => Window.FindControl<StackPanel>("ModalActions")!;
        public bool IsModalVisible => Window.FindControl<Border>("ModalOverlay")!.IsVisible;

        public void SignIn(bool waitForHome = true)
        {
            Model.UseSavedSessionCommand.Execute(null);
            Flush();
            Assert.True(Model.IsAuthenticatedVisible);
            if (waitForHome)
            {
                Assert.True(Model.IsDesignGalleryVisible, Model.StatusMessage);
            }
        }

        public void OpenSettings() => Click(Gallery.FindControl<Button>("GallerySettingsButton")!);
        public void Click(string name) => Click(Window.FindControl<Button>(name)!);
        public void Click(Button button)
        {
            button.Focus();
            ((IInvokeProvider)new ButtonAutomationPeer(button)).Invoke();
            Flush();
        }

        public void ClickContent(string text) =>
            Click(Modal.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, text)));

        public void Key(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            Window.KeyPress(key, modifiers, PhysicalKey.None, null);
            Window.KeyRelease(key, modifiers, PhysicalKey.None, null);
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

    private sealed class TestMainWindow(IControllerInputSource input, PresentationPreferences? preferences,
        LocalDiagnostics? diagnostics)
        : MainWindow(input, preferences, diagnostics)
    {
        public void DeactivateForTest() => typeof(WindowBase)
            .GetMethod("HandleDeactivated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(this, null);
    }

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
        public int Calls { get; private set; }
        public bool Pause { get; set; }
        public bool Empty { get; set; }
        public bool LongDescription { get; set; }
        public MediaPreviewError? Error { get; set; }

        public async Task<MediaPreviewHome> GetHomeAsync(AuthenticatedSession session, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Pause)
            {
                var pending = new TaskCompletionSource<MediaPreviewHome>();
                using var registration = cancellationToken.Register(() => pending.TrySetCanceled(cancellationToken));
                return await pending.Task;
            }

            if (Error is { } error)
            {
                throw new MediaPreviewException(error, "Test preview failure.");
            }

            if (Empty)
            {
                return new MediaPreviewHome(null, [], []);
            }

            var overview = LongDescription
                ? string.Concat(Enumerable.Repeat("A long readable media description. ", 100)) : "Your media description.";
            var item = new MediaPreviewItem("movie", "First movie", "2026", "Movie", null, null, overview, "1h 5m", 40);
            return new MediaPreviewHome(item, [item], [new MediaPreviewRail("movies", "Movies", [item])]);
        }
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
