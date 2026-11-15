using System;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace vView.Core
{
    /// <summary>
    /// A video Jellyfin's page handed to vView (the bridge's "play" message): its stream address and how to start it. Track numbers count
    /// from 0 among the video's own audio / subtitle tracks, in the file's order (as VLC's :audio-track / :sub-track); -1 = VLC's choice.
    /// </summary>
    public sealed class JellyfinPlay
    {
        public string Url;
        public string Title;
        public long StartMs;
        public int AudioTrack = -1;
        public int SubTrack = -1;
        /// <summary>A subtitle file the server delivers on its own (added to VLC as a slave).</summary>
        public string SubUrl;
        /// <summary>Jellyfin chose no subtitles.</summary>
        public bool SubsOff;
        /// <summary>Jellyfin already knows this one has stopped (ended, stopped by the page, or replaced) - don't tell it again.</summary>
        public bool Done;

        /// <summary>Jellyfin's id for it - the page's later messages about it (upnext, segments) carry it too.</summary>
        public string ItemId = "";
        /// <summary>The next one in the page's queue (the bridge's "upnext"): "S1 E4 · Name", the series, its picture's address; null = none.</summary>
        public string NextTitle, NextSeries, NextImage;
        /// <summary>The intro and the credits in ms, from the server (the bridge's "segments" - Intro Skipper); -1 = not known.</summary>
        public long IntroStart = -1, IntroEnd = -1, OutroStart = -1;
        /// <summary>Skip intro was used (or the intro is over); Up next was cancelled; the next one was asked for.</summary>
        public bool IntroDone, UpNextCancelled, NextAsked;
    }

    /// <summary>The Jellyfin server's address, and the script vView puts in its page (Ui/jellyfin-bridge.js).</summary>
    public static class Jellyfin
    {
        /// <summary>
        /// The server's base address from what was typed: "192.168.1.20:8096" -> "http://192.168.1.20:8096"; Jellyfin's own page
        /// (".../web/#/home") -> its server; a base path behind a reverse proxy ("https://example.com/jellyfin") kept. Null if it isn't one.
        /// </summary>
        public static string ServerBase(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return null;
            if (!text.Contains("://")) text = "http://" + text;
            Uri u;
            if (!Uri.TryCreate(text, UriKind.Absolute, out u) || (u.Scheme != "http" && u.Scheme != "https") || u.Host.Length == 0) return null;
            string path = u.AbsolutePath;
            int web = path.IndexOf("/web/", StringComparison.OrdinalIgnoreCase);
            if (web >= 0) path = path.Substring(0, web);
            else if (path.EndsWith("/web", StringComparison.OrdinalIgnoreCase)) path = path.Substring(0, path.Length - 4);
            return u.Scheme + "://" + u.Authority + path.TrimEnd('/');
        }

        /// <summary>Jellyfin's page on the server.</summary>
        public static string WebAddress(string serverBase) { return serverBase + "/web/"; }

        /// <summary>The bridge script for a server: it only does anything on that server's pages.</summary>
        public static string BridgeScript(string serverBase, string version, string device)
        {
            string js;
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("vView.jellyfin-bridge.js"))
            using (var r = new StreamReader(s)) js = r.ReadToEnd();
            // the host as the page's location.hostname shows it (an IPv6 address in brackets)
            string host = new Uri(serverBase).Host.ToLowerInvariant();
            return js.Replace("__VVIEW_HOST__", JsonSerializer.Serialize(host))
                     .Replace("__VVIEW_VERSION__", JsonSerializer.Serialize(version))
                     .Replace("__VVIEW_DEVICE__", JsonSerializer.Serialize(device));
        }
    }
}
