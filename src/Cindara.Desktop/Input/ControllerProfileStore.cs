using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cindara.Desktop.Input;

public sealed class ControllerProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    public ControllerProfileStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public Dictionary<string, ControllerProfile> Load()
    {
        FileStream stream;
        try
        {
            stream = File.OpenRead(_path);
        }
        catch (FileNotFoundException)
        {
            return [];
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }

        using (stream)
        {
            var stored = JsonSerializer.Deserialize<StoredProfiles>(stream, JsonOptions)
                ?? throw new JsonException("Controller settings must be a JSON object.");
            if (stored.Profiles is null)
                throw new JsonException("Controller profiles must be an object.");
            foreach (var profile in stored.Profiles.Values)
            {
                if (profile is null)
                    throw new JsonException("Controller profiles must not contain null entries.");
                try
                {
                    profile.Validate();
                }
                catch (ArgumentException exception)
                {
                    throw new JsonException("Controller settings contain an invalid profile.", exception);
                }
            }

            return new Dictionary<string, ControllerProfile>(stored.Profiles, StringComparer.Ordinal);
        }
    }

    public void Save(IReadOnlyDictionary<string, ControllerProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        foreach (var profile in profiles.Values)
        {
            profile.Validate();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var pendingPath = $"{_path}.{Guid.NewGuid():N}.pending";
        try
        {
            using (var stream = new FileStream(pendingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new StoredProfiles(profiles), JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(pendingPath, _path, overwrite: true);
        }
        finally
        {
            File.Delete(pendingPath);
        }
    }

    private sealed record StoredProfiles(IReadOnlyDictionary<string, ControllerProfile> Profiles);
}
