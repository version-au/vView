using System;

namespace vView.Core
{
    /// <summary>
    /// Which of VLC's messages go into vview.log (without VVIEW_VLC_LOG=1): its errors and its network warnings - but not the ones VLC
    /// gives in normal playing, which filled the log (hundreds a week) and hid the real ones:
    /// - "module not functional" / "failed to create audio output": VLC trying its first sound output and moving on to the next;
    /// - timestamp messages (can't convert / no reference clock / PCR called too late): a stream starting, seeking or buffering;
    /// - "avcodec_send_packet critical error": the decoder at a seek or the next episode;
    /// - TLS session closed / a packet that couldn't be read: a stream's connection ending (a reconnect is logged by vView itself);
    /// - SetThumbNailClip: every video.
    /// </summary>
    public static class VlcLog
    {
        static readonly string[] Routine =
        {
            "module not functional",
            "failed to create audio output",
            "could not convert timestamp",
            "timestamp conversion failed",
            "is called too late",
            "avcodec_send_packet critical error",
            "session has been invalidated",
            "error decoding the received tls packet",
            "setthumbnailclip"
        };

        /// <summary>level: VLC's ("Error", "Warning", "Notice", "Debug"); module: the VLC module ("main", "gnutls" …).</summary>
        public static bool WorthLogging(string level, string module, string message)
        {
            bool error = string.Equals(level, "Error", StringComparison.OrdinalIgnoreCase);
            bool warning = string.Equals(level, "Warning", StringComparison.OrdinalIgnoreCase);
            if (!error && !(warning && NetworkModule(module))) return false;
            string m = message ?? "";
            foreach (string r in Routine) if (m.IndexOf(r, StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return true;
        }

        /// <summary>A module that moves data over the network (its warnings are worth keeping: a stream that stalls says why).</summary>
        public static bool NetworkModule(string module)
        {
            if (string.IsNullOrEmpty(module)) return false;
            foreach (string n in new[] { "http", "tls", "access", "stream", "prefetch", "cache", "adaptive", "network" })
                if (module.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
    }
}
