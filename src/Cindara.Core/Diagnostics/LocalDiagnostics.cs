using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Cindara.Core.Diagnostics;

public sealed class LocalDiagnostics
{
    public const int MaximumFileBytes = 256 * 1024;
    public const int MaximumFiles = 4;
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly TimeProvider _time;
    private readonly DateTimeOffset _runStarted;
    private readonly AsyncLocal<long> _operation = new();
    private readonly Queue<DiagnosticEntry> _recentErrors = new();
    private long _nextOperation;
    private string? _activeFile;
    private string? _storageError;

    public LocalDiagnostics(string directory, TimeProvider? timeProvider = null)
    {
        _directory = Path.GetFullPath(directory);
        _time = timeProvider ?? TimeProvider.System;
        _runStarted = _time.GetUtcNow();
        TryStore(() =>
        {
            CreateLogDirectory();
            Prune();
        });
    }

    public DiagnosticLevel MinimumLevel { get; init; } = DiagnosticLevel.Information;

    public string? StorageError
    {
        get { lock (_gate) { return _storageError; } }
    }

    public IReadOnlyList<DiagnosticEntry> RecentErrors
    {
        get { lock (_gate) { return _recentErrors.ToArray(); } }
    }

    public DiagnosticOperation Begin(DiagnosticArea area, DiagnosticAction action) => new(this, area, action);

    public void Record(
        DiagnosticArea area,
        DiagnosticAction action,
        DiagnosticOutcome outcome,
        DiagnosticLevel level = DiagnosticLevel.Information,
        Exception? exception = null,
        long? elapsedMilliseconds = null,
        int? httpStatus = null)
    {
        var entry = new DiagnosticEntry(_time.GetUtcNow(), _runStarted, _operation.Value, level, area, action,
            outcome, elapsedMilliseconds, httpStatus, DiagnosticRedactor.Describe(exception));
        if (!DiagnosticRedactor.IsSafe(entry))
        {
            throw new ArgumentException("Diagnostic events must use defined codes and bounded numeric fields.");
        }

        if (level < MinimumLevel)
        {
            return;
        }

        lock (_gate)
        {
            if (level >= DiagnosticLevel.Warning)
            {
                _recentErrors.Enqueue(entry);
                while (_recentErrors.Count > 30)
                {
                    _recentErrors.Dequeue();
                }
            }

            TryStore(() => Append(entry));
        }
    }

    public IReadOnlyList<DiagnosticEntry> Snapshot()
    {
        lock (_gate)
        {
            // Export fails explicitly rather than presenting an incomplete bundle as success.
            Prune();
            var result = new List<DiagnosticEntry>();
            foreach (var file in LogFiles().OrderBy(file => file.Name, StringComparer.Ordinal))
            {
                if (file.Length > MaximumFileBytes || (file.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException("A diagnostic file is oversized or is a link.");
                }

                foreach (var line in File.ReadLines(file.FullName))
                {
                    var entry = JsonSerializer.Deserialize<DiagnosticEntry>(line);
                    if (entry is null || !DiagnosticRedactor.IsSafe(entry))
                    {
                        throw new InvalidDataException("A diagnostic file contains invalid fields.");
                    }

                    if (entry.Timestamp >= _time.GetUtcNow() - Retention)
                    {
                        result.Add(entry);
                    }
                }
            }

            return result;
        }
    }

    private void Append(DiagnosticEntry entry)
    {
        CreateLogDirectory();
        Prune();
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry) + "\n");
        if (_activeFile is null || !File.Exists(_activeFile)
            || new FileInfo(_activeFile).Length + bytes.Length > MaximumFileBytes)
        {
            // A timestamp is local file bookkeeping, not an installation/device identifier.
            _activeFile = Path.Combine(_directory,
                $"events-{DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture)}.jsonl");
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.Read };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using var created = new FileStream(_activeFile, options);
        }

        using (var stream = new FileStream(_activeFile, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            stream.Write(bytes);
        }

        Prune();
    }

    private FileInfo[] LogFiles() => new DirectoryInfo(_directory).GetFiles("events-*.jsonl");

    private void CreateLogDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(_directory);
        }
        else
        {
            Directory.CreateDirectory(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private void Prune()
    {
        var files = LogFiles().OrderByDescending(file => file.LastWriteTimeUtc).ToArray();
        var retained = 0;
        foreach (var file in files)
        {
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("Diagnostic files must not be links.");
            }

            if (file.LastWriteTimeUtc < (_time.GetUtcNow() - Retention).UtcDateTime
                || file.Length > MaximumFileBytes || retained >= MaximumFiles)
            {
                file.Delete();
            }
            else
            {
                retained++;
            }
        }
    }

    private void TryStore(Action action)
    {
        lock (_gate)
        {
            try
            {
                action();
                _storageError = null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                var error = string.Join(", ", DiagnosticRedactor.Describe(exception));
                if (_storageError != error)
                {
                    Console.Error.WriteLine($"Cindara local diagnostics storage failed: {error}");
                }

                _storageError = error;
            }
        }
    }

    public sealed class DiagnosticOperation : IDisposable
    {
        private readonly LocalDiagnostics _owner;
        private readonly DiagnosticArea _area;
        private readonly DiagnosticAction _action;
        private readonly long _previous;
        private readonly long _start = Stopwatch.GetTimestamp();
        private DiagnosticOutcome _outcome = DiagnosticOutcome.Failed;
        private bool _disposed;

        internal DiagnosticOperation(LocalDiagnostics owner, DiagnosticArea area, DiagnosticAction action)
        {
            _owner = owner;
            _area = area;
            _action = action;
            _previous = owner._operation.Value;
            owner._operation.Value = Interlocked.Increment(ref owner._nextOperation);
            owner.Record(area, action, DiagnosticOutcome.Started);
        }

        public void Fail(Exception exception)
        {
            _outcome = exception is OperationCanceledException ? DiagnosticOutcome.Canceled : DiagnosticOutcome.Failed;
            _owner.Record(_area, _action, _outcome,
                _outcome == DiagnosticOutcome.Canceled ? DiagnosticLevel.Information : DiagnosticLevel.Error, exception);
        }

        public void Complete() => _outcome = DiagnosticOutcome.Completed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _owner.Record(_area, _action, _outcome,
                _outcome == DiagnosticOutcome.Failed ? DiagnosticLevel.Error : DiagnosticLevel.Information,
                elapsedMilliseconds: (long)Stopwatch.GetElapsedTime(_start).TotalMilliseconds);
            _owner._operation.Value = _previous;
        }
    }
}
