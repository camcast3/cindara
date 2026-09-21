namespace Cindara.Core.Authentication;

internal static class CredentialTransportPolicy
{
    public static bool IsAllowed(Uri address) =>
        address.IsAbsoluteUri
        && (address.Scheme == Uri.UriSchemeHttps
            || address.Scheme == Uri.UriSchemeHttp && address.IsLoopback);
}
