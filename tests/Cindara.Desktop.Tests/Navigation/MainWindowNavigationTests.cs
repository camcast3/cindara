using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
using Cindara.Desktop.Libraries;
using Cindara.Desktop.Localization;
using Cindara.Desktop.Tests.Localization;
using Cindara.Desktop.ViewModels;
using Cindara.Desktop.Views;

namespace Cindara.Desktop.Tests.Navigation;

[Collection(LocalizationTestGroup.Name)]
public sealed class MainWindowNavigationTests
{
    private static readonly string[] Destinations = ["Libraries", "Search", "Downloads"];

    [Fact]
    public Task NullNextUpResponseKeepsSignInAndRecoversThroughHomeRetry() => TestAppBuilder.Run(async () =>
    {
        using var handler = new RepairableArtworkHandler(CreateReviewArtwork()) { NullNextUp = true };
        using var client = new JellyfinMediaPreviewClient(handler,
            new JellyfinClientIdentity("Cindara", "UI tests", "device", "1.0"));
        using var fixture = new ShellFixture(mediaClient: client);
        fixture.SignIn(waitForHome: false);
        await fixture.Model.OpenHomeCommand.ExecutionTask!;
        fixture.Flush();
        Assert.True(fixture.Model.IsAuthenticatedVisible);
        Assert.False(fixture.Model.IsBusy);
        Assert.False(fixture.Model.IsDesignGalleryVisible);
        Assert.Equal(Loc.Get("Error.Preview.InvalidResponse"), fixture.Model.StatusMessage);
        Assert.Equal("RetryHomeButton", Focused(fixture.Window).Name);

        handler.NullNextUp = false;
        fixture.Click(fixture.Shell.FindControl<Button>("RetryHomeButton")!);
        await fixture.Model.OpenHomeCommand.ExecutionTask!;
        fixture.Flush();

        Assert.True(fixture.Model.IsDesignGalleryVisible);
        Assert.IsType<MediaPreviewCardViewModel>(Focused(fixture.Window).DataContext);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CorruptCachedArtworkRetryFetchesFreshBytesInHomeAndLibrary(bool library) =>
        TestAppBuilder.Run(async () =>
        {
            using var handler = new RepairableArtworkHandler(CreateReviewArtwork()) { Corrupt = !library };
            using var client = new JellyfinMediaPreviewClient(handler,
                new JellyfinClientIdentity("Cindara", "UI tests", "device", "1.0"));
            using var fixture = new ShellFixture(mediaClient: client);
            fixture.SignIn(waitForHome: false);
            await fixture.Model.OpenHomeCommand.ExecutionTask!;
            fixture.Flush();
            if (!library)
            {
                Assert.False(fixture.Model.IsDesignGalleryVisible);
                Assert.Equal(Loc.Get("Error.Preview.InvalidResponse"), fixture.Model.StatusMessage);
                var firstRequests = handler.ImageRequests.Values.Sum();
                handler.Corrupt = false;
                fixture.Click(fixture.Shell.FindControl<Button>("RetryHomeButton")!);
                await fixture.Model.OpenHomeCommand.ExecutionTask!;
                fixture.Flush();
                Assert.True(fixture.Model.IsDesignGalleryVisible, fixture.Model.StatusMessage);
                Assert.True(handler.ImageRequests.Values.Sum() > firstRequests);
                Assert.True(fixture.Model.DesignGallery!.ContinueWatching[0].HasArtwork);
            }
            else
            {
                Assert.True(fixture.Model.IsDesignGalleryVisible);
                handler.Corrupt = true;
                fixture.Click(fixture.Gallery.FindControl<ItemsControl>("GalleryLibraryShortcuts")!
                    .GetVisualDescendants().OfType<Button>().Single());
                var browser = fixture.Model.LibraryBrowser!;
                await browser.OpenLibraryCommand.ExecutionTask!;
                await browser.LoadArtworkCommand.ExecutionTask!;
                fixture.Flush();
                var card = Assert.Single(browser.Items);
                Assert.False(card.HasArtwork);
                Assert.True(browser.CanRetryArtwork);
                var cardControl = fixture.Shell.LibraryView.GetVisualDescendants().OfType<Button>()
                    .Single(button => button.DataContext == card);
                handler.Corrupt = false;
                fixture.Click(fixture.Shell.LibraryView.FindControl<Button>("RetryLibraryArtwork")!);
                await browser.LoadArtworkCommand.ExecutionTask!;
                fixture.Flush();
                Assert.Same(card, Assert.Single(browser.Items));
                Assert.True(card.HasArtwork);
                Assert.False(browser.CanRetryArtwork);
                Assert.Equal(2, handler.ImageRequests["/Items/library-item/Images/Primary"]);
                Assert.Contains(cardControl, fixture.Shell.LibraryView.GetVisualDescendants().OfType<Button>());
                fixture.Click(cardControl);
                Assert.True(fixture.IsModalVisible);
                fixture.Input.Press(ControllerAction.Back);
                Assert.Same(cardControl, Focused(fixture.Window));
            }
        });

    private sealed class RepairableArtworkHandler(byte[] validArtwork) : HttpMessageHandler
    {
        public bool Corrupt { get; set; }
        public bool NullNextUp { get; set; }
        public System.Collections.Concurrent.ConcurrentDictionary<string, int> ImageRequests { get; } = new(StringComparer.Ordinal);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/Images/", StringComparison.Ordinal))
            {
                ImageRequests.AddOrUpdate(path, 1, (_, count) => count + 1);
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Corrupt ? [0, 1, 2, 3] : validArtwork),
                });
            }

            var json = path switch
            {
                "/Users/first/Items/Resume" => """{"Items":[{"Id":"resume","Name":"Resumable movie","Type":"Movie","ImageTags":{"Primary":"tag"},"UserData":{"PlayedPercentage":50}}]}""",
                "/Shows/NextUp" => NullNextUp ? """{"Items":[null]}""" : """{"Items":[]}""",
                "/Users/first/Views" => """{"Items":[{"Id":"tv","Name":"TV","CollectionType":"tvshows"}]}""",
                "/Users/first/Items/Latest" => "[]",
                "/Users/first/Items" => """{"Items":[{"Id":"library-item","Name":"Library item","Type":"Series","ImageTags":{"Primary":"tag"}}],"TotalRecordCount":1}""",
                _ => throw new InvalidOperationException($"Unexpected test request: {path}"),
            };
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    [Fact]
    public Task LibraryLayoutSaveFailureStaysInTheEditorAndDoesNotChangeHome() => TestAppBuilder.Run(() =>
    {
        var path = Path.Combine(Path.GetTempPath(), $"cindara-layout-blocked-{Guid.NewGuid():N}");
        try
        {
            using var fixture = new ShellFixture(libraryLayoutStore: new LibraryLayoutSettingsStore(path));
            fixture.Preview.LayoutLibraries = true;
            fixture.SignIn();
            File.WriteAllText(path, "Block creation of the settings directory.");
            fixture.OpenLibraryLayoutSettings();
            fixture.ClickAutomationId("ConfigureHomeLibraries");
            fixture.ClickAutomationId("library-Home-tv-toggle");
            fixture.ClickAutomationId("SaveLibraryLayout");
            Assert.True(fixture.IsModalVisible);
            Assert.Equal("SaveLibraryLayout", AutomationProperties.GetAutomationId(Focused(fixture.Window)));
            Assert.Contains(fixture.Modal.Children.OfType<TextBlock>(),
                block => block.Text == Loc.Get("LibraryLayout.SaveFailed"));
            Assert.Equal(["tv", "movies", "anime"], fixture.Model.DesignGallery!.RecentlyAddedLibraries.Select(rail => rail.LibraryId));
            fixture.ClickAutomationId("CancelLibraryLayout");
            Assert.Equal("SettingsLibraryLayoutButton", Focused(fixture.Window).Name);
        }
        finally
        {
            File.Delete(path);
        }
    });

    [Theory]
    [InlineData("en")]
    [InlineData("qps-plocm")]
    public Task LibraryLayoutEditorSavesIndependentOrdersAndRestoresThemOnlyForTheSameAccount(string cultureName) =>
        TestAppBuilder.Run(() =>
        {
            using var culture = new CultureScope(cultureName);
            var directory = Path.Combine(Path.GetTempPath(), $"cindara-layout-ui-{Guid.NewGuid():N}");
            var store = new LibraryLayoutSettingsStore(directory);
            try
            {
                using (var fixture = new ShellFixture(libraryLayoutStore: store))
                {
                    fixture.Preview.LayoutLibraries = true;
                    fixture.SignIn();
                    Assert.Equal(["tv", "movies", "anime"], fixture.Model.SidebarLibraries.Select(library => library.Id));
                    Assert.Equal(["tv", "movies", "anime"], fixture.Model.DesignGallery!.RecentlyAddedLibraries.Select(rail => rail.LibraryId));
                    var shortcutButtons = fixture.Gallery.FindControl<ItemsControl>("GalleryLibraryShortcuts")!
                        .GetVisualDescendants().OfType<Button>().ToArray();
                    var homeButton = fixture.Gallery.FindControl<Button>("SidebarHomeButton")!;
                    var homeCenter = homeButton.TranslatePoint(
                        new Point(homeButton.Bounds.Width / 2, homeButton.Bounds.Height / 2),
                        fixture.Gallery)!.Value.X;
                    Assert.All(shortcutButtons, button =>
                    {
                        Assert.Equal(homeButton.Bounds.Size, button.Bounds.Size);
                        var center = button.TranslatePoint(
                            new Point(button.Bounds.Width / 2, button.Bounds.Height / 2),
                            fixture.Gallery)!.Value.X;
                        Assert.InRange(Math.Abs(center - homeCenter), 0, 1);
                        Assert.Single(button.GetVisualDescendants().OfType<PathIcon>());
                    });
                    Assert.Equal(3, shortcutButtons.Select(button =>
                            Assert.Single(button.GetVisualDescendants().OfType<PathIcon>()).Data)
                        .Distinct().Count());
                    fixture.OpenLibraryLayoutSettings();
                    fixture.ClickAutomationId("ConfigureSidebarLibraries");
                    Assert.Equal(3, fixture.Modal.GetVisualDescendants().OfType<Button>()
                        .Count(button => AutomationProperties.GetAutomationId(button)?.EndsWith("-toggle", StringComparison.Ordinal) is true));
                    Assert.DoesNotContain(fixture.Modal.GetVisualDescendants().OfType<TextBlock>(),
                        text => text.Text is "Collections" or "People");
                    Assert.Equal(Loc.Format("LibraryLayout.HideFor", "TV"), AutomationProperties.GetName(Focused(fixture.Window)));
                    fixture.ClickAutomationId("library-Sidebar-movies-toggle");
                    Assert.Equal("library-Sidebar-movies-toggle", AutomationProperties.GetAutomationId(Focused(fixture.Window)));
                    Assert.Equal(Loc.Format("LibraryLayout.ShowFor", "Movies"), AutomationProperties.GetName(Focused(fixture.Window)));
                    Assert.Equal(Loc.Get("LibraryLayout.Show"),
                        Assert.IsType<TextBlock>(Assert.IsType<Button>(Focused(fixture.Window)).Content).Text);
                    AssertInsideWindow(fixture.Window, Focused(fixture.Window));
                    Capture(fixture.Window, $"library-layout-sidebar-{cultureName}");
                    fixture.ClickAutomationId("SaveLibraryLayout");
                    Assert.False(fixture.IsModalVisible);
                    fixture.Click(fixture.Shell.FindControl<Button>("DestinationBackButton")!);
                    Assert.Equal(["tv", "anime"], fixture.Model.DesignGallery!.SidebarLibraries.Select(library => library.Id));
                    Assert.Equal(["tv", "movies", "anime"], fixture.Model.DesignGallery.RecentlyAddedLibraries.Select(rail => rail.LibraryId));
                    var shortcuts = fixture.Gallery.FindControl<ItemsControl>("GalleryLibraryShortcuts")!
                        .GetVisualDescendants().OfType<Button>().ToArray();
                    Assert.Equal(["TV", "Anime"], shortcuts.Select(AutomationProperties.GetName));

                    fixture.OpenLibraryLayoutSettings();
                    fixture.ClickAutomationId("ConfigureHomeLibraries");
                    Assert.DoesNotContain(fixture.Modal.GetVisualDescendants().OfType<TextBlock>(),
                        text => text.Text is "Collections" or "People");
                    fixture.ClickAutomationId("library-Home-anime-up");
                    fixture.ClickAutomationId("library-Home-anime-up");
                    Assert.Equal("library-Home-anime-toggle", AutomationProperties.GetAutomationId(Focused(fixture.Window)));
                    fixture.ClickAutomationId("library-Home-tv-toggle");
                    fixture.ClickAutomationId("SaveLibraryLayout");
                    fixture.Click(fixture.Shell.FindControl<Button>("DestinationBackButton")!);
                    Assert.Equal(["anime", "movies"], fixture.Model.DesignGallery!.RecentlyAddedLibraries.Select(rail => rail.LibraryId));
                    Assert.Equal(["tv", "anime"], fixture.Model.DesignGallery.SidebarLibraries.Select(library => library.Id));
                    Assert.Equal(1, fixture.Preview.Calls);

                    fixture.OpenLibraryLayoutSettings();
                    fixture.ClickAutomationId("ConfigureSidebarLibraries");
                    fixture.ClickAutomationId("library-Sidebar-tv-toggle");
                    fixture.Key(Key.Escape);
                    Assert.False(fixture.IsModalVisible);
                    Assert.Equal(["tv", "anime"], fixture.Model.SidebarLibraries.Select(library => library.Id));
                }

                using var restored = new ShellFixture(libraryLayoutStore: store);
                restored.Preview.LayoutLibraries = true;
                restored.SignIn();
                Assert.Equal(["tv", "anime"], restored.Model.DesignGallery!.SidebarLibraries.Select(library => library.Id));
                Assert.Equal(["anime", "movies"], restored.Model.DesignGallery.RecentlyAddedLibraries.Select(rail => rail.LibraryId));
                var shortcut = restored.Gallery.FindControl<ItemsControl>("GalleryLibraryShortcuts")!
                    .GetVisualDescendants().OfType<Button>().Single(button => button.DataContext is MediaLibrary { Id: "anime" });
                restored.Click(shortcut);
                Assert.Equal("Libraries", restored.Shell.Destination);
                Assert.Equal("anime", restored.Model.LibraryBrowser!.SelectedLibrary!.Id);
                restored.Model.BackToSessionsCommand.Execute(null);
                restored.Flush();
                Assert.Null(restored.Model.LibraryLayout);
                Assert.Empty(restored.Model.SidebarLibraries);
                restored.Model.SelectedSavedSession = restored.Model.SavedSessions.Single(profile => profile.UserId == "second");
                restored.SignIn();
                Assert.Equal(["tv", "movies", "anime"], restored.Model.SidebarLibraries.Select(library => library.Id));
                Assert.Equal(["tv", "movies", "anime"], restored.Model.DesignGallery!.RecentlyAddedLibraries.Select(rail => rail.LibraryId));
            }
            finally
            {
                if (Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); }
            }
        });

    [Fact]
    public Task LibraryRemainsNavigableWhilePostersLoadAndArtworkUpdatesOnTheUiThread() => TestAppBuilder.Run(async () =>
    {
        using var fixture = new ShellFixture();
        fixture.Preview.WithLibraries = true;
        fixture.Preview.ProgressiveArtwork = true;
        fixture.SignIn();
        fixture.Click(fixture.Gallery.GetVisualDescendants().OfType<Button>()
            .Single(button => button.DataContext is MediaLibrary { Id: "movies" }));
        var model = fixture.Model.LibraryBrowser!;
        await model.OpenLibraryCommand.ExecutionTask!;
        fixture.Flush();
        Assert.False(model.IsLoading);
        Assert.True(model.LoadArtworkCommand.IsRunning);
        Assert.True(fixture.Shell.LibraryView.FindControl<Button>("NextLibraryPage")!.IsEffectivelyEnabled);
        fixture.Key(Key.Right);
        fixture.Input.Press(ControllerAction.NavigateDown);
        fixture.Flush();
        var focused = Focused(fixture.Window);
        var card = Assert.IsType<MediaPreviewCardViewModel>(focused.DataContext);
        Assert.Equal("movie-6", card.Id);
        Assert.True(card.IsArtworkLoading);
        var artworkChanged = false;
        card.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(card.Artwork))
            {
                Assert.True(Dispatcher.UIThread.CheckAccess());
                artworkChanged = true;
            }
        };

        fixture.Preview.ArtworkGate.SetResult(CreateReviewArtwork());
        await model.LoadArtworkCommand.ExecutionTask!;
        fixture.Flush();

        Assert.True(artworkChanged);
        Assert.True(card.HasArtwork);
        Assert.Same(focused, Focused(fixture.Window));
        AssertInsideWindow(fixture.Window, focused);
        Assert.False(model.CanRetryArtwork);
        Assert.False(fixture.Shell.LibraryView.FindControl<Button>("CancelLibraryArtwork")!.IsEffectivelyVisible);
    });

    [Theory]
    [InlineData(1280, 720, 299.2)]
    [InlineData(1920, 1080, 518.4)]
    [InlineData(3440, 1400, 672)]
    [InlineData(3840, 2160, 1036.8)]
    public Task HomeCardsAreTwentyPercentLargerAndFitTheViewport(int width, int height, double expectedHeroHeight) =>
        TestAppBuilder.Run(() =>
        {
            using var fixture = new ShellFixture(preferences: new PresentationPreferences(ReducedMotion: true));
            fixture.Window.WindowState = WindowState.Normal;
            fixture.Window.Width = width;
            fixture.Window.Height = height;
            fixture.SignIn();
            var gallery = fixture.Gallery;
            var continueCard = gallery.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Classes.Contains("continue-card"));
            var poster = gallery.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Classes.Contains("media-card"));
            var originalScale = Math.Clamp(gallery.Bounds.Width / 1600, 1, 1.55);
            Assert.InRange(Math.Abs(continueCard.Bounds.Width - 290 * originalScale * 1.2), 0, 1);
            Assert.InRange(Math.Abs(continueCard.Bounds.Height - 163 * originalScale * 1.2), 0, 1);
            Assert.InRange(Math.Abs(poster.Bounds.Width - 156 * originalScale * 1.2), 0, 1);
            Assert.InRange(Math.Abs(poster.Bounds.Height - 234 * originalScale * 1.2), 0, 1);
            Assert.Equal(348 * originalScale, (double)gallery.Resources["Gallery.ContinueWidth"]!, precision: 6);
            Assert.Equal(195.6 * originalScale, (double)gallery.Resources["Gallery.ContinueHeight"]!, precision: 6);
            Assert.Equal(187.2 * originalScale, (double)gallery.Resources["Gallery.PosterWidth"]!, precision: 6);
            Assert.Equal(280.8 * originalScale, (double)gallery.Resources["Gallery.PosterHeight"]!, precision: 6);
            Assert.Equal(expectedHeroHeight,
                (double)gallery.Resources["Gallery.HeroHeight"]!, precision: 6);
            var heroPanel = gallery.FindControl<Grid>("HeroPanel")!;
            var heroText = gallery.FindControl<ScrollViewer>("HeroTextScroll")!;
            var scale = (double)gallery.Resources["Gallery.HeroTitleSize"]! / 56;
            Assert.InRange(heroText.Bounds.Width, 320, heroPanel.Bounds.Width * 0.5);
            Assert.Equal(ScrollBarVisibility.Hidden, heroText.VerticalScrollBarVisibility);
            Assert.Null(Assert.IsType<Grid>(heroText.Content).Background);
            Assert.Equal(Math.Max(72, 96 * scale),
                (double)gallery.Resources["Gallery.NavigationWidth"]!, precision: 6);
            Assert.Equal(22 * scale,
                (double)gallery.Resources["Gallery.NavigationIconSize"]!, precision: 6);
            Assert.Equal(24 * scale,
                (double)gallery.Resources["Gallery.SectionHeadingSize"]!, precision: 6);
            Assert.Equal(14 * scale,
                (double)gallery.Resources["Gallery.CardTitleSize"]!, precision: 6);
            Assert.Equal(14 * scale,
                fixture.Window.FindControl<TextBlock>("GalleryReadHelp")!.FontSize, precision: 6);
            AssertInsideWindow(fixture.Window, continueCard);
            fixture.Input.Press(ControllerAction.NavigateDown);
            fixture.Flush();
            Assert.Same(poster, Focused(fixture.Window));
            AssertInsideWindow(fixture.Window, poster);
            fixture.Input.Press(ControllerAction.NavigateUp);
            fixture.Flush();
            Assert.Same(continueCard, Focused(fixture.Window));
            AssertInsideWindow(fixture.Window, continueCard);
        });

    [Theory]
    [InlineData("en", 1920)]
    [InlineData("en", 3840)]
    [InlineData("qps-plocm", 1920)]
    public Task UnifiedContinueWatchingWalkthroughUsesRealApiSelectionAndRestoresFocus(string cultureName, int width) =>
        TestAppBuilder.Run(async () =>
        {
            using var culture = new CultureScope(cultureName);
            using var handler = new ContinueWatchingUiHandler(CreateReviewArtwork());
            using var client = new JellyfinMediaPreviewClient(handler,
                new JellyfinClientIdentity("Cindara", "UI tests", "device", "1.0"));
            using var fixture = new ShellFixture(preferences: new PresentationPreferences(ReducedMotion: true), mediaClient: client);
            fixture.Window.WindowState = WindowState.Normal;
            fixture.Window.Width = width;
            fixture.Window.Height = width * 9 / 16;
            fixture.SignIn(waitForHome: false);
            await fixture.Model.OpenHomeCommand.ExecutionTask!;
            fixture.Flush();

            var gallery = fixture.Gallery;
            var row = gallery.FindControl<ItemsControl>("ContinueWatchingRow")!;
            var cards = row.GetVisualDescendants().OfType<Button>().ToArray();
            var items = cards.Select(card => Assert.IsType<MediaPreviewCardViewModel>(card.DataContext)).ToArray();
            Assert.Equal(["sky-11", "north-6", "moon", "river-1", "harbor-1", "orbit-1", "garden-1", "summit-1"],
                items.Select(item => item.Id));
            Assert.Equal(42, items[1].PlaybackProgress);
            Assert.False(items[0].HasPlaybackProgress);
            Assert.All(items, item => Assert.True(item.HasArtwork));
            Assert.Single(gallery.GetVisualDescendants().OfType<TextBlock>(),
                block => block.Text == Loc.Get("Gallery.ContinueWatching"));
            Assert.DoesNotContain(gallery.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "Next Up");
            Assert.Same(cards[0], Focused(fixture.Window));
            AssertInsideWindow(fixture.Window, cards[0]);
            Capture(fixture.Window, $"continue-watching-{cultureName}-{width}");

            var rtl = cultureName == "qps-plocm";
            fixture.Input.Press(rtl ? ControllerAction.NavigateLeft : ControllerAction.NavigateRight);
            Assert.Same(cards[1], Focused(fixture.Window));
            Assert.Equal("Northstar", fixture.Model.DesignGallery!.Featured!.HeroName);
            for (var index = 2; index < cards.Length; index++)
            {
                fixture.Key(rtl ? Key.Left : Key.Right);
                Assert.Same(cards[index], Focused(fixture.Window));
            }

            AssertInsideWindow(fixture.Window, cards[^1]);
            var scroll = row.GetVisualAncestors().OfType<ScrollViewer>().First();
            Assert.True(scroll.Offset.X > 0);
            var offset = scroll.Offset;
            Capture(fixture.Window, $"continue-watching-scrolled-{cultureName}-{width}");
            fixture.Key(Key.Enter);
            Assert.True(fixture.IsModalVisible);
            fixture.Key(Key.Escape);
            Assert.Same(cards[^1], Focused(fixture.Window));
            Assert.Equal(offset, scroll.Offset);
            fixture.OpenSettings();
            fixture.Click(fixture.Shell.FindControl<Button>("BackHomeButton")!);
            Assert.Same(cards[^1], Focused(fixture.Window));
            Assert.Equal(offset, scroll.Offset);
            fixture.Input.Press(ControllerAction.Back);
            Assert.Equal("SidebarHomeButton", Focused(fixture.Window).Name);
            fixture.Input.Press(rtl ? ControllerAction.NavigateLeft : ControllerAction.NavigateRight);
            Assert.Same(cards[^1], Focused(fixture.Window));
            fixture.Input.Press(ControllerAction.NavigateDown);
            Assert.IsType<MediaPreviewCardViewModel>(Focused(fixture.Window).DataContext);
            Assert.Contains("media-card", Focused(fixture.Window).Classes);
            Assert.DoesNotContain(gallery.FindControl<StackPanel>("MediaRowsPanel")!.GetVisualDescendants().OfType<Button>(),
                button => button.DataContext is MediaLibrary);
        });

    private static byte[] CreateReviewArtwork()
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(720, 405));
        using (var drawing = bitmap.CreateDrawingContext())
        {
            drawing.FillRectangle(new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#18394F")), new Rect(0, 0, 720, 405));
            drawing.FillRectangle(new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#367A88")), new Rect(0, 240, 720, 165));
            drawing.DrawEllipse(new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#E3BE76")), null, new Point(535, 100), 48, 48);
            drawing.DrawText(new Avalonia.Media.FormattedText("Cindara UI review",
                System.Globalization.CultureInfo.InvariantCulture, Avalonia.Media.FlowDirection.LeftToRight,
                Avalonia.Media.Typeface.Default, 30, Avalonia.Media.Brushes.White), new Point(32, 320));
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    private sealed class ContinueWatchingUiHandler(byte[] artwork) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("test-token", request.Headers.GetValues("X-Emby-Token").Single());
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/Images/", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(artwork) });
            }

            const string north = """{"Id":"north-6","Name":"Signals from Home","Type":"Episode","SeriesId":"north","SeriesName":"Northstar","ParentIndexNumber":1,"IndexNumber":6,"ImageTags":{"Primary":"tag"},"UserData":{"PlayedPercentage":42}}""";
            const string sky = """{"Id":"sky-10","Name":"The Crossing","Type":"Episode","SeriesId":"sky","SeriesName":"Skyward","ParentIndexNumber":1,"IndexNumber":10,"ImageTags":{"Primary":"tag"},"UserData":{"PlayedPercentage":95}}""";
            var json = path switch
            {
                "/Users/first/Items/Resume" => $$$"""
                    {"Items":[{{{north}}},{{{sky}}},
                    {"Id":"sky-9","Name":"Old episode","Type":"Episode","SeriesId":"sky","UserData":{"PlayedPercentage":30}},
                    {"Id":"river-season","Name":"Season 1","Type":"Season","SeriesId":"river","SeriesName":"Riverbend","UserData":{"PlayedPercentage":20}},
                    {"Id":"moon","Name":"Moon Garden","Type":"Movie","ImageTags":{"Primary":"tag"},"UserData":{"PlayedPercentage":58}}]}
                    """,
                "/Shows/NextUp" => $$$"""
                    {"Items":[{{{north}}},{{{sky}}},
                    {"Id":"river-1","Name":"First Light","Type":"Episode","SeriesId":"river","SeriesName":"Riverbend","ImageTags":{"Primary":"tag"}},
                    {"Id":"harbor-1","Name":"The Arrival","Type":"Episode","SeriesId":"harbor","SeriesName":"Harbor Lights","ImageTags":{"Primary":"tag"}},
                    {"Id":"orbit-1","Name":"New Horizons","Type":"Episode","SeriesId":"orbit","SeriesName":"Orbit","ImageTags":{"Primary":"tag"}},
                    {"Id":"garden-1","Name":"A New Season","Type":"Episode","SeriesId":"garden","SeriesName":"Wild Gardens","ImageTags":{"Primary":"tag"}},
                    {"Id":"summit-1","Name":"The Ascent","Type":"Episode","SeriesId":"summit","SeriesName":"Summit","ImageTags":{"Primary":"tag"}}]}
                    """,
                "/Shows/sky/Episodes" => """
                    {"Items":[{"Id":"sky-11","Name":"Beyond the Clouds","Type":"Episode","SeriesId":"sky","SeriesName":"Skyward",
                    "ParentIndexNumber":1,"IndexNumber":11,"ImageTags":{"Primary":"tag"},"UserData":{"PlayedPercentage":0}}]}
                    """,
                "/Users/first/Views" => """{"Items":[{"Id":"tv","Name":"TV","CollectionType":"tvshows"}]}""",
                "/Users/first/Items" when request.RequestUri.Query.Contains("ParentId=sky&", StringComparison.Ordinal) =>
                    """{"Items":[{"UserData":{"LastPlayedDate":"2026-09-22T15:00:00Z"}}]}""",
                "/Users/first/Items" when request.RequestUri.Query.Contains("ParentId=north&", StringComparison.Ordinal) =>
                    """{"Items":[{"UserData":{"LastPlayedDate":"2026-09-21T15:00:00Z"}}]}""",
                "/Users/first/Items" => """{"Items":[]}""",
                "/Users/first/Items/Latest" => $"[{north}]",
                _ => throw new InvalidOperationException($"Unexpected UI test endpoint: {path}"),
            };
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    [Theory]
    [InlineData("en", 1920)]
    [InlineData("en", 3840)]
    [InlineData("qps-plocm", 1920)]
    public Task LibrariesPageThroughMediaAndRestoreFocusAndScrollFromSummaryAndHome(string cultureName, int width) =>
        TestAppBuilder.Run(async () =>
        {
            using var culture = new CultureScope(cultureName);
            using var fixture = new ShellFixture(preferences: new PresentationPreferences(ReducedMotion: true));
            fixture.Window.WindowState = WindowState.Normal;
            fixture.Window.Width = width;
            fixture.Window.Height = width * 9 / 16;
            fixture.Preview.WithLibraries = true;
            fixture.SignIn();
            var homeLibrary = fixture.Gallery.GetVisualDescendants().OfType<Button>()
                .Single(button => button.DataContext is MediaLibrary { Id: "movies" });
            fixture.Click(homeLibrary);
            var browser = fixture.Shell.LibraryView;
            await fixture.Model.LibraryBrowser!.OpenLibraryCommand.ExecutionTask!;
            fixture.Flush();
            Assert.Equal("Libraries", fixture.Shell.Destination);
            Assert.False(fixture.Shell.FindControl<StackPanel>("LibrariesUnavailable")!.IsEffectivelyVisible);
            Assert.Equal(40, fixture.Model.LibraryBrowser!.Items.Count);
            Assert.Equal("movie-0", Assert.IsType<MediaPreviewCardViewModel>(Focused(fixture.Window).DataContext).Id);
            Assert.InRange(Focused(fixture.Window).Bounds.Width / 206, 1.195, 1.205);
            Assert.Equal(310 * 1.2, Focused(fixture.Window).Bounds.Height, precision: 6);
            var firstPageOffset = browser.FindControl<ScrollViewer>("LibraryScroll")!.Offset;
            fixture.Input.Press(cultureName == "qps-plocm" ? ControllerAction.NavigateLeft : ControllerAction.NavigateRight);
            Assert.Equal("movie-1", Assert.IsType<MediaPreviewCardViewModel>(Focused(fixture.Window).DataContext).Id);
            fixture.Input.Press(ControllerAction.NavigateDown);
            fixture.Input.Press(ControllerAction.NavigateDown);
            fixture.Flush();
            var card = Focused(fixture.Window);
            Assert.Equal("movie-11", Assert.IsType<MediaPreviewCardViewModel>(card.DataContext).Id);
            AssertInsideWindow(fixture.Window, card);
            var scroll = browser.FindControl<ScrollViewer>("LibraryScroll")!;
            var offset = scroll.Offset;
            Assert.True(offset.Y > 0);
            fixture.Input.Press(ControllerAction.Accept);
            fixture.Flush();
            Assert.True(fixture.IsModalVisible);
            Assert.Equal(Loc.Get("Action.Back"), Assert.IsType<Button>(Focused(fixture.Window)).Content);
            fixture.Input.Press(ControllerAction.Back);
            fixture.Flush();
            Assert.Same(card, Focused(fixture.Window));
            Assert.Equal(offset, scroll.Offset);
            Capture(fixture.Window, $"library-{cultureName}-{width}");

            fixture.Click(fixture.Shell.FindControl<Button>("DestinationBackButton")!);
            Assert.True(fixture.Model.IsDesignGalleryVisible);
            Assert.Same(homeLibrary, Focused(fixture.Window));
            fixture.Click(homeLibrary);
            Assert.Equal("movie-11", Assert.IsType<MediaPreviewCardViewModel>(Focused(fixture.Window).DataContext).Id);
            Assert.Same(card, Focused(fixture.Window));
            Assert.Equal(offset, scroll.Offset);
            Assert.Equal(1, fixture.Preview.LibraryCalls);

            fixture.Click(browser.FindControl<Button>("NextLibraryPage")!);
            await fixture.Model.LibraryBrowser.LoadPageCommand.ExecutionTask!;
            fixture.Flush();
            Assert.Equal(7, fixture.Model.LibraryBrowser.Items.Count);
            Assert.Equal("movie-40", Assert.IsType<MediaPreviewCardViewModel>(Focused(fixture.Window).DataContext).Id);
            Assert.False(browser.FindControl<Button>("NextLibraryPage")!.IsEffectivelyEnabled);
            fixture.Click(browser.FindControl<Button>("PreviousLibraryPage")!);
            await fixture.Model.LibraryBrowser.LoadPageCommand.ExecutionTask!;
            fixture.Flush();
            Assert.Equal("movie-0", Assert.IsType<MediaPreviewCardViewModel>(Focused(fixture.Window).DataContext).Id);
            Assert.Equal(firstPageOffset, scroll.Offset);
        });

    [Fact]
    public Task LibraryCancellationRetryAndAccountBoundaryAreExplicit() => TestAppBuilder.Run(async () =>
    {
        using var fixture = new ShellFixture();
        fixture.Preview.WithLibraries = true;
        fixture.Preview.PauseLibrary = true;
        fixture.SignIn();
        fixture.Click(fixture.Gallery.GetVisualDescendants().OfType<Button>()
            .Single(button => button.DataContext is MediaLibrary { Id: "movies" }));
        Assert.Equal("CancelLibraryLoading", Focused(fixture.Window).Name);
        fixture.Input.Press(ControllerAction.Back);
        await fixture.Model.LibraryBrowser!.LoadPageCommand.ExecutionTask!;
        fixture.Flush();
        Assert.Equal("RetryLibraryLoading", Focused(fixture.Window).Name);
        Assert.True(fixture.Model.IsAuthenticatedVisible);
        fixture.Preview.PauseLibrary = false;
        fixture.Input.Press(ControllerAction.Accept);
        await fixture.Model.LibraryBrowser.LoadPageCommand.ExecutionTask!;
        fixture.Flush();
        Assert.Equal(40, fixture.Model.LibraryBrowser!.Items.Count);
        var oldBrowser = fixture.Model.LibraryBrowser;
        fixture.Model.BackToSessionsCommand.Execute(null);
        fixture.Flush();
        Assert.Null(fixture.Model.LibraryBrowser);
        Assert.Empty(oldBrowser.Items);
        Assert.Equal("SavedAccountButton", Focused(fixture.Window).Name);
    });

    [Fact]
    public Task NextUpCardsHaveWorkingSummaryAndRestoreTheirHomeFocus() => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.Preview.WithLibraries = true;
        fixture.SignIn();
        var nextUp = fixture.Gallery.GetVisualDescendants().OfType<Button>()
            .Single(button => button.DataContext is MediaPreviewCardViewModel { Id: "next-up" });
        fixture.Click(nextUp);
        Assert.True(fixture.IsModalVisible);
        fixture.Input.Press(ControllerAction.Back);
        Assert.Same(nextUp, Focused(fixture.Window));
    });

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
            Button launcher;
            if (signedIn)
            {
                fixture.SignIn();
                fixture.OpenSettings();
                fixture.Click(fixture.Shell.FindControl<Button>("SettingsDiagnosticsCategory")!);
                launcher = fixture.Shell.FindControl<Button>("OpenDiagnosticsButton")!;
            }
            else
            {
                launcher = fixture.Window.FindControl<Button>("DiagnosticsButton")!;
            }

            fixture.Click(launcher);
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
            Assert.Same(launcher, Focused(fixture.Window));
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
        fixture.Preview.WithLibraries = true;
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
    public Task SettingsIncludesLibraryLayoutAndHomeReturnDoesNotReload() => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.Preview.WithLibraries = true;
        fixture.SignIn();
        fixture.OpenSettings();
        Assert.Equal("Settings", fixture.Shell.Destination);
        Assert.Equal("SettingsPreferencesCategory", Focused(fixture.Window).Name);
        var categories = fixture.Shell.FindControl<StackPanel>("SettingsCategories")!
            .Children.OfType<Button>().ToArray();
        Assert.Equal(new[]
            {
                Loc.Get("Settings.Preferences"), Loc.Get("LibraryLayout.Title"),
                Loc.Get("Settings.Application"), Loc.Get("Diagnostics.Title"),
            },
            categories.Select(button => button.Content));
        var actions = fixture.Shell.FindControl<StackPanel>("SettingsActions")!.Children.OfType<Button>().ToArray();
        Assert.Equal(new[] { Loc.Get("Language.Selection") },
            actions.Select(button => button.Content));
        fixture.Input.Press(ControllerAction.NavigateRight);
        Assert.Equal("SettingsLanguageButton", Focused(fixture.Window).Name);
        fixture.Input.Press(ControllerAction.Accept);
        Assert.True(fixture.IsModalVisible);
        Assert.Equal(Loc.Get("Language.English"), Assert.IsType<Button>(Focused(fixture.Window)).Content);
        fixture.Input.Press(ControllerAction.Back);
        Assert.Equal("SettingsLanguageButton", Focused(fixture.Window).Name);
        fixture.Input.Press(ControllerAction.NavigateLeft);
        Assert.Equal("SettingsPreferencesCategory", Focused(fixture.Window).Name);
        fixture.Input.Press(ControllerAction.NavigateDown);
        Assert.Equal("SettingsLibraryCategory", Focused(fixture.Window).Name);
        fixture.Input.Press(ControllerAction.NavigateDown);
        Assert.Equal("SettingsApplicationCategory", Focused(fixture.Window).Name);
        fixture.Input.Press(ControllerAction.NavigateRight);
        Assert.Equal("ExitButton", Focused(fixture.Window).Name);
        fixture.Input.Press(ControllerAction.NavigateDown);
        Assert.Equal("BackHomeButton", Focused(fixture.Window).Name);
        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();
        Assert.True(fixture.Model.IsDesignGalleryVisible);
        Assert.Equal(1, fixture.Preview.Calls);
        Assert.Equal("GallerySettingsButton", Focused(fixture.Window).Name);
    });

    [Theory]
    [InlineData("Home")]
    [InlineData("Libraries")]
    public Task ReenteringSettingsStartsOnLanguageWithoutDiscardingInPageFocus(string destination) => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.Preview.WithLibraries = true;
        fixture.SignIn();
        fixture.OpenSettings();
        fixture.Input.Press(ControllerAction.NavigateDown);
        Assert.Equal("SettingsLibraryCategory", Focused(fixture.Window).Name);
        fixture.Input.Press(ControllerAction.NavigateDown);
        Assert.Equal("SettingsApplicationCategory", Focused(fixture.Window).Name);
        fixture.Input.Press(ControllerAction.NavigateRight);
        Assert.Equal("ExitButton", Focused(fixture.Window).Name);
        fixture.Input.Press(ControllerAction.NavigateLeft);
        Assert.Equal("SettingsApplicationCategory", Focused(fixture.Window).Name);
        fixture.Input.Press(ControllerAction.NavigateLeft);
        Assert.Equal("DestinationBackButton", Focused(fixture.Window).Name);
        fixture.Input.Press(ControllerAction.NavigateRight);
        Assert.Equal("SettingsApplicationCategory", Focused(fixture.Window).Name);

        if (destination == "Home")
        {
            fixture.Input.Press(ControllerAction.NavigateRight);
            fixture.Input.Press(ControllerAction.NavigateDown);
            Assert.Equal("BackHomeButton", Focused(fixture.Window).Name);
            fixture.Input.Press(ControllerAction.Accept);
            fixture.Flush();
            fixture.OpenSettings();
        }
        else
        {
            fixture.Click(fixture.Shell.FindControl<Button>("DestinationBackButton")!);
            fixture.Click(FirstLibraryShortcut(fixture));
            fixture.Click(fixture.Shell.FindControl<Button>("DestinationBackButton")!);
            fixture.OpenSettings();
        }

        Assert.Equal("SettingsPreferencesCategory", Focused(fixture.Window).Name);
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
        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();
        Assert.False(fixture.Model.IsBusy);
        Assert.Equal(Loc.Get("Status.PreviewCanceled"), fixture.Model.StatusMessage);
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
        Assert.Equal("SettingsPreferencesCategory", Focused(fixture.Window).Name);
    });

    [Fact]
    public Task NonHomeDestinationsUseOneFullScreenBackPathAndRestoreTheirHomeSource() => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.Preview.WithLibraries = true;
        fixture.SignIn();
        foreach (var destination in Destinations)
        {
            var source = destination switch
            {
                "Libraries" => FirstLibraryShortcut(fixture),
                "Downloads" => fixture.Gallery.FindControl<Button>("GalleryDownloadsButton")!,
                _ => fixture.Gallery.GetVisualDescendants().OfType<Button>()
                    .Single(button => AutomationProperties.GetName(button) == Loc.Get("Nav.Search")),
            };
            fixture.Click(source);
            Assert.Equal(destination, fixture.Shell.Destination);
            Assert.True(fixture.Shell.FindControl<Grid>("DestinationHeader")!.IsEffectivelyVisible);
            Assert.Equal(Loc.Get($"Nav.{destination}"),
                fixture.Shell.FindControl<TextBlock>("DestinationTitle")!.Text);
            Assert.Null(fixture.Shell.FindControl<Control>("NavigationRail"));
            Assert.Null(fixture.Shell.FindControl<Button>("ReturnHomeButton"));
            fixture.Click(fixture.Shell.FindControl<Button>("DestinationBackButton")!);
            Assert.True(fixture.Model.IsDesignGalleryVisible);
            Assert.Same(source, Focused(fixture.Window));
        }
    });

    [Fact]
    public Task SearchSupportsKeyboardOverlayCombinedGridAndExactReturnState() => TestAppBuilder.Run(async () =>
    {
        using var fixture = new ShellFixture();
        fixture.SignIn();
        fixture.Click(fixture.Gallery.GetVisualDescendants().OfType<Button>()
            .Single(button => AutomationProperties.GetName(button) == Loc.Get("Nav.Search")));
        var search = fixture.Model.SearchBrowser!;
        var view = fixture.Shell.SearchView;
        var query = view.FindControl<TextBox>("SearchTextBox")!;
        Assert.Equal("Search", fixture.Shell.Destination);
        Assert.True(view.IsEffectivelyVisible);
        Assert.False(fixture.Shell.FindControl<StackPanel>("PlaceholderPage")!.IsEffectivelyVisible);
        Assert.Same(query, Focused(fixture.Window));
        Assert.Equal(Loc.Get("Search.Name"), AutomationProperties.GetName(query));
        Assert.Equal(Loc.Get("Search.Help"), AutomationProperties.GetHelpText(query));

        fixture.Input.Press(ControllerAction.Accept);
        Assert.True(fixture.IsModalVisible);
        foreach (var character in "space")
        {
            fixture.Click(fixture.Modal.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, character.ToString())));
        }
        fixture.Click(fixture.Modal.GetVisualDescendants().OfType<Button>()
            .Single(button => Equals(button.Content, Loc.Get("Keyboard.Done"))));
        Assert.False(fixture.IsModalVisible);
        await search.LoadPageCommand.ExecuteAsync(0);
        fixture.Flush();

        Assert.Equal(MediaSearchPage.PageSize, search.Items.Count);
        var cards = view.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Classes.Contains("search-card")).ToArray();
        Assert.Equal(MediaSearchPage.PageSize, cards.Length);
        var selected = cards[17];
        selected.Focus();
        view.FindControl<ScrollViewer>("SearchScroll")!.Offset = new Vector(0, 500);
        fixture.Flush();
        var offset = view.FindControl<ScrollViewer>("SearchScroll")!.Offset;
        fixture.Click(selected);
        Assert.True(fixture.IsModalVisible);
        fixture.Input.Press(ControllerAction.Back);
        Assert.Same(selected, Focused(fixture.Window));

        fixture.Click(fixture.Shell.FindControl<Button>("DestinationBackButton")!);
        fixture.Click(fixture.Gallery.FindControl<Button>("GalleryDownloadsButton")!);
        fixture.Click(fixture.Shell.FindControl<Button>("DestinationBackButton")!);
        var searchSource = fixture.Gallery.GetVisualDescendants().OfType<Button>()
            .Single(button => AutomationProperties.GetName(button) == Loc.Get("Nav.Search"));
        fixture.Click(searchSource);
        Assert.Equal("space", query.Text);
        Assert.Same(selected, Focused(fixture.Window));
        Assert.Equal(offset, view.FindControl<ScrollViewer>("SearchScroll")!.Offset);
        fixture.Input.Press(ControllerAction.Back);
        Assert.True(fixture.Model.IsDesignGalleryVisible);
        Assert.Same(searchSource, Focused(fixture.Window));
    });

    [Theory]
    [InlineData(720, 480, "en")]
    [InlineData(800, 600, "qps-ploc")]
    [InlineData(1280, 680, "en")]
    [InlineData(1280, 800, "qps-plocm")]
    [InlineData(1366, 768, "en")]
    [InlineData(1920, 1080, "en")]
    public Task ReferenceAlignedSearchLibrariesAndSettingsStayInsideActualClient(
        int width, int height, string cultureName) => TestAppBuilder.Run(async () =>
    {
        using var culture = new CultureScope(cultureName);
        using var fixture = new ShellFixture(preferences: new PresentationPreferences(1.5));
        fixture.Window.WindowState = WindowState.Normal;
        fixture.Window.Width = width;
        fixture.Window.Height = height;
        fixture.Preview.WithLibraries = true;
        fixture.SignIn();

        fixture.Click(fixture.Gallery.GetVisualDescendants().OfType<Button>()
            .Single(button => AutomationProperties.GetName(button) == Loc.Get("Nav.Search")));
        var searchView = fixture.Shell.SearchView;
        var search = fixture.Model.SearchBrowser!;
        var query = searchView.FindControl<TextBox>("SearchTextBox")!;
        query.Text = "reference";
        await search.LoadPageCommand.ExecuteAsync(0);
        fixture.Flush();

        AssertInsideWindow(fixture.Window, query);
        AssertInsideWindow(fixture.Window, searchView.FindControl<Button>("SearchKeyboardButton")!);
        Assert.Equal(width >= 1920,
            fixture.Shell.FindControl<TextBlock>("DestinationBrand")!.IsEffectivelyVisible);
        AssertInsideWindow(fixture.Window, fixture.Window.FindControl<Button>("DiagnosticsButton")!);
        var searchCards = searchView.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Classes.Contains("search-card")).ToArray();
        Assert.Equal(MediaSearchPage.PageSize, searchCards.Length);
        searchCards[^1].Focus();
        searchCards[^1].BringIntoView();
        fixture.Flush();
        Assert.Same(searchCards[^1], Focused(fixture.Window));
        AssertInsideWindow(fixture.Window, searchCards[^1]);

        query.Focus();
        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();
        Assert.True(fixture.IsModalVisible);
        AssertInsideWindow(fixture.Window, Focused(fixture.Window));
        var keyboardKeys = fixture.Modal.GetVisualDescendants().OfType<UniformGrid>()
            .Single().Children.OfType<Button>().ToArray();
        keyboardKeys[^1].Focus();
        keyboardKeys[^1].BringIntoView();
        fixture.Flush();
        AssertInsideWindow(fixture.Window, keyboardKeys[^1]);
        var done = fixture.Modal.GetVisualDescendants().OfType<Button>()
            .Single(button => Equals(button.Content, Loc.Get("Keyboard.Done")));
        done.Focus();
        done.BringIntoView();
        fixture.Flush();
        AssertInsideWindow(fixture.Window, done);
        fixture.Input.Press(ControllerAction.Back);
        Assert.False(fixture.IsModalVisible);
        Assert.Same(query, Focused(fixture.Window));

        fixture.Click(fixture.Shell.FindControl<Button>("DestinationBackButton")!);
        fixture.Click(FirstLibraryShortcut(fixture));
        var libraryView = fixture.Shell.LibraryView;
        fixture.Click(libraryView.FindControl<ItemsControl>("LibraryChoices")!
            .GetVisualDescendants().OfType<Button>().Single());
        await fixture.Model.LibraryBrowser!.OpenLibraryCommand.ExecutionTask!;
        fixture.Flush();
        var libraryCards = libraryView.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Classes.Contains("card")).ToArray();
        libraryCards[^1].Focus();
        libraryCards[^1].BringIntoView();
        fixture.Flush();
        AssertInsideWindow(fixture.Window, libraryCards[^1]);

        fixture.Click(fixture.Shell.FindControl<Button>("DestinationBackButton")!);
        fixture.OpenSettings();
        Assert.Equal("SettingsPreferencesCategory", Focused(fixture.Window).Name);
        AssertInsideWindow(fixture.Window, Focused(fixture.Window));
        Assert.All(fixture.Shell.FindControl<StackPanel>("SettingsCategories")!
            .GetVisualDescendants().OfType<Button>(), button =>
            Assert.Single(Assert.Single(button.GetVisualDescendants().OfType<TextBlock>())
                .TextLayout.TextLines));
        fixture.Input.Press(ControllerAction.NavigateRight);
        Assert.Equal("SettingsLanguageButton", Focused(fixture.Window).Name);
        AssertInsideWindow(fixture.Window, Focused(fixture.Window));
        fixture.Input.Press(ControllerAction.NavigateLeft);
        fixture.Input.Press(ControllerAction.NavigateDown);
        fixture.Input.Press(ControllerAction.NavigateDown);
        Assert.Equal("SettingsApplicationCategory", Focused(fixture.Window).Name);
        fixture.Input.Press(ControllerAction.NavigateRight);
        Assert.Equal("ExitButton", Focused(fixture.Window).Name);
        AssertInsideWindow(fixture.Window, Focused(fixture.Window));
        AssertInsideWindow(fixture.Window, fixture.Window.FindControl<Button>("DiagnosticsButton")!);
    });

    [Fact]
    public Task SearchKeyboardOverlayHonorsMaximumQueryLength() => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.SignIn();
        fixture.Click(fixture.Gallery.GetVisualDescendants().OfType<Button>()
            .Single(button => AutomationProperties.GetName(button) == Loc.Get("Nav.Search")));
        var view = fixture.Shell.SearchView;
        var query = view.FindControl<TextBox>("SearchTextBox")!;
        query.Text = new string('A', query.MaxLength);
        query.CaretIndex = query.Text.Length;
        fixture.Input.Press(ControllerAction.Accept);
        var draft = fixture.Modal.GetVisualDescendants().OfType<TextBox>().Single();
        fixture.Click(fixture.Modal.GetVisualDescendants().OfType<Button>()
            .Single(button => Equals(button.Content, "b")));
        Assert.Equal(query.MaxLength, draft.Text!.Length);
        fixture.ClickContent(Loc.Get("Keyboard.Done"));

        Assert.Equal(query.MaxLength, query.Text.Length);
        Assert.Equal(new string('A', query.MaxLength), query.Text);
    });

    [Fact]
    public Task LibraryReferenceGridExposesFiltersSortAndAllLetterChoices() => TestAppBuilder.Run(async () =>
    {
        using var fixture = new ShellFixture();
        fixture.Preview.WithLibraries = true;
        fixture.SignIn();
        fixture.Click(FirstLibraryShortcut(fixture));
        var view = fixture.Shell.LibraryView;
        fixture.Click(view.FindControl<ItemsControl>("LibraryChoices")!
            .GetVisualDescendants().OfType<Button>().Single());
        await fixture.Model.LibraryBrowser!.OpenLibraryCommand.ExecutionTask!;
        fixture.Flush();
        var model = fixture.Model.LibraryBrowser;

        fixture.Click(view.FindControl<Button>("FilterFavoritesButton")!);
        await model.SetFilterCommand.ExecutionTask!;
        fixture.Click(view.FindControl<Button>("SortDescendingButton")!);
        await model.SetSortDirectionCommand.ExecutionTask!;
        var letters = view.FindControl<StackPanel>("LetterChoices")!
            .Children.OfType<Button>().ToArray();
        Assert.Equal(27, letters.Length);
        fixture.Click(letters.Single(button => Equals(button.Tag, "M")));
        await model.SetLetterCommand.ExecutionTask!;
        fixture.Flush();

        Assert.Equal(MediaLibraryFilter.Favorites, model.SelectedFilter);
        Assert.Equal(MediaLibrarySortDirection.Descending, model.SelectedSortDirection);
        Assert.Equal('M', model.SelectedLetter);
        Assert.Equal(Loc.Get("State.Selected"),
            AutomationProperties.GetItemStatus(view.FindControl<Button>("FilterFavoritesButton")!));
        Assert.Equal(Loc.Get("State.Selected"),
            AutomationProperties.GetItemStatus(view.FindControl<Button>("SortDescendingButton")!));
        Assert.Equal(Loc.Get("State.Selected"),
            AutomationProperties.GetItemStatus(letters.Single(button => Equals(button.Tag, "M"))));
        var cards = view.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Classes.Contains("card")).ToArray();
        Assert.Equal(40, cards.Length);
        var columns = view.FindControl<ItemsControl>("LibraryCards")!
            .GetVisualDescendants().OfType<UniformGrid>().Single().Columns;
        Assert.InRange(columns, 2, 9);
        cards[columns - 1].Focus();
        fixture.Input.Press(ControllerAction.NavigateRight);
        Assert.Equal("M", Assert.IsType<Button>(Focused(fixture.Window)).Tag);
    });

    [Fact]
    public Task LanguageDialogTrapsFocusAndRestoresItsSettingsLauncher() => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.SignIn();
        fixture.OpenSettings();
        fixture.Input.Press(ControllerAction.NavigateRight);
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
        fixture.Input.Press(ControllerAction.NavigateDown);
        Assert.Equal("SettingsApplicationCategory", Focused(fixture.Window).Name);
        fixture.Input.Press(ControllerAction.NavigateRight);
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
    public Task SavedServerAndAccountStepsRestoreSessionsAndClearPreviousHome() => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        Assert.True(fixture.Model.IsServerSelectionVisible);
        Assert.Equal(Loc.Get("Auth.StepServer"),
            fixture.Window.FindControl<TextBlock>("AuthenticationProgress")!.Text);
        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();
        Assert.True(fixture.Model.AreSavedSessionsVisible);
        Assert.Equal(Loc.Get("Auth.StepAccount"),
            fixture.Window.FindControl<TextBlock>("AuthenticationProgress")!.Text);
        var accountButtons = fixture.Window.FindControl<ItemsControl>("SavedAccountsList")!
            .GetVisualDescendants().OfType<Button>()
            .Where(button => button.Classes.Contains("settings-action")).ToArray();
        Assert.Equal(2, accountButtons.Length);
        Assert.Equal(2, fixture.Window.FindControl<ItemsControl>("SavedAccountsList")!
            .GetVisualDescendants().OfType<Button>()
            .Count(button => Equals(button.Content, Loc.Get("Auth.RemoveSaved"))));
        fixture.Click(accountButtons.Single(button =>
            button.DataContext is SessionProfile { UserId: "second" }));
        fixture.Flush();
        Assert.Equal("second", fixture.Model.SelectedSavedSession!.UserId);
        Assert.True(fixture.Model.IsDesignGalleryVisible);
        var previous = fixture.Model.DesignGallery;
        fixture.Model.BackToSessionsCommand.Execute(null);
        fixture.Flush();
        Assert.True(fixture.Model.IsServerSelectionVisible);
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
            Assert.True(fixture.Model.IsServerSelectionVisible);
            Assert.IsType<Button>(Focused(fixture.Window));
            Assert.Equal("Test library", AutomationProperties.GetName(Focused(fixture.Window)));
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
        Assert.Equal("SettingsPreferencesCategory", Focused(fixture.Window).Name);
        AssertInsideWindow(fixture.Window, Focused(fixture.Window));
        fixture.Input.Press(rtl ? ControllerAction.NavigateRight : ControllerAction.NavigateLeft);
        Assert.Equal("DestinationBackButton", Focused(fixture.Window).Name);
        fixture.Input.Press(rtl ? ControllerAction.NavigateLeft : ControllerAction.NavigateRight);
        Assert.Equal("SettingsPreferencesCategory", Focused(fixture.Window).Name);
        AssertInsideWindow(fixture.Window, Focused(fixture.Window));
        Capture(fixture.Window, $"settings-{locale}");
    });

    [Theory]
    [InlineData(720, 480)]
    [InlineData(800, 600)]
    [InlineData(1280, 720)]
    [InlineData(1280, 800)]
    [InlineData(1366, 768)]
    [InlineData(1920, 1080)]
    [InlineData(3440, 1400)]
    [InlineData(3840, 2160)]
    public Task HomeSettingsAndDialogsFitRepresentativeViewports(int width, int height) => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.Window.WindowState = WindowState.Normal;
        fixture.Window.Width = width;
        fixture.Window.Height = height;
        fixture.Preview.WithLibraries = true;
        fixture.SignIn();
        AssertInsideWindow(fixture.Window, Focused(fixture.Window));
        Capture(fixture.Window, $"media-home-{width}");
        fixture.OpenSettings();
        AssertInsideWindow(fixture.Window, Focused(fixture.Window));
        Capture(fixture.Window, $"in-app-settings-{width}");
        fixture.Input.Press(ControllerAction.NavigateRight);
        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();
        AssertInsideWindow(fixture.Window, Focused(fixture.Window));
        AssertInsideWindow(fixture.Window, fixture.Window.FindControl<Border>("ModalDialog")!);
        Capture(fixture.Window, $"language-options-{width}");
    });

    [Theory]
    [InlineData(720, 480, "qps-ploc")]
    [InlineData(1280, 800, "qps-plocm")]
    [InlineData(1920, 1080, "en")]
    public Task ResizePreservesFocusAndKeepsPseudoLocalizedDestinationsReachable(
        int width, int height, string cultureName) => TestAppBuilder.Run(async () =>
    {
        using var culture = new CultureScope(cultureName);
        using var fixture = new ShellFixture(preferences: new PresentationPreferences(1.5));
        fixture.Window.WindowState = WindowState.Normal;
        fixture.Window.Width = width;
        fixture.Window.Height = height;
        fixture.Preview.WithLibraries = true;
        fixture.SignIn();
        fixture.OpenSettings();
        var focused = Focused(fixture.Window);
        Assert.Equal("SettingsPreferencesCategory", focused.Name);
        AssertInsideWindow(fixture.Window, focused);

        fixture.Window.Width = width == 720 ? 1366 : 720;
        fixture.Window.Height = width == 720 ? 768 : 480;
        fixture.Flush();
        Assert.Same(focused, Focused(fixture.Window));
        AssertInsideWindow(fixture.Window, focused);

        foreach (var destination in Destinations)
        {
            fixture.Click(fixture.Shell.FindControl<Button>("DestinationBackButton")!);
            var source = destination switch
            {
                "Libraries" => FirstLibraryShortcut(fixture),
                "Downloads" => fixture.Gallery.FindControl<Button>("GalleryDownloadsButton")!,
                _ => fixture.Gallery.GetVisualDescendants().OfType<Button>()
                    .Single(button => AutomationProperties.GetName(button) == Loc.Get("Nav.Search")),
            };
            fixture.Click(source);
            if (destination == "Libraries")
            {
                await fixture.Model.LibraryBrowser!.OpenLibraryCommand.ExecutionTask!;
            }
            fixture.Flush();
            AssertInsideWindow(fixture.Window, Focused(fixture.Window));
        }
    });

    [Theory]
    [InlineData(720, 480)]
    [InlineData(1280, 800)]
    [InlineData(1920, 1080)]
    public Task AuthenticationAndOnScreenKeyboardScrollWithinViewport(int width, int height) => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture(false, new PresentationPreferences(1.5));
        fixture.Window.WindowState = WindowState.Normal;
        fixture.Window.Width = width;
        fixture.Window.Height = height;
        fixture.Flush();
        var server = fixture.Window.FindControl<TextBox>("ServerAddressTextBox")!;
        server.Focus();
        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();

        Assert.True(fixture.IsModalVisible);
        AssertInsideWindow(fixture.Window, Focused(fixture.Window));
        AssertInsideWindow(fixture.Window, fixture.Window.FindControl<Border>("ModalDialog")!);
        Assert.True(fixture.Modal.GetVisualDescendants().OfType<Button>().Count() > 50);
    });

    [Theory]
    [InlineData(720, 480)]
    [InlineData(1280, 720)]
    [InlineData(1366, 768)]
    [InlineData(1920, 1080)]
    public Task LibraryGridReflowsWithoutReplacingFocusedCards(int width, int height) => TestAppBuilder.Run(async () =>
    {
        using var fixture = new ShellFixture();
        fixture.Window.WindowState = WindowState.Normal;
        fixture.Window.Width = width;
        fixture.Window.Height = height;
        fixture.Preview.WithLibraries = true;
        fixture.SignIn();
        fixture.Click(fixture.Gallery.GetVisualDescendants().OfType<Button>()
            .Single(button => button.DataContext is MediaLibrary));
        await fixture.Model.LibraryBrowser!.OpenLibraryCommand.ExecutionTask!;
        fixture.Flush();
        var focused = Focused(fixture.Window);
        Assert.IsType<MediaPreviewCardViewModel>(focused.DataContext);
        AssertInsideWindow(fixture.Window, focused);

        fixture.Window.Width = width < 1000 ? 1920 : 720;
        fixture.Window.Height = width < 1000 ? 1080 : 480;
        fixture.Flush();
        Assert.Same(focused, Focused(fixture.Window));
        Assert.Contains(focused, fixture.Shell.LibraryView.GetVisualDescendants().OfType<Button>());
        AssertInsideWindow(fixture.Window, focused);
        fixture.Input.Press(ControllerAction.NavigateDown);
        fixture.Flush();
        AssertInsideWindow(fixture.Window, Focused(fixture.Window));
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

    private static Button FirstLibraryShortcut(ShellFixture fixture) =>
        fixture.Gallery.FindControl<ItemsControl>("GalleryLibraryShortcuts")!
            .GetVisualDescendants().OfType<Button>().First();

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
            LocalDiagnostics? diagnostics = null, IJellyfinMediaPreviewClient? mediaClient = null,
            LibraryLayoutSettingsStore? libraryLayoutStore = null)
        {
            Model = new MainViewModel(new ServerClient(), new AuthenticationService(savedAccounts), mediaClient ?? Preview,
                libraryLayoutStore: libraryLayoutStore);
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
            if (Model.IsServerSelectionVisible)
            {
                var preferred = Model.SelectedSavedSession;
                Model.SelectServerCommand.Execute(preferred?.Server ?? Model.SavedServers[0]);
                if (preferred is not null && Model.ServerAccounts.Contains(preferred))
                {
                    Model.SelectedSavedSession = preferred;
                }
            }

            Model.UseSavedSessionCommand.Execute(null);
            Flush();
            Assert.True(Model.IsAuthenticatedVisible);
            if (waitForHome)
            {
                Assert.True(Model.IsDesignGalleryVisible, Model.StatusMessage);
            }
        }

        public void OpenSettings() => Click(Gallery.FindControl<Button>("GallerySettingsButton")!);
        public void OpenLibraryLayoutSettings()
        {
            OpenSettings();
            Click(Shell.FindControl<Button>("SettingsLibraryCategory")!);
            Click(Shell.FindControl<Button>("SettingsLibraryLayoutButton")!);
        }
        public void Click(string name) => Click(Window.FindControl<Button>(name)!);
        public void Click(Button button)
        {
            button.Focus();
            ((IInvokeProvider)new ButtonAutomationPeer(button)).Invoke();
            Flush();
        }

        public void ClickContent(string text) =>
            Click(Modal.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, text)));

        public void ClickAutomationId(string id) =>
            Click(Modal.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == id));

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
        public Task<byte[]?> GetLibraryArtworkAsync(AuthenticatedSession session, string itemId,
            CancellationToken cancellationToken = default) => ArtworkGate.Task.WaitAsync(cancellationToken);
        public TaskCompletionSource<byte[]?> ArtworkGate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ProgressiveArtwork { get; set; }
        public void ClearImageCache() { }
        public int LibraryCalls { get; private set; }
        public bool WithLibraries { get; set; }
        public bool LayoutLibraries { get; set; }
        public bool PauseLibrary { get; set; }
        public int SearchCalls { get; private set; }
        public Task<MediaSearchPage> SearchAsync(AuthenticatedSession session, string query, int startIndex,
            CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            var types = new[] { "Movie", "Series", "Season", "Episode" };
            var count = Math.Min(MediaSearchPage.PageSize, 80 - startIndex);
            return Task.FromResult(new MediaSearchPage(
                Enumerable.Range(startIndex, count)
                    .Select(index => new MediaPreviewItem(
                        $"search-{index}", $"{query} {index}", string.Empty, types[index % types.Length],
                        null, null, "Search result.", string.Empty, null))
                    .ToArray(),
                startIndex,
                80));
        }
        public async Task<MediaLibraryPage> GetLibraryPageAsync(AuthenticatedSession session, MediaLibrary library,
            int startIndex, CancellationToken cancellationToken = default)
        {
            LibraryCalls++;
            if (PauseLibrary)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return new MediaLibraryPage(Enumerable.Range(startIndex, Math.Min(40, 47 - startIndex))
                .Select(index => new MediaPreviewItem($"movie-{index}", $"Movie {index}", "2026", "Movie",
                    null, null, "A media description.", "1h 5m", null)
                {
                    ArtworkItemId = ProgressiveArtwork ? $"movie-{index}" : null,
                }).ToArray(), startIndex, 47);
        }

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
            if (LayoutLibraries)
            {
                MediaLibrary[] libraries =
                [
                    new("tv", "TV", "tvshows"), new("collections", "Collections", "boxsets"),
                    new("movies", "Movies", "movies"), new("people", "People", "people"), new("anime", "Anime", "tvshows"),
                ];
                return new MediaPreviewHome(item, [item],
                    libraries.Select(library => new MediaPreviewRail(library.Id, library.Name,
                        [item with { Id = library.Id, Name = library.Name }], library.Name)).ToArray())
                {
                    Libraries = libraries,
                };
            }

            return new MediaPreviewHome(item,
                WithLibraries ? [item, item with { Id = "next-up", Name = "Next episode", MediaType = "Episode" }] : [item],
                [new MediaPreviewRail("movies", "Movies", [item])])
            {
                Libraries = WithLibraries ? [new MediaLibrary("movies", "Movies", "movies")] : [],
            };
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
