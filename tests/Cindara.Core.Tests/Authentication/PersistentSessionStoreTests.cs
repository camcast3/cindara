using System.Net;
using System.Text;
using System.Text.Json;
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
        using var protectedValue = JsonDocument.Parse(Assert.Single(vault.Secrets).Value);
        Assert.Equal(1, protectedValue.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(session.Server.BaseUri.AbsoluteUri, protectedValue.RootElement.GetProperty("serverAddress").GetString());
        Assert.Equal(session.Server.Id, protectedValue.RootElement.GetProperty("serverId").GetString());
        Assert.Equal(session.UserId, protectedValue.RootElement.GetProperty("userId").GetString());
        Assert.Equal(session.AccessToken, protectedValue.RootElement.GetProperty("accessToken").GetString());
    }

    [Theory]
    [InlineData("https://attacker.example.com/jellyfin/")]
    [InlineData("https://media.example.com:8443/jellyfin/")]
    [InlineData("https://media.example.com/other/")]
    [InlineData("https://media.example.com/Jellyfin/")]
    [InlineData("https://media.example.com/jellyfin")]
    [InlineData("https://media.example.com/jellyfin/?destination=attacker")]
    [InlineData("https://media.example.com/jellyfin/#attacker")]
    [InlineData("http://localhost/jellyfin/")]
    public async Task OfflineAddressTamperingCannotRedirectProtectedToken(string tamperedAddress)
    {
        var vault = new TestCredentialStore();
        var original = CreateSession("protected-token");
        var saved = original with
        {
            Server = original.Server with { BaseUri = new Uri("https://media.example.com/jellyfin/") },
        };
        using (var store = CreateStore(vault))
        {
            await store.SaveAsync(saved);
        }

        var secret = Assert.Single(vault.Secrets);
        var tampered = saved.Profile with { Server = saved.Server with { BaseUri = new Uri(tamperedAddress) } };
        await WriteIndexAsync(tampered);
        using var reopened = CreateStore(vault);
        var selected = Assert.Single(await reopened.GetProfilesAsync());
        var handler = new RestoreHttpMessageHandler();
        using var authentication = CreateAuthenticationService(reopened, handler);

        var exception = await Assert.ThrowsAsync<AuthenticationException>(
            () => authentication.RestoreAsync(selected));

        Assert.Equal(AuthenticationError.SecureStorageUnavailable, exception.Error);
        Assert.IsType<SessionStoreException>(exception.InnerException);
        Assert.Contains("verified server address", exception.Message, StringComparison.Ordinal);
        Assert.Null(handler.RequestUri);
        Assert.Null(handler.AccessToken);
        Assert.DoesNotContain(saved.AccessToken, exception.ToString(), StringComparison.Ordinal);
        Assert.Equal(secret, Assert.Single(vault.Secrets));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TransplantedProtectedCredentialRejectsDifferentAccountIds(bool changeServerId)
    {
        var vault = new TestCredentialStore();
        using var store = CreateStore(vault);
        var original = CreateSession("original-secret");
        await store.SaveAsync(original);
        var protectedValue = Assert.Single(vault.Secrets).Value;
        var other = changeServerId
            ? original with { Server = original.Server with { Id = "other-server" }, AccessToken = "other-secret" }
            : original with { UserId = "other-user", AccessToken = "other-secret" };
        await store.SaveAsync(other);
        var otherKey = vault.Secrets.Single(pair => pair.Value != protectedValue).Key;
        vault.Secrets[otherKey] = protectedValue;
        var handler = new RestoreHttpMessageHandler();
        using var authentication = CreateAuthenticationService(store, handler);

        var exception = await Assert.ThrowsAsync<AuthenticationException>(
            () => authentication.RestoreAsync(other.Profile));

        Assert.Equal(AuthenticationError.SecureStorageUnavailable, exception.Error);
        Assert.Null(handler.RequestUri);
        Assert.Equal(original, await store.GetAsync(original.Profile));
    }

    [Theory]
    [InlineData("legacy-token")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"version":99,"serverId":"server-1","userId":"user-1","serverAddress":"https://media.example.com/","accessToken":"token"}""")]
    [InlineData("""{"version":1,"serverId":"server-1","userId":"user-1","accessToken":"token"}""")]
    public async Task UnboundOrInvalidCredentialsFailClosedAndCanBeExplicitlyRemoved(string secret)
    {
        var vault = new TestCredentialStore();
        using var store = CreateStore(vault);
        var session = CreateSession("token");
        await store.SaveAsync(session);
        var key = Assert.Single(vault.Secrets).Key;
        vault.Secrets[key] = secret;
        var handler = new RestoreHttpMessageHandler();
        using var authentication = CreateAuthenticationService(store, handler);

        var exception = await Assert.ThrowsAsync<AuthenticationException>(
            () => authentication.RestoreAsync(session.Profile));

        Assert.Equal(AuthenticationError.SecureStorageUnavailable, exception.Error);
        Assert.Contains("protected server binding", exception.Message, StringComparison.Ordinal);
        Assert.Null(handler.RequestUri);
        Assert.Equal(secret, vault.Secrets[key]);
        await authentication.RemoveAsync(session.Profile);
        Assert.Empty(vault.Secrets);
        Assert.Empty(await store.GetProfilesAsync());
    }

    [Fact]
    public async Task CanonicalAddressEquivalentSpellingRestoresBoundToken()
    {
        var vault = new TestCredentialStore();
        using var store = CreateStore(vault);
        var saved = CreateSession("protected-token");
        await store.SaveAsync(saved);
        var equivalent = saved.Profile with
        {
            Server = saved.Server with { BaseUri = new Uri("https://MEDIA.EXAMPLE.COM:443/") },
        };
        await WriteIndexAsync(equivalent);

        Assert.Equal(saved, await store.GetAsync(equivalent));
    }

    [Fact]
    public async Task RemovingTamperedProfileDoesNotSendTokenAndKeepsOtherAccounts()
    {
        var vault = new TestCredentialStore();
        using var store = CreateStore(vault);
        var saved = CreateSession("token");
        var other = saved with { UserId = "other-user", AccessToken = "other-token" };
        await store.SaveAsync(saved);
        await store.SaveAsync(other);
        var tampered = saved.Profile with
        {
            Server = saved.Server with { BaseUri = new Uri("https://attacker.example.com/") },
        };
        await WriteIndexAsync(tampered, other.Profile);
        var handler = new RestoreHttpMessageHandler();
        using var authentication = CreateAuthenticationService(store, handler);

        await authentication.RemoveAsync(tampered);

        Assert.Null(handler.RequestUri);
        Assert.Equal(other, await store.GetAsync(other.Profile));
        Assert.Equal(other.Profile, Assert.Single(await store.GetProfilesAsync()));
        Assert.Single(vault.Secrets);
    }

    [Fact]
    public async Task NewSignInReplacesLegacySecretWithBoundCredential()
    {
        var vault = new TestCredentialStore();
        using var store = CreateStore(vault);
        var old = CreateSession("legacy-token");
        await store.SaveAsync(old);
        var key = Assert.Single(vault.Secrets).Key;
        vault.Secrets[key] = old.AccessToken;
        var newlyAuthenticated = old with { AccessToken = "new-token" };

        await store.SaveAsync(newlyAuthenticated);

        Assert.Equal(newlyAuthenticated, await store.GetAsync(newlyAuthenticated.Profile));
        Assert.NotEqual(old.AccessToken, vault.Secrets[key]);
    }

    [Fact]
    public async Task GetUsesPersistedMetadataInsteadOfCallerMetadata()
    {
        var vault = new TestCredentialStore();
        using var store = CreateStore(vault);
        var saved = CreateSession("saved-token");
        await store.SaveAsync(saved);
        var suppliedProfile = saved.Profile with
        {
            Server = saved.Server with
            {
                BaseUri = new Uri("https://other.example.com/"),
                DisplayName = "Caller-supplied server",
            },
            Username = "caller-supplied-user",
        };
        using var reopenedStore = CreateStore(vault);

        Assert.Equal(saved, await reopenedStore.GetAsync(suppliedProfile));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task RestoreUsesCurrentPersistedDestinationWhenCallerProfileIsStale(HttpStatusCode statusCode)
    {
        var vault = new TestCredentialStore();
        using var store = CreateStore(vault);
        var original = CreateSession("old-token");
        await store.SaveAsync(original);
        var staleProfile = Assert.Single(await store.GetProfilesAsync());
        var replacement = original with
        {
            Server = original.Server with
            {
                BaseUri = new Uri("https://current.example.com/jellyfin/"),
                DisplayName = "Current server",
            },
            Username = "current-user",
            AccessToken = "current-token",
        };
        using var otherStore = CreateStore(vault);
        await otherStore.SaveAsync(replacement);
        var handler = new RestoreHttpMessageHandler { StatusCode = statusCode };
        using var service = new JellyfinAuthenticationService(
            handler,
            store,
            new JellyfinClientIdentity("Cindara", "Test", "device-1", "1.0"));

        if (statusCode == HttpStatusCode.OK)
        {
            var restored = await service.RestoreAsync(staleProfile);
            Assert.Equal(replacement, restored);
        }
        else
        {
            var exception = await Assert.ThrowsAsync<AuthenticationException>(
                () => service.RestoreAsync(staleProfile));
            Assert.Equal(AuthenticationError.RevokedSession, exception.Error);
            Assert.Empty(await store.GetProfilesAsync());
            Assert.Empty(vault.Secrets);
        }

        Assert.Equal(new Uri("https://current.example.com/jellyfin/Users/Me"), handler.RequestUri);
        Assert.Equal(replacement.AccessToken, handler.AccessToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetDoesNotReadOrphanCredentialWithoutMatchingIndexedProfile(bool missingIndex)
    {
        var vault = new TestCredentialStore();
        using var store = CreateStore(vault);
        var saved = CreateSession("saved-token");
        await store.SaveAsync(saved);
        var indexPath = Path.Combine(_directory, "sessions.json");
        if (missingIndex)
        {
            File.Delete(indexPath);
        }
        else
        {
            await File.WriteAllTextAsync(indexPath, "[]");
        }

        var previousReads = vault.GetCalls;
        Assert.Null(await store.GetAsync(saved.Profile));
        Assert.Equal(previousReads, vault.GetCalls);
        Assert.Single(vault.Secrets);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[{}]")]
    public async Task GetRejectsInvalidIndexBeforeAccessingCredential(string content)
    {
        var vault = new TestCredentialStore();
        using var store = CreateStore(vault);
        var saved = CreateSession("saved-token");
        await store.SaveAsync(saved);
        await File.WriteAllTextAsync(Path.Combine(_directory, "sessions.json"), content);
        var previousReads = vault.GetCalls;

        var exception = await Assert.ThrowsAsync<SessionStoreException>(() => store.GetAsync(saved.Profile));

        Assert.Equal(SessionStoreError.InvalidData, exception.Error);
        Assert.Equal(previousReads, vault.GetCalls);
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
        var originalProtectedValue = Assert.Single(vault.Secrets).Value;
        File.Delete(indexPath);
        Directory.CreateDirectory(indexPath);

        await Assert.ThrowsAsync<SessionStoreException>(
            () => store.SaveAsync(original with { AccessToken = "new-token" }));

        Assert.Equal(originalProtectedValue, Assert.Single(vault.Secrets).Value);
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

    [Fact]
    public async Task ConditionalRemovalPreservesNewServerBindingEvenWhenTokenMatches()
    {
        var vault = new TestCredentialStore();
        using var store = CreateStore(vault);
        var original = CreateSession("token");
        await store.SaveAsync(original);
        var replacement = original with
        {
            Server = original.Server with { BaseUri = new Uri("https://replacement.example.com/") },
        };
        await store.SaveAsync(replacement);

        Assert.False(await store.RemoveIfMatchesAsync(original.Profile, original.AccessToken));
        Assert.Equal(replacement, await store.GetAsync(replacement.Profile));
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

    private Task WriteIndexAsync(params SessionProfile[] profiles) =>
        File.WriteAllTextAsync(
            Path.Combine(_directory, "sessions.json"),
            JsonSerializer.Serialize(profiles, JsonSerializerOptions.Web));

    private static JellyfinAuthenticationService CreateAuthenticationService(
        ISessionStore store,
        HttpMessageHandler handler) =>
        new(handler, store, new JellyfinClientIdentity("Cindara", "Test", "device-1", "1.0"));

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

    private sealed class RestoreHttpMessageHandler : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;

        public Uri? RequestUri { get; private set; }

        public string? AccessToken { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            AccessToken = request.Headers.GetValues("X-Emby-Token").Single();
            return Task.FromResult(new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(
                    """{"Id":"user-1","Name":"current-user"}""",
                    Encoding.UTF8,
                    "application/json"),
            });
        }
    }

    private sealed class TestCredentialStore : ISecureCredentialStore
    {
        public Dictionary<string, string> Secrets { get; } = [];

        public int GetCalls { get; private set; }

        public bool FailRemoval { get; set; }

        public bool FailNextSet { get; set; }

        public bool CancelAfterRemoval { get; set; }

        public Func<Task>? AfterSet { get; set; }

        public Func<Task>? AfterRemove { get; set; }

        public Task<string?> GetAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            GetCalls++;
            return Task.FromResult(Secrets.GetValueOrDefault(key));
        }

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
