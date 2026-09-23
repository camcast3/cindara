using System.Collections.Concurrent;
using Cindara.Core.Authentication;
using Cindara.Core.Diagnostics;
using Cindara.Core.Jellyfin;
using Cindara.Core.Models;
using Cindara.Desktop.Tests.Localization;
using Cindara.Desktop.ViewModels;

namespace Cindara.Desktop.Tests.ViewModels;

[Collection(LocalizationTestGroup.Name)]
public sealed class LibraryArtworkTests
{
    private static readonly MediaLibrary Library = new("tv", "TV", "tvshows");
    private static readonly AuthenticatedSession Session = new(
        new ServerIdentity("server", new Uri("https://media.example/"), "Media", "10.11", "Linux"),
        "user", "Viewer", "token");
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task MetadataPublishesImmediatelyWhileSixArtworkWorkersLoadWithoutReplacingCards()
    {
        var gate = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new Client((_, token) => gate.Task.WaitAsync(token));
        var decoder = new LockedDecoder();
        using var model = Model(client, decoder);
        await model.OpenLibraryCommand.ExecuteAsync(Library).WaitAsync(TestTimeout);
        await client.SixStarted.Task.WaitAsync(TestTimeout);
        var cards = model.Items;

        Assert.Equal(40, cards.Count);
        Assert.False(model.IsLoading);
        Assert.True(model.LoadArtworkCommand.IsRunning);
        Assert.True(model.HasNextPage);
        Assert.True(model.LoadPageCommand.CanExecute(40));
        Assert.All(cards, card => Assert.True(card.IsArtworkLoading));
        Assert.Equal(6, client.Active);

        gate.SetResult([1]);
        await model.LoadArtworkCommand.ExecutionTask!.WaitAsync(TestTimeout);

        Assert.Same(cards, model.Items);
        Assert.All(cards, card =>
        {
            Assert.True(card.HasArtwork);
            Assert.False(card.IsArtworkLoading);
        });
        Assert.Equal(6, client.Maximum);
        Assert.False(model.CanRetryArtwork);
        Assert.Equal(0, client.CacheClears);
        Assert.Empty(model.ArtworkMessage);
        Assert.Equal(0, client.Active);
        model.Dispose();
        Assert.All(decoder.Resources, resource => Assert.Equal(1, resource.DisposeCount));
    }

    [Theory]
    [InlineData(MediaPreviewError.TimedOut)]
    [InlineData(MediaPreviewError.Network)]
    [InlineData(MediaPreviewError.InvalidResponse)]
    [InlineData(MediaPreviewError.UnexpectedStatus)]
    public async Task FailedPosterDoesNotDiscardPageOrSuccessfulPostersAndRetryOnlyLoadsMissing(MediaPreviewError error)
    {
        var fail = true;
        var client = new Client((id, _) => fail && id == "item-0"
            ? Task.FromException<byte[]?>(new MediaPreviewException(error, "Artwork failed."))
            : Task.FromResult<byte[]?>([1]));
        var decoder = new LockedDecoder();
        using var model = Model(client, decoder);
        await model.OpenLibraryCommand.ExecuteAsync(Library);
        await model.LoadArtworkCommand.ExecutionTask!;
        var cards = model.Items;

        Assert.Equal(40, cards.Count);
        Assert.False(cards[0].HasArtwork);
        Assert.All(cards.Skip(1), card => Assert.True(card.HasArtwork));
        Assert.True(model.CanRetryArtwork);
        Assert.Contains("Some artwork could not be loaded", model.ArtworkMessage, StringComparison.Ordinal);
        Assert.False(model.CanRetry);
        Assert.True(model.HasNextPage);
        Assert.Equal(0, client.CacheClears);

        fail = false;
        await model.LoadArtworkCommand.ExecuteAsync(null);
        Assert.Same(cards, model.Items);
        Assert.All(cards, card => Assert.True(card.HasArtwork));
        Assert.Equal(2, client.Calls["item-0"]);
        Assert.All(client.Calls.Where(pair => pair.Key != "item-0"), pair => Assert.Equal(1, pair.Value));
        Assert.False(model.CanRetryArtwork);
    }

    [Fact]
    public async Task MissingAndCorruptImagesHaveExplicitStatesWithoutFailingMetadata()
    {
        var client = new Client((id, _) => Task.FromResult<byte[]?>(id == "item-0" ? null : [1]));
        var decoder = new LockedDecoder { FailOnCall = 1 };
        using var model = Model(client, decoder);
        await model.OpenLibraryCommand.ExecuteAsync(Library);
        await model.LoadArtworkCommand.ExecutionTask!;

        Assert.Equal(40, model.Items.Count);
        Assert.Equal(2, model.Items.Count(card => !card.HasArtwork));
        Assert.True(model.CanRetryArtwork);
        Assert.False(model.CanRetry);
        Assert.All(model.Items, card => Assert.False(card.IsArtworkLoading));
        Assert.Equal(1, client.CacheClears);
    }

    [Fact]
    public async Task PagingCancelsOldImagesAndStartsNewMetadataWithoutWaitingForTheNetworkDeadline()
    {
        var gate = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new Client((_, token) => gate.Task.WaitAsync(token));
        var decoder = new LockedDecoder();
        using var model = Model(client, decoder);
        await model.OpenLibraryCommand.ExecuteAsync(Library);
        await client.SixStarted.Task.WaitAsync(TestTimeout);
        var previous = model.Items;

        await model.LoadPageCommand.ExecuteAsync(40).WaitAsync(TestTimeout);

        Assert.Equal("item-40", model.Items[0].Id);
        Assert.Equal(7, model.Items.Count);
        Assert.True(client.Canceled >= 6);
        Assert.InRange(client.Maximum, 1, 6);
        gate.SetResult([1]);
        await model.LoadArtworkCommand.ExecutionTask!.WaitAsync(TestTimeout);
        Assert.All(previous, card => Assert.False(card.HasArtwork));
        Assert.All(model.Items, card => Assert.True(card.HasArtwork));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelOrDisposeDrainsArtworkAndNeverPublishesLateImages(bool dispose)
    {
        var gate = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new Client((_, _) => gate.Task);
        var decoder = new LockedDecoder();
        using var model = Model(client, decoder);
        await model.OpenLibraryCommand.ExecuteAsync(Library);
        await client.SixStarted.Task.WaitAsync(TestTimeout);
        var cards = model.Items;
        if (dispose)
        {
            model.Dispose();
        }
        else
        {
            model.CancelLoading();
        }

        gate.SetResult([1]);
        await model.LoadArtworkCommand.ExecutionTask!.WaitAsync(TestTimeout);

        Assert.All(cards, card => Assert.False(card.HasArtwork));
        Assert.Equal(0, client.Active);
        Assert.Equal(6, client.Calls.Count);
        Assert.Equal(!dispose, model.CanRetryArtwork);
        Assert.Empty(decoder.Resources);
    }

    [Fact]
    public async Task DecodeFailureAfterCancellationDoesNotClearAnotherLoadCache()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var client = new Client((_, _) => Task.FromResult<byte[]?>([1]));
        using var model = new LibraryBrowserViewModel(client, Session, [Library], _ => Task.CompletedTask,
            decodeArtwork: _ =>
            {
                started.TrySetResult();
                Assert.True(release.Wait(TestTimeout));
                throw new MediaPreviewException(MediaPreviewError.InvalidResponse, "Corrupt artwork.");
            });
        try
        {
            await model.OpenLibraryCommand.ExecuteAsync(Library);
            await started.Task.WaitAsync(TestTimeout);
            model.CancelLoading();
        }
        finally
        {
            release.Set();
        }

        await model.LoadArtworkCommand.ExecutionTask!.WaitAsync(TestTimeout);
        Assert.Equal(0, client.CacheClears);
        Assert.All(model.Items, card => Assert.False(card.HasArtwork));
    }

    [Fact]
    public async Task RejectedArtworkSessionStopsSiblingsAndInvokesRecoveryExactlyOnce()
    {
        var gate = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new Client((_, token) => gate.Task.WaitAsync(token));
        var recoveries = 0;
        using var model = new LibraryBrowserViewModel(client, Session, [Library], exception =>
        {
            Assert.Equal(MediaPreviewError.AccessDenied, exception.Error);
            recoveries++;
            return Task.CompletedTask;
        });
        await model.OpenLibraryCommand.ExecuteAsync(Library);
        await client.SixStarted.Task.WaitAsync(TestTimeout);
        gate.SetException(new MediaPreviewException(MediaPreviewError.AccessDenied, "Rejected."));
        await model.LoadArtworkCommand.ExecutionTask!.WaitAsync(TestTimeout);
        Assert.Equal(1, recoveries);
        Assert.Equal(0, client.Active);
    }

    [Fact]
    public async Task DuplicateArtworkIsRequestedOnceAndEachCardOwnsItsDecodedResource()
    {
        var gate = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new Client((_, token) => gate.Task.WaitAsync(token)) { SharedArtwork = true };
        var decoder = new LockedDecoder();
        using var model = Model(client, decoder);
        await model.OpenLibraryCommand.ExecuteAsync(Library);
        Assert.Single(client.Calls);
        gate.SetResult([1]);
        await model.LoadArtworkCommand.ExecutionTask!.WaitAsync(TestTimeout);
        Assert.Single(client.Calls);
        Assert.Equal(40, decoder.Resources.Count);
        model.Dispose();
        Assert.All(decoder.Resources, resource => Assert.Equal(1, resource.DisposeCount));
    }

    [Fact]
    public async Task ArtworkFailuresAreRecordedWithoutIdentifiers()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cindara-artwork-{Guid.NewGuid():N}");
        try
        {
            var diagnostics = new LocalDiagnostics(directory);
            var client = new Client((_, _) => throw new MediaPreviewException(MediaPreviewError.TimedOut, "private"));
            using var model = new LibraryBrowserViewModel(client, Session, [Library], _ => Task.CompletedTask, diagnostics);
            await model.OpenLibraryCommand.ExecuteAsync(Library);
            await model.LoadArtworkCommand.ExecutionTask!;
            Assert.Contains(diagnostics.Snapshot(),
                entry => entry.Action == DiagnosticAction.LoadArtwork && entry.Errors.Contains("Media.TimedOut"));
            Assert.DoesNotContain("private", string.Join("", Directory.GetFiles(directory).Select(File.ReadAllText)), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static LibraryBrowserViewModel Model(Client client, LockedDecoder decoder) =>
        new(client, Session, [Library], _ => throw new InvalidOperationException("Unexpected rejected session."),
            decodeArtwork: decoder.Decode);

    private sealed class LockedDecoder
    {
        private readonly TestPreviewImageDecoder _decoder = new();
        public int? FailOnCall { init => _decoder.FailOnCall = value; }
        public List<TrackedImageResource> Resources => _decoder.Resources;
        public PreviewImage Decode(byte[] bytes)
        {
            lock (_decoder)
            {
                return _decoder.Decode(bytes);
            }
        }
    }

    private sealed class Client(Func<string, CancellationToken, Task<byte[]?>> respond) : IJellyfinMediaPreviewClient
    {
        private readonly object _gate = new();
        public ConcurrentDictionary<string, int> Calls { get; } = new(StringComparer.Ordinal);
        public TaskCompletionSource SixStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Active { get; private set; }
        public int Maximum { get; private set; }
        public int Canceled { get; private set; }
        public bool SharedArtwork { get; init; }
        private int _cacheClears;
        public int CacheClears => Volatile.Read(ref _cacheClears);
        public void ClearImageCache() => Interlocked.Increment(ref _cacheClears);
        public Task<MediaPreviewHome> GetHomeAsync(AuthenticatedSession session, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MediaLibraryPage> GetLibraryPageAsync(AuthenticatedSession session, MediaLibrary library,
            int startIndex, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MediaLibraryPage(Enumerable.Range(startIndex, Math.Min(40, 47 - startIndex))
                .Select(index => new MediaPreviewItem($"item-{index}", $"Item {index}", "", "Series", null, null, null, "", null)
                {
                    ArtworkItemId = SharedArtwork ? "shared" : $"item-{index}",
                }).ToArray(), startIndex, 47));

        public async Task<byte[]?> GetLibraryArtworkAsync(AuthenticatedSession session, string itemId,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(Session, session);
            Calls.AddOrUpdate(itemId, 1, (_, count) => count + 1);
            lock (_gate)
            {
                Maximum = Math.Max(Maximum, ++Active);
                if (Active == 6)
                {
                    SixStarted.TrySetResult();
                }
            }

            try
            {
                return await respond(itemId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                lock (_gate) { Canceled++; }
                throw;
            }
            finally
            {
                lock (_gate) { Active--; }
            }
        }
    }
}
