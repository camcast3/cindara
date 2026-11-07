using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cindara.Desktop.Startup;

public sealed class StartupRecoveryStore
{
    private const int SafeModeThreshold = 2;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
    };

    private readonly string _path;

    public StartupRecoveryStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public StartupRecoveryDecision Begin(bool forceSafeMode = false)
    {
        StartupState state;
        string? error = null;
        try
        {
            state = Load();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            state = new StartupState(false, SafeModeThreshold);
            error = "Startup recovery state could not be read. Safe mode is active.";
        }

        var failures = state.StartupInProgress
            ? checked(state.ConsecutiveFailedStarts + 1)
            : state.ConsecutiveFailedStarts;
        var safeMode = forceSafeMode || failures >= SafeModeThreshold || error is not null;

        try
        {
            Save(new StartupState(true, failures));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error ??= "Startup recovery state could not be saved. Safe mode is active.";
            safeMode = true;
        }

        return new StartupRecoveryDecision(safeMode, failures, error);
    }

    public void Complete()
    {
        Save(new StartupState(false, 0));
    }

    private StartupState Load()
    {
        FileStream stream;
        try
        {
            stream = File.OpenRead(_path);
        }
        catch (FileNotFoundException)
        {
            return new StartupState(false, 0);
        }
        catch (DirectoryNotFoundException)
        {
            return new StartupState(false, 0);
        }

        using (stream)
        {
            var state = JsonSerializer.Deserialize<StartupState>(stream, JsonOptions)
                ?? throw new JsonException("Startup recovery state must be a JSON object.");
            if (state.ConsecutiveFailedStarts < 0)
            {
                throw new JsonException("Startup failure count cannot be negative.");
            }

            return state;
        }
    }

    private void Save(StartupState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var pendingPath = $"{_path}.{Guid.NewGuid():N}.pending";
        try
        {
            using (var stream = new FileStream(pendingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, state, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(pendingPath, _path, overwrite: true);
        }
        finally
        {
            File.Delete(pendingPath);
        }
    }

    private sealed record StartupState(bool StartupInProgress, int ConsecutiveFailedStarts);
}
