using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cindara.Desktop.Accessibility;

public sealed class PresentationSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
    };

    private readonly string _path;

    public PresentationSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public PresentationPreferences Load()
    {
        FileStream stream;
        try
        {
            stream = File.OpenRead(_path);
        }
        catch (FileNotFoundException)
        {
            return new PresentationPreferences();
        }
        catch (DirectoryNotFoundException)
        {
            return new PresentationPreferences();
        }

        using (stream)
        {
            var stored = JsonSerializer.Deserialize<StoredPreferences>(stream, JsonOptions)
                ?? throw new JsonException("Presentation settings must be a JSON object.");
            try
            {
                return new PresentationPreferences(stored.TextScale, stored.HighContrast, stored.ReducedMotion);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw new JsonException("Presentation settings contain an unsupported text scale.", exception);
            }
        }
    }

    public void Save(PresentationPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var pendingPath = $"{_path}.{Guid.NewGuid():N}.pending";
        try
        {
            using (var stream = new FileStream(pendingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new StoredPreferences(
                    preferences.TextScale, preferences.HighContrast, preferences.ReducedMotion), JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(pendingPath, _path, overwrite: true);
        }
        finally
        {
            File.Delete(pendingPath);
        }
    }

    private sealed record StoredPreferences(double TextScale, bool HighContrast, bool ReducedMotion);
}
