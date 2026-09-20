namespace Cindara.Desktop.Authentication;

internal static class DeviceIdentityProvider
{
    public static string GetOrCreate(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var existing = File.ReadAllText(path).Trim();
                if (Guid.TryParse(existing, out _))
                {
                    return existing;
                }
            }

            var directory = Path.GetDirectoryName(path)
                ?? throw new IOException("The device identity path has no parent directory.");
            Directory.CreateDirectory(directory);
            var deviceId = Guid.NewGuid().ToString("D");
            File.WriteAllText(path, deviceId);
            return deviceId;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "Cindara could not create its stable Jellyfin device identity.",
                exception);
        }
    }
}
