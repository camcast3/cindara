using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Cindara.Core.Authentication;
using Cindara.Core.Diagnostics;
using Cindara.Core.Models;

namespace Cindara.Core.Tests.Diagnostics;

public sealed class LocalDiagnosticsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"cindara-diagnostics-{Guid.NewGuid():N}");
    private static DiagnosticEnvironment EnvironmentInfo => new(new Version(1, 0), new Version(10, 0),
        new Version(12, 0), DiagnosticPlatform.Linux, DiagnosticArchitecture.X64, DiagnosticRenderer.Skia, true, 1, true);

    [Theory]
    [InlineData("Authorization: MediaBrowser Client=\"private-client\", DeviceId=\"secret-device\", Token=\"private-token\"")]
    [InlineData("X-Emby-Token: private-token")]
    [InlineData("X-MediaBrowser-Token: private-token")]
    [InlineData("Authorization: Bearer private-token")]
    [InlineData("Authorization: Basic dXNlcjpwYXNzd29yZA==")]
    [InlineData("{\"Username\":\"private-user\",\"Pw\":\"private-password\",\"AccessToken\":\"private-token\"}")]
    [InlineData("https://private-user:private-password@private-server.example/private-path?api_key=private-token")]
    [InlineData("https://private-server.example/Items/private-media?X-Emby-Token=private-token&secret=anything#private")]
    [InlineData(@"C:\Users\private-user\Videos\private-movie.mkv")]
    [InlineData(@"/home/private-user/media/private-movie.mkv")]
    [InlineData(@"\\private-server\private-share\private-movie.mkv")]
    [InlineData("private-unlabeled-password\n{\"Level\":\"Information\",\"Errors\":[]}")]
    public void RedactionOmitsAllUntrustedExceptionTextAndData(string sensitive)
    {
        var log = new LocalDiagnostics(_directory);
        var inner = new IOException(sensitive);
        inner.Data["private"] = sensitive;
        var exception = new AuthenticationException(AuthenticationError.SecureStorageUnavailable, sensitive,
            new AggregateException(sensitive, inner, new HttpRequestException(sensitive)));
        log.Record(DiagnosticArea.Authentication, DiagnosticAction.SignIn, DiagnosticOutcome.Failed,
            DiagnosticLevel.Error, exception);

        var bundle = SupportBundle.Create(log, EnvironmentInfo);
        var output = string.Join("\n", bundle.Files.Values);
        Assert.DoesNotContain("private", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(sensitive, output, StringComparison.Ordinal);
        Assert.Contains("Authentication.SecureStorageUnavailable", output, StringComparison.Ordinal);
        Assert.Equal(["Authentication.SecureStorageUnavailable", "Unknown", "IO", "HttpRequest"],
            Assert.Single(log.RecentErrors).Errors);
        Assert.All(Directory.GetFiles(_directory), file =>
            Assert.DoesNotContain("private", File.ReadAllText(file), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExceptionTreesAreBoundedAndUnknownTypesDoNotLeakTypeNames()
    {
        var codes = DiagnosticRedactor.Describe(new AggregateException(
            Enumerable.Range(0, 100).Select(_ => new SecretNamedException())));
        Assert.Equal(8, codes.Length);
        Assert.All(codes, code => Assert.Equal("Unknown", code));
    }

    [Fact]
    public void LogsRotateAndRemainWithinAggregateBudget()
    {
        var log = new LocalDiagnostics(_directory);
        for (var index = 0; index < 5000; index++)
        {
            log.Record(DiagnosticArea.Network, DiagnosticAction.Request, DiagnosticOutcome.Completed, httpStatus: 200);
        }

        var files = new DirectoryInfo(_directory).GetFiles();
        Assert.Equal(LocalDiagnostics.MaximumFiles, files.Length);
        Assert.All(files, file => Assert.InRange(file.Length, 1, LocalDiagnostics.MaximumFileBytes));
        Assert.InRange(files.Sum(file => file.Length), 1, LocalDiagnostics.MaximumFiles * LocalDiagnostics.MaximumFileBytes);
        Assert.NotEmpty(log.Snapshot());
    }

    [Fact]
    public void StartupAndSnapshotPruneExpiredAndOversizedLogsWithoutTouchingOtherFiles()
    {
        Directory.CreateDirectory(_directory);
        var old = Path.Combine(_directory, "events-old.jsonl");
        File.WriteAllText(old, "old");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow - TimeSpan.FromDays(8));
        var oversized = Path.Combine(_directory, "events-large.jsonl");
        File.WriteAllBytes(oversized, new byte[LocalDiagnostics.MaximumFileBytes + 1]);
        var unrelated = Path.Combine(_directory, "sessions.json");
        File.WriteAllText(unrelated, "private");
        var log = new LocalDiagnostics(_directory);
        Assert.False(File.Exists(old));
        Assert.False(File.Exists(oversized));
        Assert.True(File.Exists(unrelated));

        log.Record(DiagnosticArea.Startup, DiagnosticAction.Start, DiagnosticOutcome.Completed);
        var current = Assert.Single(Directory.GetFiles(_directory, "events-*.jsonl"));
        File.SetLastWriteTimeUtc(current, DateTime.UtcNow - TimeSpan.FromDays(8));
        Assert.Empty(log.Snapshot());
        Assert.False(File.Exists(current));
    }

    [Fact]
    public void LevelFilteringAndRecentErrorQueueAreBounded()
    {
        var log = new LocalDiagnostics(_directory) { MinimumLevel = DiagnosticLevel.Warning };
        log.Record(DiagnosticArea.Startup, DiagnosticAction.Start, DiagnosticOutcome.Completed);
        Assert.Empty(log.Snapshot());
        for (var index = 0; index < 40; index++)
        {
            log.Record(DiagnosticArea.Controller, DiagnosticAction.OpenController, DiagnosticOutcome.Failed,
                DiagnosticLevel.Warning);
        }

        Assert.Equal(30, log.RecentErrors.Count);
        Assert.Equal(40, log.Snapshot().Count);
    }

    [Fact]
    public async Task ConcurrentOperationsHaveSeparateCorrelationAndPreserveItAcrossAwaits()
    {
        var log = new LocalDiagnostics(_directory);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 12).Select(async _ =>
        {
            using var operation = log.Begin(DiagnosticArea.Authentication, DiagnosticAction.SignIn);
            await gate.Task;
            log.Record(DiagnosticArea.Storage, DiagnosticAction.SignIn, DiagnosticOutcome.Completed);
            operation.Complete();
        }).ToArray();
        gate.SetResult();
        await Task.WhenAll(tasks);
        var groups = log.Snapshot().GroupBy(entry => entry.Operation).ToArray();
        Assert.Equal(12, groups.Length);
        Assert.All(groups, group =>
        {
            Assert.True(group.Key > 0);
            Assert.Equal(3, group.Count());
            Assert.Contains(group, entry => entry.Area == DiagnosticArea.Storage);
            Assert.Single(group.Select(entry => entry.RunStarted).Distinct());
        });
        log.Record(DiagnosticArea.Startup, DiagnosticAction.Stop, DiagnosticOutcome.Completed);
        Assert.Equal(0, log.Snapshot()[^1].Operation);
    }

    [Fact]
    public async Task NetworkInstrumentationOnlySendsInvokedRequestsAndOmitsAllPayloads()
    {
        var log = new LocalDiagnostics(_directory);
        var handler = new PrivateHandler();
        using var client = new HttpClient(new DiagnosticHttpHandler(log, handler));
        _ = SupportBundle.Create(log, EnvironmentInfo);
        Assert.Equal(0, handler.Calls);
        using (var operation = log.Begin(DiagnosticArea.Authentication, DiagnosticAction.SignIn))
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://private.example/private?token=private-token");
            request.Headers.TryAddWithoutValidation("Authorization", "MediaBrowser Token=\"private-token\"");
            request.Headers.TryAddWithoutValidation("X-Emby-Token", "private-token");
            request.Content = new StringContent("{\"Pw\":\"private-password\"}", Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            operation.Complete();
        }

        Assert.Equal(1, handler.Calls);
        var entry = Assert.Single(log.Snapshot(), entry => entry.Action == DiagnosticAction.Request);
        Assert.Equal(401, entry.HttpStatus);
        Assert.True(entry.Operation > 0);
        Assert.Equal(DiagnosticOutcome.Failed, entry.Outcome);
        Assert.DoesNotContain("private", SupportBundle.Create(log, EnvironmentInfo).Files["events.jsonl"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NetworkFailuresAndCancellationAreSanitizedAndRethrown(bool cancel)
    {
        var log = new LocalDiagnostics(_directory);
        using var cancellation = new CancellationTokenSource();
        var failure = cancel ? (Exception)new OperationCanceledException("private-token")
            : new HttpRequestException("private-url?token=private-token");
        using var client = new HttpClient(new DiagnosticHttpHandler(log, new FailingHandler(failure, cancellation, cancel)));
        if (cancel)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync("https://private.example", cancellation.Token));
        }
        else
        {
            await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://private.example", cancellation.Token));
        }
        var entry = Assert.Single(log.Snapshot());
        Assert.Equal(cancel ? DiagnosticOutcome.Canceled : DiagnosticOutcome.Failed, entry.Outcome);
        Assert.Equal(cancel ? "Canceled" : "HttpRequest", Assert.Single(entry.Errors));
        Assert.DoesNotContain("private", SupportBundle.Create(log, EnvironmentInfo).Files["events.jsonl"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task BundleContainsExactlyPreviewedSnapshotAndRequiresExplicitExport()
    {
        var log = new LocalDiagnostics(Path.Combine(_directory, "logs"));
        log.Record(DiagnosticArea.Startup, DiagnosticAction.Start, DiagnosticOutcome.Completed);
        var bundle = SupportBundle.Create(log, EnvironmentInfo);
        var destination = Path.Combine(_directory, "exports", "support.zip");
        Assert.False(Directory.Exists(Path.GetDirectoryName(destination)));
        Assert.Equal(["environment.json", "events.jsonl", "privacy.txt"], bundle.Files.Keys);
        log.Record(DiagnosticArea.Controller, DiagnosticAction.InitializeController, DiagnosticOutcome.Completed);
        await bundle.ExportAsync(destination);
        using (var zip = ZipFile.OpenRead(destination))
        {
            Assert.Equal(bundle.Files.Count, zip.Entries.Count);
            foreach (var entry in zip.Entries)
            {
                using var reader = new StreamReader(entry.Open());
                Assert.Equal(bundle.Files[entry.FullName], await reader.ReadToEndAsync());
            }
        }

        var original = await File.ReadAllBytesAsync(destination);
        await Assert.ThrowsAsync<IOException>(() => bundle.ExportAsync(destination));
        Assert.Equal(original, await File.ReadAllBytesAsync(destination));
        Assert.False(File.Exists(destination + ".partial"));
    }

    [Fact]
    public async Task CanceledExportLeavesNoPartialOrSuccessFile()
    {
        var log = new LocalDiagnostics(Path.Combine(_directory, "logs"));
        var bundle = SupportBundle.Create(log, EnvironmentInfo);
        var destination = Path.Combine(_directory, "support.zip");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bundle.ExportAsync(destination, cancellation.Token));
        Assert.False(File.Exists(destination));
        Assert.False(File.Exists(destination + ".partial"));
    }

    [Fact]
    public void StorageFailuresAreVisibleWithoutThrowingThroughProductOperations()
    {
        Directory.CreateDirectory(_directory);
        var blocked = Path.Combine(_directory, "not-a-directory");
        File.WriteAllText(blocked, "private");
        var log = new LocalDiagnostics(blocked);
        log.Record(DiagnosticArea.Startup, DiagnosticAction.Start, DiagnosticOutcome.Failed, DiagnosticLevel.Error,
            new IOException("private"));
        Assert.NotNull(log.StorageError);
        Assert.DoesNotContain("private", log.StorageError, StringComparison.Ordinal);
        Assert.Single(log.RecentErrors);
        Assert.ThrowsAny<IOException>(() => SupportBundle.Create(log, EnvironmentInfo));
    }

    [Fact]
    public void SnapshotRevalidatesLogsAndNeverCopiesUntrustedFieldsIntoExport()
    {
        var log = new LocalDiagnostics(_directory);
        log.Record(DiagnosticArea.Startup, DiagnosticAction.Start, DiagnosticOutcome.Completed);
        var file = Assert.Single(Directory.GetFiles(_directory));
        var entry = Assert.Single(log.Snapshot());
        File.WriteAllText(file, JsonSerializer.Serialize(entry)[..^1] + ",\"private\":\"private-token\"}\n");
        Assert.DoesNotContain("private", SupportBundle.Create(log, EnvironmentInfo).Files["events.jsonl"], StringComparison.Ordinal);
        File.WriteAllText(file, JsonSerializer.Serialize(entry with { Errors = ["private-token"] }));
        Assert.Throws<InvalidDataException>(() => log.Snapshot());
        File.WriteAllText(file, "not json private-token");
        Assert.Throws<JsonException>(() => log.Snapshot());
    }

    [Fact]
    public async Task StorageEventsShareAuthCorrelationButNeverIncludeStoredCredentials()
    {
        var log = new LocalDiagnostics(_directory);
        var store = new DiagnosticSessionStore(new InMemorySessionStore(), log);
        var session = new AuthenticatedSession(
            new ServerIdentity("private-id", new Uri("https://private.example"), "private-server", "10", null),
            "private-user-id", "private-user", "private-token");
        using (var operation = log.Begin(DiagnosticArea.Authentication, DiagnosticAction.SignIn))
        {
            await store.SaveAsync(session);
            Assert.Equal(session, await store.GetAsync(session.Profile));
            operation.Complete();
        }

        Assert.Single(log.Snapshot().Select(entry => entry.Operation).Distinct());
        Assert.Contains(log.Snapshot(), entry => entry.Area == DiagnosticArea.Storage && entry.Outcome == DiagnosticOutcome.Completed);
        Assert.DoesNotContain("private", SupportBundle.Create(log, EnvironmentInfo).Files["events.jsonl"], StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class SecretNamedException : Exception;

    private sealed class FailingHandler(Exception failure, CancellationTokenSource source, bool cancel) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (cancel)
            {
                source.Cancel();
            }

            return Task.FromException<HttpResponseMessage>(failure);
        }
    }

    private sealed class PrivateHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Contains("private", request.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                ReasonPhrase = "private-server-details",
                Content = new StringContent("private-response"),
            });
        }
    }
}
