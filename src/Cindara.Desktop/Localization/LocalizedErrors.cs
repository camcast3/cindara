using Cindara.Core.Authentication;
using Cindara.Core.Jellyfin;

namespace Cindara.Desktop.Localization;

public static class LocalizedErrors
{
    public static string Get(AuthenticationException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return Enum.IsDefined(exception.Error)
            ? Loc.Get($"Error.Authentication.{exception.Error}")
            : Loc.Get("Error.Unknown");
    }

    public static string Get(ServerConnectionException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return Enum.IsDefined(exception.Error)
            ? Loc.Get($"Error.Server.{exception.Error}")
            : Loc.Get("Error.Unknown");
    }

    public static string Get(MediaPreviewException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return Enum.IsDefined(exception.Error)
            ? Loc.Get($"Error.Preview.{exception.Error}")
            : Loc.Get("Error.Unknown");
    }
}
