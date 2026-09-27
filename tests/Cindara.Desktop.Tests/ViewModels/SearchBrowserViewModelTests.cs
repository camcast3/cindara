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
    public async Task ResultsAppendInBoundedBatchesAndRetryKeepsPreviousCards()
    {
        var client = new Client { Total = 83 };
        using var immediate = Model(client);
        immediate.Query = "space";
        await immediate.RefreshCommand.ExecuteAsync(null);
        Assert.Equal([0], client.StartIndexes);
        Assert.True(immediate.HasMore);
        Assert.All(immediate.Items, item => Assert.True(item.MediaType is "Movie" or "Series"));

        client.ErrorAt = 40;
        await immediate.LoadMoreCommand.ExecuteAsync(null);
        Assert.Equal(40, immediate.Items.Count);
        Assert.True(immediate.CanRetryMore);
        Assert.Equal(string.Empty, immediate.Message);

        client.ErrorAt = -1;
        await immediate.RetryCommand.ExecuteAsync(null);
        Assert.Equal(80, immediate.Items.Count);
        Assert.Equal(Loc.Format("Search.Loaded", 80, 83), immediate.CountDescription);
        await immediate.LoadMoreCommand.ExecuteAsync(null);
        Assert.Equal(83, immediate.Items.Count);
        Assert.False(immediate.HasMore);
        Assert.Equal([0, 40, 40, 80], client.StartIndexes);
        Assert.Equal(14, immediate.Rows.Count);
    }

    [Fact]
    public async Task SupersededResponseCannotReplaceTheCurrentQuery()
    {
        var client = new Client { Pending = true };
        using var model = ImmediateModel(client);
        model.Query = "old";
        await client.WaitForCallsAsync(1);
        model.Query = "new";
        await client.WaitForCallsAsync(2);
        client.Complete("new", Item("new", "New", "Movie"));
        await WaitForAsync(() => model.Items.Count == 1);
        client.Complete("old", Item("old", "Old", "Movie"));
        await WaitForAsync(() => client.Completed == 2);
        Assert.Equal("new", Assert.Single(model.Items).Id);
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
        using var model = ImmediateModel(client);
        model.Query = "missing";
        await WaitForAsync(() => model.IsEmpty);
        Assert.Equal(Loc.Get("Search.Empty"), model.Message);

        client.ErrorAt = 0;
        model.Query = "another";
        await WaitForAsync(() => model.CanRetry);
        Assert.Equal(Loc.Get("Error.Preview.Network"), model.Message);
        client.ErrorAt = -1;
        await model.RetryCommand.ExecuteAsync(null);
        Assert.True(model.IsEmpty);
    }

    [Fact]
    public async Task InvalidClientResultsNeverEnterTheGrid()
    {
        var client = new Client { Total = 1, UnexpectedType = "Episode" };
        using var model = ImmediateModel(client);
        model.Query = "show";
        await WaitForAsync(() => model.CanRetry);
        Assert.Empty(model.Items);
        Assert.Equal(Loc.Get("Error.Preview.InvalidResponse"), model.Message);
    }

    [Fact]
    public async Task CanceledAppendCannotAddLateResultsAndCanBeRetried()
    {
        var client = new Client { Total = 80, PendingAt = 40 };
        using var model = Model(client);
        model.Query = "many";
        await model.RefreshCommand.ExecuteAsync(null);
        model.LoadMoreCommand.Execute(null);
        await client.WaitForCallsAsync(2);
        model.CancelLoading();
        Assert.True(model.CanRetryMore);
        Assert.Equal(40, model.Items.Count);
        client.CompletePage("many", 40, 80);
        await WaitForAsync(() => client.Completed == 1);
        Assert.Equal(40, model.Items.Count);
        client.PendingAt = -1;
        await model.RetryCommand.ExecuteAsync(null);
        Assert.Equal(80, model.Items.Count);
    }

    private static SearchBrowserViewModel ImmediateModel(Client client) =>
        new(client, Session, _ => Task.CompletedTask, delay: (_, _) => Task.CompletedTask);

    private static SearchBrowserViewModel Model(Client client) =>
        new(client, Session, _ => Task.CompletedTask,
            delay: (_, token) => Task.Delay(Timeout.Infinite, token));

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 10000 && !condition(); attempt++)
            await Task.Yield();
        Assert.True(condition());
    }

    private static MediaPreviewItem Item(string id, string name, string type) =>
        new(id, name, string.Empty, type, null, null, null, string.Empty, null);

    private sealed class Client : IJellyfinMediaPreviewClient
    {
        private readonly Dictionary<string, TaskCompletionSource<MediaSearchPage>> _pending = [];
        public List<int> StartIndexes { get; } = [];
        public int Calls { get; private set; }
        public int Completed { get; private set; }
        public bool Pending { get; set; }
        public int PendingAt { get; set; } = -1;
        public int Total { get; set; } = 43;
        public int ErrorAt { get; set; } = -1;
        public string? UnexpectedType { get; set; }

        public Task<MediaSearchPage> SearchAsync(AuthenticatedSession session, string query,
            int startIndex, CancellationToken cancellationToken = default)
        {
            Assert.Equal(Session, session);
            Calls++;
            StartIndexes.Add(startIndex);
            if (startIndex == ErrorAt)
                throw new MediaPreviewException(MediaPreviewError.Network, "Search failure.");
            if (Pending || startIndex == PendingAt)
            {
                var source = new TaskCompletionSource<MediaSearchPage>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _pending[$"{query}:{startIndex}"] = source;
                return CompleteAsync(source.Task);
            }
            var count = Math.Min(MediaSearchPage.PageSize, Total - startIndex);
            return Task.FromResult(new MediaSearchPage(
                Enumerable.Range(startIndex, count)
                    .Select(index => Item($"title-{index}", $"Title {index}",
                        UnexpectedType ?? (index % 2 == 0 ? "Movie" : "Series"))).ToArray(), startIndex, Total));
        }

        private async Task<MediaSearchPage> CompleteAsync(Task<MediaSearchPage> task)
        {
            var result = await task;
            Completed++;
            return result;
        }

        public void Complete(string query, MediaPreviewItem item) =>
            _pending[$"{query}:0"].SetResult(new MediaSearchPage([item], 0, 1));

        public void CompletePage(string query, int startIndex, int total) =>
            _pending[$"{query}:{startIndex}"].SetResult(new MediaSearchPage(
                Enumerable.Range(startIndex, Math.Min(40, total - startIndex))
                    .Select(index => Item($"title-{index}", $"Title {index}", "Movie")).ToArray(),
                startIndex, total));

        public async Task WaitForCallsAsync(int calls)
        {
            while (Calls < calls)
                await Task.Yield();
        }

        public Task<byte[]?> GetLibraryArtworkAsync(AuthenticatedSession session, string itemId,
            CancellationToken cancellationToken = default) => Task.FromResult<byte[]?>(null);
        public void ClearImageCache() { }
        public Task<MediaPreviewHome> GetHomeAsync(AuthenticatedSession session, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<MediaLibraryPage> GetLibraryPageAsync(AuthenticatedSession session, MediaLibrary library,
            int startIndex, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
