using System.Collections.Concurrent;
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
    private readonly TimeProvider _timeProvider;

    public JellyfinMediaPreviewClient(JellyfinClientIdentity clientIdentity)
        : this(CreateSecureTransport(), clientIdentity)
    {
    }

    internal JellyfinMediaPreviewClient(
        HttpMessageHandler httpHandler,
        JellyfinClientIdentity clientIdentity,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpHandler);
        ArgumentNullException.ThrowIfNull(clientIdentity);
        _httpClient = new HttpClient(httpHandler)
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        _clientIdentity = clientIdentity;
        _timeProvider = timeProvider ?? TimeProvider.System;
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

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30), _timeProvider);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, deadline.Token);
        using var load = new PreviewLoad(session, cancellation);
        try
        {
            var resumeTask = GetResumeAsync(load);
            var librariesTask = GetLibraryRailsAsync(load);
            await Task.WhenAll(resumeTask, librariesTask).ConfigureAwait(false);

            var (resumeItems, resume) = await resumeTask.ConfigureAwait(false);
            var libraries = await librariesTask.ConfigureAwait(false);
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
                    backdrop = await GetBackdropAsync(load, backdropItemId).ConfigureAwait(false);
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

            cancellationToken.ThrowIfCancellationRequested();
            deadline.Token.ThrowIfCancellationRequested();
            return new MediaPreviewHome(
                Featured: featured,
                ContinueWatching: resume,
                RecentlyAddedLibraries: libraries);
        }
        catch (OperationCanceledException) when (
            deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new MediaPreviewException(
                MediaPreviewError.TimedOut,
                "The Jellyfin media preview timed out.");
        }
    }

    private async Task<(IReadOnlyList<JellyfinItem> Sources, IReadOnlyList<MediaPreviewItem> Items)>
        GetResumeAsync(PreviewLoad load)
    {
        var items = await GetWrappedItemsAsync(
            load,
            $"Users/{Uri.EscapeDataString(load.Session.UserId)}/Items/Resume"
            + $"?Limit={ItemLimit}&Recursive=true&Fields=PrimaryImageAspectRatio,SeriesName,SeriesId,SeriesPrimaryImageTag,ParentBackdropItemId,ParentIndexNumber,IndexNumber,ProductionYear,UserData,Overview,RunTimeTicks,OfficialRating,BackdropImageTags",
            "continue-watching media").ConfigureAwait(false);
        var previews = await PopulateArtworkAsync(load, items, landscape: true).ConfigureAwait(false);
        return (items, previews);
    }

    private async Task<IReadOnlyList<MediaPreviewRail>> GetLibraryRailsAsync(PreviewLoad load)
    {
        var views = await GetWrappedItemsAsync(
            load,
            $"Users/{Uri.EscapeDataString(load.Session.UserId)}/Views",
            "media libraries").ConfigureAwait(false);
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
                load,
                $"Users/{Uri.EscapeDataString(load.Session.UserId)}/Items/Latest"
                + $"?ParentId={Uri.EscapeDataString(view.Id!)}&Limit={ItemLimit}"
                + "&Fields=PrimaryImageAspectRatio,SeriesName,SeriesId,SeriesPrimaryImageTag,ParentBackdropItemId,ParentIndexNumber,IndexNumber,ProductionYear,UserData,Overview,RunTimeTicks,OfficialRating,BackdropImageTags"
                + (view.CollectionType == "movies"
                    ? "&IncludeItemTypes=Movie"
                    : "&IncludeItemTypes=Series,Season,Episode"),
                $"recently added media in {view.Name}").ConfigureAwait(false);
            var items = await PopulateArtworkAsync(
                load,
                latest,
                landscape: false).ConfigureAwait(false);
            return new MediaPreviewRail(
                view.Id!,
                view.Name!,
                items,
                LibraryName: view.Name);
        });

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<JellyfinItem>> GetWrappedItemsAsync(
        PreviewLoad load,
        string relativeUri,
        string operation)
    {
        var result = await GetAsync<ItemResult>(
            load,
            relativeUri,
            operation)
            .ConfigureAwait(false);
        return result?.Items ?? [];
    }

    private async Task<IReadOnlyList<JellyfinItem>> GetItemsAsync(
        PreviewLoad load,
        string relativeUri,
        string operation) =>
        await GetAsync<JellyfinItem[]>(
            load,
            relativeUri,
            operation)
            .ConfigureAwait(false) ?? [];

    private Task<T?> GetAsync<T>(
        PreviewLoad load,
        string relativeUri,
        string operation) =>
        load.RunRequestAsync(async cancellationToken =>
        {
            using var request = CreateAuthenticatedRequest(
                load.Session,
                new Uri(load.Session.Server.BaseUri, relativeUri));
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
        });

    private async Task<IReadOnlyList<MediaPreviewItem>> PopulateArtworkAsync(
        PreviewLoad load,
        IReadOnlyList<JellyfinItem> items,
        bool landscape)
    {
        var tasks = items
            .Where(item => !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Name))
            .Select(async (item, index) =>
        {
            var artworkItemId = !landscape
                && item.Type == "Episode"
                && !string.IsNullOrWhiteSpace(item.SeriesId)
                && !string.IsNullOrWhiteSpace(item.SeriesPrimaryImageTag)
                    ? item.SeriesId
                    : item.ImageTags?.ContainsKey("Primary") is true
                        ? item.Id
                        : null;
            var artworkTask = artworkItemId is not null
                ? GetArtworkAsync(
                    load,
                    artworkItemId,
                    landscape)
                : Task.FromResult<byte[]?>(null);
            var hasBackdrop = item.BackdropImageTags is { Length: > 0 }
                || !string.IsNullOrWhiteSpace(item.ParentBackdropItemId);
            var shouldLoadBackdrop = landscape || index < 8 && hasBackdrop;
            var backdropTask = shouldLoadBackdrop
                ? GetBackdropAsync(load, GetBackdropItemId(item))
                : Task.FromResult<byte[]?>(null);
            await Task.WhenAll(artworkTask, backdropTask).ConfigureAwait(false);
            var artwork = await artworkTask.ConfigureAwait(false);
            var backdrop = await backdropTask.ConfigureAwait(false);
            return new MediaPreviewItem(
                item.Id!,
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
                Metadata: CreateMetadata(item, preferSeriesTitle: !landscape));
        });

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task<byte[]?> GetArtworkAsync(
        PreviewLoad load,
        string itemId,
        bool landscape)
    {
        var imageType = landscape ? "Thumb" : "Primary";
        var width = landscape ? 720 : 320;
        var artwork = await GetImageAsync(
            load,
            $"Items/{Uri.EscapeDataString(itemId)}/Images/{imageType}?maxWidth={width}&quality=88",
            "media artwork").ConfigureAwait(false);
        // Release the request slot before falling back, and share the primary request with other rails.
        return artwork is null && landscape
            ? await GetArtworkAsync(load, itemId, landscape: false).ConfigureAwait(false)
            : artwork;
    }

    private Task<byte[]?> GetBackdropAsync(PreviewLoad load, string itemId) =>
        GetImageAsync(
            load,
            $"Items/{Uri.EscapeDataString(itemId)}/Images/Backdrop/0?maxWidth=2560&quality=88",
            "featured artwork");

    private Task<byte[]?> GetImageAsync(PreviewLoad load, string relativeUri, string operation) =>
        load.Images.GetOrAdd(
            relativeUri,
            _ => new Lazy<Task<byte[]?>>(() => load.RunRequestAsync<byte[]?>(async cancellationToken =>
            {
                using var request = CreateAuthenticatedRequest(
                    load.Session, new Uri(load.Session.Server.BaseUri, relativeUri));
                using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                EnsureSuccess(response, operation);
                return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            }))).Value;

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

    private sealed class PreviewLoad(
        AuthenticatedSession session,
        CancellationTokenSource cancellation) : IDisposable
    {
        // Metadata shares the limit so servers with many libraries cannot fan out unbounded requests.
        private readonly SemaphoreSlim _requests = new(6, 6);

        public AuthenticatedSession Session { get; } = session;

        public ConcurrentDictionary<string, Lazy<Task<byte[]?>>> Images { get; } = new(StringComparer.Ordinal);

        public async Task<T> RunRequestAsync<T>(Func<CancellationToken, Task<T>> operation)
        {
            await _requests.WaitAsync(cancellation.Token).ConfigureAwait(false);
            var completed = false;
            try
            {
                var result = await operation(cancellation.Token).ConfigureAwait(false);
                completed = true;
                return result;
            }
            finally
            {
                // Sibling tasks are still awaited by WhenAll before this load's resources are disposed.
                if (!completed)
                {
                    cancellation.Cancel();
                }

                _requests.Release();
            }
        }

        public void Dispose() => _requests.Dispose();
    }

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
