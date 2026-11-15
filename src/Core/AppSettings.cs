using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace vView.Core
{
    /// <summary>A Jellyfin server in Options > Jellyfin: a name of your own (may be empty) and its address as typed.</summary>
    public sealed class JellyfinEntry
    {
        public string Name { get; set; } = "";
        public string Address { get; set; } = "";
        /// <summary>The server's base address (Jellyfin.ServerBase), null if it isn't one.</summary>
        [JsonIgnore] public string Base { get { return Jellyfin.ServerBase(Address); } }
        /// <summary>What vView calls it: the name, else the server's host.</summary>
        [JsonIgnore] public string Label
        {
            get { string b = Base; return Name.Length > 0 ? Name : b != null ? new Uri(b).Authority : Address; }
        }
    }

    /// <summary>vView's own settings (%APPDATA%\vView\settings.json). The colours are vShell's - see VShellThemeSource.</summary>
    public sealed class AppSettings : vSuite.Ui.IUpdateSettings
    {
        /// <summary>Window position and size in DIPs; Width 0 = never saved (centre on the main monitor).</summary>
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Maximized { get; set; }
        /// <summary>The folder of the last file opened (where Open… starts).</summary>
        public string LastFolder { get; set; } = "";

        // ---- Options > Pictures ----
        /// <summary>Pictures smaller than the window: "Actual" size, or "Fill" (enlarged to fit).</summary>
        public string SmallPictures { get; set; } = "Actual";
        /// <summary>The mouse wheel: "Zoom" or "Browse" (previous / next).</summary>
        public string Wheel { get; set; } = "Zoom";
        public bool ShowStrip { get; set; } = true;
        /// <summary>A file opened while vView is open (from Explorer, vXplorer …): shown in a new window instead of this one.</summary>
        public bool OpenInNewWindow { get; set; }
        public bool ShowDetails { get; set; }
        public bool ConfirmDelete { get; set; } = true;

        // ---- Options > Video ----
        public bool AutoPlay { get; set; } = true;
        public bool Loop { get; set; }
        /// <summary>Decode on the graphics card (VLC's --avcodec-hw=any).</summary>
        public bool HardwareDecoding { get; set; } = true;
        public int Volume { get; set; } = 80;
        public bool Muted { get; set; }
        /// <summary>Network streams opened lately, newest first (Open network stream lists them).</summary>
        public List<string> RecentStreams { get; set; } = new List<string>();
        /// <summary>How much of a network stream VLC holds before playing, in ms (more = smoother on a poor network, later).</summary>
        public int NetworkCaching { get; set; } = 1000;

        // ---- music (the bar's buttons under a song) ----
        /// <summary>"Off", "All" (the playlist over again) or "One" (this song over again).</summary>
        public string MusicRepeat { get; set; } = "Off";
        public bool MusicShuffle { get; set; }
        /// <summary>The playlist beside the song (the bar's list button).</summary>
        public bool ShowPlaylist { get; set; } = true;
        /// <summary>Where songs play: "Full" (the main window) or "Classic" (the small classic player).</summary>
        public string MusicView { get; set; } = "Full";
        /// <summary>The classic player: its place (null = not placed yet), one line, kept on top, its equaliser and list showing, time left.</summary>
        public double? ClassicLeft { get; set; }
        public double? ClassicTop { get; set; }
        public bool ClassicShade { get; set; }
        public bool ClassicOnTop { get; set; }
        public bool ClassicShowEq { get; set; }
        public bool ClassicShowList { get; set; } = true;
        public bool ClassicTimeLeft { get; set; }
        /// <summary>The classic player's playlist, px tall (drag its bottom edge).</summary>
        public double ClassicListHeight { get; set; } = 230;

        // ---- the equaliser (VLC's: a preamp and ten bands, -20 to +20 dB) ----
        public bool EqOn { get; set; }
        public string EqPreset { get; set; } = "Flat";
        public double EqPreamp { get; set; }
        public List<double> EqBands { get; set; } = new List<double> { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

        // ---- Options > Jellyfin ----
        /// <summary>The Jellyfin servers, in the order they were added.</summary>
        public List<JellyfinEntry> JellyfinServers { get; set; } = new List<JellyfinEntry>();
        /// <summary>The address of the server opened last (Ctrl+J opens it).</summary>
        public string JellyfinLast { get; set; } = "";
        /// <summary>Near an episode's end: "Up next" with the next one in Jellyfin's queue, counting down (Options > Jellyfin).</summary>
        public bool JellyfinUpNext { get; set; } = true;
        /// <summary>How long Up next counts down (10 / 20 / 30 s) - from the start of the credits when the server knows them, else this long before the end.</summary>
        public int JellyfinUpNextSeconds { get; set; } = 20;
        /// <summary>When the countdown ends, play the next episode (skipping the rest of the credits); off: only Play now does.</summary>
        public bool JellyfinAutoNext { get; set; } = true;
        /// <summary>A Skip intro button during the intro (the server's intro times - Intro Skipper).</summary>
        public bool JellyfinSkipIntro { get; set; } = true;
        /// <summary>0.1.9-0.1.10's one server - moved into JellyfinServers when settings load.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string JellyfinServer { get; set; }

        /// <summary>The servers whose address works.</summary>
        public IEnumerable<JellyfinEntry> JellyfinValid() { return JellyfinServers.Where(e => e.Base != null); }

        /// <summary>The server Ctrl+J opens: the one opened last, else the first; null when there's none.</summary>
        public JellyfinEntry JellyfinPick()
        {
            return JellyfinValid().FirstOrDefault(e => e.Base == Jellyfin.ServerBase(JellyfinLast)) ?? JellyfinValid().FirstOrDefault();
        }

        // ---- Options > Slideshow ----
        public int SlideSeconds { get; set; } = 5;
        public bool SlideVideos { get; set; }
        public bool SlideRepeat { get; set; } = true;

        // ---- updates (Options > Updates; see Updater / UpdateCenter) ----
        /// <summary>Look for a new version on the update site a minute after starting, then about once a day.</summary>
        /// <summary>Options > Look: the windows see-through at vShell's opacity, like its bars (the wallpaper behind); off = solid.</summary>
        public bool ThemeOpacity { get; set; } = true;
        public bool UpdateAuto { get; set; } = true;
        /// <summary>Offer pre-releases too.</summary>
        public bool UpdatePre { get; set; }
        public DateTime UpdateLastCheck { get; set; } = DateTime.MinValue;
        /// <summary>A version you chose to skip (or that didn't start and was rolled back): not offered again; a newer one is.</summary>
        public string UpdateSkip { get; set; } = "";

        static string FilePath { get { return Path.Combine(Log.Folder, "settings.json"); } }

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                    if (s != null) { s.Normalize(); return s; }
                }
            }
            catch (Exception ex) { Log.Write("Settings: " + ex.Message); }
            // the first start (no settings yet): the defaults filled in too
            var fresh = new AppSettings();
            fresh.Normalize();
            return fresh;
        }

        void Normalize()
        {
            if (LastFolder == null) LastFolder = "";
            if (SmallPictures != "Fill") SmallPictures = "Actual";
            if (Wheel != "Browse") Wheel = "Zoom";
            if (Volume < 0 || Volume > 100) Volume = 80;
            if (SlideSeconds != 3 && SlideSeconds != 10 && SlideSeconds != 20) SlideSeconds = 5;
            if (RecentStreams == null) RecentStreams = new List<string>();
            if (UpdateSkip == null) UpdateSkip = "";
            if (JellyfinServers == null) JellyfinServers = new List<JellyfinEntry>();
            JellyfinServers.RemoveAll(e => e == null);
            foreach (JellyfinEntry e in JellyfinServers) { e.Name = (e.Name ?? "").Trim(); e.Address = (e.Address ?? "").Trim(); }
            if (!string.IsNullOrWhiteSpace(JellyfinServer) && JellyfinServers.Count == 0)
            {
                JellyfinServers.Add(new JellyfinEntry { Address = JellyfinServer.Trim() });
                JellyfinLast = JellyfinServer.Trim();
            }
            JellyfinServer = null;
            if (JellyfinLast == null) JellyfinLast = "";
            RecentStreams.RemoveAll(string.IsNullOrWhiteSpace);
            if (NetworkCaching != 300 && NetworkCaching != 3000) NetworkCaching = 1000;
            if (JellyfinUpNextSeconds != 10 && JellyfinUpNextSeconds != 30) JellyfinUpNextSeconds = 20;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Log.Folder);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(tmp, FilePath, true);
            }
            catch (Exception ex) { Log.Write("Settings save: " + ex.Message); }
        }
    }
}
