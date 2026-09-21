using Cindara.Core.Authentication;
using Cindara.Core.Models;

namespace Cindara.Core.Tests.Authentication;

public sealed class PersistentSessionStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"cindara-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task SavePersistsMetadataWithoutTokenAndRestoresCredential()
    {
        var vault = new TestCredentialStore();
        using var store = CreateStore(vault);
        var session = CreateSession("token-that-must-not-be-plaintext");

        await store.SaveAsync(session);
        var restored = await store.GetAsync(session.Profile);
        var index = await File.ReadAllTextAsync(Path.Combine(_directory, "sessions.json"));

        Assert.Equal(session, restored);
        Assert.DoesNotContain(session.AccessToken, index, StringComparison.Ordinal);
        Assert.Contains(session.Username, index, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveReplacesMatchingProfileAndRemoveDeletesCredential()
    {
        var vault = new TestCredentialStore();
        using var store = CreateStore(vault);
        var original = CreateSession("old-token");
        var replacement = original with { AccessToken = "new-token" };

        await store.SaveAsync(original);
        await store.SaveAsync(replacement);

        Assert.Single(await store.GetProfilesAsync());
        Assert.Equal(replacement, await store.GetAsync(replacement.Profile));
        Assert.True(await store.RemoveAsync(replacement.Profile));
        Assert.Empty(await store.GetProfilesAsync());
        Assert.Null(await store.GetAsync(replacement.Profile));
    }

    [Fact]
    public async Task SaveReportsMetadataPersistenceFailureAndRemovesCredential()
    {
        var vault = new TestCredentialStore();
        var indexPath = Path.Combine(_directory, "sessions.json");
        Directory.CreateDirectory(indexPath);
        using var store = new PersistentSessionStore(indexPath, vault);

        var exception = await Assert.ThrowsAsync<SessionStoreException>(
            () => store.SaveAsync(CreateSession("token")));

        Assert.Equal(SessionStoreError.PersistenceFailure, exception.Error);
        Assert.Empty(vault.Secrets);
    }

    [Fact]
    public async Task FailedReplacementRestoresPreviousCredential()
    {
        var vault = new TestCredentialStore();
        var indexPath = Path.Combine(_directory, "sessions.json");
        using var store = new PersistentSessionStore(indexPath, vault);
        var original = CreateSession("old-token");
        await store.SaveAsync(original);
        File.Delete(indexPath);
        Directory.CreateDirectory(indexPath);

        await Assert.ThrowsAsync<SessionStoreException>(
            () => store.SaveAsync(original with { AccessToken = "new-token" }));

        Assert.Equal("old-token", Assert.Single(vault.Secrets).Value);
    }

    [Fact]
    public async Task FailedCredentialReplacementRestoresPreviousCredential()
    {
        var vault = new TestCredentialStore();
        using var store = CreateStore(vault);
        var original = CreateSession("old-token");
        await store.SaveAsync(original);
        vault.FailNextSet = true;

        await Assert.ThrowsAsync<SessionStoreException>(
            () => store.SaveAsync(original with { AccessToken = "new-token" }));

        Assert.Equal(original, await store.GetAsync(original.Profile));
    }

    [Fact]
    public async Task FailedCredentialRemovalRetainsProfileForRetry()
    {
        var vault = new TestCredentialStore();
        using var store = CreateStore(vault);
        var session = CreateSession("token");
        await store.SaveAsync(session);
        vault.FailRemoval = true;

        await Assert.ThrowsAsync<SessionStoreException>(
            () => store.RemoveAsync(session.Profile));

        Assert.Single(await store.GetProfilesAsync());
        Assert.Equal(session, await store.GetAsync(session.Profile));
    }

    [Fact]
    public async Task CancellationAfterCredentialRemovalRestoresCredential()
    {
        var vault = new TestCredentialStore();
        using var store = CreateStore(vault);
        var session = CreateSession("token");
        await store.SaveAsync(session);
        vault.CancelAfterRemoval = true;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.RemoveAsync(session.Profile));

        Assert.Single(await store.GetProfilesAsync());
        Assert.Equal(session, await store.GetAsync(session.Profile));
    }

    [Fact]
    public async Task ValidJsonWithInvalidProfileReportsCorruption()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            Path.Combine(_directory, "sessions.json"),
            """[{"server":null,"userId":"","username":""}]""");
        using var store = CreateStore(new TestCredentialStore());

        var exception = await Assert.ThrowsAsync<SessionStoreException>(
            () => store.GetProfilesAsync());

        Assert.Equal(SessionStoreError.InvalidData, exception.Error);
    }

    [Fact]
    public async Task ProfileWithUriCredentialsReportsCorruption()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            Path.Combine(_directory, "sessions.json"),
            """
            [{
              "server": {
                "id": "server-1",
                "baseUri": "https://user:password@media.example.com/",
                "displayName": "Living Room",
                "version": "10.10.7",
                "operatingSystem": "Linux"
              },
              "userId": "user-1",
              "username": "viewer"
            }]
            """);
        using var store = CreateStore(new TestCredentialStore());

        var exception = await Assert.ThrowsAsync<SessionStoreException>(
            () => store.GetProfilesAsync());

        Assert.Equal(SessionStoreError.InvalidData, exception.Error);
    }

    [Fact]
    public async Task ConcurrentStoreInstancesDoNotDropProfiles()
    {
        var vault = new TestCredentialStore();
        using var firstStore = CreateStore(vault);
        using var secondStore = CreateStore(vault);
        var first = CreateSession("first-token");
        var second = first with
        {
            UserId = "user-2",
            Username = "second",
            AccessToken = "second-token",
        };

        await Task.WhenAll(firstStore.SaveAsync(first), secondStore.SaveAsync(second));

        Assert.Equal(2, (await firstStore.GetProfilesAsync()).Count);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CredentialReadsWaitForTransactionRollback(bool separateStore, bool remove)
    {
        var vault = new TestCredentialStore();
        using var writer = CreateStore(vault);
        using var otherStore = CreateStore(vault);
        var reader = separateStore ? otherStore : writer;
        var original = CreateSession("original-token");
        await writer.SaveAsync(original);
        var mutated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task PauseThenFail()
        {
            mutated.SetResult();
            await release.Task;
            throw new SessionStoreException(SessionStoreError.PersistenceFailure, "Simulated failure.");
        }

        if (remove)
        {
            vault.AfterRemove = PauseThenFail;
        }
        else
        {
            vault.AfterSet = PauseThenFail;
        }

        var transaction = remove
            ? writer.RemoveAsync(original.Profile)
            : writer.SaveAsync(original with { AccessToken = "uncommitted-token" });
        Task<AuthenticatedSession?>? read = null;
        try
        {
            await mutated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            read = reader.GetAsync(original.Profile);
            Assert.False(read.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
            await Assert.ThrowsAsync<SessionStoreException>(() => transaction);
        }

        Assert.NotNull(read);
        Assert.Equal(original, await read.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanceledCredentialReadReleasesLocks(bool separateStore)
    {
        var vault = new TestCredentialStore();
        using var writer = CreateStore(vault);
        using var otherStore = CreateStore(vault);
        var reader = separateStore ? otherStore : writer;
        var original = CreateSession("original-token");
        await writer.SaveAsync(original);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mutated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vault.AfterSet = async () =>
        {
            mutated.SetResult();
            await release.Task;
        };

        var replacement = original with { AccessToken = "replacement-token" };
        var write = writer.SaveAsync(replacement);
        using var cancellation = new CancellationTokenSource();
        try
        {
            await mutated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var read = reader.GetAsync(original.Profile, cancellation.Token);
            Assert.False(read.IsCompleted);
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        }
        finally
        {
            release.TrySetResult();
            await write;
        }

        Assert.Equal(replacement, await reader.GetAsync(original.Profile).WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConditionalRemovalWaitsForReplacementCommit(bool separateStore)
    {
        var vault = new TestCredentialStore();
        using var writer = CreateStore(vault);
        using var otherStore = CreateStore(vault);
        var remover = separateStore ? otherStore : writer;
        var original = CreateSession("original-token");
        await writer.SaveAsync(original);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mutated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vault.AfterSet = async () =>
        {
            mutated.SetResult();
            await release.Task;
        };
        var replacement = original with { AccessToken = "replacement-token" };
        var write = writer.SaveAsync(replacement);
        Task<bool>? removal = null;
        try
        {
            await mutated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            removal = remover.RemoveIfMatchesAsync(original.Profile, original.AccessToken);
            Assert.False(removal.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
            await write;
        }

        Assert.NotNull(removal);
        Assert.False(await removal.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(replacement, await remover.GetAsync(original.Profile));
        Assert.Single(await remover.GetProfilesAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("different-token")]
    [InlineData("current-token")]
    public async Task ConditionalRemovalDeletesOnlyMatchingCredentials(string? expectedToken)
    {
        var vault = new TestCredentialStore();
        using var store = CreateStore(vault);
        var current = CreateSession("current-token");
        await store.SaveAsync(current);

        var removed = await store.RemoveIfMatchesAsync(current.Profile, expectedToken);

        Assert.Equal(expectedToken == current.AccessToken, removed);
        Assert.Equal(removed ? null : current, await store.GetAsync(current.Profile));
        Assert.Equal(removed ? 0 : 1, (await store.GetProfilesAsync()).Count);
    }

    [Fact]
    public async Task ConditionalRemovalCleansMissingCredentialProfileIdempotently()
    {
        var vault = new TestCredentialStore();
        using var store = CreateStore(vault);
        var session = CreateSession("token");
        await store.SaveAsync(session);
        vault.Secrets.Clear();

        Assert.True(await store.RemoveIfMatchesAsync(session.Profile, null));
        Assert.Empty(await store.GetProfilesAsync());
        Assert.True(await store.RemoveIfMatchesAsync(session.Profile, null));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    private PersistentSessionStore CreateStore(TestCredentialStore vault) =>
        new(Path.Combine(_directory, "sessions.json"), vault);

    private static AuthenticatedSession CreateSession(string token) =>
        new(
            new ServerIdentity(
                "server-1",
                new Uri("https://media.example.com/"),
                "Living Room",
                "10.10.7",
                "Linux"),
            "user-1",
            "viewer",
            token);

    private sealed class TestCredentialStore : ISecureCredentialStore
    {
        public Dictionary<string, string> Secrets { get; } = [];

        public bool FailRemoval { get; set; }

        public bool FailNextSet { get; set; }

        public bool CancelAfterRemoval { get; set; }

        public Func<Task>? AfterSet { get; set; }

        public Func<Task>? AfterRemove { get; set; }

        public Task<string?> GetAsync(
            string key,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Secrets.GetValueOrDefault(key));

        public async Task SetAsync(
            string key,
            string secret,
            CancellationToken cancellationToken = default)
        {
            if (FailNextSet)
            {
                FailNextSet = false;
                throw new SessionStoreException(
                    SessionStoreError.SecureStorageUnavailable,
                    "Credential replacement failed.");
            }

            Secrets[key] = secret;
            var afterSet = AfterSet;
            AfterSet = null;
            if (afterSet is not null)
            {
                await afterSet();
            }
        }

        public async Task<bool> RemoveAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            if (FailRemoval)
            {
                throw new SessionStoreException(
                    SessionStoreError.SecureStorageUnavailable,
                    "Credential removal failed.");
            }

            var removed = Secrets.Remove(key);
            var afterRemove = AfterRemove;
            AfterRemove = null;
            if (afterRemove is not null)
            {
                await afterRemove();
            }

            return CancelAfterRemoval
                ? await Task.FromCanceled<bool>(new CancellationToken(true))
                : removed;
        }
    }
}
