namespace Cindara.Core.Authentication;

public sealed record JellyfinClientIdentity(
    string ClientName,
    string DeviceName,
    string DeviceId,
    string Version)
{
    internal string CreateAuthorizationHeader(string? accessToken = null)
    {
        var header = $"MediaBrowser Client=\"{Escape(ClientName)}\", "
        + $"Device=\"{Escape(DeviceName)}\", "
        + $"DeviceId=\"{Escape(DeviceId)}\", "
        + $"Version=\"{Escape(Version)}\"";
        return string.IsNullOrEmpty(accessToken)
            ? header
            : $"{header}, Token=\"{Escape(accessToken)}\"";
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}
