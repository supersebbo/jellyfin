using System;
using System.IO;
using System.Text;

namespace Jellyfin.Api.Helpers;

/// <summary>
/// Helpers for classifying an upstream HLS playlist and rewriting a media (segment) playlist
/// so that every segment/key/map URI is fetched back through the Jellyfin proxy endpoint.
/// </summary>
public static class HlsPlaylistProxyHelper
{
    /// <summary>
    /// The kind of HLS playlist.
    /// </summary>
    public enum HlsPlaylistKind
    {
        /// <summary>
        /// Not an HLS playlist (or empty/continuous stream).
        /// </summary>
        None,

        /// <summary>
        /// A master/variant playlist (contains <c>#EXT-X-STREAM-INF</c>).
        /// </summary>
        Master,

        /// <summary>
        /// A media playlist with a fetchable segment list (contains <c>#EXTINF</c>).
        /// </summary>
        Media
    }

    /// <summary>
    /// Classifies an HLS playlist body as a master playlist, a segmented media playlist, or neither.
    /// </summary>
    /// <param name="content">The raw playlist body.</param>
    /// <returns>The detected <see cref="HlsPlaylistKind"/>.</returns>
    public static HlsPlaylistKind Classify(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return HlsPlaylistKind.None;
        }

        // A valid HLS playlist must start with the #EXTM3U tag.
        if (content.IndexOf("#EXTM3U", StringComparison.Ordinal) < 0)
        {
            return HlsPlaylistKind.None;
        }

        // A master playlist references variant playlists, not segments. It is never a passthrough target.
        if (content.Contains("#EXT-X-STREAM-INF", StringComparison.Ordinal))
        {
            return HlsPlaylistKind.Master;
        }

        // A media playlist contains a fetchable segment list.
        if (content.Contains("#EXTINF", StringComparison.Ordinal))
        {
            return HlsPlaylistKind.Media;
        }

        return HlsPlaylistKind.None;
    }

    /// <summary>
    /// Rewrites a media playlist so that every segment line and every <c>URI="..."</c> attribute
    /// (key, map, etc.) is resolved to an absolute upstream URL and routed through the proxy endpoint.
    /// </summary>
    /// <param name="content">The upstream media playlist body.</param>
    /// <param name="playlistUri">The absolute URI the playlist was fetched from (used to resolve relative URIs).</param>
    /// <param name="segmentEndpoint">Relative endpoint name handling proxied segments (e.g. <c>hls-proxy-segment</c>).</param>
    /// <param name="apiKey">Optional access token appended to each proxied URL.</param>
    /// <returns>The rewritten media playlist.</returns>
    public static string RewriteMediaPlaylist(string content, Uri playlistUri, string segmentEndpoint, string? apiKey)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(playlistUri);

        var sb = new StringBuilder(content.Length + 256);
        using var reader = new StringReader(content);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                sb.Append('\n');
                continue;
            }

            if (trimmed[0] == '#')
            {
                // Tag line. Rewrite an embedded URI="..." (EXT-X-KEY, EXT-X-MAP, EXT-X-MEDIA, ...) if present.
                sb.Append(RewriteUriAttribute(trimmed, playlistUri, segmentEndpoint, apiKey)).Append('\n');
            }
            else
            {
                // Segment URI line.
                if (Uri.TryCreate(playlistUri, trimmed, out var absolute))
                {
                    sb.Append(BuildProxyUrl(segmentEndpoint, absolute, apiKey)).Append('\n');
                }
                else
                {
                    sb.Append(trimmed).Append('\n');
                }
            }
        }

        return sb.ToString();
    }

    private static string RewriteUriAttribute(string tagLine, Uri playlistUri, string segmentEndpoint, string? apiKey)
    {
        const string Marker = "URI=\"";
        var start = tagLine.IndexOf(Marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return tagLine;
        }

        var valueStart = start + Marker.Length;
        var valueEnd = tagLine.IndexOf('"', valueStart);
        if (valueEnd < 0)
        {
            return tagLine;
        }

        var original = tagLine.Substring(valueStart, valueEnd - valueStart);
        if (!Uri.TryCreate(playlistUri, original, out var absolute))
        {
            return tagLine;
        }

        var proxied = BuildProxyUrl(segmentEndpoint, absolute, apiKey);
        return string.Concat(tagLine.AsSpan(0, valueStart), proxied, tagLine.AsSpan(valueEnd));
    }

    private static string BuildProxyUrl(string segmentEndpoint, Uri absoluteUpstreamUri, string? apiKey)
    {
        // Relative URL resolved by the HLS client against the playlist URL.
        var sb = new StringBuilder(segmentEndpoint);
        sb.Append("?u=").Append(Uri.EscapeDataString(absoluteUpstreamUri.AbsoluteUri));
        if (!string.IsNullOrEmpty(apiKey))
        {
            sb.Append("&ApiKey=").Append(Uri.EscapeDataString(apiKey));
        }

        return sb.ToString();
    }
}
