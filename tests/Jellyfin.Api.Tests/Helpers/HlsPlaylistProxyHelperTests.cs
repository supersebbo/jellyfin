using System;
using Jellyfin.Api.Helpers;
using Xunit;

namespace Jellyfin.Api.Tests.Helpers
{
    public static class HlsPlaylistProxyHelperTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not a playlist at all")]
        public static void Classify_NotAPlaylist_ReturnsNone(string? content)
        {
            Assert.Equal(HlsPlaylistProxyHelper.HlsPlaylistKind.None, HlsPlaylistProxyHelper.Classify(content));
        }

        [Fact]
        public static void Classify_Extm3uWithoutSegments_ReturnsNone()
        {
            const string Content = "#EXTM3U\n#EXT-X-VERSION:3\n";
            Assert.Equal(HlsPlaylistProxyHelper.HlsPlaylistKind.None, HlsPlaylistProxyHelper.Classify(Content));
        }

        [Fact]
        public static void Classify_MasterPlaylist_ReturnsMaster()
        {
            const string Content = "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1280000,CODECS=\"hvc1,mp4a\"\nvariant_720.m3u8\n";
            Assert.Equal(HlsPlaylistProxyHelper.HlsPlaylistKind.Master, HlsPlaylistProxyHelper.Classify(Content));
        }

        [Fact]
        public static void Classify_MediaPlaylist_ReturnsMedia()
        {
            const string Content = "#EXTM3U\n#EXT-X-TARGETDURATION:6\n#EXTINF:6.0,\nseg0.ts\n#EXTINF:6.0,\nseg1.ts\n";
            Assert.Equal(HlsPlaylistProxyHelper.HlsPlaylistKind.Media, HlsPlaylistProxyHelper.Classify(Content));
        }

        [Fact]
        public static void Classify_MasterTakesPrecedenceOverMedia()
        {
            // A master playlist with an inlined EXTINF should still be treated as a master (never a passthrough target).
            const string Content = "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1\nv.m3u8\n#EXTINF:6.0,\nseg.ts\n";
            Assert.Equal(HlsPlaylistProxyHelper.HlsPlaylistKind.Master, HlsPlaylistProxyHelper.Classify(Content));
        }

        [Fact]
        public static void RewriteMediaPlaylist_RelativeSegments_AreProxiedAndAbsolutized()
        {
            var playlistUri = new Uri("http://upstream.example/live/path/index.m3u8");
            const string Content = "#EXTM3U\n#EXTINF:6.0,\nseg0.ts\n#EXTINF:6.0,\nseg1.ts\n";

            var result = HlsPlaylistProxyHelper.RewriteMediaPlaylist(Content, playlistUri, "hls-proxy-segment", "tok");

            var expected0 = "hls-proxy-segment?u=" + Uri.EscapeDataString("http://upstream.example/live/path/seg0.ts") + "&ApiKey=tok";
            var expected1 = "hls-proxy-segment?u=" + Uri.EscapeDataString("http://upstream.example/live/path/seg1.ts") + "&ApiKey=tok";

            Assert.Contains(expected0, result, StringComparison.Ordinal);
            Assert.Contains(expected1, result, StringComparison.Ordinal);
            // Tag lines are preserved.
            Assert.Contains("#EXTINF:6.0,", result, StringComparison.Ordinal);
            // The raw upstream segment names must not leak to the client unproxied.
            Assert.DoesNotContain("\nseg0.ts", result, StringComparison.Ordinal);
        }

        [Fact]
        public static void RewriteMediaPlaylist_AbsoluteSegmentUrl_IsPreservedThenProxied()
        {
            var playlistUri = new Uri("http://upstream.example/path/index.m3u8");
            const string Content = "#EXTM3U\n#EXTINF:6.0,\nhttp://cdn.example/abs/seg.ts\n";

            var result = HlsPlaylistProxyHelper.RewriteMediaPlaylist(Content, playlistUri, "hls-proxy-segment", null);

            var expected = "hls-proxy-segment?u=" + Uri.EscapeDataString("http://cdn.example/abs/seg.ts");
            Assert.Contains(expected, result, StringComparison.Ordinal);
        }

        [Fact]
        public static void RewriteMediaPlaylist_NoApiKey_OmitsApiKeyParam()
        {
            var playlistUri = new Uri("http://upstream.example/path/index.m3u8");
            const string Content = "#EXTM3U\n#EXTINF:6.0,\nseg.ts\n";

            var result = HlsPlaylistProxyHelper.RewriteMediaPlaylist(Content, playlistUri, "hls-proxy-segment", null);

            Assert.DoesNotContain("ApiKey", result, StringComparison.Ordinal);
            Assert.Contains("hls-proxy-segment?u=" + Uri.EscapeDataString("http://upstream.example/path/seg.ts"), result, StringComparison.Ordinal);
        }

        [Fact]
        public static void RewriteMediaPlaylist_KeyAndMapUris_AreRewritten()
        {
            var playlistUri = new Uri("http://upstream.example/path/index.m3u8");
            const string Content =
                "#EXTM3U\n" +
                "#EXT-X-KEY:METHOD=AES-128,URI=\"secret.key\",IV=0x1\n" +
                "#EXT-X-MAP:URI=\"init.mp4\"\n" +
                "#EXTINF:6.0,\nseg0.m4s\n";

            var result = HlsPlaylistProxyHelper.RewriteMediaPlaylist(Content, playlistUri, "hls-proxy-segment", "tok");

            var keyProxied = "URI=\"hls-proxy-segment?u=" + Uri.EscapeDataString("http://upstream.example/path/secret.key") + "&ApiKey=tok\"";
            var mapProxied = "URI=\"hls-proxy-segment?u=" + Uri.EscapeDataString("http://upstream.example/path/init.mp4") + "&ApiKey=tok\"";

            Assert.Contains(keyProxied, result, StringComparison.Ordinal);
            Assert.Contains(mapProxied, result, StringComparison.Ordinal);
            // The EXT-X-KEY attributes other than URI are preserved.
            Assert.Contains("METHOD=AES-128", result, StringComparison.Ordinal);
            Assert.Contains("IV=0x1", result, StringComparison.Ordinal);
        }

        [Fact]
        public static void RewriteMediaPlaylist_TagWithoutUri_IsLeftUnchanged()
        {
            var playlistUri = new Uri("http://upstream.example/path/index.m3u8");
            const string Content = "#EXTM3U\n#EXT-X-TARGETDURATION:6\n#EXTINF:6.0,\nseg.ts\n";

            var result = HlsPlaylistProxyHelper.RewriteMediaPlaylist(Content, playlistUri, "hls-proxy-segment", "tok");

            Assert.Contains("#EXT-X-TARGETDURATION:6", result, StringComparison.Ordinal);
        }
    }
}
