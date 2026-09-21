namespace Cindara.Desktop.Authentication;

internal static class DeviceIdentityProvider
{
    public static string GetOrCreate(string path)
    {
        try
        {
            var existing = ReadValidId(path);
            if (existing is not null)
            {
                return existing;
            }

            var directory = Path.GetDirectoryName(path)
                ?? throw new IOException("The device identity path has no parent directory.");
            Directory.CreateDirectory(directory);
            using var fileLock = AcquireExclusiveLock($"{path}.lock");
            existing = ReadValidId(path);
            if (existing is not null)
            {
                return existing;
            }

            var deviceId = Guid.NewGuid().ToString("D");
            if (!File.Exists(path))
            {
                using var stream = new FileStream(
                    path,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None);
                using var writer = new StreamWriter(stream);
                writer.Write(deviceId);
                return deviceId;
            }

            var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(temporaryPath, deviceId);
                File.Move(temporaryPath, path, true);
                return deviceId;
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "Cindara could not create its stable Jellyfin device identity.",
                exception);
        }
    }

    private static string? ReadValidId(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var value = File.ReadAllText(path).Trim();
            return Guid.TryParse(value, out _) ? value : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static FileStream AcquireExclusiveLock(string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return new FileStream(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (IOException) when (attempt < 100)
            {
                Thread.Sleep(20);
            }
        }
    }
}
