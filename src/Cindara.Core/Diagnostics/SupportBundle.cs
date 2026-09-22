using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Cindara.Core.Diagnostics;

/// <summary>An immutable, bounded snapshot: the preview and export contain exactly the same files.</summary>
public sealed class SupportBundle
{
    private static readonly JsonSerializerOptions EnvironmentOptions = new() { WriteIndented = true };
    private SupportBundle(Dictionary<string, string> files)
    {
        Files = new ReadOnlyDictionary<string, string>(files);
    }

    public IReadOnlyDictionary<string, string> Files { get; }

    public static SupportBundle Create(LocalDiagnostics diagnostics, DiagnosticEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(environment);
        return new SupportBundle(new Dictionary<string, string>
        {
            ["environment.json"] = JsonSerializer.Serialize(environment, EnvironmentOptions),
            ["events.jsonl"] = string.Join("\n", diagnostics.Snapshot().Select(entry => JsonSerializer.Serialize(entry))),
            ["privacy.txt"] = "Cindara local support snapshot. Created only at the user's request; never uploaded.\n"
                + "Contains versions/backend state and bounded typed events with timestamps, local operation numbers, "
                + "durations, HTTP status codes and allowlisted error codes.\n"
                + "Excludes tokens, passwords, headers, URLs, paths, account/device/server IDs, names, media, "
                + "exception messages, stacks, environment variables and configuration files.\n",
        });
    }

    public async Task WriteAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var (name, content) in Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            await using var stream = entry.Open();
            await stream.WriteAsync(Encoding.UTF8.GetBytes(content), cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task ExportAsync(string destination, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        // Write beside the destination, then move without overwriting any existing export.
        var temporary = fullPath + ".partial";
        var created = false;
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            await using (var stream = new FileStream(temporary, options))
            {
                created = true;
                await WriteAsync(stream, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, fullPath, overwrite: false);
            created = false;
        }
        finally
        {
            if (created)
            {
                File.Delete(temporary);
            }
        }
    }
}

public sealed record DiagnosticEnvironment(
    Version AppVersion,
    Version RuntimeVersion,
    Version AvaloniaVersion,
    DiagnosticPlatform Platform,
    DiagnosticArchitecture Architecture,
    DiagnosticRenderer Renderer,
    bool ControllerAvailable,
    int ConnectedControllers,
    bool DiagnosticStorageAvailable)
{
    public string ControllerBackend { get; } = "SDL3";
    public string PlaybackBackend { get; } = "Not implemented (roadmap #8)";
}

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<DiagnosticPlatform>))]
public enum DiagnosticPlatform { Linux, Windows, MacOS, Other }

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<DiagnosticArchitecture>))]
public enum DiagnosticArchitecture { X64, Arm64, Other }

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<DiagnosticRenderer>))]
public enum DiagnosticRenderer { Skia, Unknown }
