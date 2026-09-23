using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cindara.Core.Authentication;

namespace Cindara.Desktop.Libraries;

public sealed record LibraryLayoutPreferences(string[] SidebarLibraryIds, string[] HomeLibraryIds);

public sealed class LibraryLayoutSettingsStore(string directory)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
    };
    private readonly string _directory = Path.GetFullPath(directory);

    public LibraryLayoutPreferences? Load(SessionProfile profile)
    {
        FileStream stream;
        try
        {
            stream = File.OpenRead(GetPath(profile));
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }

        using (stream)
        {
            var preferences = JsonSerializer.Deserialize<LibraryLayoutPreferences>(stream, JsonOptions)
                ?? throw new JsonException("Library layout must be a JSON object.");
            Validate(preferences);
            return preferences;
        }
    }

    public void Save(SessionProfile profile, LibraryLayoutPreferences preferences)
    {
        Validate(preferences);
        Directory.CreateDirectory(_directory);
        var path = GetPath(profile);
        var pending = $"{path}.{Guid.NewGuid():N}.pending";
        try
        {
            using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, preferences, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(pending, path, overwrite: true);
        }
        finally
        {
            File.Delete(pending);
        }
    }

    private string GetPath(SessionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var binding = JsonSerializer.Serialize(new[]
        {
            profile.Server.BaseUri.AbsoluteUri, profile.Server.Id, profile.UserId,
        });
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(binding)));
        return Path.Combine(_directory, $"{key}.json");
    }

    private static void Validate(LibraryLayoutPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        foreach (var ids in new[] { preferences.SidebarLibraryIds, preferences.HomeLibraryIds })
        {
            if (ids is null || ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            {
                throw new JsonException("Library selections must contain unique, nonempty identifiers.");
            }
        }
    }
}
