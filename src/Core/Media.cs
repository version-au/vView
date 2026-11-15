using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Media;

namespace vView.Core
{
    public enum MediaKind { None, Picture, Video, Audio }

    /// <summary>A file vView can show: a picture (Windows' own decoders), a video or a sound (VLC). One per entry in the strip.</summary>
    public sealed class MediaItem : INotifyPropertyChanged
    {
        /// <summary>The file's path - or, for a network stream, its address (rtsp://, rtmp://, http://...).</summary>
        public string Path { get; private set; }
        public bool IsStream { get; private set; }
        /// <summary>A video played from Jellyfin's page (its stream address is Path): how to start it. Null for anything else.</summary>
        public JellyfinPlay Jellyfin { get; private set; }
        public string Name { get { return Jellyfin != null ? Jellyfin.Title : IsStream ? Media.StreamName(Path) : System.IO.Path.GetFileName(Path); } }
        public MediaKind Kind { get; private set; }
        public bool IsVideo { get { return Kind == MediaKind.Video; } }
        public bool IsAudio { get { return Kind == MediaKind.Audio; } }

        public MediaItem(string path) { Path = path; Kind = Media.KindOf(path); }

        /// <summary>A network stream (played as a video - VLC works out whether there's a picture).</summary>
        public static MediaItem Stream(string address) { return new MediaItem(address) { IsStream = true, Kind = MediaKind.Video }; }

        /// <summary>A video Jellyfin's page asked vView to play (see MainWindow.Jellyfin.cs).</summary>
        public static MediaItem FromJellyfin(JellyfinPlay play) { return new MediaItem(play.Url) { IsStream = true, Kind = MediaKind.Video, Jellyfin = play }; }

        ImageSource _thumb;
        public ImageSource Thumb { get { return _thumb; } set { _thumb = value; Changed("Thumb"); } }
        public bool ThumbPending;

        bool _current;
        public bool IsCurrent { get { return _current; } set { if (_current != value) { _current = value; Changed("IsCurrent"); } } }

        // ---- a sound in the playlist: its tags (read by VLC, see MusicInfo), its place and its cover ----

        public TrackInfo Info { get; private set; }
        public bool InfoQueued;
        /// <summary>The song's title (its tag), else the file's name without the extension.</summary>
        public string Title { get { return !string.IsNullOrWhiteSpace(Info?.Title) ? Info.Title : System.IO.Path.GetFileNameWithoutExtension(Path); } }
        public string Artist { get { return Info?.Artist ?? ""; } }
        public string LengthText { get { return Info != null && Info.DurationMs > 0 ? Ui.Format.Time(Info.DurationMs) : ""; } }

        public void SetInfo(TrackInfo info)
        {
            Info = info;
            Changed("Info"); Changed("Title"); Changed("Artist"); Changed("LengthText");
        }

        int _number;
        /// <summary>Its place in the playlist, from 1.</summary>
        public int Number { get { return _number; } set { if (_number != value) { _number = value; Changed("Number"); } } }

        public ImageSource Cover;
        public bool CoverTried;

        void Changed(string n) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n)); }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    public static class Media
    {
        /// <summary>Pictures: what Windows' decoders read (HEIC, AVIF and camera RAW need their Microsoft Store extensions).</summary>
        static readonly HashSet<string> Pictures = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".jfif", ".jpe", ".png", ".bmp", ".dib", ".gif", ".tif", ".tiff", ".ico", ".webp", ".heic", ".heif", ".avif",
            ".jxr", ".wdp", ".hdp", ".dds", ".jxl",
            ".dng", ".cr2", ".cr3", ".nef", ".nrw", ".arw", ".orf", ".rw2", ".raf", ".pef", ".srw", ".3fr", ".erf", ".kdc", ".mrw", ".x3f"
        };

        static readonly HashSet<string> Videos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".m4v", ".mov", ".mkv", ".webm", ".avi", ".wmv", ".asf", ".flv", ".f4v", ".mpg", ".mpeg", ".mpe", ".m2v", ".ts", ".mts",
            ".m2ts", ".3gp", ".3g2", ".ogv", ".vob", ".divx", ".rm", ".rmvb", ".dv", ".mxf"
        };

        static readonly HashSet<string> Sounds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".oga", ".opus", ".wma", ".aiff", ".aif", ".ape", ".alac", ".mka", ".ac3", ".dts", ".mid", ".midi"
        };

        /// <summary>Every file type vView opens, with its kind (for Options > Default app and --register-types).</summary>
        public static IEnumerable<KeyValuePair<string, MediaKind>> AllTypes
        {
            get
            {
                foreach (string e in Pictures) yield return new KeyValuePair<string, MediaKind>(e, MediaKind.Picture);
                foreach (string e in Videos) yield return new KeyValuePair<string, MediaKind>(e, MediaKind.Video);
                foreach (string e in Sounds) yield return new KeyValuePair<string, MediaKind>(e, MediaKind.Audio);
            }
        }

        /// <summary>A network address VLC can play ("rtsp://camera/stream", "rtmp://...", "https://.../video.m3u8" ...), not a file.</summary>
        public static bool IsAddress(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            Uri u;
            return Uri.TryCreate(text.Trim(), UriKind.Absolute, out u) && !u.IsFile && u.Scheme.Length > 1 &&
                   u.Scheme != "javascript" && u.Scheme != "data";
        }

        /// <summary>A short name for a stream: the host and the end of its path ("192.168.2.40 · live/main").</summary>
        public static string StreamName(string address)
        {
            Uri u;
            if (!Uri.TryCreate(address, UriKind.Absolute, out u)) return address;
            string path = u.AbsolutePath.Trim('/');
            return u.Host + (path.Length > 0 ? " · " + (path.Length > 40 ? "…" + path.Substring(path.Length - 39) : path) : "");
        }

        public static MediaKind KindOf(string path)
        {
            string ext = System.IO.Path.GetExtension(path ?? "");
            if (Pictures.Contains(ext)) return MediaKind.Picture;
            if (Videos.Contains(ext)) return MediaKind.Video;
            if (Sounds.Contains(ext)) return MediaKind.Audio;
            return MediaKind.None;
        }

        /// <summary>For the Open dialog.</summary>
        public static string DialogFilter
        {
            get
            {
                Func<IEnumerable<string>, string> pat = s => string.Join(";", s.Select(e => "*" + e));
                return "Pictures, videos and sounds|" + pat(Pictures.Concat(Videos).Concat(Sounds)) + ";*.m3u;*.m3u8|Pictures|" + pat(Pictures) +
                       "|Videos|" + pat(Videos) + "|Sounds|" + pat(Sounds) + "|Playlists|*.m3u;*.m3u8|All files|*.*";
            }
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        static extern int StrCmpLogicalW(string a, string b);

        /// <summary>
        /// What vView goes through from path: a picture brings the other pictures in its folder, in Explorer's name order ("2.jpg"
        /// before "10.jpg"); a sound brings the folder's sounds (the playlist); a video is played on its own, as VLC does.
        /// </summary>
        public static List<MediaItem> Folder(string path)
        {
            var list = new List<MediaItem>();
            MediaKind kind = KindOf(path);
            if (kind == MediaKind.Video || kind == MediaKind.None) { list.Add(new MediaItem(path)); return list; }
            try
            {
                string dir = System.IO.Path.GetDirectoryName(path);
                var opts = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
                foreach (string f in Directory.EnumerateFiles(dir, "*", opts))
                    if (KindOf(f) == kind) list.Add(new MediaItem(f));
                list.Sort((a, b) => StrCmpLogicalW(a.Name, b.Name));
            }
            catch (Exception ex) { Log.Write("Folder of " + path + ": " + ex.Message); }
            // the file itself, even if it's hidden or its folder can't be listed
            if (!list.Any(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase))) list.Insert(0, new MediaItem(path));
            return list;
        }

        // ---- playlists (.m3u / .m3u8) ----

        public static bool IsPlaylist(string path)
        {
            string ext = System.IO.Path.GetExtension(path ?? "");
            return ext.Equals(".m3u", StringComparison.OrdinalIgnoreCase) || ext.Equals(".m3u8", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The sounds a playlist lists that are there (relative paths are from the playlist's folder); its #comments skipped.</summary>
        public static List<string> ReadPlaylist(string path)
        {
            var list = new List<string>();
            string dir = System.IO.Path.GetDirectoryName(path) ?? "";
            // UTF-8 (every .m3u8, most .m3u); an old .m3u that isn't valid UTF-8 is read as Latin-1
            byte[] bytes = File.ReadAllBytes(path);
            string text;
            try { text = new System.Text.UTF8Encoding(false, true).GetString(bytes); }
            catch (System.Text.DecoderFallbackException) { text = System.Text.Encoding.Latin1.GetString(bytes); }
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim().TrimStart('﻿');
                if (line.Length == 0 || line.StartsWith("#")) continue;
                string f = line;
                Uri u;
                if (Uri.TryCreate(line, UriKind.Absolute, out u)) { if (!u.IsFile) continue; f = u.LocalPath; }
                else if (!System.IO.Path.IsPathRooted(f)) f = System.IO.Path.Combine(dir, f);
                try { f = System.IO.Path.GetFullPath(f); } catch { continue; }
                if (KindOf(f) == MediaKind.Audio && File.Exists(f)) list.Add(f);
            }
            return list;
        }

        /// <summary>Save a playlist (.m3u8, UTF-8): each song with its length and "Artist - Title", by its full path.</summary>
        public static void WritePlaylist(string path, IEnumerable<MediaItem> items)
        {
            var sb = new System.Text.StringBuilder("#EXTM3U\r\n");
            foreach (MediaItem it in items)
            {
                if (it.IsStream) continue;
                long secs = it.Info != null && it.Info.DurationMs > 0 ? it.Info.DurationMs / 1000 : -1;
                sb.Append("#EXTINF:" + secs + "," + (it.Artist.Length > 0 ? it.Artist + " - " : "") + it.Title + "\r\n");
                sb.Append(it.Path + "\r\n");
            }
            File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(false));
        }

        /// <summary>For Add songs.</summary>
        public static string SoundFilter { get { return "Sounds|" + string.Join(";", Sounds.Select(e => "*" + e)) + "|All files|*.*"; } }

        /// <summary>
        /// The files vView can show among what was dropped: the files themselves, and the files in each folder (not its sub-folders), in
        /// Explorer's name order - pictures first, then videos and sounds.
        /// </summary>
        public static List<string> Dropped(IEnumerable<string> paths)
        {
            var files = new List<string>();
            var opts = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
            foreach (string p in paths)
            {
                try
                {
                    if (Directory.Exists(p)) files.AddRange(Directory.EnumerateFiles(p, "*", opts).Where(f => KindOf(f) != MediaKind.None));
                    else if (File.Exists(p) && KindOf(p) != MediaKind.None) files.Add(p);
                }
                catch (Exception ex) { Log.Write("Dropped " + p + ": " + ex.Message); }
            }
            files = files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            files.Sort((a, b) =>
            {
                int ka = KindOf(a) == MediaKind.Picture ? 0 : 1, kb = KindOf(b) == MediaKind.Picture ? 0 : 1;
                return ka != kb ? ka - kb : StrCmpLogicalW(System.IO.Path.GetFileName(a), System.IO.Path.GetFileName(b));
            });
            return files;
        }

        /// <summary>
        /// The next (or previous) file of the same kind - video or sound - in path's folder, in Explorer's name order; null at either
        /// end. For the bar's next / back buttons (a video still plays on its own).
        /// </summary>
        public static string Neighbour(string path, bool next)
        {
            try
            {
                MediaKind kind = KindOf(path);
                string dir = System.IO.Path.GetDirectoryName(path), name = System.IO.Path.GetFileName(path);
                var opts = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
                List<string> files = Directory.EnumerateFiles(dir, "*", opts).Where(f => KindOf(f) == kind).ToList();
                files.Sort((a, b) => StrCmpLogicalW(System.IO.Path.GetFileName(a), System.IO.Path.GetFileName(b)));
                int i = files.FindIndex(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
                if (i < 0)   // the file itself is hidden: where it would go
                {
                    i = files.FindIndex(f => StrCmpLogicalW(System.IO.Path.GetFileName(f), name) > 0);
                    if (i < 0) i = files.Count;
                    return next ? (i < files.Count ? files[i] : null) : (i > 0 ? files[i - 1] : null);
                }
                int j = next ? i + 1 : i - 1;
                return j >= 0 && j < files.Count ? files[j] : null;
            }
            catch (Exception ex) { Log.Write("Folder of " + path + ": " + ex.Message); return null; }
        }
    }
}
