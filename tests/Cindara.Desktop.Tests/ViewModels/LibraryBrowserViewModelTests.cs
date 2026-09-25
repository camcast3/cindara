using Cindara.Core.Authentication;
using Cindara.Core.Jellyfin;
using Cindara.Core.Models;
using Cindara.Desktop.Localization;
using Cindara.Desktop.Tests.Localization;
using Cindara.Desktop.Tests.Navigation;
using Cindara.Desktop.ViewModels;

namespace Cindara.Desktop.Tests.ViewModels;

[Collection(LocalizationTestGroup.Name)]
public sealed class LibraryBrowserViewModelTests
{
    [Fact]
    public async Task UnsupportedViewsAreAbsentFromTheChooserAndCannotBeOpened()
    {
        var client = new Client();
        var unsupported = new MediaLibrary("people", "People", "people");
        using var model = new LibraryBrowserViewModel(client, Session, [Library, unsupported], _ => Task.CompletedTask);
        Assert.Equal(Library, Assert.Single(model.Libraries));
        await Assert.ThrowsAsync<ArgumentException>(() => model.OpenLibraryCommand.ExecuteAsync(unsupported));
        Assert.Empty(client.StartIndexes);
    }

    private static readonly MediaLibrary Library = new("movies", "Movies", "movies");
    private static readonly AuthenticatedSession Session = new(
        new ServerIdentity("server", new Uri("https://media.example/"), "Media", "10.11", "Linux"),
        "user", "Viewer", "token");

    [Fact]
    public async Task IncrementalLoadPreservesItemsWhenLibraryShrinksAndQueryResetCanRecover()
    {
        var requests = new List<int>();
        using var handler = new ShrinkingLibraryHandler(start =>
        {
            requests.Add(start);
            var total = requests.Count <= 2 ? 87 : 47;
            var count = Math.Clamp(total - start, 0, MediaLibraryPage.PageSize);
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                Items = Enumerable.Range(start, count).Select(index => new { Id = $"item-{index}", Name = $"Item {index}", Type = "Movie" }),
                TotalRecordCount = total,
            });
        });
        using var client = new JellyfinMediaPreviewClient(handler,
            new JellyfinClientIdentity("Cindara", "Tests", "device", "1.0"));
        using var model = new LibraryBrowserViewModel(client, Session, [Library], _ => Task.CompletedTask);
        await model.OpenLibraryCommand.ExecuteAsync(Library);
        var original = model.Items;
        await model.LoadMoreCommand.ExecuteAsync(null);
        Assert.Same(original, model.Items);
        Assert.Equal(80, model.Items.Count);
        var description = model.PageDescription;

        await model.LoadMoreCommand.ExecuteAsync(null);

        Assert.Same(original, model.Items);
        Assert.Equal(description, model.PageDescription);
        Assert.Equal(80, model.Items.Count);
        Assert.Equal("item-0", model.Items[0].Id);
        Assert.True(model.CanRetryMore);
        Assert.Equal(80, model.RetryIndex);
        Assert.True(model.HasMessage);
        Assert.False(model.IsAnyLoading);

        await model.SetFilterCommand.ExecuteAsync(MediaLibraryFilter.All);

        Assert.Equal("item-0", model.Items[0].Id);
        Assert.Equal(40, model.Items.Count);
        Assert.False(model.CanRetry);
        Assert.False(model.HasMessage);
        Assert.Equal([0, 40, 80, 0], requests);
    }

    private sealed class ShrinkingLibraryHandler(Func<int, string> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
            var start = int.Parse(query["StartIndex"]!, System.Globalization.CultureInfo.InvariantCulture);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(respond(start), System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("qps-ploc")]
    [InlineData("qps-plocm")]
    public async Task BatchesAccumulateAndReopeningPreservesLoadedItems(string culture)
    {
        using var scope = new CultureScope(culture);
        var client = new Client();
        using var model = Model(client);
        await model.OpenLibraryCommand.ExecuteAsync(Library);
        Assert.Equal(40, model.Items.Count);
        Assert.True(model.HasMore);
        Assert.Equal(40, model.NextIndex);
        var first = model.Items[0];
        model.SetColumnCount(7);
        var finalInitialRow = model.Rows[^1];
        await model.LoadMoreCommand.ExecuteAsync(null);
        Assert.Equal(47, model.Items.Count);
        Assert.Same(first, model.Items[0]);
        Assert.Same(finalInitialRow, model.Rows[5]);
        Assert.Equal(7, finalInitialRow.Items.Count);
        Assert.False(model.HasMore);
        Assert.Equal(Loc.Format("Library.Loaded", 47, 47), model.PageDescription);
        var previous = model.Items;
        await model.OpenLibraryCommand.ExecuteAsync(Library);
        Assert.Same(previous, model.Items);
        Assert.Equal([0, 40], client.StartIndexes);
    }

    [Theory]
    [InlineData(MediaPreviewError.Network)]
    [InlineData(MediaPreviewError.TimedOut)]
    [InlineData(MediaPreviewError.InvalidResponse)]
    public async Task FailedLoadMorePreservesItemsAndCanRetryTheFailedOffset(MediaPreviewError error)
    {
        var client = new Client();
        using var model = Model(client);
        await model.OpenLibraryCommand.ExecuteAsync(Library);
        var original = model.Items;
        client.Error = error;
        await model.LoadMoreCommand.ExecuteAsync(null);
        Assert.Same(original, model.Items);
        Assert.True(model.CanRetryMore);
        Assert.Equal(40, model.RetryIndex);
        Assert.True(model.HasMessage);
        Assert.False(model.IsAnyLoading);
        client.Error = null;
        await model.RetryPageCommand.ExecuteAsync(null);
        Assert.False(model.CanRetryMore);
        Assert.False(model.HasMessage);
        Assert.Equal(47, model.Items.Count);
        Assert.Equal("movie-40", model.Items[40].Id);
    }

    [Fact]
    public async Task RejectedSessionInvokesAccountRecovery()
    {
        var client = new Client { Error = MediaPreviewError.AccessDenied };
        var recoveries = 0;
        using var model = new LibraryBrowserViewModel(client, Session, [Library], exception =>
        {
            Assert.Equal(MediaPreviewError.AccessDenied, exception.Error);
            recoveries++;
            return Task.CompletedTask;
        });
        await model.OpenLibraryCommand.ExecuteAsync(Library);
        Assert.Equal(1, recoveries);
        Assert.Empty(model.Items);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanceledAndDisposedLoadsNeverPublishLatePages(bool dispose)
    {
        var client = new Client { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var model = Model(client);
        var loading = model.OpenLibraryCommand.ExecuteAsync(Library);
        Assert.True(model.IsLoading);
        Assert.False(model.LoadPageCommand.CanExecute(0));
        if (dispose)
        {
            model.Dispose();
        }
        else
        {
            model.CancelLoading();
        }

        client.Pending.SetResult(new MediaLibraryPage([Item(0)], 0, 1));
        await loading;
        Assert.Empty(model.Items);
        Assert.False(model.IsLoading);
        Assert.Equal(!dispose, model.OpenLibraryCommand.CanExecute(Library));
        Assert.Equal(!dispose, model.LoadPageCommand.CanExecute(0));
    }

    [Fact]
    public async Task EmptyLibraryIsNotARequestFailure()
    {
        var client = new Client { Total = 0 };
        using var model = Model(client);
        await model.OpenLibraryCommand.ExecuteAsync(Library);
        Assert.True(model.IsEmpty);
        Assert.False(model.CanRetry);
        Assert.False(model.HasMessage);
        Assert.False(model.HasMore);
    }

    [Fact]
    public async Task SwitchingLibrariesDisposesCardsAndCannotOpenAnotherAccountsLibrary()
    {
        var decoder = new TestPreviewImageDecoder();
        var client = new Client { Artwork = true };
        var other = new MediaLibrary("tv", "TV", "tvshows");
        using var model = new LibraryBrowserViewModel(client, Session, [Library, other],
            _ => Task.CompletedTask, createCard: item => new MediaPreviewCardViewModel(item, decoder.Decode));
        await model.OpenLibraryCommand.ExecuteAsync(Library);
        var old = decoder.Resources.ToArray();
        await model.OpenLibraryCommand.ExecuteAsync(other);
        Assert.All(old, resource => Assert.Equal(1, resource.DisposeCount));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            model.OpenLibraryCommand.ExecuteAsync(new MediaLibrary("private", "Private", "movies")));
        model.Dispose();
        Assert.All(decoder.Resources, resource => Assert.Equal(1, resource.DisposeCount));
    }

    private static LibraryBrowserViewModel Model(Client client) =>
        new(client, Session, [Library], _ => throw new InvalidOperationException("Unexpected rejected session."));

    [Fact]
    public Task CardDecodingLeavesUiThreadAndPublishesOnUiThread() => TestAppBuilder.Run(async () =>
    {
        using var bitmap = new Avalonia.Media.Imaging.WriteableBitmap(new Avalonia.PixelSize(2, 2), new Avalonia.Vector(96, 96));
        using var stream = new MemoryStream();
        bitmap.Save(stream, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        var artwork = stream.ToArray();
        var created = 0;
        using var model = new LibraryBrowserViewModel(new Client(), Session, [Library],
            _ => Task.CompletedTask, createCard: item =>
            {
                Assert.False(Avalonia.Threading.Dispatcher.UIThread.CheckAccess());
                created++;
                return new MediaPreviewCardViewModel(item with { Artwork = artwork });
            });
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(model.Items))
            {
                Assert.True(Avalonia.Threading.Dispatcher.UIThread.CheckAccess());
            }
        };
        await model.OpenLibraryCommand.ExecuteAsync(Library);
        Assert.Equal(MediaLibraryPage.PageSize, created);
        Assert.Equal(MediaLibraryPage.PageSize, model.Items.Count);
        Assert.All(model.Items, item => Assert.True(item.HasArtwork));
    });

    [Theory]
    [InlineData("en")]
    [InlineData("qps-ploc")]
    [InlineData("qps-plocm")]
    public async Task CorruptPageArtworkReportsFailureAndDisposesOnlyThePartialReplacement(string culture)
    {
        using var scope = new CultureScope(culture);
        var decoder = new TestPreviewImageDecoder();
        var client = new Client { Artwork = true };
        using var model = new LibraryBrowserViewModel(client, Session, [Library],
            _ => Task.CompletedTask, createCard: item => new MediaPreviewCardViewModel(item, decoder.Decode));
        await model.OpenLibraryCommand.ExecuteAsync(Library);
        var original = model.Items;
        var initialResources = decoder.Resources.Count;
        decoder.FailOnCall = initialResources + 3;

        await model.LoadMoreCommand.ExecuteAsync(null);

        Assert.Same(original, model.Items);
        Assert.True(model.CanRetryMore);
        Assert.Contains(Loc.Get("Error.Preview.InvalidResponse"), model.Message, StringComparison.Ordinal);
        Assert.All(decoder.Resources.Take(28), resource => Assert.Equal(0, resource.DisposeCount));
        Assert.All(decoder.Resources.Skip(28).Take(initialResources - 28),
            resource => Assert.Equal(1, resource.DisposeCount));
        Assert.All(decoder.Resources.Skip(initialResources), resource => Assert.Equal(1, resource.DisposeCount));
        model.Dispose();
        Assert.All(decoder.Resources, resource => Assert.Equal(1, resource.DisposeCount));
    }

    [Fact]
    public async Task FilterSortAndLetterChangesCommitOnlyAfterSuccessfulPages()
    {
        var client = new Client();
        using var model = Model(client);
        await model.OpenLibraryCommand.ExecuteAsync(Library);
        var original = model.Items;
        client.Error = MediaPreviewError.Network;

        await model.SetFilterCommand.ExecuteAsync(MediaLibraryFilter.Favorites);

        Assert.Equal(MediaLibraryFilter.All, model.SelectedFilter);
        Assert.Same(original, model.Items);
        Assert.True(model.CanRetry);
        client.Error = null;
        await model.RetryPageCommand.ExecuteAsync(null);
        Assert.Equal(MediaLibraryFilter.Favorites, model.SelectedFilter);

        await model.SetSortDirectionCommand.ExecuteAsync(MediaLibrarySortDirection.Descending);
        await model.SetLetterCommand.ExecuteAsync("M");

        Assert.Equal(MediaLibrarySortDirection.Descending, model.SelectedSortDirection);
        Assert.Equal('M', model.SelectedLetter);
        Assert.Equal(
            new MediaLibraryQuery(
                Filter: MediaLibraryFilter.Favorites,
                SortDirection: MediaLibrarySortDirection.Descending,
                StartsWith: 'M'),
            client.Queries[^1]);
    }

    [Fact]
    public async Task CancelLoadingStopsFilterRequestsAndKeepsTheActiveQuery()
    {
        var client = new Client();
        using var model = Model(client);
        await model.OpenLibraryCommand.ExecuteAsync(Library);
        var original = model.Items;
        client.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);

        var loading = model.SetFilterCommand.ExecuteAsync(MediaLibraryFilter.Unwatched);
        Assert.True(model.IsLoading);
        model.CancelLoading();
        client.Pending.SetResult(new MediaLibraryPage([Item(0)], 0, 1));
        await loading;

        Assert.Equal(MediaLibraryFilter.All, model.SelectedFilter);
        Assert.Same(original, model.Items);
        Assert.True(model.CanRetry);
        Assert.Equal(Loc.Get("Library.Canceled"), model.Message);
    }

    [Fact]
    public async Task OnlyOneIncrementalRequestRunsAndFailureAppendsRetryRow()
    {
        var client = new Client();
        using var model = Model(client);
        await model.OpenLibraryCommand.ExecuteAsync(Library);
        var original = model.Items;
        client.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);

        var loading = model.LoadMoreCommand.ExecuteAsync(null);

        Assert.True(model.IsLoadingMore);
        Assert.False(model.LoadMoreCommand.CanExecute(null));
        Assert.Equal(2, client.Queries.Count);
        client.Pending.SetException(new MediaPreviewException(MediaPreviewError.Network, "Load more failed."));
        await loading;

        Assert.Same(original, model.Items);
        Assert.Equal(40, model.Items.Count);
        Assert.True(model.CanRetryMore);
        Assert.True(Assert.Single(model.Rows, row => row.HasRetry).HasRetry);

        client.Pending = null;
        await model.RetryPageCommand.ExecuteAsync(null);

        Assert.Equal(47, model.Items.Count);
        Assert.False(model.CanRetryMore);
        Assert.DoesNotContain(model.Rows, row => row.HasRetry);
    }

    private static MediaPreviewItem Item(int index) =>
        new($"movie-{index}", $"Movie {index}", "2026", "Movie", null, null, "Overview", "2026", null);

    private sealed class Client : IJellyfinMediaPreviewClient
    {
        public Task<byte[]?> GetLibraryArtworkAsync(AuthenticatedSession session, string itemId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public List<int> StartIndexes { get; } = [];
        public List<MediaLibraryQuery> Queries { get; } = [];
        public MediaPreviewError? Error { get; set; }
        public int Total { get; set; } = 47;
        public bool Artwork { get; set; }
        public TaskCompletionSource<MediaLibraryPage>? Pending { get; set; }

        public void ClearImageCache() { }
        public Task<MediaPreviewHome> GetHomeAsync(AuthenticatedSession session, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MediaLibraryPage> GetLibraryPageAsync(AuthenticatedSession session, MediaLibrary library,
            int startIndex, CancellationToken cancellationToken = default)
            => GetLibraryPageAsync(
                session,
                library,
                new MediaLibraryQuery(startIndex),
                cancellationToken);

        public Task<MediaLibraryPage> GetLibraryPageAsync(
            AuthenticatedSession session,
            MediaLibrary library,
            MediaLibraryQuery query,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(Session, session);
            query.Validate();
            StartIndexes.Add(query.StartIndex);
            Queries.Add(query);
            if (Error is { } error)
            {
                throw new MediaPreviewException(error, "Page failure.");
            }

            return Pending?.Task ?? Task.FromResult(new MediaLibraryPage(
                Enumerable.Range(query.StartIndex, Math.Min(MediaLibraryPage.PageSize, Total - query.StartIndex))
                    .Select(index => Item(index) with { Artwork = Artwork ? [1] : null }).ToArray(),
                query.StartIndex,
                Total));
        }
    }
}
