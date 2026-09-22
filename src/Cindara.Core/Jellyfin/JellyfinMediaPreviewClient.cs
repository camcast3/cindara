using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cindara.Core.Authentication;

namespace Cindara.Core.Jellyfin;

public sealed class JellyfinMediaPreviewClient : IJellyfinMediaPreviewClient, IDisposable
{
    private const int ItemLimit = 20;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly JellyfinClientIdentity _clientIdentity;

    public JellyfinMediaPreviewClient(JellyfinClientIdentity clientIdentity)
        : this(CreateSecureTransport(), clientIdentity)
    {
    }

    internal JellyfinMediaPreviewClient(
        HttpMessageHandler httpHandler,
        JellyfinClientIdentity clientIdentity)
    {
        ArgumentNullException.ThrowIfNull(httpHandler);
        ArgumentNullException.ThrowIfNull(clientIdentity);
        _httpClient = new HttpClient(httpHandler)
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        _clientIdentity = clientIdentity;
    }

    internal static HttpClientHandler CreateSecureTransport() => new()
    {
        AllowAutoRedirect = false,
    };

    public void Dispose() => _httpClient.Dispose();

    public async Task<MediaPreviewHome> GetHomeAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!CredentialTransportPolicy.IsAllowed(session.Server.BaseUri))
        {
            throw new MediaPreviewException(
                MediaPreviewError.InsecureConnection,
                "The media preview requires HTTPS or local HTTP loopback to protect your access token.");
        }

        var resumeTask = GetWrappedItemsAsync(
            session,
            $"Users/{Uri.EscapeDataString(session.UserId)}/Items/Resume"
            + $"?Limit={ItemLimit}&Recursive=true&Fields=PrimaryImageAspectRatio,SeriesName,SeriesId,SeriesPrimaryImageTag,ParentBackdropItemId,ParentIndexNumber,IndexNumber,ProductionYear,UserData,Overview,RunTimeTicks,OfficialRating,BackdropImageTags",
            cancellationToken);
        var viewsTask = GetWrappedItemsAsync(
            session,
            $"Users/{Uri.EscapeDataString(session.UserId)}/Views",
            "media libraries",
            cancellationToken);

        await Task.WhenAll(resumeTask, viewsTask).ConfigureAwait(false);

        var resumeItems = await resumeTask.ConfigureAwait(false);
        var resume = await PopulateArtworkAsync(
            session,
            resumeItems,
            landscape: true,
            cancellationToken).ConfigureAwait(false);
        var libraries = await GetLibraryRailsAsync(
            session,
            await viewsTask.ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
        var featured = resume.Count > 0
            ? resume[0]
            : libraries.SelectMany(rail => rail.Items).FirstOrDefault();
        if (featured is not null)
        {
            var featuredSource = resumeItems.FirstOrDefault(item => item.Id == featured.Id);
            var backdrop = featured.Backdrop;
            if (backdrop is null)
            {
                var backdropItemId = featuredSource is null
                    ? featured.Id
                    : GetBackdropItemId(featuredSource);
                backdrop = await GetBackdropAsync(session, backdropItemId, cancellationToken)
                    .ConfigureAwait(false);
            }

            featured = featuredSource is not null
                ? featured with
                {
                    Name = BuildName(featuredSource, preferSeriesTitle: true),
                    Metadata = CreateMetadata(featuredSource, preferSeriesTitle: true),
                    Backdrop = backdrop ?? featured.Artwork,
                }
                : featured with { Backdrop = backdrop ?? featured.Artwork };
        }

        return new MediaPreviewHome(
            Featured: featured,
            ContinueWatching: resume,
            RecentlyAddedLibraries: libraries);
    }

    private async Task<IReadOnlyList<MediaPreviewRail>> GetLibraryRailsAsync(
        AuthenticatedSession session,
        IReadOnlyList<JellyfinItem> views,
        CancellationToken cancellationToken)
    {
        var mediaViews = views
            .Where(view => !string.IsNullOrWhiteSpace(view.Id)
                && !string.IsNullOrWhiteSpace(view.Name)
                && view.CollectionType is "movies" or "tvshows")
            .OrderBy(view => view.Name!.Contains("anime", StringComparison.OrdinalIgnoreCase)
                ? 2
                : view.CollectionType == "tvshows" ? 0 : 1)
            .ToArray();
        var tasks = mediaViews.Select(async view =>
        {
            var latest = await GetItemsAsync(
                session,
                $"Users/{Uri.EscapeDataString(session.UserId)}/Items/Latest"
                + $"?ParentId={Uri.EscapeDataString(view.Id!)}&Limit={ItemLimit}"
                + "&Fields=PrimaryImageAspectRatio,SeriesName,SeriesId,SeriesPrimaryImageTag,ParentBackdropItemId,ParentIndexNumber,IndexNumber,ProductionYear,UserData,Overview,RunTimeTicks,OfficialRating,BackdropImageTags"
                + (view.CollectionType == "movies"
                    ? "&IncludeItemTypes=Movie"
                    : "&IncludeItemTypes=Series,Season,Episode"),
                $"recently added media in {view.Name}",
                cancellationToken).ConfigureAwait(false);
            var items = await PopulateArtworkAsync(
                session,
                latest,
                landscape: false,
                cancellationToken).ConfigureAwait(false);
            return new MediaPreviewRail(
                view.Id!,
                view.Name!,
                items,
                LibraryName: view.Name);
        });

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<JellyfinItem>> GetWrappedItemsAsync(
        AuthenticatedSession session,
        string relativeUri,
        string operation,
        CancellationToken cancellationToken)
    {
        var result = await GetAsync<ItemResult>(
            session,
            relativeUri,
            operation,
            cancellationToken)
            .ConfigureAwait(false);
        return result?.Items ?? [];
    }

    private Task<IReadOnlyList<JellyfinItem>> GetWrappedItemsAsync(
        AuthenticatedSession session,
        string relativeUri,
        CancellationToken cancellationToken) =>
        GetWrappedItemsAsync(session, relativeUri, "continue-watching media", cancellationToken);

    private async Task<IReadOnlyList<JellyfinItem>> GetItemsAsync(
        AuthenticatedSession session,
        string relativeUri,
        string operation,
        CancellationToken cancellationToken) =>
        await GetAsync<JellyfinItem[]>(
            session,
            relativeUri,
            operation,
            cancellationToken)
            .ConfigureAwait(false) ?? [];

    private async Task<T?> GetAsync<T>(
        AuthenticatedSession session,
        string relativeUri,
        string operation,
        CancellationToken cancellationToken)
    {
        using var request = CreateAuthenticatedRequest(
            session,
            new Uri(session.Server.BaseUri, relativeUri));
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, operation);

        try
        {
            return await response.Content
                .ReadFromJsonAsync<T>(SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw InvalidResponse(exception);
        }
        catch (NotSupportedException exception)
        {
            throw InvalidResponse(exception);
        }
    }

    private async Task<IReadOnlyList<MediaPreviewItem>> PopulateArtworkAsync(
        AuthenticatedSession session,
        IReadOnlyList<JellyfinItem> items,
        bool landscape,
        CancellationToken cancellationToken)
    {
        var previews = new List<MediaPreviewItem>(items.Count);
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Name))
            {
                continue;
            }

            var artworkItemId = !landscape
                && item.Type == "Episode"
                && !string.IsNullOrWhiteSpace(item.SeriesId)
                && !string.IsNullOrWhiteSpace(item.SeriesPrimaryImageTag)
                    ? item.SeriesId
                    : item.ImageTags?.ContainsKey("Primary") is true
                        ? item.Id
                        : null;
            var artwork = artworkItemId is not null
                ? await GetArtworkAsync(
                    session,
                    artworkItemId,
                    landscape,
                    cancellationToken).ConfigureAwait(false)
                : null;
            var hasBackdrop = item.BackdropImageTags is { Length: > 0 }
                || !string.IsNullOrWhiteSpace(item.ParentBackdropItemId);
            var shouldLoadBackdrop = landscape || previews.Count < 8 && hasBackdrop;
            var backdrop = shouldLoadBackdrop
                ? await GetBackdropAsync(
                    session,
                    GetBackdropItemId(item),
                    cancellationToken).ConfigureAwait(false)
                : null;
            previews.Add(new MediaPreviewItem(
                item.Id,
                BuildName(item, preferSeriesTitle: !landscape),
                string.Empty,
                item.Type ?? "Unknown",
                artwork,
                backdrop ?? artwork,
                item.Overview,
                string.Empty,
                item.UserData?.PlayedPercentage is { } percentage
                    ? Math.Clamp(percentage, 0, 100)
                    : null,
                HeroName: BuildName(item, preferSeriesTitle: true),
                Metadata: CreateMetadata(item, preferSeriesTitle: !landscape)));
        }

        return previews;
    }

    private async Task<byte[]?> GetArtworkAsync(
        AuthenticatedSession session,
        string itemId,
        bool landscape,
        CancellationToken cancellationToken)
    {
        var imageType = landscape ? "Thumb" : "Primary";
        var width = landscape ? 720 : 320;
        using var request = CreateAuthenticatedRequest(
            session,
            new Uri(
                session.Server.BaseUri,
                $"Items/{Uri.EscapeDataString(itemId)}/Images/{imageType}?maxWidth={width}&quality=88"));
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound && landscape)
        {
            return await GetArtworkAsync(
                session,
                itemId,
                landscape: false,
                cancellationToken).ConfigureAwait(false);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        EnsureSuccess(response, "media artwork");
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte[]?> GetBackdropAsync(
        AuthenticatedSession session,
        string itemId,
        CancellationToken cancellationToken)
    {
        using var request = CreateAuthenticatedRequest(
            session,
            new Uri(
                session.Server.BaseUri,
                $"Items/{Uri.EscapeDataString(itemId)}/Images/Backdrop/0?maxWidth=2560&quality=88"));
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        EnsureSuccess(response, "featured artwork");
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    private HttpRequestMessage CreateAuthenticatedRequest(
        AuthenticatedSession session,
        Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation(
            "Authorization",
            _clientIdentity.CreateAuthorizationHeader(session.AccessToken));
        request.Headers.TryAddWithoutValidation("X-Emby-Token", session.AccessToken);
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MediaPreviewException(
                MediaPreviewError.TimedOut,
                "The Jellyfin media preview timed out.");
        }
        catch (HttpRequestException exception)
        {
            throw new MediaPreviewException(
                MediaPreviewError.Network,
                "The Jellyfin media preview could not be loaded.",
                exception);
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response, string operation)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new MediaPreviewException(
                MediaPreviewError.AccessDenied,
                $"Jellyfin rejected the session while loading {operation} (HTTP 401).");
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new MediaPreviewException(
                MediaPreviewError.AccessDenied,
                $"This Jellyfin account cannot access {operation} (HTTP 403).");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new MediaPreviewException(
                MediaPreviewError.UnexpectedStatus,
                $"Jellyfin returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}) while loading the preview.");
        }
    }

    private static MediaPreviewException InvalidResponse(Exception exception) =>
        new(
            MediaPreviewError.InvalidResponse,
            "Jellyfin returned media data in an unexpected format.",
            exception);

    private static string BuildName(JellyfinItem item, bool preferSeriesTitle) =>
        preferSeriesTitle
        && item.Type is "Episode" or "Season"
        && !string.IsNullOrWhiteSpace(item.SeriesName)
            ? item.SeriesName
            : item.Name!;

    private static string GetBackdropItemId(JellyfinItem item) =>
        !string.IsNullOrWhiteSpace(item.ParentBackdropItemId)
            ? item.ParentBackdropItemId
            : item.Type is "Episode" or "Season" && !string.IsNullOrWhiteSpace(item.SeriesId)
                ? item.SeriesId
                : item.Id!;

    private static MediaPreviewMetadata CreateMetadata(JellyfinItem item, bool preferSeriesTitle) =>
        new(
            item.Name!,
            item.SeriesName,
            item.ParentIndexNumber,
            item.IndexNumber,
            item.ProductionYear,
            item.RunTimeTicks,
            item.OfficialRating,
            preferSeriesTitle);

    private sealed record ItemResult(JellyfinItem[]? Items);

    private sealed record JellyfinItem(
        string? Id,
        string? Name,
        string? Type,
        string? SeriesName,
        string? SeriesId,
        string? SeriesPrimaryImageTag,
        string? ParentBackdropItemId,
        string? CollectionType,
        int? ParentIndexNumber,
        int? IndexNumber,
        int? ProductionYear,
        long? RunTimeTicks,
        string? OfficialRating,
        string? Overview,
        string[]? BackdropImageTags,
        Dictionary<string, string>? ImageTags,
        JellyfinUserData? UserData);

    private sealed record JellyfinUserData(double? PlayedPercentage);
}
