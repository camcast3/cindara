using System.Text.Json;

namespace Cindara.Core.Authentication;

internal sealed record ProtectedSessionCredential(
    int Version,
    string ServerId,
    string UserId,
    string ServerAddress,
    string AccessToken)
{
    private const int CurrentVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize(AuthenticatedSession session) =>
        JsonSerializer.Serialize(
            new ProtectedSessionCredential(
                CurrentVersion,
                session.Server.Id,
                session.UserId,
                CanonicalizeAddress(session.Server.BaseUri),
                session.AccessToken),
            SerializerOptions);

    public static ProtectedSessionCredential Deserialize(string secret)
    {
        ProtectedSessionCredential? credential;
        try
        {
            credential = JsonSerializer.Deserialize<ProtectedSessionCredential>(secret, SerializerOptions);
        }
        catch (JsonException)
        {
            // Legacy bare tokens cannot be bound using an untrusted metadata file.
            throw InvalidCredential();
        }

        if (credential is null
            || credential.Version != CurrentVersion
            || string.IsNullOrWhiteSpace(credential.ServerId)
            || string.IsNullOrWhiteSpace(credential.UserId)
            || string.IsNullOrWhiteSpace(credential.AccessToken)
            || !Uri.TryCreate(credential.ServerAddress, UriKind.Absolute, out var address)
            || !string.Equals(credential.ServerAddress, CanonicalizeAddress(address), StringComparison.Ordinal))
        {
            throw InvalidCredential();
        }

        return credential;
    }

    public bool Matches(SessionProfile profile) =>
        string.Equals(ServerId, profile.Server.Id, StringComparison.Ordinal)
        && string.Equals(UserId, profile.UserId, StringComparison.Ordinal)
        && string.Equals(ServerAddress, CanonicalizeAddress(profile.Server.BaseUri), StringComparison.Ordinal);

    public override string ToString() => "Protected Jellyfin credential";

    private static string CanonicalizeAddress(Uri address)
    {
        if (!address.IsAbsoluteUri
            || (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(address.Host)
            || !string.IsNullOrEmpty(address.UserInfo)
            || !string.IsNullOrEmpty(address.Query)
            || !string.IsNullOrEmpty(address.Fragment))
        {
            throw InvalidCredential();
        }

        // Bind the base path too: separate Jellyfin instances can share an origin.
        return address.AbsoluteUri;
    }

    private static SessionStoreException InvalidCredential() =>
        new(
            SessionStoreError.InvalidData,
            "The saved credential has no valid protected server binding. Remove this saved account, "
            + "then connect to a verified server address and sign in again.");
}
