using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using vView.Core;

namespace vView.Ui
{
    /// <summary>
    /// vView's Options: the suite's Options window (vSuite's OptionsWindowBase - the page list, cards, rows, switches and the Updates page)
    /// with vView's pages. Every change is saved and applied to every vView window at once - there's no OK button. One at a time.
    /// </summary>
    public sealed class OptionsWindow : OptionsWindowBase
    {
        /// <summary>The open Options window, if there is one.</summary>
        public static OptionsWindow Current { get; private set; }

        const string PgGeneral = "General", PgLook = "Look", PgPictures = "Pictures", PgVideo = "Video", PgAudio = "Audio", PgSlideshow = "Slideshow", PgAbout = "About";
        public const string PgJellyfin = "Jellyfin";

        readonly AppSettings _s;
        readonly VShellThemeSource _themeSource;
        readonly Action _changed;

        /// <summary>Show Options (or bring the open one forward); about = open it on About.</summary>
        public static void Open(Window owner, AppSettings s, VShellThemeSource themeSource, Theme theme, bool blur, Action changed, bool about)
        {
            if (Current == null)
            {
                Current = new OptionsWindow(s, themeSource, theme, blur, changed) { Owner = owner };
                Current.Show();
            }
            else Current.Activate();
            if (about) Current.ShowPage(PgAbout);
        }

        OptionsWindow(AppSettings s, VShellThemeSource themeSource, Theme theme, bool blur, Action changed) : base(theme, blur)
        {
            _s = s;
            _themeSource = themeSource;
            _changed = changed;
            Closed += (o, e) => { if (Current == this) Current = null; };
            Finish();
        }

        // =================================================================
        // The pages
        // =================================================================

        protected override void BuildPages()
        {
            // ---- General ----
            StackPanel general = Page(PgGeneral, "", "The window and moving through a folder");
            StackPanel c = Card(general, "The window");
            Switch(c, "Show the folder along the bottom", "For pictures only - the folder's other pictures; click one to show it", null, _s.ShowStrip, v => _s.ShowStrip = v);
            Switch(c, "Show details", "Size, date, camera … beside the picture - also I", null, _s.ShowDetails, v => _s.ShowDetails = v);
            Row(c, "Opening a file while vView is open", "From Explorer, vXplorer, a shortcut …", null, Segmented(
                Opt("Use this window", !_s.OpenInNewWindow, () => _s.OpenInNewWindow = false),
                Opt("Open a new window", _s.OpenInNewWindow, () => _s.OpenInNewWindow = true)));
            c = Card(general, "The mouse wheel");
            Row(c, "Turning the wheel over a picture", "Ctrl + wheel always zooms", null, Segmented(
                Opt("Zooms", _s.Wheel == "Zoom", () => _s.Wheel = "Zoom"),
                Opt("Goes to the next one", _s.Wheel == "Browse", () => _s.Wheel = "Browse")));
            c = Card(general, "Default app");
            _defaultStatus = Dim("", 12, new Thickness(0, 2, 0, 0));
            var defLeft = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            defLeft.Children.Add(Label("Open pictures, videos and sounds with vView",
                null, "Windows doesn't let programs make themselves the default, so vView signs itself up for every type it opens and " +
                      "opens Settings > Default apps on vView's page - click Set default there (or pick vView for the types you want)."));
            defLeft.Children.Add(_defaultStatus);
            var defRow = new Grid();
            defRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 120 });
            defRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            defRow.Children.Add(defLeft);
            Button makeDefault = TextButton("Make vView the default…", MakeDefault);
            makeDefault.VerticalAlignment = VerticalAlignment.Center;
            makeDefault.Margin = new Thickness(16, 0, 0, 0);
            Grid.SetColumn(makeDefault, 1);
            defRow.Children.Add(makeDefault);
            Place(c, defRow);
            DrawDefault();
            Activated += (o, e) => DrawDefault();   // back from Settings: show the new count

            c = Card(general, "Deleting");
            Switch(c, "Ask before moving to the Recycle Bin", "When you press Delete", null, _s.ConfirmDelete, v => _s.ConfirmDelete = v);

            // ---- Look ----
            StackPanel look = Page(PgLook, "\uE790", "How vView's window looks");
            c = Card(look, "The window");
            Switch(c, "Use the theme's opacity", "See-through like vShell's bars - off: solid",
                   "On: the window shows your wallpaper through its colour at the opacity set in vShell (blurred with Blur or Frosted), like the bars and widgets. Off: solid colours.",
                   _s.ThemeOpacity, v => _s.ThemeOpacity = v);
            Note(look, "Colours, opacity, blur and corners come from your vShell theme - change them in vShell's settings.");

            // ---- Pictures ----
            StackPanel pics = Page(PgPictures, "", "How pictures are shown");
            c = Card(pics, "Size");
            Row(c, "Pictures smaller than the window", "Bigger ones always fit the window to start with", null, Segmented(
                Opt("Actual size", _s.SmallPictures != "Fill", () => _s.SmallPictures = "Actual"),
                Opt("Fill the window", _s.SmallPictures == "Fill", () => _s.SmallPictures = "Fill")));
            Note(pics, "vView uses Windows' own picture support. HEIC (iPhone), AVIF and camera RAW files need their free extensions from the " +
                       "Microsoft Store: HEIF Image Extensions, AV1 Video Extension, Raw Image Extension.");

            // ---- Video ----
            StackPanel video = Page(PgVideo, "", "Videos - songs have the Audio page");
            c = Card(video, "Playing");
            Switch(c, "Start playing straight away", "Off: a video opens paused on its first frame", null, _s.AutoPlay, v => _s.AutoPlay = v);
            Switch(c, "Play again from the start", "When it ends - also the button in the bar under a video", null, _s.Loop, v => _s.Loop = v);
            c = Card(video, "Network streams");
            Row(c, "Before a stream plays, hold", "More = smoother on a slow network, but further behind live", null, Segmented(
                Opt("0.3 s", _s.NetworkCaching == 300, () => _s.NetworkCaching = 300),
                Opt("1 s", _s.NetworkCaching == 1000, () => _s.NetworkCaching = 1000),
                Opt("3 s", _s.NetworkCaching == 3000, () => _s.NetworkCaching = 3000)));
            c = Card(video, "Performance");
            Switch(c, "Use the graphics card to decode", "Smoother 4K and HEVC, less work for the processor",
                   "Takes effect when vView next starts. Turn it off if a video shows green or broken pictures.", _s.HardwareDecoding, v => _s.HardwareDecoding = v);

            // ---- Audio ----
            StackPanel audio = Page(PgAudio, "", "Songs, the playlist, the classic player and the equaliser");
            c = Card(audio, "Playing songs");
            Row(c, "Songs play in", "Opening one - from Explorer, a double-click, Open, a drop",
                "The full window: now playing with the cover and the playlist beside it. The classic player: a small window of its own " +
                "with the equaliser and the playlist, that can fold into one line on top of other windows. A picture or a video always " +
                "opens in the full window. The buttons in each switch between them too.", Segmented(
                Opt("The full window", _s.MusicView != "Classic", () => _s.MusicView = "Full"),
                Opt("The classic player", _s.MusicView == "Classic", () => _s.MusicView = "Classic")));
            Switch(c, "Show the playlist beside the song", "In the full window - also the list button in the bar", null, _s.ShowPlaylist, v => _s.ShowPlaylist = v);
            c = Card(audio, "The classic player");
            Switch(c, "Keep it on top of other windows", "Also the pin at its top; folded into one line it's always on top", null, _s.ClassicOnTop, v => _s.ClassicOnTop = v);
            Switch(c, "Show the equaliser", "Also its EQ button", null, _s.ClassicShowEq, v => _s.ClassicShowEq = v);
            Switch(c, "Show the playlist", "Also its List button - drag the list's bottom edge to make it longer", null, _s.ClassicShowList, v => _s.ClassicShowList = v);
            Switch(c, "Show the time left", "Instead of the time gone - also a click on the time", null, _s.ClassicTimeLeft, v => _s.ClassicTimeLeft = v);
            c = Card(audio, "Equaliser");
            Switch(c, "Use the equaliser", "Its sliders and presets are in the classic player (EQ)", null, _s.EqOn, v => _s.EqOn = v);
            Note(audio, "Songs show their cover (the picture inside the file, or a cover.jpg / folder.jpg beside it), title, artist and album. " +
                        "The visualiser only listens to vView - other apps' sound doesn't show in it. Playlists (.m3u, .m3u8) open as songs.");

            // ---- Jellyfin ----
            StackPanel jf = Page(PgJellyfin, "", "Your Jellyfin server, in vView's window");
            _jfList = Card(jf, "Servers");
            DrawJellyfinServers();
            c = Card(jf, "Add a server");
            string newName = "", newAddress = "";
            var addStatus = Dim("", 12, new Thickness(0, 2, 0, 0));
            Action drawAdd = () =>
            {
                string server = Jellyfin.ServerBase(newAddress);
                addStatus.Text = newAddress.Length == 0 ? "Type the server's address, then Add."
                               : server == null ? "That doesn't look like a server's address - like http://192.168.1.20:8096."
                               : _s.JellyfinValid().Any(e => e.Base == server) ? "That server is already in the list."
                               : "Jellyfin's page: " + Jellyfin.WebAddress(server);
            };
            TextBox nameBox = TextField("", v => newName = (v ?? "").Trim(), 300);
            TextBox addressBox = TextField("", v => { newAddress = (v ?? "").Trim(); drawAdd(); }, 300);
            addressBox.TextChanged += (o, e) => { newAddress = addressBox.Text.Trim(); drawAdd(); };
            Row(c, "Name", "Your own name for it, like Home - empty: its address", null, nameBox);
            Row(c, "Address", "The one you open Jellyfin with in a browser", null, addressBox);
            Button add = TextButton("Add", () =>
            {
                newName = nameBox.Text.Trim();
                newAddress = addressBox.Text.Trim();
                string server = Jellyfin.ServerBase(newAddress);
                if (server == null || _s.JellyfinValid().Any(e => e.Base == server)) { drawAdd(); return; }
                _s.JellyfinServers.Add(new JellyfinEntry { Name = newName, Address = newAddress });
                if (_s.JellyfinServers.Count == 1) _s.JellyfinLast = newAddress;
                nameBox.Text = addressBox.Text = "";
                Saved();
                DrawJellyfinServers();
            });
            var addRow = new DockPanel();
            DockPanel.SetDock(add, Dock.Right);
            add.Margin = new Thickness(16, 0, 0, 0);
            addRow.Children.Add(add);
            addRow.Children.Add(addStatus);
            Place(c, addRow);
            drawAdd();
            c = Card(jf, "Watching episodes");
            Switch(c, "Up next near the end", "The next episode in Jellyfin's queue, with a countdown, Play now and Cancel - as Jellyfin's own player",
                   null, _s.JellyfinUpNext, v => _s.JellyfinUpNext = v);
            Row(c, "Count down for", "From the start of the credits when the server knows them (Intro Skipper), else that long before the end",
                null, Segmented(
                Opt("10 s", _s.JellyfinUpNextSeconds == 10, () => _s.JellyfinUpNextSeconds = 10),
                Opt("20 s", _s.JellyfinUpNextSeconds == 20, () => _s.JellyfinUpNextSeconds = 20),
                Opt("30 s", _s.JellyfinUpNextSeconds == 30, () => _s.JellyfinUpNextSeconds = 30)));
            Switch(c, "Play the next episode when it gets to 0", "Skipping the rest of the credits - off: only Play now does, and the episode stops at its end",
                   null, _s.JellyfinAutoNext, v => _s.JellyfinAutoNext = v);
            Switch(c, "Skip intro button", "During an episode's intro, when the server knows where it is (Intro Skipper)",
                   null, _s.JellyfinSkipIntro, v => _s.JellyfinSkipIntro = v);
            Note(jf,"Browse and sign in on Jellyfin's own page - each server keeps its own sign-in. Videos you play there play in vView with VLC - " +
                     "nearly any format, without the server converting them - and where you got to and what you've watched go back to Jellyfin. " +
                     "Music plays in the page's own player. With more than one server, Jellyfin in vView's menu lists them; Ctrl+J opens the last one.");

            // ---- Slideshow ----
            StackPanel slides = Page(PgSlideshow, "", "F5 starts one, Esc stops it");
            c = Card(slides, "Slideshow");
            Row(c, "Each picture shows for", null, null, Segmented(
                Opt("3 s", _s.SlideSeconds == 3, () => _s.SlideSeconds = 3),
                Opt("5 s", _s.SlideSeconds == 5, () => _s.SlideSeconds = 5),
                Opt("10 s", _s.SlideSeconds == 10, () => _s.SlideSeconds = 10),
                Opt("20 s", _s.SlideSeconds == 20, () => _s.SlideSeconds = 20)));
            Switch(c, "Include videos", "Each one plays to the end, then the slideshow carries on", null, _s.SlideVideos, v => _s.SlideVideos = v);
            Switch(c, "Start again after the last one", null, null, _s.SlideRepeat, v => _s.SlideRepeat = v);

            // ---- Updates ----
            UpdatesPage(_s);

            // ---- About ----
            StackPanel about = Page(PgAbout, "", null);
            c = Card(about, "vView");
            Version ver = Assembly.GetExecutingAssembly().GetName().Version;
            var info = new StackPanel();
            info.Children.Add(new TextBlock { Text = "vView " + ver.Major + "." + ver.Minor + "." + ver.Build, FontSize = 13.5, FontWeight = FontWeights.SemiBold });
            info.Children.Add(Dim("Pictures and videos in your vShell theme.", 12, new Thickness(0, 2, 0, 0)));
            Place(c, info);
            Row(c, "Colours", _themeSource.Found ? "Your vShell theme - vView follows it as you change it" : "vShell's default theme (vShell's settings weren't found)", null, null);
            var folder = TextButton("Open", () =>
            {
                try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Log.Folder + "\"") { UseShellExecute = false }); } catch { }
            });
            folder.Margin = new Thickness(0);
            Row(c, "Settings folder", Log.Folder, null, folder);
            c = Card(about, "Made with");
            Row(c, "VLC's engine (libVLC)", "Plays the videos and sounds · VideoLAN · LGPL 2.1 - its files are in vView's libvlc folder", null, null);
            Row(c, "Windows Imaging Component", "Reads the pictures", null, null);
            Row(c, "Microsoft Edge WebView2", "Shows Jellyfin's page", null, null);
        }


        StackPanel _jfList;

        /// <summary>Options > Jellyfin's list: each server with Open, Sign out and Remove.</summary>
        void DrawJellyfinServers()
        {
            _jfList.Children.Clear();
            if (_s.JellyfinServers.Count == 0)
            {
                Row(_jfList, "No servers yet", "Add one below", null, null);
                return;
            }
            foreach (JellyfinEntry s in _s.JellyfinServers.ToList())
            {
                JellyfinEntry entry = s;
                string server = entry.Base;
                Button open = TextButton("Open", () =>
                {
                    (Owner as MainWindow)?.ShowJellyfin(entry);
                    Close();
                });
                Button signOut = TextButton("Sign out", () =>
                {
                    if (Ask.YesNo(this, "Sign out of " + entry.Label + "?",
                                  "vView forgets your sign-in on this server and what its page saved. Your other servers, and what you've watched, aren't touched.",
                                  "Sign out", "Cancel", true)) (Owner as MainWindow)?.ForgetJellyfin(server);
                });
                Button remove = TextButton("Remove", () =>
                {
                    if (!Ask.YesNo(this, "Remove " + entry.Label + "?", "vView forgets this server and your sign-in on it. The server itself isn't touched.",
                                   "Remove", "Cancel", true)) return;
                    _s.JellyfinServers.Remove(entry);
                    (Owner as MainWindow)?.ForgetJellyfin(server);
                    Saved();
                    DrawJellyfinServers();
                });
                open.IsEnabled = signOut.IsEnabled = server != null;
                foreach (Button b in new[] { open, signOut, remove }) b.Margin = new Thickness(6, 0, 0, 0);
                Row(_jfList, entry.Label, server != null ? Jellyfin.WebAddress(server) : "That isn't a server's address: " + entry.Address, null,
                    Buttons(open, signOut, remove));
            }
        }

        TextBlock _defaultStatus;

        void DrawDefault()
        {
            if (_defaultStatus == null) return;
            int total, n = DefaultApp.CountDefault(out total);
            _defaultStatus.Text = n == 0 ? "vView isn't the default for any of its " + total + " file types yet."
                                : n == total ? "vView opens all " + total + " of its file types."
                                : "vView opens " + n + " of its " + total + " file types.";
        }

        void MakeDefault()
        {
            try { if (!DefaultApp.IsRegistered) DefaultApp.Register(); }
            catch (Exception ex)
            {
                Ask.YesNo(this, "Default app", "vView couldn't sign itself up for its file types: " + ex.Message, "OK", null, false);
                return;
            }
            DefaultApp.OpenSettings();
        }


        protected override void Saved()
        {
            _s.Save();
            _changed?.Invoke();
        }
    }
}
