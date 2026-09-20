using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cindara.Core.Models;

namespace Cindara.Core.Jellyfin;

public sealed class JellyfinServerClient(HttpClient httpClient) : IJellyfinServerClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<ServerIdentity> ConnectAsync(
        string serverAddress,
        CancellationToken cancellationToken = default)
    {
        var baseUri = ParseServerAddress(serverAddress);
        var endpoint = new Uri(baseUri, "System/Info/Public");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(endpoint, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ServerConnectionException(
                ServerConnectionError.TimedOut,
                "The Jellyfin server did not respond before the request timed out.");
        }
        catch (HttpRequestException exception)
        {
            throw new ServerConnectionException(
                ServerConnectionError.Unreachable,
                "The Jellyfin server could not be reached.",
                exception);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new ServerConnectionException(
                    ServerConnectionError.AccessDenied,
                    "The server denied access to its public system information.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ServerConnectionException(
                    ServerConnectionError.UnexpectedStatus,
                    $"The server returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }

            PublicSystemInfo? info;
            try
            {
                info = await response.Content
                    .ReadFromJsonAsync<PublicSystemInfo>(SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException exception)
            {
                throw new ServerConnectionException(
                    ServerConnectionError.InvalidResponse,
                    "The server returned system information in an unexpected format.",
                    exception);
            }
            catch (NotSupportedException exception)
            {
                throw new ServerConnectionException(
                    ServerConnectionError.InvalidResponse,
                    "The server returned an unsupported system information response.",
                    exception);
            }

            if (info is null
                || string.IsNullOrWhiteSpace(info.Id)
                || string.IsNullOrWhiteSpace(info.ServerName)
                || string.IsNullOrWhiteSpace(info.Version))
            {
                throw new ServerConnectionException(
                    ServerConnectionError.InvalidResponse,
                    "The endpoint did not return valid Jellyfin server information.");
            }

            return new ServerIdentity(
                info.Id,
                baseUri,
                info.ServerName,
                info.Version,
                info.OperatingSystem);
        }
    }

    private static Uri ParseServerAddress(string serverAddress)
    {
        if (string.IsNullOrWhiteSpace(serverAddress))
        {
            throw new ServerConnectionException(
                ServerConnectionError.InvalidAddress,
                "Enter a Jellyfin server address.");
        }

        var candidate = serverAddress.Trim();
        if (!candidate.Contains("://", StringComparison.Ordinal))
        {
            candidate = $"https://{candidate}";
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ServerConnectionException(
                ServerConnectionError.InvalidAddress,
                "Enter an absolute HTTP or HTTPS Jellyfin server address.");
        }

        var builder = new UriBuilder(uri)
        {
            Path = $"{uri.AbsolutePath.TrimEnd('/')}/",
        };

        return builder.Uri;
    }

    private sealed record PublicSystemInfo(
        string? Id,
        string? ServerName,
        string? Version,
        string? OperatingSystem);
}
