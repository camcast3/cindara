using Cindara.Core.Jellyfin;
using Cindara.Desktop.Localization;

namespace Cindara.Desktop.ViewModels;

internal static class MediaTrackDescription
{
    public static string Format(MediaItemDetails? details, string type)
    {
        var tracks = (details?.Tracks ?? []).Where(track => track.TrackType == type).ToArray();
        if (tracks.Length == 0) return Loc.Get("Details.NotProvided");
        var track = tracks.FirstOrDefault(track => track.IsDefault) ?? tracks[0];
        if (type == "Subtitle" && !tracks.Any(candidate => candidate.IsDefault))
            return Loc.Format("Details.TrackCount", tracks.Length);

        string description;
        if (!string.IsNullOrWhiteSpace(track.DisplayTitle))
        {
            description = track.DisplayTitle;
        }
        else
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(track.Codec)) parts.Add(track.Codec.ToUpperInvariant());
            if (!string.IsNullOrWhiteSpace(track.Language)) parts.Add(track.Language);
            if (track.Width is > 0 && track.Height is > 0)
                parts.Add(Loc.Format("Details.Resolution", track.Width, track.Height));
            if (track.Channels is > 0) parts.Add(Loc.Format("Details.Channels", track.Channels));
            description = parts.Count > 0 ? string.Join(Loc.Get("Format.DetailSeparator"), parts)
                : Loc.Get("Details.NotProvided");
        }
        return tracks.Length > 1
            ? Loc.Format("Details.MoreTracks", description, tracks.Length - 1) : description;
    }
}
