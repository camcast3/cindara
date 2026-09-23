using Cindara.Core.Authentication;
using Cindara.Core.Jellyfin;
using Cindara.Core.Models;
using Cindara.Desktop.Localization;
using Cindara.Desktop.Tests.Localization;
using Cindara.Desktop.ViewModels;

namespace Cindara.Desktop.Tests.ViewModels;

[Collection(LocalizationTestGroup.Name)]
public sealed class SearchBrowserViewModelTests
{
    private static readonly AuthenticatedSession Session = new(
        new ServerIdentity("server", new Uri("https://media.example/"), "Media", "10.11", "Linux"),
        "user", "Viewer", "token");

    [Fact]
    public async Task ResultsAreGroupedPagedAndKeepThePreviousPageWhenPagingFails()
    {
        var client = new Client();
        using var model = Model(client);
        model.Query = "space";
        await model.LoadPageCommand.ExecuteAsync(0);

        Assert.Equal(["Movies", "Series", "Episodes"], model.Groups.Select(group => group.Title));
        Assert.Equal(["movie", "series", "episode"], model.Groups.SelectMany(group => group.Items).Select(item => item.Id));
        Assert.True(model.HasNextPage);
        Assert.Equal(3, model.NextIndex);
        var original = model.Groups;

        client.Error = MediaPreviewError.Network;
        await model.LoadPageCommand.ExecuteAsync(model.NextIndex);

        Assert.Same(original, model.Groups);
        Assert.True(model.CanRetry);
        Assert.Equal(3, model.RetryIndex);
        Assert.Equal(Loc.Get("Error.Preview.Network"), model.Message);
    }

    [Fact]
    public async Task SupersededResponseCannotReplaceTheCurrentQuery()
    {
        var client = new Client { Pending = true };
        using var model = new SearchBrowserViewModel(client, Session, _ => Task.CompletedTask,
            delay: (_, _) => Task.CompletedTask);
        model.Query = "old";
        await client.WaitForCallsAsync(1);
        model.Query = "new";
        await client.WaitForCallsAsync(2);

        client.Complete("new", Item("new", "New", "Movie"));
        await client.WaitForCompletionAsync("new");
        await WaitForAsync(() => model.Groups.Count > 0);
        client.Complete("old", Item("old", "Old", "Movie"));
        await client.WaitForCompletionAsync("old");

        Assert.Equal("new", Assert.Single(Assert.Single(model.Groups).Items).Id);
        Assert.Equal("new", model.Query);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("qps-ploc")]
    [InlineData("qps-plocm")]
    public async Task EmptyAndErrorStatesAreLocalized(string culture)
    {
        using var scope = new CultureScope(culture);
        var client = new Client { Total = 0 };
        using var model = Model(client);
        model.Query = "missing";
        await model.LoadPageCommand.ExecuteAsync(0);
        Assert.True(model.IsEmpty);
        Assert.Equal(Loc.Get("Search.Empty"), model.Message);

        client.Error = MediaPreviewError.TimedOut;
        await model.LoadPageCommand.ExecuteAsync(0);
        Assert.Equal(Loc.Get("Error.Preview.TimedOut"), model.Message);
    }

    private static SearchBrowserViewModel Model(Client client) =>
        new(client, Session, _ => Task.CompletedTask, delay: (_, token) => Task.Delay(Timeout.Infinite, token));

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 1000 && !condition(); attempt++)
        {
            await Task.Yield();
        }

        Assert.True(condition());
    }

    private static MediaPreviewItem Item(string id, string name, string type) =>
        new(id, name, string.Empty, type, null, null, null, string.Empty, null);

    private sealed class Client : IJellyfinMediaPreviewClient
    {
        private readonly Dictionary<string, TaskCompletionSource<MediaSearchPage>> _pending = [];
        private readonly Dictionary<string, TaskCompletionSource> _completed = [];
        public int Calls { get; private set; }
        public bool Pending { get; set; }
        public int Total { get; set; } = 43;
        public MediaPreviewError? Error { get; set; }

        public Task<MediaSearchPage> SearchAsync(
            AuthenticatedSession session,
            string query,
            int startIndex,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(Session, session);
            Calls++;
            if (Error is { } error)
            {
                throw new MediaPreviewException(error, "Search failure.");
            }

            if (Pending)
            {
                var source = new TaskCompletionSource<MediaSearchPage>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _pending[query] = source;
                _completed[query] = new(TaskCreationOptions.RunContinuationsAsynchronously);
                return CompleteAsync(query, source.Task);
            }

            return Task.FromResult(new MediaSearchPage(
                Total == 0
                    ? []
                    : [Item("movie", "Movie", "Movie"), Item("series", "Series", "Series"),
                        Item("episode", "Episode", "Episode")],
                startIndex,
                Total));
        }

        private async Task<MediaSearchPage> CompleteAsync(string query, Task<MediaSearchPage> task)
        {
            var result = await task;
            _completed[query].SetResult();
            return result;
        }

        public void Complete(string query, MediaPreviewItem item) =>
            _pending[query].SetResult(new MediaSearchPage([item], 0, 1));

        public async Task WaitForCallsAsync(int calls)
        {
            while (Calls < calls)
            {
                await Task.Yield();
            }
        }

        public Task WaitForCompletionAsync(string query) => _completed[query].Task;

        public Task<byte[]?> GetLibraryArtworkAsync(
            AuthenticatedSession session,
            string itemId,
            CancellationToken cancellationToken = default) => Task.FromResult<byte[]?>(null);

        public void ClearImageCache() { }
        public Task<MediaPreviewHome> GetHomeAsync(AuthenticatedSession session, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<MediaLibraryPage> GetLibraryPageAsync(
            AuthenticatedSession session,
            MediaLibrary library,
            int startIndex,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
