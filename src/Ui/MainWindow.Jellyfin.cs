using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using vView.Core;

namespace vView.Ui
{
    /// <summary>
    /// Jellyfin in vView's window: the server's own web page (WebView2, the Edge engine Windows 11 has) for browsing and signing in, with
    /// vView's player put into it (jellyfin-bridge.js, through the page's NativeShell hook - as Jellyfin's own desktop app does it). Press
    /// Play on a video in the page and it plays here with VLC (the page hides); the page is told where it's up to, so resume points and
    /// watched marks reach the server. Back to Jellyfin (Esc, the arrow at the top left) stops it and shows the page again where it was.
    /// - The page's sign-in, settings and cache live in %APPDATA%\vView\WebView2 (one for every window - the same Jellyfin sign-in).
    /// - Messages are only taken from the server's own pages; links elsewhere open in the default browser.
    /// </summary>
    public partial class MainWindow
    {
        static Task<CoreWebView2Environment> _webEnv;
        WebView2 _web;
        /// <summary>The server the page and the bridge are loaded for.</summary>
        string _webServer;
        string _bridgeId;
        bool _webStarting;
        /// <summary>Jellyfin's page is showing.</summary>
        bool _jfShown;
        bool _jfClosing;
        int _jfTicks;
        /// <summary>After a Jellyfin video ends or the page stops it: back to the page, unless it plays the next one first.</summary>
        DispatcherTimer _jfBack;

        static string WebFolder { get { return Path.Combine(Log.Folder, "WebView2"); } }

        /// <summary>
        /// Show Jellyfin's page: the server this window has open (Ctrl+J, Back to Jellyfin), else the one opened last - or Options >
        /// Jellyfin while there's no server yet.
        /// </summary>
        public void ShowJellyfin()
        {
            JellyfinEntry open = _webServer == null ? null : _settings.JellyfinValid().FirstOrDefault(e => e.Base == _webServer);
            ShowJellyfin(open ?? _settings.JellyfinPick());
        }

        /// <summary>Show this server's page (the menu's list of servers, Options > Jellyfin > Open).</summary>
        public async void ShowJellyfin(JellyfinEntry entry)
        {
            string server = entry?.Base;
            if (server == null)
            {
                ShowOptions(false);
                OptionsWindow.Current?.ShowPageNamed(OptionsWindow.PgJellyfin);
                return;
            }
            if (_settings.JellyfinLast != entry.Address)
            {
                _settings.JellyfinLast = entry.Address;
                _settings.Save();
            }
            // from the classic player (its menu, Ctrl+J): the page is in this window - it comes back (songs still open in the classic player)
            if (ClassicShowing) LeaveClassic(false);
            JellyfinBackTimer().Stop();
            StopSlideshow();
            LeaveJellyfinVideo();
            _ = StopVideo();
            HidePicture();
            if (Current != null) Current.IsCurrent = false;
            _items.Clear();
            _index = -1;
            _jfShown = true;
            Empty.Visibility = Visibility.Collapsed;
            PrevBtn.Visibility = NextBtn.Visibility = Visibility.Collapsed;
            Tools.Visibility = Visibility.Collapsed;
            JellyfinHost.Visibility = Visibility.Visible;
            NameText.Text = "Jellyfin";
            PlaceText.Text = entry.Label;
            Title = "Jellyfin - vView";
            UpdateJellyfinButton();
            UpdateDetailsCard();
            UpdateStrip();
            if (!await EnsureWeb() || !_jfShown) return;
            if (_webServer != server) await LoadServer(server);
            _web.Focus();
        }

        /// <summary>Something else is shown: the page goes (it stays loaded, where it was, for next time).</summary>
        void HideJellyfinPage()
        {
            if (!_jfShown) return;
            _jfShown = false;
            JellyfinHost.Visibility = Visibility.Collapsed;
            Tools.Visibility = Visibility.Visible;
            UpdateDetailsCard();
            TakeKeys();   // the hidden page's browser window kept the keyboard - Space did nothing to the video
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SetFocus(IntPtr hWnd);

        /// <summary>
        /// The keyboard back to vView's own window (not WebView2's, which is a window of its own and keeps the keys while hidden). Only while
        /// vView is the active window - it never pulls vView to the front.
        /// </summary>
        void TakeKeys()
        {
            if (!IsActive || _jfShown) return;
            IntPtr h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (h != IntPtr.Zero) SetFocus(h);
            System.Windows.Input.Keyboard.Focus(this);
        }

        void UpdateJellyfinButton()
        {
            JellyfinBtn.Visibility = !_jfShown && Current != null && Current.Jellyfin != null ? Visibility.Visible : Visibility.Collapsed;
        }

        DispatcherTimer JellyfinBackTimer()
        {
            if (_jfBack == null)
            {
                _jfBack = new DispatcherTimer();
                _jfBack.Tick += (s, e) =>
                {
                    _jfBack.Stop();
                    MediaItem it = Current;
                    if (!_jfShown && it != null && it.Jellyfin != null && it.Jellyfin.Done) ShowJellyfin();
                };
            }
            return _jfBack;
        }

        void BackToJellyfinSoon(double seconds)
        {
            DispatcherTimer t = JellyfinBackTimer();
            t.Stop();
            t.Interval = TimeSpan.FromSeconds(seconds);
            t.Start();
        }

        /// <summary>The page, made once per window (WebView2 started in vView's settings folder).</summary>
        async Task<bool> EnsureWeb()
        {
            if (_web != null) return true;
            if (_webStarting) return false;
            _webStarting = true;
            JellyfinNote.Text = "Opening Jellyfin…";
            try
            {
                if (_webEnv == null) _webEnv = CoreWebView2Environment.CreateAsync(null, WebFolder);
                CoreWebView2Environment env = await _webEnv;
                // Jellyfin's dark page; a file dropped on it would send the page to that file
                var web = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 16, 16, 16), AllowExternalDrop = false };
                JellyfinHost.Children.Add(web);
                await web.EnsureCoreWebView2Async(env);
                CoreWebView2 core = web.CoreWebView2;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.AreBrowserAcceleratorKeysEnabled = false;   // Ctrl+N, F5 … are vView's, not the browser's
                core.Settings.AreDevToolsEnabled = Environment.GetEnvironmentVariable("VVIEW_WEB_DEVTOOLS") == "1";
                core.WebMessageReceived += OnJellyfinMessage;
                core.NewWindowRequested += (s, e) => { e.Handled = true; OpenOutside(e.Uri); };
                core.NavigationCompleted += (s, e) => { if (!e.IsSuccess) Log.Write("Jellyfin page: " + e.WebErrorStatus + " " + core.Source); };
                core.ProcessFailed += (s, e) => Log.Write("Jellyfin page: the browser stopped (" + e.ProcessFailedKind + ")");
                _web = web;
                Log.Write("Jellyfin page: WebView2 " + env.BrowserVersionString);
                return true;
            }
            catch (WebView2RuntimeNotFoundException)
            {
                JellyfinNote.Text = "Jellyfin needs Microsoft Edge WebView2, which comes with Windows 11 - it's missing on this PC.\n" +
                                    "Install the \"Evergreen WebView2 Runtime\" from Microsoft, then try again.";
                return false;
            }
            catch (Exception ex)
            {
                Log.Write("Jellyfin page: " + ex);
                JellyfinNote.Text = "Jellyfin's page couldn't open: " + ex.Message;
                _webEnv = null;
                return false;
            }
            finally { _webStarting = false; }
        }

        /// <summary>The bridge for this server, then its page.</summary>
        async Task LoadServer(string server)
        {
            _webServer = server;   // first: ApplyOptions doesn't load it again meanwhile
            CoreWebView2 core = _web.CoreWebView2;
            if (_bridgeId != null) core.RemoveScriptToExecuteOnDocumentCreated(_bridgeId);
            Version v = Assembly.GetExecutingAssembly().GetName().Version;
            _bridgeId = await core.AddScriptToExecuteOnDocumentCreatedAsync(
                Jellyfin.BridgeScript(server, v.Major + "." + v.Minor + "." + v.Build, Environment.MachineName));
            Log.Write("Jellyfin page: " + server);
            core.Navigate(Jellyfin.WebAddress(server));
        }

        /// <summary>Options changed the servers while the page shows: the shown one removed (or its address changed) - the next one.</summary>
        void CheckJellyfinServer()
        {
            if (!_jfShown || _web == null || _webServer == null) return;
            if (_settings.JellyfinValid().Any(e => e.Base == _webServer)) PlaceText.Text = _settings.JellyfinValid().First(e => e.Base == _webServer).Label;
            else if (_settings.JellyfinPick() != null) ShowJellyfin(_settings.JellyfinPick());
            else { HideJellyfinPage(); ShowEmpty(null); }   // the last server removed
        }

        /// <summary>
        /// Options > Jellyfin > Sign out (or Remove): forget one server's sign-in and what its page saved - the other servers' stay. Windows
        /// showing it go back to its sign-in.
        /// </summary>
        public async void ForgetJellyfin(string server)
        {
            if (server == null) return;
            Uri u = new Uri(server);
            string origin = u.Scheme + "://" + u.Authority;
            var windows = Application.Current.Windows.OfType<MainWindow>().ToList();
            MainWindow with = windows.FirstOrDefault(w => w._web != null && w._web.CoreWebView2 != null) ?? this;
            try
            {
                if (!await with.EnsureWeb()) return;
                await with._web.CoreWebView2.CallDevToolsProtocolMethodAsync("Storage.clearDataForOrigin",
                    JsonSerializer.Serialize(new { origin = origin, storageTypes = "all" }));
                Log.Write("Jellyfin: signed out of " + u.Authority + " (its page's data cleared)");
            }
            catch (Exception ex) { Log.Write("Jellyfin sign out: " + ex.Message); }
            foreach (MainWindow w in windows.Where(w => w._webServer == server))
            {
                w._webServer = null;   // loaded again when it's next shown (to the sign-in)
                if (!w._jfShown) continue;
                JellyfinEntry same = _settings.JellyfinValid().FirstOrDefault(e => e.Base == server) ?? _settings.JellyfinPick();
                if (same != null) w.ShowJellyfin(same);
                else { w.HideJellyfinPage(); w.ShowEmpty(null); }
            }
        }

        static void OpenOutside(string url)
        {
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u) || (u.Scheme != "http" && u.Scheme != "https" && u.Scheme != "mailto")) return;
            try { Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Write("Open link: " + ex.Message); }
        }

        // =================================================================
        // The bridge: messages to and from vView's player in the page
        // =================================================================

        void PostJellyfin(object message)
        {
            try { _web?.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(message)); }
            catch (Exception ex) { Log.Write("Jellyfin message: " + ex.Message); }
        }

        void OnJellyfinMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            Uri from;
            if (_webServer == null || !Uri.TryCreate(e.Source, UriKind.Absolute, out from) ||
                !string.Equals(from.Host, new Uri(_webServer).Host, StringComparison.OrdinalIgnoreCase)) return;
            JsonElement m;
            try { using (JsonDocument d = JsonDocument.Parse(e.WebMessageAsJson)) m = d.RootElement.Clone(); }
            catch { return; }
            if (m.ValueKind != JsonValueKind.Object) return;
            MediaItem it = Current;
            bool playing = it != null && it.Jellyfin != null && _playing == it;
            switch (Str(m, "type"))
            {
                case "ready": Log.Write("Jellyfin page: vView's player is in (" + _web.CoreWebView2.Source + ")"); break;
                case "play":
                    JellyfinPlay(new JellyfinPlay
                    {
                        Url = Str(m, "url"), Title = Str(m, "title") ?? "Jellyfin", StartMs = (long)Num(m, "startMs", 0),
                        AudioTrack = (int)Num(m, "audioTrack", -1), SubTrack = (int)Num(m, "subTrack", -1),
                        SubUrl = Str(m, "subUrl"), SubsOff = Bool(m, "subsOff"), ItemId = Str(m, "itemId") ?? ""
                    });
                    break;
                case "upnext":   // the next one in the page's queue (for Up next)
                    if (playing && it.Jellyfin.ItemId.Length > 0 && Str(m, "itemId") == it.Jellyfin.ItemId)
                    {
                        it.Jellyfin.NextTitle = Str(m, "title") ?? "";
                        it.Jellyfin.NextSeries = Str(m, "series") ?? "";
                        it.Jellyfin.NextImage = Str(m, "image");
                    }
                    break;
                case "segments":   // the intro and the credits, from the server (Intro Skipper)
                    if (playing && it.Jellyfin.ItemId.Length > 0 && Str(m, "itemId") == it.Jellyfin.ItemId)
                    {
                        it.Jellyfin.IntroStart = (long)Num(m, "introStart", -1);
                        it.Jellyfin.IntroEnd = (long)Num(m, "introEnd", -1);
                        it.Jellyfin.OutroStart = (long)Num(m, "outroStart", -1);
                        Log.Write("Jellyfin: " + (it.Jellyfin.IntroEnd > 0 ? "intro " + Format.Time(Math.Max(0, it.Jellyfin.IntroStart)) + "-" + Format.Time(it.Jellyfin.IntroEnd) : "no intro") +
                                  ", " + (it.Jellyfin.OutroStart > 0 ? "credits from " + Format.Time(it.Jellyfin.OutroStart) : "no credits time"));
                    }
                    break;
                case "stop":   // the page stopped it (it's playing something else next, or it was stopped from another Jellyfin app)
                    if (it != null && it.Jellyfin != null && !it.Jellyfin.Done)
                    {
                        it.Jellyfin.Done = true;
                        _ = StopVideo();
                        BackToJellyfinSoon(1.5);
                    }
                    break;
                case "pause": if (playing) SetPaused(true); break;
                case "unpause": if (playing) SetPaused(false); break;
                case "seek":
                    if (playing)
                    {
                        long to = (long)Num(m, "ms", 0);
                        Interlocked.Exchange(ref _vTime, to);
                        Vlc(() => { if (_mp.IsSeekable) _mp.Time = to; });
                    }
                    break;
                case "volume": SetVolume((int)Num(m, "v", _settings.Volume)); break;
                case "mute": if (Bool(m, "on") != _settings.Muted) ToggleMute(); break;
                case "audio": if (playing) SetTrackNumber(false, (int)Num(m, "track", -1)); break;
                case "subtitle":
                    if (!playing) break;
                    string url = Str(m, "subUrl");
                    if (url != null) AddSubtitleSlave(url);
                    else SetTrackNumber(true, Bool(m, "subsOff") ? -1 : (int)Num(m, "subTrack", -1));
                    break;
                case "fullscreen": SetFullScreen(!_fullScreen); break;
                case "options": ShowOptions(false); break;
                case "openurl": OpenOutside(Str(m, "url")); break;
            }
        }

        /// <summary>The page asked to play a video: here, with VLC.</summary>
        void JellyfinPlay(JellyfinPlay p)
        {
            Uri u;
            if (p.Url == null || !Uri.TryCreate(p.Url, UriKind.Absolute, out u) || (u.Scheme != "http" && u.Scheme != "https")) return;
            JellyfinBackTimer().Stop();
            if (Current != null && Current.Jellyfin != null) Current.Jellyfin.Done = true;   // the page has moved on from it
            StopSlideshow();
            HideJellyfinPage();
            if (Current != null) Current.IsCurrent = false;
            _items.Clear();
            _items.Add(MediaItem.FromJellyfin(p));
            _index = -1;
            ShowAt(0);
            PostJellyfin(new { type = "accepted" });
            Log.Write("Jellyfin: playing " + p.Title + (p.StartMs > 0 ? " from " + Format.Time(p.StartMs) : ""));
        }

        /// <summary>A Jellyfin video stops here (Back to Jellyfin, another file, closing): the page reports where it got to.</summary>
        void LeaveJellyfinVideo()
        {
            MediaItem it = Current;
            if (it == null || it.Jellyfin == null || it.Jellyfin.Done) return;
            it.Jellyfin.Done = true;
            PostJellyfin(new { type = "stop", t = Interlocked.Read(ref _vTime) });
        }

        /// <summary>Every half second while a Jellyfin video plays: where it's up to (from VLC's events - no VLC calls).</summary>
        void JellyfinTick()
        {
            MediaItem it = _playing;
            if (it == null || it.Jellyfin == null || it.Jellyfin.Done || ++_jfTicks % 2 != 0) return;
            PostJellyfin(new
            {
                type = "state", t = Interlocked.Read(ref _vTime), d = Interlocked.Read(ref _vLength), playing = _vPlaying,
                volume = _settings.Volume, muted = _settings.Muted, full = _fullScreen
            });
        }

        /// <summary>A Jellyfin video played to its end: the page marks it watched and plays the next one, if it's set to.</summary>
        bool JellyfinEnded()
        {
            MediaItem it = _playing;
            if (it == null || it.Jellyfin == null) return false;
            if (!it.Jellyfin.Done)
            {
                it.Jellyfin.Done = true;
                long length = Interlocked.Read(ref _vLength);
                PostJellyfin(new { type = "state", t = length, d = length, playing = true, volume = _settings.Volume, muted = _settings.Muted, full = _fullScreen });
                // Play now / the countdown already asked for the next one; Cancel (or no playing on by itself): stop here, watched
                if (it.Jellyfin.NextAsked) { }
                else if (JellyfinGoesOn(it.Jellyfin)) PostJellyfin(new { type = "ended" });
                else PostJellyfin(new { type = "stop", t = length });
            }
            _ended = true;
            UpdateVideoBar();
            BackToJellyfinSoon(2.5);
            return true;
        }

        void JellyfinError()
        {
            MediaItem it = _playing;
            if (it == null || it.Jellyfin == null || it.Jellyfin.Done) return;
            it.Jellyfin.Done = true;
            PostJellyfin(new { type = "error" });   // the page may try again, converted by the server - or it says what went wrong
            BackToJellyfinSoon(4);
        }

        void JellyfinNextPrevious(bool next) { PostJellyfin(new { type = next ? "next" : "previous" }); }

        /// <summary>Pause or play (the page, or another Jellyfin app controlling this one).</summary>
        void SetPaused(bool pause)
        {
            if (_mp == null || _playing == null || _ended) return;
            PauseVideo(pause);
        }

        /// <summary>An audio track or subtitle by its place among the video's own (0 = the first); subtitles -1 = off.</summary>
        void SetTrackNumber(bool subtitles, int n)
        {
            if (_mp == null || _playing == null) return;
            Vlc(() =>
            {
                if (n < 0) { if (subtitles) _mp.SetSpu(-1); return; }
                var list = (subtitles ? _mp.SpuDescription : _mp.AudioTrackDescription)?.Where(t => t.Id >= 0).ToArray();
                if (list == null || n >= list.Length) return;
                if (subtitles) _mp.SetSpu(list[n].Id); else _mp.SetAudioTrack(list[n].Id);
            });
        }

        static string Str(JsonElement m, string name)
        {
            JsonElement v;
            return m.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }

        static double Num(JsonElement m, string name, double fallback)
        {
            JsonElement v;
            return m.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : fallback;
        }

        static bool Bool(JsonElement m, string name)
        {
            JsonElement v;
            return m.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.True;
        }
    }
}
