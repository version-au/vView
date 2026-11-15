using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using LibVLCSharp.Shared;

namespace vView.Core
{
    /// <summary>A song's tags and format, for the music view and the playlist.</summary>
    public sealed class TrackInfo
    {
        public string Title, Artist, Album, Year;
        public long DurationMs;
        /// <summary>"FLAC", "44.1 kHz", "Stereo", "320 kbps" - what's known.</summary>
        public List<string> Format = new List<string>();
    }

    /// <summary>
    /// Reading a song: its tags through VLC (every format VLC plays - MP3, FLAC, Ogg, Opus, M4A …), without playing it; its cover from
    /// Windows' thumbnail (the picture in the file) or a cover.jpg / folder.jpg beside it.
    /// </summary>
    static class MusicInfo
    {
        /// <summary>The tags (on a worker thread - VLC is never called from the window's thread).</summary>
        public static TrackInfo Read(LibVLC vlc, string path)
        {
            var ti = new TrackInfo();
            try
            {
                using (var m = new LibVLCSharp.Shared.Media(vlc, new Uri(path)))
                {
                    MediaParsedStatus status = m.Parse(MediaParseOptions.ParseLocal, 5000).GetAwaiter().GetResult();
                    string file = Path.GetFileName(path), bare = Path.GetFileNameWithoutExtension(path);
                    string title = Clean(m.Meta(MetadataType.Title));
                    // with no title tag VLC gives the file's name
                    if (title != null && (title == file || title == bare)) title = null;
                    ti.Title = title;
                    ti.Artist = Clean(m.Meta(MetadataType.Artist)) ?? Clean(m.Meta(MetadataType.AlbumArtist));
                    ti.Album = Clean(m.Meta(MetadataType.Album));
                    string date = Clean(m.Meta(MetadataType.Date));
                    if (date != null && date.Length >= 4 && int.TryParse(date.Substring(0, 4), out int y) && y > 1000) ti.Year = y.ToString();
                    ti.DurationMs = Math.Max(0, m.Duration);
                    ti.Format.Add(Path.GetExtension(path).TrimStart('.').ToUpperInvariant());
                    foreach (MediaTrack t in m.Tracks)
                    {
                        if (t.TrackType != TrackType.Audio) continue;
                        uint rate = t.Data.Audio.Rate, ch = t.Data.Audio.Channels;
                        if (rate > 0) ti.Format.Add((rate / 1000.0).ToString(rate % 1000 == 0 ? "0" : "0.#") + " kHz");
                        if (ch == 1) ti.Format.Add("Mono"); else if (ch == 2) ti.Format.Add("Stereo"); else if (ch > 2) ti.Format.Add(ch + " channels");
                        if (t.Bitrate > 0) ti.Format.Add(Math.Round(t.Bitrate / 1000.0) + " kbps");
                        break;
                    }
                    if (status != MediaParsedStatus.Done) Log.Write("Song " + path + ": VLC's reading " + status);
                }
            }
            catch (Exception ex) { Log.Write("Song " + path + ": " + ex.GetType().Name + ": " + ex.Message); }
            return ti;
        }

        static string Clean(string s)
        {
            s = s?.Trim();
            return string.IsNullOrEmpty(s) ? null : s;
        }

        // ---- the cover ----

        static readonly string[] CoverNames = { "cover", "folder", "front", "albumart", "album" };

        /// <summary>The cover, px across (on a thread of its own - Windows' thumbnails want STA); null when there's none.</summary>
        public static Task<BitmapSource> CoverAsync(string path, int px)
        {
            var done = new TaskCompletionSource<BitmapSource>();
            var t = new Thread(() =>
            {
                BitmapSource b = null;
                try { b = ShellThumbnail(path, px) ?? FolderPicture(path, px); }
                catch (Exception ex) { Log.Write("Cover " + path + ": " + ex.Message); }
                done.SetResult(b);
            }) { IsBackground = true, Name = "Cover" };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            return done.Task;
        }

        /// <summary>cover.jpg, folder.jpg, front.jpg … beside the song.</summary>
        static BitmapSource FolderPicture(string path, int px)
        {
            string dir = Path.GetDirectoryName(path);
            if (dir == null || !Directory.Exists(dir)) return null;
            foreach (string name in CoverNames)
                foreach (string ext in new[] { ".jpg", ".jpeg", ".png", ".webp" })
                {
                    string f = Path.Combine(dir, name + ext);
                    if (!File.Exists(f)) continue;
                    try
                    {
                        var bi = new BitmapImage();
                        using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        {
                            bi.BeginInit();
                            bi.StreamSource = fs;
                            bi.CacheOption = BitmapCacheOption.OnLoad;
                            bi.DecodePixelWidth = px;
                            bi.EndInit();
                        }
                        bi.Freeze();
                        return bi;
                    }
                    catch (Exception ex) { Log.Write("Cover " + f + ": " + ex.Message); }
                }
            return null;
        }

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory
        {
            [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SIZE { public int cx, cy; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
                                                       [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);

        const int SIIGBF_BIGGERSIZEOK = 0x1, SIIGBF_THUMBNAILONLY = 0x8;

        /// <summary>Windows' thumbnail of the file - for a song, the picture inside it; never its icon.</summary>
        static BitmapSource ShellThumbnail(string path, int px)
        {
            IShellItemImageFactory f = null;
            try
            {
                SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out f);
                if (f.GetImage(new SIZE { cx = px, cy = px }, SIIGBF_THUMBNAILONLY | SIIGBF_BIGGERSIZEOK, out IntPtr hbm) != 0 || hbm == IntPtr.Zero) return null;
                try
                {
                    BitmapSource b = Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    b.Freeze();
                    return b;
                }
                finally { DeleteObject(hbm); }
            }
            catch { return null; }
            finally { if (f != null) Marshal.ReleaseComObject(f); }
        }
    }
}
