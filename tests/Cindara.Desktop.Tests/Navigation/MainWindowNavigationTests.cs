using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
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
    private static readonly string[] MovieSummaryIds = ["MovieVideoSummary", "MovieAudioSummary", "MovieSubtitleSummary"];

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    [InlineData(3440, 1440)]
    public Task FreshHomeRowsStartAtTheirFirstPoster(int width, int height) => TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.Window.WindowState = WindowState.Normal;
        fixture.Window.Width = width;
        fixture.Window.Height = height;
        fixture.Preview.HomeItemsPerRail = 20;
        fixture.Preview.LayoutLibraries = true;
        var focusTrace = new List<string>();
        fixture.Window.AddHandler(InputElement.GotFocusEvent, (_, args) =>
        {
            if (args.Source is Button button)
                focusTrace.Add(button.DataContext is MediaPreviewCardViewModel item ? item.Id : button.Name ?? "button");
        });
        fixture.Preview.HomeGate = new TaskCompletionSource();
        fixture.SignIn(waitForHome: false);
        Assert.Equal("CancelLoadingButton", Focused(fixture.Window).Name);
        fixture.Preview.HomeGate.SetResult();
        fixture.Flush();
        Assert.True(fixture.Model.IsDesignGalleryVisible);
        Assert.DoesNotContain(focusTrace, id => id.StartsWith("tv", StringComparison.Ordinal));
        var rows = fixture.Gallery.GetVisualDescendants().OfType<ItemsControl>()
            .Where(control => control.Classes.Contains("media-row")).ToArray();
        Assert.True(rows.Length > 1);
        foreach (var row in rows)
        {
            var scroll = row.GetVisualAncestors().OfType<ScrollViewer>().First();
            Assert.True(scroll.Offset.X == 0, $"Offset {scroll.Offset.X}; focus: {string.Join(" -> ", focusTrace)}");
            var first = row.GetVisualDescendants().OfType<Button>().First();
            Assert.InRange(first.TranslatePoint(default, scroll)!.Value.X, 0, 1);
        }
        fixture.Flush();
        foreach (var row in rows)
            Assert.Equal(0, row.GetVisualAncestors().OfType<ScrollViewer>().First().Offset.X);
    });

    [Fact]
    public Task CastOutlineFollowsControllerInsteadOfStationaryPointerAndArtworkIsRounded() => TestAppBuilder.Run(async () =>
    {
        using var fixture = new ShellFixture();
        fixture.Window.WindowState = WindowState.Normal;
        fixture.Window.Width = 1280;
        fixture.Window.Height = 720;
        fixture.Preview.WithLibraries = true;
        fixture.Preview.PopulatedMovie = true;
        fixture.SignIn();
        fixture.Click(fixture.Gallery.GetVisualDescendants().OfType<Button>()
            .Single(button => button.DataContext is MediaLibrary { Id: "movies" }));
        await fixture.Model.LibraryBrowser!.OpenLibraryCommand.ExecutionTask!;
        fixture.Flush();
        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();
        var view = fixture.Window.FindControl<MovieDetailsView>("MovieDetails")!;
        var cards = view.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Classes.Contains("cast-card")).ToArray();
        using var solid = new RenderTargetBitmap(new PixelSize(64, 64));
        using (var drawing = solid.CreateDrawingContext())
            drawing.FillRectangle(Avalonia.Media.Brushes.Magenta, new Rect(0, 0, 64, 64));
        using var stream = new MemoryStream();
        solid.Save(stream, PngBitmapEncoderOptions.Default);
        fixture.Model.MovieDetails!.Cast[0].SetImage(PreviewImage.Decode(stream.ToArray()));
        fixture.Model.MovieDetails.Cast[3].SetImage(PreviewImage.Decode(stream.ToArray()));
        var pointer = cards[0].TranslatePoint(new Point(cards[0].Bounds.Width / 2, cards[0].Bounds.Height / 2),
            fixture.Window)!.Value;
        fixture.Window.MouseMove(pointer);
        view.FindControl<Button>("SynopsisButton")!.Focus();
        fixture.Input.Press(ControllerAction.NavigateDown);
        fixture.Input.Press(ControllerAction.NavigateRight);
        fixture.Flush();
        Assert.True(cards[0].IsPointerOver);
        Assert.Same(cards[1], Focused(fixture.Window));
        Assert.Equal(default, cards[0].GetVisualDescendants().OfType<ContentPresenter>().First().BorderThickness);
        Assert.Equal(new Thickness(4), cards[1].GetVisualDescendants().OfType<ContentPresenter>().First().BorderThickness);
        fixture.Input.Press(ControllerAction.NavigateRight);
        fixture.Flush();
        Assert.Same(cards[2], Focused(fixture.Window));
        Assert.Single(cards, card => card.GetVisualDescendants().OfType<ContentPresenter>().First().BorderThickness.Left > 0);
        Assert.All(cards, card => Assert.Null(card.FocusAdorner));
        Assert.Equal(cards[0].Bounds.Size, cards[1].Bounds.Size);
        var emptyFrame = cards[1].GetVisualDescendants().OfType<Border>()
            .Single(border => border.Name == "CastArtworkFrame");
        var placeholder = emptyFrame.GetVisualDescendants().OfType<TextBlock>().Single();
        var placeholderCenter = placeholder.TranslatePoint(
            new Point(placeholder.Bounds.Width / 2, placeholder.Bounds.Height / 2), emptyFrame)!.Value;
        Assert.Equal(Avalonia.Media.TextAlignment.Center, placeholder.TextAlignment);
        Assert.InRange(Math.Abs(placeholderCenter.X - emptyFrame.Bounds.Width / 2), 0, 1);
        Assert.InRange(Math.Abs(placeholderCenter.Y - emptyFrame.Bounds.Height / 2), 0, 1);
        fixture.Input.Press(ControllerAction.NavigateRight);
        fixture.Flush();
        Assert.Same(cards[3], Focused(fixture.Window));

        var artworkFrame = cards[0].GetVisualDescendants().OfType<Border>()
            .Single(border => border.Name == "CastArtworkFrame");
        Assert.True(artworkFrame.ClipToBounds);
        Assert.True(artworkFrame.CornerRadius.TopLeft > 0);
        using var rendered = new RenderTargetBitmap(new PixelSize(1280, 720));
        rendered.Render(fixture.Window);
        var origin = artworkFrame.TranslatePoint(default, fixture.Window)!.Value;
        var corners = new[]
        {
            new Point(1, 1), new Point(artworkFrame.Bounds.Width - 2, 1),
            new Point(1, artworkFrame.Bounds.Height - 2),
            new Point(artworkFrame.Bounds.Width - 2, artworkFrame.Bounds.Height - 2),
        };
        foreach (var corner in corners)
            Assert.NotEqual(Avalonia.Media.Colors.Magenta, ReadPixel(rendered,
                new Point(origin.X + corner.X, origin.Y + corner.Y)));
        Assert.Equal(Avalonia.Media.Colors.Magenta, ReadPixel(rendered,
            new Point(origin.X + artworkFrame.Bounds.Width / 2, origin.Y + artworkFrame.Bounds.Height / 2)));
        Capture(fixture.Window, "cast-rounded-single-focus");
    });

    private static Avalonia.Media.Color ReadPixel(Bitmap bitmap, Point point)
    {
        var memory = System.Runtime.InteropServices.Marshal.AllocHGlobal(4);
        try
        {
            bitmap.CopyPixels(new PixelRect((int)point.X, (int)point.Y, 1, 1), memory, 4, 4);
            var pixel = new byte[4];
            System.Runtime.InteropServices.Marshal.Copy(memory, pixel, 0, 4);
            return Avalonia.Media.Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(memory);
        }
    }

    [Theory]
    [InlineData("en", 720, 480, 1)]
    [InlineData("en", 1280, 720, 1)]
    [InlineData("en", 2000, 838, 1)]
    [InlineData("en", 3840, 2160, 1)]
    [InlineData("en", 720, 480, 1.5)]
    [InlineData("en", 1280, 720, 1.5)]
    [InlineData("qps-plocm", 720, 480, 1)]
    [InlineData("qps-ploc", 1920, 1080, 1)]
    public Task MovieOverviewFitsWithoutVerticalScrollingAndExpandsWithoutLosingPosition(
        string culture, int width, int height, double textScale) => TestAppBuilder.Run(async () =>
    {
        using var cultureScope = new CultureScope(culture);
        using var fixture = new ShellFixture(preferences: new PresentationPreferences(TextScale: textScale));
        fixture.Window.WindowState = WindowState.Normal;
        fixture.Window.Width = width;
        fixture.Window.Height = height;
        fixture.Preview.WithLibraries = true;
        fixture.Preview.PopulatedMovie = true;
        fixture.SignIn();
        fixture.Click(fixture.Gallery.GetVisualDescendants().OfType<Button>()
            .Single(button => button.DataContext is MediaLibrary { Id: "movies" }));
        await fixture.Model.LibraryBrowser!.OpenLibraryCommand.ExecutionTask!;
        fixture.Flush();
        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();
        var view = fixture.Window.FindControl<MovieDetailsView>("MovieDetails")!;
        fixture.Flush();
        Capture(fixture.Window, $"movie-fit-{culture}-{width}-{textScale}");
        var heading = view.FindControl<Grid>("MovieHeading")!;
        var cast = view.FindControl<Grid>("CastSection")!;
        var headingBottom = heading.TranslatePoint(new(0, heading.Bounds.Height), view)!.Value.Y;
        Assert.True(headingBottom <= cast.TranslatePoint(default, view)!.Value.Y);
        var poster = view.FindControl<Border>("MoviePoster")!;
        if (poster.IsEffectivelyVisible)
            Assert.Equal(1.5, poster.Bounds.Height / poster.Bounds.Width, precision: 2);
        var summary = view.FindControl<Grid>("MediaSummary")!;
        var playbackNotice = view.FindControl<TextBlock>("PlaybackNotice")!;
        if (playbackNotice.IsEffectivelyVisible)
            Assert.True(summary.TranslatePoint(new(0, summary.Bounds.Height), view)!.Value.Y
                <= playbackNotice.TranslatePoint(default, view)!.Value.Y);
        var copy = view.FindControl<Grid>("MovieInformation")!;
        var actions = view.FindControl<Grid>("MovieActions")!;
        if (Grid.GetRow(actions) > 0)
            Assert.True(copy.TranslatePoint(new(0, copy.Bounds.Height), view)!.Value.Y
                <= actions.TranslatePoint(default, view)!.Value.Y);
        var buttons = view.FindControl<WrapPanel>("MovieButtons")!;
        Assert.True(actions.TranslatePoint(new(0, actions.Bounds.Height), view)!.Value.Y
            <= buttons.TranslatePoint(default, view)!.Value.Y);
        foreach (var control in heading.GetVisualDescendants().OfType<Control>()
            .Where(control => control.IsEffectivelyVisible && (control is TextBlock or Button)
                && !control.GetVisualAncestors().Any(parent => parent is Button)))
        {
            AssertInsideWindow(fixture.Window, control);
            var bottom = control.TranslatePoint(new(0, control.Bounds.Height), view)!.Value.Y;
            Assert.True(bottom <= headingBottom + 1, $"Overview overlap: {control.Name} ({bottom} > {headingBottom}).");
            if (control.GetVisualAncestors().Contains(copy))
                Assert.True(bottom <= copy.TranslatePoint(new(0, copy.Bounds.Height), view)!.Value.Y + 1,
                    $"Movie information overlaps actions: {control.Name}.");
        }
        var scroll = Assert.Single(view.GetVisualDescendants().OfType<ScrollViewer>());
        Assert.Equal(ScrollBarVisibility.Disabled, scroll.VerticalScrollBarVisibility);
        Assert.InRange(scroll.Extent.Height, 0, scroll.Viewport.Height + 1);
        Assert.Equal(0, scroll.Offset.Y);
        Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(),
            button => button.IsEffectivelyVisible && AutomationProperties.GetAutomationId(button) is "MovieRefresh" or "MovieRetry");
        var synopsis = view.FindControl<Button>("SynopsisButton")!;
        synopsis.Focus();
        fixture.Input.Press(ControllerAction.NavigateDown);
        Assert.Equal("Actor 0", Assert.IsType<MovieCreditViewModel>(Focused(fixture.Window).DataContext).Name);
        var forward = culture == "qps-plocm" ? ControllerAction.NavigateLeft : ControllerAction.NavigateRight;
        for (var index = 0; index < 20; index++) fixture.Input.Press(forward);
        fixture.Flush();
        var castCard = Assert.IsType<Button>(Focused(fixture.Window));
        Assert.Equal("Actor 20", Assert.IsType<MovieCreditViewModel>(castCard.DataContext).Name);
        AssertInsideWindow(fixture.Window, castCard);
        Assert.True(scroll.Offset.X > 0);
        var castOffset = scroll.Offset;
        fixture.Input.Press(ControllerAction.NavigateUp);
        Assert.Same(synopsis, Focused(fixture.Window));
        fixture.Input.Press(ControllerAction.NavigateDown);
        Assert.Same(castCard, Focused(fixture.Window));
        Assert.Equal(castOffset, scroll.Offset);
        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();
        var selectedCredits = Assert.Single(fixture.Modal.GetVisualDescendants().OfType<MovieCreditsView>());
        Assert.Contains(selectedCredits.GetVisualDescendants().OfType<Border>(),
            border => border.DataContext is MovieCreditEntry { IsSelected: true, Credit.Name: "Actor 20" });
        fixture.Input.Press(ControllerAction.Back);
        Assert.Same(castCard, Focused(fixture.Window));
        Assert.Equal(castOffset, scroll.Offset);
        fixture.Click(synopsis);
        Assert.True(fixture.IsModalVisible);
        Assert.Equal(Loc.Get("Action.Back"), Assert.IsType<Button>(Focused(fixture.Window)).Content);
        Assert.Contains(fixture.Modal.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text == fixture.Model.MovieDetails!.Overview);
        fixture.ClickContent(Loc.Get("Details.ReadBelow"));
        var synopsisScroll = fixture.Modal.GetVisualDescendants().OfType<ScrollViewer>().Single();
        if (synopsisScroll.Extent.Height > synopsisScroll.Viewport.Height + 1)
            Assert.True(synopsisScroll.Offset.Y > 0);
        fixture.Input.Press(ControllerAction.Back);
        Assert.Same(synopsis, Focused(fixture.Window));
        Assert.Equal(castOffset, scroll.Offset);
        fixture.Click(view.FindControl<Button>("CreditsButton")!);
        var credits = Assert.Single(fixture.Modal.GetVisualDescendants().OfType<MovieCreditsView>());
        Assert.InRange(credits.Columns, width >= 1280 ? 2 : 1, 20);
        AssertInsideWindow(fixture.Window, credits.BackAction);
        AssertInsideWindow(fixture.Window, credits.FindControl<Button>("NextCredits")!);
        Capture(fixture.Window, $"movie-credits-{culture}-{width}-{textScale}");
        Assert.Contains(fixture.Modal.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text!.Contains("Actor 39", StringComparison.Ordinal));
        fixture.Key(Key.PageDown);
        var creditScroll = credits.FindControl<ScrollViewer>("CreditsScroll")!;
        if (creditScroll.Extent.Height > creditScroll.Viewport.Height + 1)
            Assert.True(creditScroll.Offset.Y > 0);
        else
            Assert.False(credits.FindControl<Button>("NextCredits")!.IsEnabled);
        fixture.Input.Press(ControllerAction.Back);
        Assert.Equal(castOffset, scroll.Offset);
        Assert.Equal(0, fixture.Preview.StateWrites);
    });

    [Theory]
    [InlineData("en", 720, 480)]
    [InlineData("en", 1280, 720)]
    [InlineData("qps-plocm", 1920, 1080)]
    public Task MovieActionsUpdateIndicatorsWithoutConfirmationOrSuccessBanners(string culture, int width, int height) =>
        TestAppBuilder.Run(async () =>
        {
            using var cultureScope = new CultureScope(culture);
            using var fixture = new ShellFixture();
            fixture.Window.WindowState = WindowState.Normal;
            fixture.Window.Width = width;
            fixture.Window.Height = height;
            fixture.Preview.WithLibraries = true;
            fixture.SignIn();
            fixture.Click(fixture.Gallery.GetVisualDescendants().OfType<Button>()
                .Single(button => button.DataContext is MediaLibrary { Id: "movies" }));
            await fixture.Model.LibraryBrowser!.OpenLibraryCommand.ExecutionTask!;
            fixture.Flush();
            var card = Focused(fixture.Window);
            var page = fixture.Model.LibraryBrowser.Items;
            fixture.Input.Press(ControllerAction.Accept);
            fixture.Flush();
            var view = fixture.Window.FindControl<MovieDetailsView>("MovieDetails")!;
            Assert.True(view.IsVisible);
            Assert.False(fixture.Window.FindControl<Grid>("MainSurface")!.IsEffectivelyEnabled);
            Assert.Equal("DetailsBack", Focused(fixture.Window).Name);
            var model = fixture.Model.MovieDetails!;
            Assert.Equal(0, fixture.Preview.StateWrites);
            var favorite = view.FindControl<Button>("FavoriteButton")!;
            var heading = view.FindControl<Grid>("MovieActions")!;
            foreach (var id in MovieSummaryIds)
            {
                var summary = view.GetVisualDescendants().OfType<TextBlock>()
                    .Single(text => AutomationProperties.GetAutomationId(text) == id);
                Assert.Contains(heading, summary.GetVisualAncestors());
                Assert.InRange(summary.MaxLines, 1, 2);
                Assert.True(summary.TranslatePoint(default, heading)!.Value.Y
                    < favorite.TranslatePoint(default, heading)!.Value.Y);
            }
            favorite.BringIntoView();
            fixture.Flush();
            AssertInsideWindow(fixture.Window, favorite);
            fixture.Click(favorite);
            await model.ToggleFavoriteCommand.ExecutionTask!;
            fixture.Flush();
            Assert.Equal(Loc.Get("Details.RemoveFavorite"), favorite.Content);
            var watched = view.FindControl<Button>("WatchedButton")!;
            var unwatchedIndicator = view.FindControl<Avalonia.Controls.Shapes.Ellipse>("UnwatchedActionIndicator")!;
            var watchedIndicator = view.FindControl<Avalonia.Controls.Shapes.Path>("WatchedActionIndicator")!;
            Assert.True(unwatchedIndicator.IsEffectivelyVisible);
            Assert.False(watchedIndicator.IsEffectivelyVisible);
            fixture.Click(watched);
            await model.ToggleWatchedCommand.ExecutionTask!;
            fixture.Flush();
            Assert.False(fixture.IsModalVisible);
            Assert.True(view.IsEffectivelyEnabled);
            Assert.True(model.Details!.UserState.IsPlayed);
            Assert.Equal(2, fixture.Preview.StateWrites);
            Assert.True(watchedIndicator.IsEffectivelyVisible);
            Assert.False(unwatchedIndicator.IsEffectivelyVisible);
            Assert.Equal(Loc.Get("Details.Watched"), AutomationProperties.GetItemStatus(watched));
            Assert.Equal(Loc.Get("Details.MarkUnwatched"), AutomationProperties.GetName(watched));
            Assert.False(model.HasMessage);
            Assert.Same(watched, Focused(fixture.Window));
            fixture.Click(watched);
            await model.ToggleWatchedCommand.ExecutionTask!;
            fixture.Flush();
            Assert.False(fixture.IsModalVisible);
            Assert.True(unwatchedIndicator.IsEffectivelyVisible);
            Assert.False(watchedIndicator.IsEffectivelyVisible);
            Assert.False(model.HasMessage);
            Assert.Equal(3, fixture.Preview.StateWrites);
            Assert.Null(view.FindControl<ScrollViewer>("DetailsScroll"));
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(), button =>
                AutomationProperties.GetAutomationId(button) == "MovieRefresh");
            fixture.Input.Press(ControllerAction.Back);
            Assert.False(view.IsVisible);
            Assert.Same(card, Focused(fixture.Window));
            Assert.Same(page, fixture.Model.LibraryBrowser.Items);
            Assert.Equal(1, fixture.Preview.LibraryCalls);
        });

    [Fact]
    public Task MovieSearchBackRestoresTheExactQueryCardAndScroll() => TestAppBuilder.Run(async () =>
    {
        using var fixture = new ShellFixture();
        fixture.SignIn();
        fixture.Click(fixture.Gallery.GetVisualDescendants().OfType<Button>()
            .Single(button => AutomationProperties.GetName(button) == Loc.Get("Nav.Search")));
        var search = fixture.Model.SearchBrowser!;
        search.Query = "movie";
        await search.RefreshCommand.ExecuteAsync(null);
        fixture.Flush();
        var searchView = fixture.Shell.SearchView;
        searchView.FindControl<ListBox>("SearchRows")!.ScrollIntoView(20 / search.Rows[0].Items.Count);
        fixture.Flush();
        var card = searchView.GetVisualDescendants().OfType<Button>()
            .Single(button => button.DataContext is MediaPreviewCardViewModel { Id: "search-20" });
        card.Focus();
        card.BringIntoView();
        fixture.Flush();
        var scroll = searchView.FindControl<ListBox>("SearchRows")!
            .GetVisualDescendants().OfType<ScrollViewer>().First();
        var offset = scroll.Offset;
        var items = search.Items;
        fixture.Click(card);
        Assert.True(fixture.Model.MovieDetails!.HasDetails);
        fixture.Input.Press(ControllerAction.Back);
        fixture.Flush();
        Assert.Same(card, Focused(fixture.Window));
        Assert.Equal(offset, scroll.Offset);
        Assert.Same(items, search.Items);
        Assert.Equal("movie", search.Query);
        Assert.Equal(0, fixture.Preview.StateWrites);
    });

    [Fact]
    public Task SearchControllerTraversesBatchBoundaryWithoutPageControls() => TestAppBuilder.Run(async () =>
    {
        using var fixture = new ShellFixture();
        fixture.SignIn();
        fixture.Click(fixture.Gallery.GetVisualDescendants().OfType<Button>()
            .Single(button => AutomationProperties.GetName(button) == Loc.Get("Nav.Search")));
        var search = fixture.Model.SearchBrowser!;
        search.Query = "many";
        await search.RefreshCommand.ExecuteAsync(null);
        fixture.Flush();
        var view = fixture.Shell.SearchView;
        Assert.Equal(40, search.Items.Count);
        Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(),
            button => button.Name is "PreviousSearchPage" or "NextSearchPage");
        var rows = view.FindControl<ListBox>("SearchRows")!;
        var columns = search.Rows[0].Items.Count;
        rows.ScrollIntoView(39 / columns);
        fixture.Flush();
        var last = view.GetVisualDescendants().OfType<Button>()
            .Single(button => button.DataContext is MediaPreviewCardViewModel { Id: "search-39" });
        last.Focus();
        fixture.Input.Press(ControllerAction.NavigateDown);
        if (search.LoadMoreCommand.ExecutionTask is { } loading)
            await loading;
        fixture.Flush();
        Assert.Equal(80, search.Items.Count);
        Assert.Equal(2, fixture.Preview.SearchCalls);
        Assert.Equal("search-43", Assert.IsType<MediaPreviewCardViewModel>(
            Focused(fixture.Window).DataContext).Id);
        Assert.Equal("many", search.Query);
        Assert.All(search.Items, item => Assert.True(item.MediaType is "Movie" or "Series"));
        Assert.False(search.HasMore);
        Assert.Equal(0, fixture.Preview.StateWrites);
    });

    [Fact]
    public Task MoviePermissionFailureKeepsSessionAndOffersVisibleRefresh() => TestAppBuilder.Run(async () =>
    {
        using var fixture = new ShellFixture();
        fixture.Preview.WithLibraries = true;
        fixture.Preview.DetailError = MediaPreviewError.Forbidden;
        fixture.SignIn();
        fixture.Click(fixture.Gallery.GetVisualDescendants().OfType<Button>()
            .Single(button => button.DataContext is MediaLibrary { Id: "movies" }));
        await fixture.Model.LibraryBrowser!.OpenLibraryCommand.ExecutionTask!;
        fixture.Flush();
        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();
        Assert.True(fixture.Model.IsAuthenticatedVisible);
        Assert.False(fixture.Model.MovieDetails!.HasDetails);
        var view = fixture.Window.FindControl<MovieDetailsView>("MovieDetails")!;
        Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(),
            text => text.IsEffectivelyVisible && text.Text == Loc.Get("Error.Preview.Forbidden"));
        var retry = view.GetVisualDescendants().OfType<Button>()
            .Single(button => AutomationProperties.GetAutomationId(button) == "MovieRetry");
        Assert.True(retry.IsEffectivelyVisible);
        fixture.Preview.DetailError = null;
        fixture.Click(retry);
        await fixture.Model.MovieDetails.RefreshCommand.ExecutionTask!;
        fixture.Flush();
        Assert.True(fixture.Model.MovieDetails.HasDetails);
        Assert.False(retry.IsEffectivelyVisible);
        fixture.Model.BackToSessionsCommand.Execute(null);
        fixture.Flush();
        Assert.False(view.IsVisible);
        Assert.Null(fixture.Model.MovieDetails);
        Assert.True(fixture.Window.FindControl<Grid>("MainSurface")!.IsEffectivelyEnabled);
    });

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
                Assert.True(fixture.Window.FindControl<SeriesOverviewView>("SeriesOverview")!.IsVisible);
                Assert.Equal("library-item", fixture.Model.SeriesOverview!.Summary.Details!.Id);
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
                "/Users/first/Items/library-item" => """{"Id":"library-item","Name":"Library item","Type":"Series","UserData":{"Played":false,"IsFavorite":false}}""",
                "/Shows/library-item/Seasons" => """{"Items":[]}""",
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
        Assert.True(model.HasMore);
        fixture.Key(Key.Right);
        fixture.Input.Press(ControllerAction.NavigateDown);
        fixture.Flush();
        var focused = Focused(fixture.Window);
        var card = Assert.IsType<MediaPreviewCardViewModel>(focused.DataContext);
        Assert.Equal($"movie-{model.ColumnCount + 1}", card.Id);
        Assert.True(card.IsArtworkLoading);
        Assert.Equal(0, card.MetadataOpacity);
        Assert.False(card.ShowArtworkPlaceholder);
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
        Assert.Equal(1, card.MetadataOpacity);
        Assert.Same(focused, Focused(fixture.Window));
        AssertInsideWindow(fixture.Window, focused);
        Assert.False(model.CanRetryArtwork);
        Assert.Null(fixture.Shell.LibraryView.FindControl<Button>("CancelLibraryArtwork"));
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
            Assert.Same(fixture.Gallery.FindControl<Button>("GallerySettingsButton"), Focused(fixture.Window));
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
    public Task LibrariesIncrementallyLoadAndRestoreFocusAndScrollFromDetailsAndHome(string cultureName, int width) =>
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
            Assert.InRange(
                Focused(fixture.Window).Bounds.Height / Focused(fixture.Window).Bounds.Width,
                1.49,
                1.51);
            fixture.Input.Press(cultureName == "qps-plocm" ? ControllerAction.NavigateLeft : ControllerAction.NavigateRight);
            Assert.Equal("movie-1", Assert.IsType<MediaPreviewCardViewModel>(Focused(fixture.Window).DataContext).Id);
            fixture.Input.Press(ControllerAction.NavigateDown);
            fixture.Input.Press(ControllerAction.NavigateDown);
            fixture.Flush();
            var card = Focused(fixture.Window);
            var cardItem = Assert.IsType<MediaPreviewCardViewModel>(card.DataContext);
            Assert.Equal($"movie-{1 + (fixture.Model.LibraryBrowser.ColumnCount * 2)}", cardItem.Id);
            AssertInsideWindow(fixture.Window, card);
            var scroll = browser.FindControl<ListBox>("LibraryRows")!
                .GetVisualDescendants().OfType<ScrollViewer>().First();
            var offset = scroll.Offset;
            Assert.True(offset.Y > 0);
            fixture.Input.Press(ControllerAction.Accept);
            fixture.Flush();
            Assert.True(fixture.Window.FindControl<MovieDetailsView>("MovieDetails")!.IsVisible);
            Assert.False(fixture.IsModalVisible);
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
            Assert.Equal(cardItem.Id, Assert.IsType<MediaPreviewCardViewModel>(Focused(fixture.Window).DataContext).Id);
            Assert.Same(card, Focused(fixture.Window));
            Assert.Equal(offset, scroll.Offset);
            Assert.Equal(1, fixture.Preview.LibraryCalls);

            while (fixture.Model.LibraryBrowser.Items.IndexOf(
                       Assert.IsType<MediaPreviewCardViewModel>(Focused(fixture.Window).DataContext))
                   < fixture.Model.LibraryBrowser.Items.Count - fixture.Model.LibraryBrowser.ColumnCount)
            {
                fixture.Input.Press(ControllerAction.NavigateDown);
                fixture.Flush();
                if (fixture.Model.LibraryBrowser.LoadMoreCommand.IsRunning)
                {
                    break;
                }
            }

            if (fixture.Model.LibraryBrowser.LoadMoreCommand.ExecutionTask is { } loadMore)
            {
                await loadMore;
            }
            fixture.Flush();
            Assert.Equal(47, fixture.Model.LibraryBrowser.Items.Count);
            Assert.Equal("movie-0", fixture.Model.LibraryBrowser.Items[0].Id);
            Assert.Equal("movie-40", fixture.Model.LibraryBrowser.Items[40].Id);
            Assert.Null(browser.FindControl<Button>("NextLibraryPage"));
            Assert.Null(browser.FindControl<Button>("PreviousLibraryPage"));
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
        Assert.Equal("DestinationBackButton", Focused(fixture.Window).Name);
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
        Assert.True(fixture.Model.IsServerSelectionVisible);
        Assert.Same(Server, Assert.IsType<ServerIdentity>(Focused(fixture.Window).DataContext));
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
            if (destination == "Libraries")
            {
                Assert.False(fixture.Shell.FindControl<TextBlock>("DestinationTitle")!.IsEffectivelyVisible);
                Assert.Equal(fixture.Model.LibraryBrowser!.SelectedLibrary!.Name,
                    fixture.Shell.FindControl<TextBlock>("LibraryTitle")!.Text);
            }
            else
            {
                Assert.Equal(Loc.Get($"Nav.{destination}"),
                    fixture.Shell.FindControl<TextBlock>("DestinationTitle")!.Text);
            }
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
        await search.RefreshCommand.ExecuteAsync(null);
        fixture.Flush();

        Assert.Equal(MediaSearchPage.PageSize, search.Items.Count);
        var cards = view.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Classes.Contains("search-card")).ToArray();
        Assert.InRange(cards.Length, 1, MediaSearchPage.PageSize);
        var selected = cards.First(button =>
            button.DataContext is MediaPreviewCardViewModel { MediaType: "Series" });
        selected.Focus();
        var searchRows = view.FindControl<ListBox>("SearchRows")!;
        selected.BringIntoView();
        fixture.Flush();
        var searchScroll = searchRows.GetVisualDescendants().OfType<ScrollViewer>().First();
        var offset = searchScroll.Offset;
        fixture.Click(selected);
        Assert.True(fixture.Window.FindControl<SeriesOverviewView>("SeriesOverview")!.IsVisible);
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
        Assert.Equal(offset, searchScroll.Offset);
        fixture.Input.Press(ControllerAction.Back);
        Assert.True(fixture.Model.IsDesignGalleryVisible);
        Assert.Same(searchSource, Focused(fixture.Window));
    });

    [Theory]
    [InlineData("Season", false)]
    [InlineData("Episode", false)]
    [InlineData("Episode", true)]
    public Task HomeGroupedSeriesPostersOpenParentAndRestoreSourceWithoutChangingContinueWatching(
        string type, bool emptyCredits) =>
        TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.Window.WindowState = WindowState.Normal;
        fixture.Window.Width = 1280;
        fixture.Window.Height = 720;
        fixture.Preview.HomeSeriesEntryType = type;
        fixture.Preview.EmptyEpisodeCredits = emptyCredits;
        fixture.SignIn();
        var gallery = fixture.Model.DesignGallery!;
        var recent = Assert.Single(gallery.RecentlyAddedLibraries);
        var target = recent.Items[^1];
        Assert.Equal(type, target.MediaType);
        Assert.Equal("series-parent", target.SeriesId);
        var source = fixture.Gallery.GetVisualDescendants().OfType<Button>()
            .Single(button => ReferenceEquals(button.DataContext, target));
        source.Focus();
        source.BringIntoView();
        fixture.Flush();
        var scroll = source.GetVisualAncestors().OfType<ScrollViewer>().First();
        var offset = scroll.Offset;
        Assert.True(offset.X > 0);
        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();
        var overview = fixture.Window.FindControl<SeriesOverviewView>("SeriesOverview")!;
        var browser = fixture.Window.FindControl<SeasonBrowserView>("SeasonBrowser")!;
        Assert.True(browser.IsEffectivelyVisible);
        Assert.False(fixture.IsModalVisible);
        Assert.True(browser.FindControl<Border>("SeasonPoster")!.IsVisible);
        Assert.True(fixture.Model.SeasonBrowser!.SelectedEpisode is not null,
            $"Season browser: {fixture.Model.SeasonBrowser.Message}; selected season: {fixture.Model.SeasonBrowser.SelectedSeason?.Id}");
        Assert.Equal("series-parent", fixture.Model.SeasonBrowser.SelectedEpisode.Episode.SeriesId);
        Assert.Equal(type == "Season" ? "season-12" : "season-1",
            fixture.Model.SeasonBrowser.SelectedSeason!.Id);
        if (type == "Episode")
            Assert.Equal("episode-2",
                (Focused(fixture.Window) as Button)?.DataContext is EpisodeCardViewModel episode
                    ? episode.Episode.Id : null);
        else
        {
            Assert.Same(browser.BackAction, Focused(fixture.Window));
            fixture.Input.Press(ControllerAction.NavigateDown);
            Assert.Equal("episode-1",
                (Focused(fixture.Window) as Button)?.DataContext is EpisodeCardViewModel episode
                    ? episode.Episode.Id : null);
        }
        fixture.Input.Press(type == "Episode" ? ControllerAction.NavigateLeft : ControllerAction.NavigateRight);
        Assert.Equal(type == "Episode" ? "episode-1" : "episode-2",
            fixture.Model.SeasonBrowser.SelectedEpisode!.Episode.Id);
        if (!emptyCredits)
            Assert.Equal($"Actor {fixture.Model.SeasonBrowser.SelectedEpisode.Episode.Id}",
                Assert.Single(fixture.Model.SeasonBrowser.EpisodeCredits).Name);
        Assert.Equal(fixture.Model.SeasonBrowser.EpisodeTitle,
            browser.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => AutomationProperties.GetAutomationId(text) == "SelectedEpisodeTitle").Text);
        Assert.Equal("4K AV1 SDR", browser.GetVisualDescendants().OfType<TextBlock>()
            .Single(text => AutomationProperties.GetAutomationId(text) == "EpisodeVideoSummary").Text);
        if (emptyCredits)
        {
            Assert.Empty(fixture.Model.SeasonBrowser.EpisodeCredits);
            Assert.Contains(browser.GetVisualDescendants().OfType<TextBlock>(),
                text => text.IsEffectivelyVisible && text.Text == Loc.Get("Details.NoCredits"));
            var focusedEpisode = Focused(fixture.Window);
            fixture.Input.Press(ControllerAction.NavigateDown);
            Assert.Same(focusedEpisode, Focused(fixture.Window));
        }
        fixture.Input.Press(ControllerAction.Accept);
        Assert.True(browser.IsVisible);
        Assert.False(fixture.IsModalVisible);
        Assert.Equal(type == "Episode" ? "episode-1" : "episode-2",
            fixture.Model.SeasonBrowser.SelectedEpisode!.Episode.Id);
        Assert.DoesNotContain(browser.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text == Loc.Get("Details.Watched") || text.Text == Loc.Get("Details.Unwatched")
                || text.Text == Loc.Format("Season.Progress", 45));
        Assert.Equal(0, fixture.Preview.StateWrites);
        fixture.Input.Press(ControllerAction.Back);
        Assert.True(overview.IsVisible);
        Assert.False(browser.IsVisible);
        Assert.Equal(type == "Season" ? "season-12" : "season-1",
            (Focused(fixture.Window) as Button)?.DataContext is SeasonCardViewModel season
                ? season.Season.Id : null);
        fixture.Input.Press(ControllerAction.Back);
        Assert.Same(source, Focused(fixture.Window));
        Assert.Equal(offset, scroll.Offset);
        Assert.Equal(1, fixture.Preview.Calls);
        Assert.Equal(0, fixture.Preview.StateWrites);

        var continuing = fixture.Gallery.GetVisualDescendants().OfType<Button>()
            .Single(button => ReferenceEquals(button.DataContext, gallery.ContinueWatching[0]));
        fixture.Click(continuing);
        Assert.True(fixture.IsModalVisible);
        Assert.False(overview.IsVisible);
        fixture.Input.Press(ControllerAction.Back);
        Assert.Same(continuing, Focused(fixture.Window));
    });

    [Fact]
    public Task SeasonBrowserKeepsLoadingContentHiddenAndPinsRowWhenEpisodeDetailsReflow() =>
        TestAppBuilder.Run(async () =>
    {
        using var fixture = new ShellFixture();
        fixture.Window.WindowState = WindowState.Normal;
        fixture.Window.Width = 1280;
        fixture.Window.Height = 720;
        fixture.Preview.HomeSeriesEntryType = "Season";
        fixture.Preview.LongEpisodeTitle = true;
        fixture.Preview.EpisodeGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.SignIn();
        var season = fixture.Model.DesignGallery!.RecentlyAddedLibraries.Single().Items[^1];
        var source = fixture.Gallery.GetVisualDescendants().OfType<Button>()
            .Single(button => ReferenceEquals(button.DataContext, season));
        fixture.Click(source);
        var browser = fixture.Window.FindControl<SeasonBrowserView>("SeasonBrowser")!;
        var content = browser.FindControl<StackPanel>("BrowserContent")!;
        Assert.True(browser.IsVisible);
        Assert.False(content.IsVisible);
        Assert.Equal(Loc.Get("Season.Loading"), fixture.Model.SeasonBrowser!.Message);
        fixture.Preview.ReleaseEpisodeGate();
        await Task.Yield();
        fixture.Flush();
        Assert.True(content.IsVisible);
        var cards = browser.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Classes.Contains("episode-card")).ToArray();
        browser.FocusSelectedEpisode("episode-1");
        fixture.Flush();
        var before = cards[0].TranslatePoint(default, browser)!.Value.Y;
        fixture.Input.Press(ControllerAction.NavigateRight);
        Assert.Same(cards[1], Focused(fixture.Window));
        Assert.InRange(Math.Abs(cards[1].TranslatePoint(default, browser)!.Value.Y - before), 0, 1);
        Assert.True(fixture.Model.SeasonBrowser.EpisodeTitle.Length > 100);
        fixture.Input.Press(ControllerAction.NavigateUp);
        Assert.Same(browser.BackAction, Focused(fixture.Window));
        Assert.Equal(0, browser.FindControl<ScrollViewer>("BrowserScroll")!.Offset.Y);
        fixture.Input.Press(ControllerAction.Back);
        Assert.False(browser.IsVisible);
        Assert.True(fixture.Window.FindControl<SeriesOverviewView>("SeriesOverview")!.IsVisible);
    });

    [Fact]
    public Task SearchExcludesSeasonsAndEpisodesWhileSeriesOpensOverview() => TestAppBuilder.Run(async () =>
    {
        using var fixture = new ShellFixture();
        fixture.SignIn();
        fixture.Click(fixture.Gallery.GetVisualDescendants().OfType<Button>()
            .Single(button => AutomationProperties.GetName(button) == Loc.Get("Nav.Search")));
        var search = fixture.Model.SearchBrowser!;
        search.Query = "Series";
        await search.RefreshCommand.ExecuteAsync(null);
        fixture.Flush();
        Assert.All(search.Items, item => Assert.True(item.MediaType is "Movie" or "Series"));
        var source = fixture.Shell.SearchView.GetVisualDescendants().OfType<Button>()
            .First(button => button.DataContext is MediaPreviewCardViewModel { MediaType: "Series" });
        fixture.Click(source);
        Assert.True(fixture.Window.FindControl<SeriesOverviewView>("SeriesOverview")!.IsVisible);
        Assert.False(fixture.IsModalVisible);
        fixture.Input.Press(ControllerAction.Back);
        Assert.Same(source, Focused(fixture.Window));
        Assert.Equal("Series", search.Query);
    });

    [Theory]
    [InlineData(720, 480, "en", 1.5, 163.2)]
    [InlineData(1280, 720, "en", 1, 244.8)]
    [InlineData(1920, 1080, "en", 1, 367.2)]
    [InlineData(3440, 1440, "en", 1, 489.6)]
    [InlineData(3840, 2160, "en", 1, 734.4)]
    [InlineData(1280, 720, "qps-ploc", 1.5, 244.8)]
    [InlineData(1280, 720, "qps-plocm", 1.5, 244.8)]
    public Task SeriesOverviewRestoresSearchAndTraversesMissingSeasonPosters(
        int width, int height, string cultureName, double scale, double seasonHeight) => TestAppBuilder.Run(async () =>
    {
        using var culture = new CultureScope(cultureName);
        using var fixture = new ShellFixture(preferences: new PresentationPreferences(scale));
        fixture.Window.WindowState = WindowState.Normal;
        fixture.Window.Width = width;
        fixture.Window.Height = height;
        fixture.SignIn();
        fixture.Click(fixture.Gallery.GetVisualDescendants().OfType<Button>()
            .Single(button => AutomationProperties.GetName(button) == Loc.Get("Nav.Search")));
        var search = fixture.Model.SearchBrowser!;
        search.Query = "Series";
        await search.RefreshCommand.ExecuteAsync(null);
        fixture.Flush();
        var source = fixture.Shell.SearchView.GetVisualDescendants().OfType<Button>()
            .First(button => button.DataContext is MediaPreviewCardViewModel { MediaType: "Series" });
        source.Focus();
        var searchScroll = fixture.Shell.SearchView.FindControl<ListBox>("SearchRows")!
            .GetVisualDescendants().OfType<ScrollViewer>().First();
        var searchOffset = searchScroll.Offset;
        fixture.Click(source);
        var overview = fixture.Window.FindControl<SeriesOverviewView>("SeriesOverview")!;
        Assert.True(overview.IsEffectivelyVisible);
        Assert.True(fixture.Model.SeriesOverview!.Summary.HasDetails);
        Assert.Same(overview.BackAction, Focused(fixture.Window));
        Assert.False(fixture.Window.FindControl<Grid>("MainSurface")!.IsEnabled);
        var seasons = overview.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Classes.Contains("season-card")).ToArray();
        Assert.Equal(12, seasons.Length);
        Assert.All(seasons, card => Assert.InRange(card.Bounds.Width / card.Bounds.Height, 0.66, 0.68));
        Assert.All(seasons, card => Assert.InRange(card.Bounds.Height, seasonHeight - 1, seasonHeight + 1));
        Assert.Equal(Loc.Get("Details.StateUnknown"), AutomationProperties.GetItemStatus(seasons[2]));
        Assert.DoesNotContain(overview.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text == Loc.Get("Details.Watched") || text.Text == Loc.Get("Details.Unwatched")
                || text.Text == Loc.Get("Series.ReviewBoundary"));
        var info = overview.FindControl<Button>("InformationButton")!;
        info.Focus();
        fixture.Input.Press(ControllerAction.NavigateDown);
        Assert.Same(seasons[0], Focused(fixture.Window));
        var scroll = overview.FindControl<ScrollViewer>("SeasonScroll")!;
        for (var index = 1; index < seasons.Length; index++)
        {
            var previousOffset = scroll.Offset.X;
            fixture.Input.Press(cultureName == "qps-plocm" ? ControllerAction.NavigateLeft : ControllerAction.NavigateRight);
            Assert.Same(seasons[index], Focused(fixture.Window));
            Assert.True(scroll.Offset.X >= previousOffset);
        }
        AssertInsideWindow(fixture.Window, seasons[^1]);
        var offset = scroll.Offset;
        Assert.True(offset.X > 0);
        fixture.Input.Press(ControllerAction.Accept);
        var browser = fixture.Window.FindControl<SeasonBrowserView>("SeasonBrowser")!;
        Assert.True(browser.IsEffectivelyVisible);
        Assert.Equal(width >= 900 && height >= 600,
            browser.FindControl<Border>("SeasonPoster")!.IsVisible);
        Assert.False(fixture.IsModalVisible);
        Assert.Equal("season-12", fixture.Model.SeasonBrowser!.SelectedSeason!.Id);
        Assert.Equal("Season 12", fixture.Model.SeasonBrowser.SeasonTitle);
        var episodeCards = browser.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Classes.Contains("episode-card")).ToArray();
        Assert.Equal(16, episodeCards.Length);
        var expectedWidth = 348 * Math.Clamp(width / 1600d, 1, 1.55);
        Assert.All(episodeCards, card => Assert.InRange(card.Bounds.Width, expectedWidth - 1, expectedWidth + 1));
        var episodeViewport = browser.FindControl<ScrollViewer>("EpisodeScroll")!;
        Assert.True(episodeViewport.Extent.Height <= episodeViewport.Viewport.Height + 1);
        var creditsViewport = browser.FindControl<ScrollViewer>("CreditsScroll")!;
        Assert.True(creditsViewport.Extent.Height <= creditsViewport.Viewport.Height + 1);
        var episodeSection = browser.FindControl<Grid>("EpisodeSection")!;
        var castSection = browser.FindControl<Grid>("CastSection")!;
        var rowGap = castSection.TranslatePoint(default, browser)!.Value.Y
            - episodeSection.TranslatePoint(default, browser)!.Value.Y - episodeSection.Bounds.Height;
        Assert.InRange(rowGap, 31, 33);
        var poster = browser.FindControl<Border>("SeasonPoster")!;
        if (poster.IsVisible) Assert.True(poster.Bounds.Width >= 300);
        var rowY = episodeCards[0].TranslatePoint(default, browser)!.Value.Y;
        Assert.All(episodeCards, card =>
            Assert.InRange(Math.Abs(card.TranslatePoint(default, browser)!.Value.Y - rowY), 0, 1));
        browser.FocusSelectedEpisode("episode-1");
        var browserScroll = browser.FindControl<ScrollViewer>("BrowserScroll")!;
        var episodeRowOffset = browserScroll.Offset.Y;
        if (browserScroll.Extent.Height > browserScroll.Viewport.Height + 1)
            Assert.True(episodeRowOffset > 0);
        fixture.Input.Press(cultureName == "qps-plocm" ? ControllerAction.NavigateLeft : ControllerAction.NavigateRight);
        Assert.Same(episodeCards[1], Focused(fixture.Window));
        Assert.InRange(Math.Abs(browserScroll.Offset.Y - episodeRowOffset), 0, 1);
        fixture.Input.Press(cultureName == "qps-plocm" ? ControllerAction.NavigateRight : ControllerAction.NavigateLeft);
        Assert.Same(episodeCards[0], Focused(fixture.Window));
        Assert.InRange(Math.Abs(browserScroll.Offset.Y - episodeRowOffset), 0, 1);
        fixture.Input.Press(ControllerAction.NavigateDown);
        Assert.IsType<StackPanel>(Focused(fixture.Window));
        Assert.Equal("Actor episode-1", Assert.IsType<MovieCreditViewModel>(
            Focused(fixture.Window).DataContext).Name);
        fixture.Input.Press(ControllerAction.NavigateUp);
        Assert.Same(episodeCards[0], Focused(fixture.Window));
        Assert.InRange(Math.Abs(browserScroll.Offset.Y - episodeRowOffset), 0, 1);
        for (var index = 1; index < episodeCards.Length; index++)
            fixture.Input.Press(cultureName == "qps-plocm" ? ControllerAction.NavigateLeft : ControllerAction.NavigateRight);
        Assert.Same(episodeCards[^1], Focused(fixture.Window));
        Assert.True(browser.FindControl<ScrollViewer>("EpisodeScroll")!.Offset.X > 0);
        fixture.Input.Press(ControllerAction.NavigateDown);
        Assert.IsType<StackPanel>(Focused(fixture.Window));
        Assert.Equal(0, browser.FindControl<ScrollViewer>("CreditsScroll")!.Offset.X);
        fixture.Input.Press(ControllerAction.NavigateUp);
        Assert.Same(episodeCards[0], Focused(fixture.Window));
        Assert.Equal(0, browser.FindControl<ScrollViewer>("EpisodeScroll")!.Offset.X);
        fixture.Input.Press(ControllerAction.NavigateUp);
        Assert.Same(browser.BackAction, Focused(fixture.Window));
        Assert.Equal(0, browserScroll.Offset.Y);
        fixture.Input.Press(ControllerAction.NavigateDown);
        Assert.Same(episodeCards[0], Focused(fixture.Window));
        Assert.InRange(Math.Abs(browserScroll.Offset.Y - episodeRowOffset), 0, 1);
        browser.FocusSelectedEpisode("episode-1");
        Assert.Contains(browser.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text == Loc.Format("Season.EpisodeCount", 16));
        Assert.Contains(browser.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text == Loc.Get("Details.PlaybackUnavailable"));
        Assert.DoesNotContain(browser.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text == Loc.Get("Details.Watched") || text.Text == Loc.Get("Details.Unwatched"));
        Capture(fixture.Window, $"season-browser-{cultureName}-{width}-{scale}");
        fixture.Input.Press(ControllerAction.Back);
        Assert.Same(seasons[^1], Focused(fixture.Window));
        Assert.Equal(offset, scroll.Offset);
        fixture.Input.Press(ControllerAction.NavigateUp);
        Assert.Same(info, Focused(fixture.Window));
        fixture.Input.Press(ControllerAction.Accept);
        Assert.True(fixture.IsModalVisible);
        fixture.Input.Press(ControllerAction.Back);
        Assert.Same(info, Focused(fixture.Window));
        Assert.Equal(offset, scroll.Offset);
        Assert.Equal(0, fixture.Preview.StateWrites);
        Capture(fixture.Window, $"series-overview-{cultureName}-{width}-{scale}");
        fixture.Input.Press(ControllerAction.Back);
        Assert.False(overview.IsVisible);
        Assert.Same(source, Focused(fixture.Window));
        Assert.Equal(searchOffset, searchScroll.Offset);
        Assert.Equal("Series", search.Query);
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
        await search.RefreshCommand.ExecuteAsync(null);
        fixture.Flush();

        AssertInsideWindow(fixture.Window, query);
        AssertInsideWindow(fixture.Window, searchView.FindControl<Button>("SearchKeyboardButton")!);
        Assert.Equal(width >= 1920,
            fixture.Shell.FindControl<TextBlock>("DestinationBrand")!.IsEffectivelyVisible);
        Assert.False(fixture.Window.FindControl<Button>("DiagnosticsButton")!.IsEffectivelyVisible);
        var searchCards = searchView.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Classes.Contains("search-card")).ToArray();
        Assert.InRange(searchCards.Length, 1, MediaSearchPage.PageSize);
        searchCards[0].Focus();
        searchCards[0].BringIntoView();
        fixture.Flush();
        Assert.Same(searchCards[0], Focused(fixture.Window));
        AssertCardVisibleInViewport(fixture.Window, searchCards[0]);

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
        await fixture.Model.LibraryBrowser!.OpenLibraryCommand.ExecutionTask!;
        fixture.Flush();
        var libraryCards = libraryView.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Classes.Contains("card")).ToArray();
        libraryCards[^1].Focus();
        libraryCards[^1].BringIntoView();
        fixture.Flush();
        AssertCardVisibleInViewport(fixture.Window, libraryCards[^1]);

        fixture.Click(fixture.Shell.FindControl<Button>("DestinationBackButton")!);
        fixture.OpenSettings();
        Assert.Equal("SettingsPreferencesCategory", Focused(fixture.Window).Name);
        AssertInsideWindow(fixture.Window, Focused(fixture.Window));
        Assert.All(fixture.Shell.FindControl<StackPanel>("SettingsCategories")!
            .GetVisualDescendants().OfType<Button>(), button =>
            {
                var label = Assert.Single(button.GetVisualDescendants().OfType<TextBlock>());
                Assert.NotEmpty(label.TextLayout.TextLines);
                Assert.True(label.TextLayout.Height <= label.Bounds.Height + 1);
                Assert.All(label.TextLayout.TextLines, line => Assert.False(line.HasCollapsed));
                Assert.True(label.Bounds.Height <= button.Bounds.Height);
            });
        var intoSettings = cultureName == "qps-plocm" ? ControllerAction.NavigateLeft : ControllerAction.NavigateRight;
        var outOfSettings = cultureName == "qps-plocm" ? ControllerAction.NavigateRight : ControllerAction.NavigateLeft;
        fixture.Input.Press(intoSettings);
        Assert.Equal("SettingsLanguageButton", Focused(fixture.Window).Name);
        AssertInsideWindow(fixture.Window, Focused(fixture.Window));
        fixture.Input.Press(outOfSettings);
        fixture.Input.Press(ControllerAction.NavigateDown);
        fixture.Input.Press(ControllerAction.NavigateDown);
        Assert.Equal("SettingsApplicationCategory", Focused(fixture.Window).Name);
        fixture.Input.Press(intoSettings);
        Assert.Equal("ExitButton", Focused(fixture.Window).Name);
        AssertInsideWindow(fixture.Window, Focused(fixture.Window));
        fixture.Click(fixture.Shell.FindControl<Button>("SettingsDiagnosticsCategory")!);
        var diagnostics = fixture.Shell.FindControl<Button>("OpenDiagnosticsButton")!;
        diagnostics.Focus();
        diagnostics.BringIntoView();
        fixture.Flush();
        AssertInsideWindow(fixture.Window, diagnostics);
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

    [Theory]
    [InlineData(720, 480)]
    [InlineData(1280, 800)]
    [InlineData(3440, 1440)]
    [InlineData(3840, 2160)]
    public Task SearchKeyboardKeepsPromptKeysAndActionsTogetherAtEveryViewport(int width, int height) =>
        TestAppBuilder.Run(() =>
    {
        using var fixture = new ShellFixture();
        fixture.Window.WindowState = WindowState.Normal;
        fixture.Window.Width = width;
        fixture.Window.Height = height;
        fixture.SignIn();
        fixture.Click(fixture.Gallery.GetVisualDescendants().OfType<Button>()
            .Single(button => AutomationProperties.GetName(button) == Loc.Get("Nav.Search")));
        fixture.Flush();

        var entry = fixture.Shell.SearchView.FindControl<Grid>("SearchEntry")!;
        var query = fixture.Shell.SearchView.FindControl<TextBox>("SearchTextBox")!;
        Assert.InRange(entry.Bounds.Width, 200, 840);
        Assert.InRange(Math.Abs(entry.TranslatePoint(default, fixture.Window)!.Value.X
            + entry.Bounds.Width / 2 - fixture.Window.ClientSize.Width / 2), 0, 170);
        AssertInsideWindow(fixture.Window, entry);

        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();
        var dialog = fixture.Window.FindControl<Border>("ModalDialog")!;
        var panel = fixture.Window.FindControl<Grid>("ModalContent")!;
        var title = fixture.Window.FindControl<TextBlock>("ModalTitle")!;
        var draft = fixture.Modal.Children.OfType<TextBox>().Single();
        var keys = fixture.Modal.Children.OfType<UniformGrid>().Single();
        var actions = fixture.Modal.Children.OfType<WrapPanel>().Single();
        Assert.True(dialog.Bounds.Width > fixture.Window.ClientSize.Width * .8);
        Assert.InRange(panel.Bounds.Width, 500, 840);
        Assert.InRange(Math.Abs(panel.TranslatePoint(default, fixture.Window)!.Value.X
            + panel.Bounds.Width / 2 - fixture.Window.ClientSize.Width / 2), 0, 2);
        Assert.InRange(draft.Bounds.Width, keys.Bounds.Width - 1, 840);
        Assert.InRange(keys.Bounds.Width, 540, 744);
        Assert.Equal(width < 900 ? 10 : 12, keys.Columns);
        Assert.Equal(Loc.Get("Search.Name"), title.Text);
        AssertInsideWindow(fixture.Window, dialog);
        AssertInsideWindow(fixture.Window, title);
        AssertInsideWindow(fixture.Window, draft);
        Capture(fixture.Window, $"search-keyboard-{width}");
        var first = keys.Children.OfType<Button>().First();
        var second = keys.Children.OfType<Button>().Skip(1).First();
        Assert.InRange(second.Bounds.X - first.Bounds.Right, 5, 16);
        Assert.InRange(first.Bounds.Width, 48, 60);
        Assert.Same(first, Focused(fixture.Window));
        fixture.Input.Press(ControllerAction.NavigateRight);
        Assert.Same(second, Focused(fixture.Window));
        fixture.Input.Press(ControllerAction.Accept);
        Assert.Equal("2", draft.Text);

        for (var step = 0; step < 16 && !actions.Children.Contains(Focused(fixture.Window)); step++)
            fixture.Input.Press(ControllerAction.NavigateDown);
        Assert.Contains(Focused(fixture.Window), actions.Children);
        AssertCardVisibleInViewport(fixture.Window, Focused(fixture.Window));
        var done = actions.Children.OfType<Button>()
            .Single(button => Equals(button.Content, Loc.Get("Keyboard.Done")));
        done.BringIntoView();
        fixture.Flush();
        AssertInsideWindow(fixture.Window, done);
        fixture.Input.Press(ControllerAction.Back);
        Assert.False(fixture.IsModalVisible);
        Assert.Equal(string.Empty, query.Text);
        Assert.Same(query, Focused(fixture.Window));
    });

    [Fact]
    public Task LibraryReferenceGridExposesFiltersSortAndAllLetterChoices() => TestAppBuilder.Run(async () =>
    {
        using var fixture = new ShellFixture();
        fixture.Preview.WithLibraries = true;
        fixture.SignIn();
        fixture.Click(FirstLibraryShortcut(fixture));
        var view = fixture.Shell.LibraryView;
        await fixture.Model.LibraryBrowser!.OpenLibraryCommand.ExecutionTask!;
        fixture.Flush();
        var model = fixture.Model.LibraryBrowser;

        fixture.Click(view.FindControl<Button>("FilterMenuButton")!);
        fixture.ClickContent(Loc.Get("Library.Filter.Favorites"));
        await model.SetFilterCommand.ExecutionTask!;
        fixture.Click(view.FindControl<Button>("SortMenuButton")!);
        fixture.ClickContent(Loc.Get("Library.Sort.Descending"));
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
        Assert.Equal(Loc.Format("Library.Filter.Action", Loc.Get("Library.Filter.Favorites")),
            AutomationProperties.GetName(view.FindControl<Button>("FilterMenuButton")!));
        Assert.Equal(Loc.Format("Library.Sort.Action", Loc.Get("Library.Sort.Descending")),
            AutomationProperties.GetName(view.FindControl<Button>("SortMenuButton")!));
        Assert.Null(view.FindControl<ItemsControl>("LibraryChoices"));
        Assert.Equal(Loc.Get("State.Selected"),
            AutomationProperties.GetItemStatus(letters.Single(button => Equals(button.Tag, "M"))));
        var cards = view.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Classes.Contains("card")).ToArray();
        var columns = model.ColumnCount;
        Assert.InRange(cards.Length, columns, model.Items.Count);
        Assert.InRange(columns, 2, 14);
        Assert.Single(view.FindControl<ListBox>("LibraryRows")!
            .GetVisualDescendants().OfType<VirtualizingStackPanel>());
        Assert.Null(view.FindControl<Button>("NextLibraryPage"));
        Assert.Null(view.FindControl<Button>("PreviousLibraryPage"));
        cards[columns - 1].Focus();
        fixture.Input.Press(ControllerAction.NavigateRight);
        Assert.Equal("M", Assert.IsType<Button>(Focused(fixture.Window)).Tag);
    });

    [Theory]
    [InlineData("FilterMenuButton")]
    [InlineData("SortMenuButton")]
    public Task LibraryMenusCancelWithoutChangingGridAndMarkCurrentChoices(string name) => TestAppBuilder.Run(async () =>
    {
        using var fixture = new ShellFixture();
        fixture.Preview.WithLibraries = true;
        fixture.SignIn();
        fixture.Click(FirstLibraryShortcut(fixture));
        var model = fixture.Model.LibraryBrowser!;
        await model.OpenLibraryCommand.ExecutionTask!;
        fixture.Flush();
        var view = fixture.Shell.LibraryView;
        var rows = view.FindControl<ListBox>("LibraryRows")!;
        rows.ScrollIntoView(2);
        fixture.Flush();
        var scroll = rows.GetVisualDescendants().OfType<ScrollViewer>().First();
        var originalItems = model.Items;
        var calls = fixture.Preview.LibraryCalls;

        var launcher = view.FindControl<Button>(name)!;
        fixture.Click(launcher);
        var offset = scroll.Offset;
        Assert.True(fixture.IsModalVisible);
        var selected = Assert.IsType<Button>(Focused(fixture.Window));
        Assert.Equal(Loc.Get("State.Selected"), AutomationProperties.GetItemStatus(selected));
        fixture.Key(Key.Tab, RawInputModifiers.Shift);
        Assert.Equal(Loc.Get("Action.Back"), Assert.IsType<Button>(Focused(fixture.Window)).Content);
        fixture.Key(Key.Tab);
        Assert.Same(selected, Focused(fixture.Window));
        fixture.Input.Press(ControllerAction.NavigateDown);
        fixture.Input.Press(ControllerAction.Back);
        fixture.Flush();
        Assert.False(fixture.IsModalVisible);
        Assert.Same(launcher, Focused(fixture.Window));
        Assert.Equal(offset, scroll.Offset);
        Assert.Same(originalItems, model.Items);
        Assert.Equal(calls, fixture.Preview.LibraryCalls);
        Assert.Equal(MediaLibraryFilter.All, model.SelectedFilter);
        Assert.Equal(MediaLibrarySortDirection.Ascending, model.SelectedSortDirection);

        fixture.Click(launcher);
        fixture.Input.Press(ControllerAction.Accept);
        fixture.Flush();
        Assert.False(fixture.IsModalVisible);
        Assert.Same(launcher, Focused(fixture.Window));
        Assert.Equal(calls, fixture.Preview.LibraryCalls);
    });

    [Theory]
    [InlineData("en", 1920, 1080, 1, false)]
    [InlineData("qps-ploc", 1280, 800, 1.5, false)]
    [InlineData("qps-plocm", 720, 480, 1.5, true)]
    public Task LibraryNameSwitcherAndMenusFitAndSupportControllerNavigation(
        string culture, int width, int height, double scale, bool longName) => TestAppBuilder.Run(async () =>
    {
        using var scope = new CultureScope(culture);
        using var fixture = new ShellFixture(preferences: new PresentationPreferences(TextScale: scale));
        fixture.Window.WindowState = WindowState.Normal;
        fixture.Window.Width = width;
        fixture.Window.Height = height;
        fixture.Preview.LayoutLibraries = true;
        fixture.Preview.LongLibraryName = longName;
        fixture.SignIn();
        fixture.Click(FirstLibraryShortcut(fixture));
        var model = fixture.Model.LibraryBrowser!;
        await model.OpenLibraryCommand.ExecutionTask!;
        fixture.Flush();
        var switcher = fixture.Shell.FindControl<Button>("LibrarySwitcher")!;
        var title = fixture.Shell.FindControl<TextBlock>("LibraryTitle")!;
        var filter = fixture.Shell.LibraryView.FindControl<Button>("FilterMenuButton")!;
        var sort = fixture.Shell.LibraryView.FindControl<Button>("SortMenuButton")!;
        fixture.Shell.FocusBack();
        fixture.Input.Press(culture == "qps-plocm" ? ControllerAction.NavigateLeft : ControllerAction.NavigateRight);
        Assert.Same(switcher, Focused(fixture.Window));
        fixture.Input.Press(ControllerAction.Accept);
        Assert.True(fixture.IsModalVisible);
        var choices = fixture.Modal.Children.OfType<Button>().Where(button => button.Tag is MediaLibrary).ToArray();
        Assert.Equal(["tv", "movies", "anime"], choices.Select(button => ((MediaLibrary)button.Tag!).Id));
        Assert.Same(choices[0], Focused(fixture.Window));
        Assert.Equal(Loc.Get("State.Selected"), AutomationProperties.GetItemStatus(choices[0]));
        fixture.Input.Press(ControllerAction.NavigateDown);
        fixture.Input.Press(ControllerAction.NavigateDown);
        Assert.Same(choices[2], Focused(fixture.Window));
        AssertInsideWindow(fixture.Window, choices[2]);
        fixture.Input.Press(ControllerAction.Accept);
        await model.OpenLibraryCommand.ExecutionTask!;
        fixture.Flush();

        Assert.Equal("anime", model.SelectedLibrary!.Id);
        Assert.Equal(model.SelectedLibrary.Name, title.Text);
        Assert.Equal(model.SelectedLibrary.Name, AutomationProperties.GetName(switcher));
        Assert.False(fixture.Shell.FindControl<TextBlock>("DestinationTitle")!.IsEffectivelyVisible);
        Assert.Equal(Loc.Format("Library.Count", 47),
            fixture.Shell.LibraryView.FindControl<TextBlock>("LibraryItemCount")!.Text);
        AssertInsideWindow(fixture.Window, switcher);
        AssertInsideWindow(fixture.Window, filter);
        AssertInsideWindow(fixture.Window, sort);
        Assert.True(title.Bounds.Width <= switcher.Bounds.Width);
        switcher.Focus();
        fixture.Input.Press(ControllerAction.NavigateDown);
        Assert.Same(filter, Focused(fixture.Window));
        fixture.Input.Press(ControllerAction.NavigateUp);
        Assert.Same(switcher, Focused(fixture.Window));
        fixture.Input.Press(ControllerAction.Accept);
        Assert.Equal("anime", Assert.IsType<MediaLibrary>(Assert.IsType<Button>(Focused(fixture.Window)).Tag).Id);
        fixture.Input.Press(ControllerAction.Back);
        Assert.Same(switcher, Focused(fixture.Window));
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
    public Task ControllerChangesAndBackgroundActionsDoNotStealFocusAndResumeOnActivation() => TestAppBuilder.Run(() =>
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

        fixture.Window.ActivateForTest();
        fixture.Flush();
        Assert.True(fixture.Window.IsActive);
        Assert.True(fixture.Input.ApplicationActive);
        Assert.Same(focused, Focused(fixture.Window));
        fixture.Input.Press(ControllerAction.Menu);
        Assert.Equal(WindowState.Normal, fixture.Window.WindowState);
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
            if (destination == "Libraries")
                AssertCardVisibleInViewport(fixture.Window, Focused(fixture.Window));
            else
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
    [InlineData(3440, 1440)]
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
        var focusedItem = Assert.IsType<MediaPreviewCardViewModel>(focused.DataContext);
        AssertCardVisibleInViewport(fixture.Window, focused);
        var densityScale = Math.Clamp(width / 1600d, 1, 1.55);
        Assert.Equal(270d * densityScale,
            (double)fixture.Window.Resources["Cindara.Media.GridPosterWidth"]!,
            precision: 6);
        Assert.Equal(405d * densityScale,
            (double)fixture.Window.Resources["Cindara.Media.GridPosterHeight"]!,
            precision: 6);
        Assert.Equal(24d * densityScale,
            (double)fixture.Window.Resources["Cindara.Media.GridSpacing"]!,
            precision: 6);
        if (width >= 2800)
        {
            Assert.Equal(6, fixture.Model.LibraryBrowser.ColumnCount);
        }

        fixture.Window.Width = width < 1000 ? 1920 : 720;
        fixture.Window.Height = width < 1000 ? 1080 : 480;
        fixture.Flush();
        Assert.Same(focusedItem, Assert.IsType<MediaPreviewCardViewModel>(Focused(fixture.Window).DataContext));
        Assert.Contains(Focused(fixture.Window),
            fixture.Shell.LibraryView.GetVisualDescendants().OfType<Button>());
        AssertCardVisibleInViewport(fixture.Window, Focused(fixture.Window));
        fixture.Input.Press(ControllerAction.NavigateDown);
        fixture.Flush();
        Assert.Same(fixture.Model.LibraryBrowser.Items[fixture.Model.LibraryBrowser.ColumnCount],
            Focused(fixture.Window).DataContext);
        AssertCardVisibleInViewport(fixture.Window, Focused(fixture.Window));
    });

    [Fact]
    public Task DeepVirtualizedLibraryFocusSurvivesResponsiveRegrouping() => TestAppBuilder.Run(async () =>
    {
        using var fixture = new ShellFixture();
        fixture.Window.WindowState = WindowState.Normal;
        fixture.Window.Width = 1920;
        fixture.Window.Height = 720;
        fixture.Preview.WithLibraries = true;
        fixture.SignIn();
        fixture.Click(FirstLibraryShortcut(fixture));
        var model = fixture.Model.LibraryBrowser!;
        await model.OpenLibraryCommand.ExecutionTask!;
        await model.LoadMoreCommand.ExecuteAsync(null);
        fixture.Flush();
        var target = model.Items[45];
        var row = 45 / model.ColumnCount;
        var rows = fixture.Shell.LibraryView.FindControl<ListBox>("LibraryRows")!;
        rows.ScrollIntoView(row);
        fixture.Flush();
        var button = fixture.Shell.LibraryView.GetVisualDescendants().OfType<Button>()
            .Single(control => ReferenceEquals(control.DataContext, target));
        button.Focus();
        fixture.Flush();

        fixture.Window.Width = 720;
        fixture.Window.Height = 480;
        fixture.Flush();

        Assert.Same(target, Assert.IsType<MediaPreviewCardViewModel>(Focused(fixture.Window).DataContext));
        AssertCardVisibleInViewport(fixture.Window, Focused(fixture.Window));
    });

    [Theory]
    [InlineData("en", 1920, 1080, 1)]
    [InlineData("qps-plocm", 1920, 1080, 1.5)]
    [InlineData("en", 720, 480, 1.5)]
    public Task LibraryBufferFillsWithoutMovingSlotsOrScroll(
        string culture, int width, int height, double textScale) => TestAppBuilder.Run(async () =>
    {
        using var scope = new CultureScope(culture);
        using var fixture = new ShellFixture(
            preferences: new PresentationPreferences(TextScale: textScale, ReducedMotion: true));
        fixture.Window.WindowState = WindowState.Normal;
        fixture.Window.Width = width;
        fixture.Window.Height = height;
        fixture.Preview.WithLibraries = true;
        fixture.Preview.LibraryTotal = 250;
        fixture.Preview.LongLibraryTitles = true;
        fixture.SignIn();
        fixture.Click(FirstLibraryShortcut(fixture));
        var model = fixture.Model.LibraryBrowser!;
        await model.OpenLibraryCommand.ExecutionTask!;
        fixture.Flush();
        var slots = model.Rows.SelectMany(row => row.Slots).ToArray();
        Assert.Equal(100, slots.Length);
        Assert.Equal(60, slots.Count(slot => slot.IsPlaceholder));
        var view = fixture.Shell.LibraryView;
        var filter = view.FindControl<Button>("FilterMenuButton")!;
        var back = fixture.Shell.FindControl<Button>("DestinationBackButton")!;
        back.Focus();
        var rows = view.FindControl<ListBox>("LibraryRows")!;
        var scroll = rows.GetVisualDescendants().OfType<ScrollViewer>().First();
        fixture.Preview.LibraryGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        rows.ScrollIntoView(40 / model.ColumnCount);
        fixture.Flush();

        Assert.True(model.IsLoadingMore);
        Assert.False(filter.IsEnabled);
        Assert.Same(back, Focused(fixture.Window));
        Assert.Equal(2, fixture.Preview.LibraryCalls);
        Assert.Equal(slots, model.Rows.SelectMany(row => row.Slots));
        var placeholder = view.GetVisualDescendants().OfType<Grid>()
            .Single(grid => grid.Classes.Contains("library-slot") && ReferenceEquals(grid.DataContext, slots[40]));
        var offset = scroll.Offset;
        var position = placeholder.TranslatePoint(default, view);
        var size = placeholder.Bounds.Size;
        Assert.True(slots[40].IsPlaceholder);
        fixture.Preview.LibraryGate.SetResult(true);
        await model.LoadMoreCommand.ExecutionTask!;
        fixture.Flush();

        Assert.Equal(80, model.Items.Count);
        Assert.Equal(2, fixture.Preview.LibraryCalls);
        Assert.True(filter.IsEnabled);
        Assert.Same(back, Focused(fixture.Window));
        Assert.Equal(offset, scroll.Offset);
        Assert.Equal(position, placeholder.TranslatePoint(default, view));
        Assert.Equal(size, placeholder.Bounds.Size);
        Assert.Contains(placeholder, view.GetVisualDescendants());
        Assert.Same(slots[40], placeholder.DataContext);
        Assert.True(slots[40].HasItem);
        Assert.Equal(60, model.Rows.SelectMany(row => row.Slots).Count(slot => slot.IsPlaceholder));
        Assert.Contains(placeholder.GetVisualDescendants().OfType<Button>(),
            button => button.IsEffectivelyVisible && ReferenceEquals(button.DataContext, model.Items[40]));
    });

    [Fact]
    public Task ScrollingIntoTheBlankBufferLoadsOnceAndShowsRetryWithoutMovingRows() => TestAppBuilder.Run(async () =>
    {
        using var fixture = new ShellFixture();
        fixture.Window.WindowState = WindowState.Normal;
        fixture.Window.Width = 1920;
        fixture.Window.Height = 1080;
        fixture.Preview.WithLibraries = true;
        fixture.Preview.LibraryTotal = 250;
        fixture.SignIn();
        fixture.Click(FirstLibraryShortcut(fixture));
        var model = fixture.Model.LibraryBrowser!;
        await model.OpenLibraryCommand.ExecutionTask!;
        fixture.Flush();
        var view = fixture.Shell.LibraryView;
        var rows = view.FindControl<ListBox>("LibraryRows")!;
        var originalRows = model.Rows.ToArray();
        var slots = originalRows.SelectMany(row => row.Slots).ToArray();
        fixture.Preview.LibraryGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        rows.ScrollIntoView(80 / model.ColumnCount);
        fixture.Flush();

        Assert.True(model.IsLoadingMore);
        Assert.Equal(2, fixture.Preview.LibraryCalls);
        Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(),
            button => button.DataContext is MediaPreviewCardViewModel && button.IsEffectivelyVisible);
        var scroll = rows.GetVisualDescendants().OfType<ScrollViewer>().First();
        var offset = scroll.Offset;
        fixture.Preview.LibraryGate.SetException(
            new MediaPreviewException(MediaPreviewError.Network, "Library unavailable."));
        await model.LoadMoreCommand.ExecutionTask!;
        fixture.Flush();

        Assert.Equal(2, fixture.Preview.LibraryCalls);
        Assert.Equal(originalRows, model.Rows);
        Assert.Equal(slots, model.Rows.SelectMany(row => row.Slots));
        Assert.Equal(offset, scroll.Offset);
        Assert.True(slots[40].IsRetry);

        rows.ScrollIntoView(40 / model.ColumnCount);
        fixture.Flush();
        var retry = view.GetVisualDescendants().OfType<Button>()
            .Single(button => button.Name == "RetryLoadingMore" && button.IsEffectivelyVisible);
        var slot = view.GetVisualDescendants().OfType<Grid>()
            .Single(grid => grid.Classes.Contains("library-slot") && ReferenceEquals(grid.DataContext, slots[40]));
        var size = slot.Bounds.Size;
        var position = slot.TranslatePoint(default, view);
        offset = scroll.Offset;
        fixture.Preview.LibraryGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Click(retry);
        Assert.Equal(3, fixture.Preview.LibraryCalls);
        Assert.Equal(size, slot.Bounds.Size);
        Assert.Equal(position, slot.TranslatePoint(default, view));
        Assert.Equal(offset, scroll.Offset);
        fixture.Preview.LibraryGate.SetResult(true);
        await model.RetryPageCommand.ExecutionTask!;
        fixture.Flush();
        Assert.Equal(80, model.Items.Count);
        Assert.Equal(size, slot.Bounds.Size);
        Assert.Equal(position, slot.TranslatePoint(default, view));
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

    private static void AssertCardVisibleInViewport(Window window, Control control)
    {
        Assert.True(control.IsEffectivelyVisible);
        var scroll = control.GetVisualAncestors().OfType<ScrollViewer>().First();
        var viewport = scroll.GetVisualDescendants().OfType<ScrollContentPresenter>().First();
        var origin = control.TranslatePoint(default, viewport)!.Value;
        var visible = new Rect(origin, control.Bounds.Size).Intersect(new Rect(viewport.Bounds.Size));
        Assert.InRange(visible.Width, control.Bounds.Width - 1, control.Bounds.Width + 1);
        // Approved large posters can exceed compact viewport height; the visible portion must fill it.
        Assert.InRange(visible.Height, Math.Min(control.Bounds.Height, viewport.Bounds.Height) - 1,
            Math.Min(control.Bounds.Height, viewport.Bounds.Height) + 1);
        Assert.True(visible.Height >= 48,
            $"Visible={visible}; viewport={viewport.Bounds}; card={control.Bounds}; name={control.Name}; item={control.DataContext}");
        AssertInsideWindow(window, viewport);
    }

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

        public void ActivateForTest() => typeof(WindowBase)
            .GetMethod("HandleActivated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
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
        public MediaPreviewError? DetailError { get; set; }
        public bool PopulatedMovie { get; set; }
        public string? HomeSeriesEntryType { get; set; }
        public bool EmptyEpisodeCredits { get; set; }
        public bool LongEpisodeTitle { get; set; }
        public TaskCompletionSource<IReadOnlyList<MediaEpisode>>? EpisodeGate { get; set; }
        public int StateWrites { get; private set; }
        private string? _episodeSeriesId;
        private string? _episodeSeasonId;
        private MediaUserState _userState = new(false, false, 0, 0);
        public Task<MediaItemDetails> GetItemDetailsAsync(AuthenticatedSession session, string itemId,
            CancellationToken cancellationToken = default) => DetailError is { } error
            ? Task.FromException<MediaItemDetails>(new MediaPreviewException(error, "details"))
            : Task.FromResult(new MediaItemDetails(
                itemId, PopulatedMovie ? "A movie with a rather long title for a compact viewport" : "Movie details",
                itemId.StartsWith("season-", StringComparison.Ordinal) ? "Season"
                : itemId.StartsWith("episode-", StringComparison.Ordinal) ? "Episode"
                : itemId == "series-parent" || itemId.StartsWith("search-", StringComparison.Ordinal)
                    && int.Parse(itemId.AsSpan(7), System.Globalization.CultureInfo.InvariantCulture) % 4 == 1
                    ? "Series" : "Movie", itemId.StartsWith("episode-", StringComparison.Ordinal)
                        ? _episodeSeriesId : null, null, itemId.StartsWith("episode-", StringComparison.Ordinal)
                        ? _episodeSeasonId : null, null, null, 2026, null,
                TimeSpan.FromMinutes(65).Ticks, "PG", ["Drama", "Science Fiction"], [new("Community", 8.2)],
                PopulatedMovie ? string.Concat(Enumerable.Repeat("A full movie synopsis that stays readable. ", 100)) : "A synopsis.",
                itemId.StartsWith("episode-", StringComparison.Ordinal) && !EmptyEpisodeCredits
                    ? [new MediaCredit($"actor-{itemId}", $"Actor {itemId}", "Character", "Actor", null)]
                    : itemId.StartsWith("season-", StringComparison.Ordinal)
                    ? [new MediaCredit("actor", "Actor", "Character", "Actor", null)]
                    : PopulatedMovie ? Enumerable.Range(0, 40)
                        .Select(index => new MediaCredit($"actor-{index}", $"Actor {index}", $"Role {index}", "Actor", null)).ToArray() : [],
                [new("Video", "av1", "4K AV1 SDR", null, 3840, 2160, null, true, false),
                 new("Audio", "eac3", "English - Dolby Digital Plus + Dolby Atmos - 5.1 - Default", "eng", null, null, 6, true, false),
                 new("Subtitle", "srt", "English - Default - SUBRIP", "eng", null, null, null, true, false)],
                _userState, false, false));
        public Task<IReadOnlyList<MediaSeason>> GetSeasonsAsync(AuthenticatedSession session, string seriesId,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<MediaSeason>>(
                Enumerable.Range(1, 12).Select(number => new MediaSeason($"season-{number}", $"Season {number}",
                    number, new(false, number == 1, 0, 0), false, number != 3)).ToArray());
        public Task<IReadOnlyList<MediaEpisode>> GetEpisodesAsync(AuthenticatedSession session, string seriesId,
            string seasonId, CancellationToken cancellationToken = default)
        {
            _episodeSeriesId = seriesId;
            _episodeSeasonId = seasonId;
            return EpisodeGate?.Task ?? Task.FromResult<IReadOnlyList<MediaEpisode>>(Episodes(seriesId, seasonId));
        }
        public void ReleaseEpisodeGate()
        {
            EpisodeGate!.SetResult(Episodes(_episodeSeriesId!, _episodeSeasonId!));
            EpisodeGate = null;
        }
        private MediaEpisode[] Episodes(string seriesId, string seasonId) =>
            Enumerable.Range(1, 16).Select(number =>
                    new MediaEpisode($"episode-{number}",
                        number == 2 && LongEpisodeTitle
                            ? string.Concat(Enumerable.Repeat("An unusually long episode title ", 8))
                            : $"Episode {number}", seriesId, seasonId, 1, number,
                        null, TimeSpan.FromMinutes(30).Ticks, "PG", "Episode synopsis",
                        EmptyEpisodeCredits ? [] :
                            [new MediaCredit($"actor-episode-{number}", $"Actor episode-{number}",
                                "Character", "Actor", null)],
                        new(false, number == 2, number == 2 ? 100 : 25, 0), false)).ToArray();
        public Task<MediaItemDetails?> GetSeriesContinuationAsync(AuthenticatedSession session, string seriesId,
            CancellationToken cancellationToken = default) => Task.FromResult<MediaItemDetails?>(null);
        public Task<MediaUserState> SetFavoriteAsync(AuthenticatedSession session, string itemId, bool isFavorite,
            CancellationToken cancellationToken = default)
        {
            StateWrites++;
            _userState = _userState with { IsFavorite = isFavorite };
            return Task.FromResult(_userState);
        }
        public Task<MediaUserState> SetPlayedAsync(AuthenticatedSession session, string itemId, bool isPlayed,
            CancellationToken cancellationToken = default)
        {
            StateWrites++;
            _userState = _userState with { IsPlayed = isPlayed, PlaybackPositionTicks = 0 };
            return Task.FromResult(_userState);
        }

        public Task<byte[]?> GetLibraryArtworkAsync(AuthenticatedSession session, string itemId,
            CancellationToken cancellationToken = default) => ArtworkGate.Task.WaitAsync(cancellationToken);
        public TaskCompletionSource<byte[]?> ArtworkGate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ProgressiveArtwork { get; set; }
        public void ClearImageCache() { }
        public int LibraryCalls { get; private set; }
        public bool WithLibraries { get; set; }
        public bool LayoutLibraries { get; set; }
        public int HomeItemsPerRail { get; set; } = 1;
        public bool PauseLibrary { get; set; }
        public int LibraryTotal { get; set; } = 47;
        public bool LongLibraryTitles { get; set; }
        public bool LongLibraryName { get; set; }
        public TaskCompletionSource<bool>? LibraryGate { get; set; }
        public int SearchCalls { get; private set; }
        public Task<MediaSearchPage> SearchAsync(AuthenticatedSession session, string query, int startIndex,
            CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            var types = new[] { "Movie", "Series" };
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

            if (LibraryGate is { } gate)
            {
                await gate.Task.WaitAsync(cancellationToken);
            }

            return new MediaLibraryPage(Enumerable.Range(startIndex, Math.Min(40, LibraryTotal - startIndex))
                .Select(index => new MediaPreviewItem($"movie-{index}",
                    LongLibraryTitles ? $"Movie {index} with a long title wrapping across multiple lines" : $"Movie {index}",
                    "2026", "Movie",
                    null, null, "A media description.", "1h 5m", null)
                {
                    ArtworkItemId = ProgressiveArtwork ? $"movie-{index}" : null,
                }).ToArray(), startIndex, LibraryTotal);
        }

        public int Calls { get; private set; }
        public bool Pause { get; set; }
        public bool Empty { get; set; }
        public bool LongDescription { get; set; }
        public TaskCompletionSource? HomeGate { get; set; }
        public MediaPreviewError? Error { get; set; }

        public async Task<MediaPreviewHome> GetHomeAsync(AuthenticatedSession session, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (HomeGate is { } gate)
                await gate.Task.WaitAsync(cancellationToken);
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
            if (HomeSeriesEntryType is { } type)
            {
                var child = item with
                {
                    Id = type == "Season" ? "season-1" : "episode-1",
                    Name = "Series title",
                    MediaType = type,
                    SeriesId = "series-parent",
                    SeasonId = type == "Episode" ? "season-1" : null,
                };
                return new MediaPreviewHome(child, [child with { MediaType = "Episode" }],
                    [new MediaPreviewRail("tv", "TV", Enumerable.Range(0, 12)
                        .Select(index => child with
                        {
                            Id = type == "Season" ? $"season-{index + 1}" : "episode-2",
                        }).ToArray(), "TV")])
                {
                    Libraries = [new MediaLibrary("tv", "TV", "tvshows")],
                };
            }
            if (LayoutLibraries)
            {
                MediaLibrary[] libraries =
                [
                    new("tv", "TV", "tvshows"), new("collections", "Collections", "boxsets"),
                    new("movies", "Movies", "movies"), new("people", "People", "people"),
                    new("anime", LongLibraryName ? string.Concat(Enumerable.Repeat("Anime ", 30)) : "Anime", "tvshows"),
                ];
                return new MediaPreviewHome(item, [item],
                    libraries.Select(library => new MediaPreviewRail(library.Id, library.Name,
                        Enumerable.Range(0, HomeItemsPerRail).Select(index =>
                            item with { Id = index == 0 ? library.Id : $"{library.Id}-{index}", Name = library.Name }).ToArray(), library.Name)).ToArray())
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
