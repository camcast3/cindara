using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cindara.Core.Authentication;
using Cindara.Core.Diagnostics;

namespace Cindara.Core.Jellyfin;

public sealed class JellyfinMediaPreviewClient : IJellyfinMediaPreviewClient, IDisposable
{
    private const int ItemLimit = 20;
    private const int ActivityBatchLimit = 200;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly JellyfinClientIdentity _clientIdentity;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _cacheGate = new();
    private AuthenticatedSession? _cacheSession;
    private MediaImageCache _imageCache = new();
    private const string ItemFields = "PrimaryImageAspectRatio,SeriesName,SeriesId,SeriesPrimaryImageTag,ParentBackdropItemId,ParentIndexNumber,IndexNumber,ProductionYear,UserData,Overview,RunTimeTicks,OfficialRating,BackdropImageTags";

    public JellyfinMediaPreviewClient(JellyfinClientIdentity clientIdentity, LocalDiagnostics? diagnostics = null)
        : this(diagnostics is null ? CreateSecureTransport()
            : new DiagnosticHttpHandler(diagnostics, CreateSecureTransport()), clientIdentity)
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
            MaxResponseContentBufferSize = 8 * 1024 * 1024,
        };
        _clientIdentity = clientIdentity;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    internal static HttpClientHandler CreateSecureTransport() => new()
    {
        AllowAutoRedirect = false,
    };

    public void Dispose()
    {
        ClearImageCache();
        _httpClient.Dispose();
    }

    public void ClearImageCache()
    {
        lock (_cacheGate)
        {
            _cacheSession = null;
            _imageCache = new(_timeProvider);
        }
    }

    private MediaImageCache GetImageCache(AuthenticatedSession session)
    {
        lock (_cacheGate)
        {
            if (_cacheSession != session)
            {
                _cacheSession = session;
                _imageCache = new(_timeProvider);
            }

            return _imageCache;
        }
    }

    public async Task<MediaPreviewHome> GetHomeAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30), _timeProvider);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, deadline.Token);
        using var load = new PreviewLoad(session, cancellation, GetImageCache(session));
        try
        {
            var continuingTask = GetContinueWatchingAsync(load);
            var librariesTask = GetLibraryRailsAsync(load);
            await Task.WhenAll(continuingTask, librariesTask).ConfigureAwait(false);

            var (continuingSources, continuing) = await continuingTask.ConfigureAwait(false);
            var (views, libraries) = await librariesTask.ConfigureAwait(false);
            var featured = continuing.Count > 0
                ? continuing[0]
                : libraries.SelectMany(rail => rail.Items).FirstOrDefault();
            if (featured is not null)
            {
                var featuredSource = continuingSources.FirstOrDefault(item => item.Id == featured.Id);
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
                ContinueWatching: continuing,
                RecentlyAddedLibraries: libraries)
            {
                Libraries = views,
            };
        }
        catch (OperationCanceledException) when (
            deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new MediaPreviewException(
                MediaPreviewError.TimedOut,
                "The Jellyfin media preview timed out.");
        }
    }

    public async Task<MediaLibraryPage> GetLibraryPageAsync(
        AuthenticatedSession session,
        MediaLibrary library,
        int startIndex,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentException.ThrowIfNullOrWhiteSpace(library.Id);
        if (!library.IsSupportedVideoLibrary)
        {
            throw new ArgumentException("Only movie and TV libraries support video browsing.", nameof(library));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30), _timeProvider);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        using var load = new PreviewLoad(session, cancellation, GetImageCache(session));
        try
        {
            var result = await GetAsync<ItemResult>(load,
                $"Users/{Uri.EscapeDataString(session.UserId)}/Items"
                + $"?ParentId={Uri.EscapeDataString(library.Id)}&StartIndex={startIndex}&Limit={MediaLibraryPage.PageSize}"
                + $"&Recursive=true&SortBy=SortName&SortOrder=Ascending&EnableTotalRecordCount=true&Fields={ItemFields}"
                + (library.CollectionType == "movies" ? "&IncludeItemTypes=Movie"
                    : library.CollectionType == "tvshows" ? "&IncludeItemTypes=Series" : string.Empty),
                "library media").ConfigureAwait(false);
            if (result?.Items is not { } sources || result.TotalRecordCount is not { } total
                || total < 0 || startIndex > 0 && startIndex >= total
                || sources.Length > MediaLibraryPage.PageSize
                || (sources.Length == 0 && startIndex < total)
                || (sources.Length > 0 && (long)startIndex + sources.Length > total)
                || sources.Any(item => item is null || string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Name)))
            {
                throw InvalidResponse(new JsonException("Invalid library page."));
            }

            var items = sources.Select(item => CreatePreviewItem(item, landscape: false, null, null) with
            {
                ArtworkItemId = GetArtworkItemId(item, landscape: false),
            }).ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            deadline.Token.ThrowIfCancellationRequested();
            return new MediaLibraryPage(items, startIndex, total);
        }
        catch (OperationCanceledException) when (
            deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new MediaPreviewException(MediaPreviewError.TimedOut, "Loading the library timed out.");
        }
    }

    public async Task<byte[]?> GetLibraryArtworkAsync(
        AuthenticatedSession session,
        string itemId,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var load = new PreviewLoad(session, cancellation, GetImageCache(session));
        return await GetArtworkAsync(load, itemId, landscape: false).ConfigureAwait(false);
    }

    private static void ValidateSession(AuthenticatedSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!CredentialTransportPolicy.IsAllowed(session.Server.BaseUri))
        {
            throw new MediaPreviewException(MediaPreviewError.InsecureConnection,
                "Media browsing requires HTTPS or local HTTP loopback to protect your access token.");
        }
    }

    private async Task<(IReadOnlyList<JellyfinItem> Sources, IReadOnlyList<MediaPreviewItem> Items)>
        GetContinueWatchingAsync(PreviewLoad load)
    {
        var resumeTask = GetWrappedItemsAsync(
            load,
            $"Users/{Uri.EscapeDataString(load.Session.UserId)}/Items/Resume"
            + $"?Limit={ItemLimit}&Recursive=true&EnableUserData=true&IncludeItemTypes=Movie,Episode&Fields={ItemFields}",
            "continue-watching media");
        var nextUpTask = GetWrappedItemsAsync(load,
            $"Shows/NextUp?UserId={Uri.EscapeDataString(load.Session.UserId)}&Limit={ItemLimit}&Fields={ItemFields}"
            + "&EnableUserData=true&EnableResumable=true&EnableRewatching=false&DisableFirstEpisode=true",
            "next-up media");
        await Task.WhenAll(resumeTask, nextUpTask).ConfigureAwait(false);
        var resume = await resumeTask.ConfigureAwait(false);
        var nextUp = await nextUpTask.ConfigureAwait(false);
        if (resume.Count > ItemLimit || nextUp.Count > ItemLimit)
        {
            throw InvalidResponse(new JsonException("Continue Watching responses exceed the requested limit."));
        }

        // Resume is authoritative for a show's current episode; never substitute an older
        // partially watched episode when the most recently played one has reached the cutoff.
        var candidates = resume
            .Where(item => item.Type is "Movie" or "Episode")
            .OrderByDescending(item => item.UserData?.LastPlayedDate)
            .Concat(nextUp.Where(item => item.Type == "Episode"))
            .Where(item => !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Name))
            .DistinctBy(item => item.Type == "Episode" && !string.IsNullOrWhiteSpace(item.SeriesId)
                ? ("series", item.SeriesId) : ("item", item.Id))
            .ToArray();
        var resolved = await Task.WhenAll(candidates.Select(async source =>
        {
            var item = IsContinueCandidate(source) ? source
                : source.Type == "Episode" && !string.IsNullOrWhiteSpace(source.SeriesId)
                    ? await GetFollowingEpisodeAsync(load, source).ConfigureAwait(false)
                    : null;
            return (Item: item, Source: source);
        })).ConfigureAwait(false);
        var playable = resolved.Where(result => result.Item is not null).ToArray();
        var seriesIds = playable.Select(result => result.Source)
            .Where(source => source.Type == "Episode" && !string.IsNullOrWhiteSpace(source.SeriesId))
            .Select(source => source.SeriesId!).ToHashSet(StringComparer.Ordinal);
        var recentActivity = await GetRecentActivityAsync(load, seriesIds).ConfigureAwait(false);
        var ranked = await Task.WhenAll(playable.Select(async result =>
            (result.Item, LastPlayed: await GetLastPlaybackAsync(load, result.Source, recentActivity).ConfigureAwait(false))))
            .ConfigureAwait(false);
        var items = ranked.OrderByDescending(result => result.LastPlayed)
            .Select(result => result.Item).OfType<JellyfinItem>().DistinctBy(item => item.Id).Take(ItemLimit).ToArray();
        var previews = await PopulateArtworkAsync(load, items, landscape: true).ConfigureAwait(false);
        return (items, previews);
    }

    private async Task<Dictionary<string, DateTimeOffset>> GetRecentActivityAsync(PreviewLoad load, HashSet<string> seriesIds)
    {
        var dates = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        if (seriesIds.Count < 2)
        {
            return dates;
        }

        var activity = await GetWrappedItemsAsync(load,
            $"Users/{Uri.EscapeDataString(load.Session.UserId)}/Items"
            + $"?Recursive=true&IncludeItemTypes=Episode&SortBy=DatePlayed&SortOrder=Descending&Limit={ActivityBatchLimit}"
            + "&EnableUserData=true&EnableImages=false&EnableTotalRecordCount=false&ExcludeLocationTypes=Virtual",
            "recent series playback activity").ConfigureAwait(false);
        if (activity.Count > ActivityBatchLimit || activity.Any(item => item is null || item.Type != "Episode"
            || string.IsNullOrWhiteSpace(item.SeriesId)))
        {
            throw InvalidResponse(new JsonException("Invalid recent series playback activity."));
        }

        foreach (var item in activity)
        {
            if (seriesIds.Contains(item.SeriesId!) && item.UserData?.LastPlayedDate is { } date
                && (!dates.TryGetValue(item.SeriesId!, out var current) || date > current))
            {
                dates[item.SeriesId!] = date;
            }
        }

        return dates;
    }

    private async Task<DateTimeOffset?> GetLastPlaybackAsync(
        PreviewLoad load, JellyfinItem source, Dictionary<string, DateTimeOffset> recentActivity)
    {
        var lastPlayed = source.UserData?.LastPlayedDate;
        if (source.Type == "Episode" && !string.IsNullOrWhiteSpace(source.SeriesId))
        {
            if (recentActivity.TryGetValue(source.SeriesId, out var activityDate))
            {
                return lastPlayed is null || activityDate > lastPlayed ? activityDate : lastPlayed;
            }

            // Next-up episodes have not been played yet. Rank the series using its
            // latest playback when it is absent from the bounded activity batch.
            var recent = await GetWrappedItemsAsync(load,
                $"Users/{Uri.EscapeDataString(load.Session.UserId)}/Items"
                + $"?ParentId={Uri.EscapeDataString(source.SeriesId)}&Recursive=true&IncludeItemTypes=Episode"
                + "&SortBy=DatePlayed&SortOrder=Descending&Limit=1&EnableUserData=true&EnableImages=false&EnableTotalRecordCount=false",
                "series playback activity").ConfigureAwait(false);
            if (recent.Count > 0 && recent[0] is null)
            {
                throw InvalidResponse(new JsonException("Invalid series playback activity."));
            }

            if (recent.Count > 0 && recent[0].UserData?.LastPlayedDate is { } seriesLastPlayed
                && (lastPlayed is null || seriesLastPlayed > lastPlayed))
            {
                lastPlayed = seriesLastPlayed;
            }
        }

        return lastPlayed;
    }

    private static double? GetPlayedPercentage(JellyfinItem item) =>
        item.UserData?.PlayedPercentage
        ?? (item.RunTimeTicks is > 0 && item.UserData?.PlaybackPositionTicks is { } position
            ? (double)position / item.RunTimeTicks.Value * 100 : null);

    private static bool IsContinueCandidate(JellyfinItem item) =>
        item.UserData?.Played is not true && (GetPlayedPercentage(item) ?? 0) < 90;

    private async Task<JellyfinItem?> GetFollowingEpisodeAsync(PreviewLoad load, JellyfinItem current)
    {
        var cursor = current.Id!;
        var visited = new HashSet<string>(StringComparer.Ordinal) { cursor };
        while (true)
        {
            // Jellyfin's StartItemId is inclusive. StartIndex=1 skips the current
            // episode while preserving the server's episode/specials ordering.
            var episodes = await GetWrappedItemsAsync(load,
                $"Shows/{Uri.EscapeDataString(current.SeriesId!)}/Episodes"
                + $"?UserId={Uri.EscapeDataString(load.Session.UserId)}&StartItemId={Uri.EscapeDataString(cursor)}"
                + $"&StartIndex=1&Limit={ItemLimit}&IsMissing=false&EnableUserData=true&Fields={ItemFields}",
                "following episode").ConfigureAwait(false);
            foreach (var episode in episodes)
            {
                if (episode is null || string.IsNullOrWhiteSpace(episode.Id) || string.IsNullOrWhiteSpace(episode.Name)
                    || episode.SeriesId != current.SeriesId || episode.Type != "Episode"
                    || !visited.Add(episode.Id))
                {
                    throw InvalidResponse(new JsonException("Invalid following episode."));
                }

                if (IsContinueCandidate(episode))
                {
                    return episode;
                }

                cursor = episode.Id;
            }

            if (episodes.Count < ItemLimit)
            {
                return null;
            }
        }
    }

    private async Task<(IReadOnlyList<MediaLibrary> Libraries, IReadOnlyList<MediaPreviewRail> Rails)> GetLibraryRailsAsync(PreviewLoad load)
    {
        var views = await GetWrappedItemsAsync(
            load,
            $"Users/{Uri.EscapeDataString(load.Session.UserId)}/Views",
            "media libraries").ConfigureAwait(false);
        var mediaViews = views
            .Where(view => !string.IsNullOrWhiteSpace(view.Id)
                && !string.IsNullOrWhiteSpace(view.Name))
            .Select(view => new MediaLibrary(view.Id!, view.Name!, view.CollectionType))
            .Where(library => library.IsSupportedVideoLibrary)
            .OrderBy(view => view.Name.Contains("anime", StringComparison.OrdinalIgnoreCase)
                ? 2
                : view.CollectionType == "tvshows" ? 0 : 1)
            .ToArray();
        var tasks = mediaViews.Select(async view =>
        {
            var latest = await GetItemsAsync(
                load,
                $"Users/{Uri.EscapeDataString(load.Session.UserId)}/Items/Latest"
                + $"?ParentId={Uri.EscapeDataString(view.Id)}&Limit={ItemLimit}"
                + $"&Fields={ItemFields}"
                + (view.CollectionType == "movies"
                    ? "&IncludeItemTypes=Movie"
                    : "&IncludeItemTypes=Series,Season,Episode"),
                $"recently added media in {view.Name}").ConfigureAwait(false);
            var items = await PopulateArtworkAsync(
                load,
                latest.Take(ItemLimit).ToArray(),
                landscape: false).ConfigureAwait(false);
            return new MediaPreviewRail(
                view.Id,
                view.Name,
                items,
                LibraryName: view.Name);
        });

        var rails = await Task.WhenAll(tasks).ConfigureAwait(false);
        return (mediaViews, rails);
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
        return result?.Items ?? throw InvalidResponse(new JsonException("Missing media items."));
    }

    private async Task<IReadOnlyList<JellyfinItem>> GetItemsAsync(
        PreviewLoad load,
        string relativeUri,
        string operation) =>
        await GetAsync<JellyfinItem[]>(
            load,
            relativeUri,
            operation)
            .ConfigureAwait(false) ?? throw InvalidResponse(new JsonException("Missing latest media."));

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
            var artworkItemId = GetArtworkItemId(item, landscape);
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
            return CreatePreviewItem(item, landscape, artwork, backdrop);
        });

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static string? GetArtworkItemId(JellyfinItem item, bool landscape) =>
        !landscape && item.Type == "Episode" && !string.IsNullOrWhiteSpace(item.SeriesId)
            && !string.IsNullOrWhiteSpace(item.SeriesPrimaryImageTag)
            ? item.SeriesId
            : item.ImageTags?.ContainsKey("Primary") is true ? item.Id : null;

    private static MediaPreviewItem CreatePreviewItem(JellyfinItem item, bool landscape, byte[]? artwork, byte[]? backdrop) =>
        new(item.Id!, BuildName(item, preferSeriesTitle: !landscape), string.Empty,
            item.Type ?? "Unknown", artwork, backdrop ?? artwork, item.Overview, string.Empty,
            GetPlayedPercentage(item) is { } percentage ? Math.Clamp(percentage, 0, 100) : null,
            HeroName: BuildName(item, preferSeriesTitle: true),
            Metadata: CreateMetadata(item, preferSeriesTitle: !landscape));

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
                if (load.Cache.Get(relativeUri) is { } cached)
                {
                    return cached;
                }

                using var request = CreateAuthenticatedRequest(
                    load.Session, new Uri(load.Session.Server.BaseUri, relativeUri));
                using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                EnsureSuccess(response, operation);
                var image = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                load.Cache.Add(relativeUri, image);
                return image;
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
        CancellationTokenSource cancellation,
        MediaImageCache cache) : IDisposable
    {
        // Metadata shares the limit so servers with many libraries cannot fan out unbounded requests.
        private readonly SemaphoreSlim _requests = new(6, 6);

        public AuthenticatedSession Session { get; } = session;
        public MediaImageCache Cache { get; } = cache;

        public ConcurrentDictionary<string, Lazy<Task<byte[]?>>> Images { get; } = new(StringComparer.Ordinal);

        public async Task<T> RunRequestAsync<T>(Func<CancellationToken, Task<T>> operation)
        {
            await _requests.WaitAsync(cancellation.Token).ConfigureAwait(false);
            var completed = false;
            try
            {
                cancellation.Token.ThrowIfCancellationRequested();
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

    private sealed record ItemResult(JellyfinItem[]? Items, int? TotalRecordCount);

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

    private sealed record JellyfinUserData(
        double? PlayedPercentage,
        long? PlaybackPositionTicks,
        bool? Played,
        DateTimeOffset? LastPlayedDate);
}
