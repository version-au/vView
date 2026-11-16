using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using vView.Core;

namespace vView.Ui
{
    /// <summary>
    /// vView's window: one picture, video or sound at a time, the rest of its folder in the strip along the bottom (Left / Right go
    /// through them), and the details beside it. Pictures: MainWindow.Picture.cs; video and sound: MainWindow.Video.cs.
    /// </summary>
    public partial class MainWindow : Window, IThemedWindow
    {
        readonly AppSettings _settings;
        readonly VShellThemeSource _themeSource;
        readonly ShellIcons _icons;
        readonly Action _themeChanged;
        IntPtr _hwnd;
        Theme _theme;
        bool _blur;

        /// <summary>The folder's pictures, videos and sounds; _index is the one showing.</summary>
        readonly ObservableCollection<MediaItem> _items = new ObservableCollection<MediaItem>();
        int _index = -1;
        MediaItem Current { get { return _index >= 0 && _index < _items.Count ? _items[_index] : null; } }
        int _showVersion;

        public Theme CurrentTheme { get { return _theme; } }
        public bool Blurred { get { return _blur; } }
        public AppSettings Settings { get { return _settings; } }

        public MainWindow(AppSettings settings, VShellThemeSource themeSource, string file)
        {
            _settings = settings;
            _themeSource = themeSource;
            InitializeComponent();
            _icons = new ShellIcons(Dispatcher);
            Strip.ItemsSource = _items;

            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 44, ResizeBorderThickness = new Thickness(6), CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false
            });
            RestorePlacement();
            SourceInitialized += (s, e) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                HwndSource.FromHwnd(_hwnd).AddHook(WndProc);
                ApplyTheme();
            };
            StateChanged += (s, e) => UpdateMaximized();
            _startFile = file;
            Loaded += (s, e) => Start();
            Closing += OnClosing;
            PreviewKeyDown += OnPreviewKeyDown;
            // back to vView with Jellyfin's page hidden: the keys are vView's, not the hidden page's (Windows gives them to its window)
            Activated += (s, e) => { if (_web != null) Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(TakeKeys)); };
            Drop += OnDrop;
            DragOver += OnDragOver;
            // over a playing video the drop lands on its see-through overlay (a window of its own, shown by LibVLCSharp)
            InitSpectrum();
            InitMusic();
            VideoOverlay.AllowDrop = true;
            VideoOverlay.Drop += OnDrop;
            VideoOverlay.DragOver += OnDragOver;
            _themeChanged = () => Dispatcher.BeginInvoke(new Action(ApplyTheme));
            _themeSource.Changed += _themeChanged;
            _slideTimer = new DispatcherTimer();
            _slideTimer.Tick += (s, e) => SlideNext();
        }

        readonly string _startFile;
        bool _started;

        /// <summary>Once: the options, VLC starting, the file it was started with.</summary>
        void Start()
        {
            if (_started) return;
            _started = true;
            ApplyOptions();
            // the update notice: a small window of its own over the picture area's corner - a playing video and the Jellyfin page are
            // native windows that covered it when it was drawn in this window
            if (_notice == null) { _notice = new NoticeWindow(this, Stage); _notice.ApplyLook(_theme, _blur); _notice.Sync(); }
            InitVideo();
            if (!string.IsNullOrEmpty(_startFile)) Open(_startFile);
            else ShowEmpty(null);
        }

        /// <summary>Started with a song (or a playlist) while songs play in the classic player: it opens straight in that.</summary>
        public bool StartsInClassic { get { return SongForClassic(_startFile); } }

        /// <summary>A song (or a playlist) on this PC, while songs play in the classic player.</summary>
        bool SongForClassic(string f)
        {
            return _settings.MusicView == "Classic" && !string.IsNullOrEmpty(f) && !Media.IsAddress(f) && File.Exists(f) &&
                   (Media.KindOf(f) == MediaKind.Audio || Media.IsPlaylist(f));
        }

        /// <summary>
        /// Start without showing this window (StartsInClassic): its handle and theme, then the song - which shows the classic player. If
        /// that didn't happen (the file couldn't be played), this window shows after all, saying why.
        /// </summary>
        public void StartHidden()
        {
            new WindowInteropHelper(this).EnsureHandle();
            Start();
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { if (!IsVisible && !ClassicShowing) Show(); }));
        }

        // =================================================================
        // Theme and window
        // =================================================================

        /// <summary>The window's icons in the theme's colours (sharp taskbar icon, vShell's live icon, the Instance property).</summary>
        WindowIcons _windowIcons;
        NoticeWindow _notice;

        void ApplyTheme()
        {
            Theme t = Theme.From(_themeSource.Current);
            _theme = t;
            bool blur = _settings.ThemeOpacity;   // see-through at vShell's opacity (Options > Look), else solid
            Log.Write("Theme: found=" + _themeSource.Found + " " + _themeSource.Current.Signature + " blur=" + blur);
            t.Apply(Application.Current.Resources, blur);
            WindowEffects.Look(this, t, blur);   // Blur / Frosted: the blurred wallpaper under the theme colour, as vShell's bars
            _blur = blur;
            if (OptionsWindow.Current != null) OptionsWindow.Current.ApplyTheme(t, blur);
            _classic?.ApplyLook(t, blur);
            _notice?.ApplyLook(t, blur);
            _jfPrompt?.ApplyLook(t, blur);
            if (_windowIcons == null) _windowIcons = new WindowIcons(this);
            _windowIcons.Apply(t);
            try { AppMark.Source = AppIcon.Image(t, true); }
            catch (Exception ex) { Log.Write("Icon: " + ex.Message); }
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(LayoutPicture));
        }
        void RestorePlacement()
        {
            if (_settings.Width >= 300 && _settings.Height >= 200)
            {
                Rect work = SystemParameters.VirtualScreenWidth > 0
                    ? new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight)
                    : SystemParameters.WorkArea;
                var r = new Rect(_settings.Left, _settings.Top, _settings.Width, _settings.Height);
                if (work.IntersectsWith(r))
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = r.Left; Top = r.Top; Width = r.Width; Height = r.Height;
                }
                else WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;
            if (_settings.Maximized) WindowState = WindowState.Maximized;
        }

        void UpdateMaximized()
        {
            bool max = WindowState == WindowState.Maximized;
            // a borderless window maximised hangs off the screen by its frame (not in full screen, which covers it on purpose)
            Root.Margin = max && !_fullScreen ? new Thickness(WindowEffects.MaximizedOverhang(_hwnd)) : new Thickness(0);
            // Jellyfin's page is a window of its own and would take the mouse at the edges: room for the resize border around it
            JellyfinHost.Margin = WindowState == WindowState.Normal ? new Thickness(6, 0, 6, 6) : new Thickness(0);
            MaxBtn.Content = max ? "" : "";
            MaxBtn.ToolTip = max ? "Restore" : "Maximise";
        }

        void OnClosing(object sender, CancelEventArgs e)
        {
            // a Jellyfin video playing: stopped first, and a moment for the page to tell the server where it got to
            if (!_jfClosing && Current != null && Current.Jellyfin != null && !Current.Jellyfin.Done && _web != null)
            {
                e.Cancel = true;
                _jfClosing = true;
                LeaveJellyfinVideo();
                _ = StopVideo();
                var wait = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
                wait.Tick += (s, a) => { wait.Stop(); Close(); };
                wait.Start();
                return;
            }
            StopSlideshow();
            if (!_fullScreen)
            {
                Rect r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
                _settings.Left = r.Left; _settings.Top = r.Top; _settings.Width = r.Width; _settings.Height = r.Height;
                _settings.Maximized = WindowState == WindowState.Maximized;
            }
            _settings.Save();
            if (_classic != null) { _classic.AllowClose = true; _classic.Close(); }
            CloseVideo();
            try { _web?.Dispose(); } catch { }
            _icons.Dispose();
            _themeSource.Changed -= _themeChanged;
        }

        void Minimize_Click(object sender, RoutedEventArgs e) { WindowState = WindowState.Minimized; }
        void Maximize_Click(object sender, RoutedEventArgs e) { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }
        void Close_Click(object sender, RoutedEventArgs e) { Close(); }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_SYSCOMMAND = 0x112, SC_KEYMENU = 0xF100;
            if (msg == 0x4A /* WM_COPYDATA */)
            {
                string command = TestHooks.Received(lParam);   // tests only (VVIEW_TEST_HOOKS=1)
                if (command != null)
                {
                    handled = true;
                    Dispatcher.BeginInvoke(new Action(() => RunTestCommand(command)));
                    return new IntPtr(1);
                }
                string file = null;
                try { file = Instance.Received(lParam); } catch { }
                if (file != null && UpdateCenter.IsCommand(file))
                {
                    // vShell's Settings: "update now" / "include pre-releases" - answered as handled, so the second start stops; the check runs after this message
                    handled = true;
                    string cmd = file;
                    Dispatcher.BeginInvoke(new Action(() => UpdateCenter.HandleCommand(cmd)));
                    return new IntPtr(1);
                }
                if (file != null)   // vView started again with a file: here, or in a new window (Options > General)
                {
                    handled = true;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (file.Length > 0 && _settings.OpenInNewWindow)
                        {
                            // a song while a classic player shows: into that one (not a second player, stacked on it and playing too)
                            MainWindow player = SongForClassic(file) ? Application.Current.Windows.OfType<MainWindow>().FirstOrDefault(w => w.ClassicShowing) : null;
                            if (player != null) { player.Open(file); player.ActivateFront(); }
                            else OpenNewWindow(file);
                            return;
                        }
                        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                        if (file.Length > 0) Open(file);
                        ActivateFront();   // the classic player if the song went there
                    }));
                    return new IntPtr(1);
                }
            }
            // Alt+Space with the glass on (no WS_SYSMENU): the window menu, by hand
            if (msg == WM_SYSCOMMAND && ((int)wParam & 0xFFF0) == SC_KEYMENU && (int)lParam == ' ' && _blur)
            {
                handled = true;
                SystemCommands.ShowSystemMenu(this, PointToScreen(new Point(8, 44)));
            }
            return IntPtr.Zero;
        }

        // =================================================================
        // Opening and moving through the folder
        // =================================================================

        /// <summary>Show path, with its folder's pictures and videos in the strip.</summary>
        public void Open(string path)
        {
            if (Media.IsAddress(path)) { OpenStream(path); return; }
            LeaveJellyfinVideo();
            try { path = Path.GetFullPath(path); } catch { }
            if (!File.Exists(path)) { ShowEmpty("vView can't find “" + path + "”."); return; }
            if (Media.IsPlaylist(path)) { OpenPlaylist(path); return; }
            if (Media.KindOf(path) == MediaKind.None) { ShowEmpty("vView doesn't show this kind of file (" + Path.GetExtension(path) + ")."); return; }
            _settings.LastFolder = Path.GetDirectoryName(path) ?? "";
            List<MediaItem> list = Media.Folder(path);
            _items.Clear();
            foreach (MediaItem it in list) _items.Add(it);
            int i = list.FindIndex(it => string.Equals(it.Path, path, StringComparison.OrdinalIgnoreCase));
            ShowAt(Math.Max(0, i));
        }

        /// <summary>Play a network stream (rtsp://, rtmp://, http(s)://, udp:// …) - on its own, no folder.</summary>
        public void OpenStream(string address)
        {
            address = address.Trim();
            _settings.RecentStreams.RemoveAll(a => string.Equals(a, address, StringComparison.OrdinalIgnoreCase));
            _settings.RecentStreams.Insert(0, address);
            if (_settings.RecentStreams.Count > 12) _settings.RecentStreams.RemoveRange(12, _settings.RecentStreams.Count - 12);
            _settings.Save();
            LeaveJellyfinVideo();
            _items.Clear();
            _items.Add(MediaItem.Stream(address));
            _index = -1;
            ShowAt(0);
        }

        void OpenStreamDialog()
        {
            string a = StreamWindow.Ask(this, _settings);
            if (a != null) OpenStream(a);
        }

        void ShowAt(int index)
        {
            if (_items.Count == 0) { ShowEmpty(null); return; }
            HideJellyfinPage();
            index = Math.Max(0, Math.Min(_items.Count - 1, index));
            if (Current != null) Current.IsCurrent = false;
            _index = index;
            MediaItem it = Current;
            it.IsCurrent = true;
            Strip.ScrollIntoView(it);
            int v = ++_showVersion;
            NameText.Text = it.Name;
            UpdatePlace();
            Title = it.Name + " - vView";
            UpdateJellyfinButton();
            Empty.Visibility = Visibility.Collapsed;
            // a song: the playlist moves through them, not the side buttons
            PrevBtn.Visibility = NextBtn.Visibility = _items.Count > 1 && it.Kind != MediaKind.Audio ? Visibility.Visible : Visibility.Collapsed;
            FillDetails(it);   // first: a picture decoded ahead adds its own details straight away
            if (it.Kind == MediaKind.Picture)
            {
                StopVideo();
                ShowPicture(it, v);
            }
            else
            {
                HidePicture();
                PlayMedia(it, v);
                if (it.Kind == MediaKind.Audio && !it.IsStream) ShowMusic(it);
            }
            UpdateMusicButtons();
            SyncClassic();
            ZoomTools.Visibility = it.Kind == MediaKind.Picture ? Visibility.Visible : Visibility.Collapsed;
            UpdateStrip();
            PreloadAround();
        }

        /// <summary>Nothing to show: why (or the "Open a picture" hint), with an Open button.</summary>
        void ShowEmpty(string why)
        {
            StopVideo();
            HidePicture();
            Empty.Visibility = Visibility.Visible;
            EmptyText.Text = why ?? "Open a media file, or drop one here";
            EmptyGlyph.Text = why == null ? "" : "";
            PrevBtn.Visibility = NextBtn.Visibility = Visibility.Collapsed;
            ZoomTools.Visibility = Visibility.Collapsed;
            if (_items.Count == 0) { NameText.Text = "vView"; PlaceText.Text = ""; Title = "vView"; }
            UpdateJellyfinButton();
            UpdateStrip();
            if (ClassicShowing) LeaveClassic(false);   // nothing to play: the full window, saying why
        }

        /// <summary>Another vView window with this file, a little down and to the right of this one.</summary>
        void OpenNewWindow(string file)
        {
            var w = new MainWindow(_settings, _themeSource, file);
            if (WindowState == WindowState.Normal)
            {
                w.WindowStartupLocation = WindowStartupLocation.Manual;
                w.WindowState = WindowState.Normal;
                w.Left = Left + 32;
                w.Top = Top + 32;
                w.Width = Width;
                w.Height = Height;
            }
            // a song for the classic player: only that shows (as when vView starts with one) - showing this window first left it
            // standing empty beside the player
            if (w.StartsInClassic) { w.StartHidden(); return; }
            w.Show();
            w.Activate();
        }

        void Next() { if (_items.Count > 1) ShowAt(_index + 1 >= _items.Count ? 0 : _index + 1); }
        void Prev() { if (_items.Count > 1) ShowAt(_index <= 0 ? _items.Count - 1 : _index - 1); }

        void Prev_Click(object sender, RoutedEventArgs e) { Prev(); }
        void Next_Click(object sender, RoutedEventArgs e) { Next(); }

        void OpenDialog()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Open in vView", Filter = Media.DialogFilter };
            if (Directory.Exists(_settings.LastFolder)) dlg.InitialDirectory = _settings.LastFolder;
            if (dlg.ShowDialog(Front) == true) Open(dlg.FileName);
        }

        void Open_Click(object sender, RoutedEventArgs e) { OpenDialog(); }
        void OpenFolder_Click(object sender, RoutedEventArgs e) { OpenFolderDialog(); }

        /// <summary>Open a folder (Ctrl+Shift+O): as dropping it - its pictures, else its songs as the playlist, else its first video.</summary>
        void OpenFolderDialog()
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Open a folder in vView" };
            if (Directory.Exists(_settings.LastFolder)) dlg.InitialDirectory = _settings.LastFolder;
            if (dlg.ShowDialog(Front) == true) OpenFolder(dlg.FolderName);
        }

        void OpenFolder(string folder)
        {
            List<string> files = Media.Dropped(new[] { folder });
            if (files.Count == 0) { ShowEmpty("There's nothing vView can show in “" + Path.GetFileName(folder.TrimEnd('\\')) + "”."); return; }
            Open(files[0]);   // a picture brings the folder's pictures, a song its songs
        }

        /// <summary>What a drop holds: files / folders, or a web address (a link dragged from a browser).</summary>
        static string[] DroppedFiles(IDataObject data)
        {
            try { return data.GetDataPresent(DataFormats.FileDrop) ? data.GetData(DataFormats.FileDrop) as string[] : null; } catch { return null; }
        }

        static string DroppedAddress(IDataObject data)
        {
            foreach (string format in new[] { "UniformResourceLocatorW", DataFormats.UnicodeText, DataFormats.Text })
            {
                try
                {
                    if (!data.GetDataPresent(format)) continue;
                    object o = data.GetData(format);
                    string s = o as string;
                    if (s == null && o is MemoryStream ms) s = System.Text.Encoding.Unicode.GetString(ms.ToArray()).TrimEnd('\0');
                    s = s?.Trim();
                    if (s != null && s.IndexOfAny(new[] { '\r', '\n' }) < 0 && Media.IsAddress(s)) return s;
                }
                catch { }
            }
            return null;
        }

        void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = DroppedFiles(e.Data) != null || DroppedAddress(e.Data) != null ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        /// <summary>
        /// A drop: one file opens as Open would (a picture with its folder); a folder shows its pictures (or plays its first video or
        /// sound); several pictures are gone through on their own, in name order; a link plays as a network stream.
        /// </summary>
        void OnDrop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            DropData(e.Data);
        }

        void DropData(IDataObject data)
        {
            string[] dropped = DroppedFiles(data);
            if (dropped == null || dropped.Length == 0)
            {
                string address = DroppedAddress(data);
                if (address != null) { Activate(); OpenStream(address); }
                return;
            }
            Activate();
            if (dropped.Length == 1 && !Directory.Exists(dropped[0])) { Open(dropped[0]); return; }
            List<string> files = Media.Dropped(dropped);
            List<string> pictures = files.Where(f => Media.KindOf(f) == MediaKind.Picture).ToList();
            List<string> songs = files.Where(f => Media.KindOf(f) == MediaKind.Audio).ToList();
            // songs dropped (and no pictures): a playlist of just those - one folder of them, as opening its first song
            if (pictures.Count == 0 && songs.Count > 0)
            {
                if (dropped.Length == 1) Open(songs[0]);
                else { _settings.LastFolder = Path.GetDirectoryName(songs[0]) ?? ""; SetList(songs, 0); }
                return;
            }
            if (pictures.Count == 0)
            {
                if (files.Count > 0) Open(files[0]);
                else if (Current == null) ShowEmpty("There's nothing vView can show in what was dropped.");
                else Flash("Nothing here vView can show");   // what's showing stays
                return;
            }
            if (dropped.Length == 1) { Open(pictures[0]); return; }   // one folder: as opening its first picture
            LeaveJellyfinVideo();
            _settings.LastFolder = Path.GetDirectoryName(pictures[0]) ?? "";
            _items.Clear();
            foreach (string f in pictures) _items.Add(new MediaItem(f));
            _index = -1;
            ShowAt(0);
        }

        // ---- the strip ----

        void StripItem_Loaded(object sender, RoutedEventArgs e)
        {
            var it = (sender as FrameworkElement)?.DataContext as MediaItem;
            if (it == null || it.Thumb != null || it.ThumbPending) return;
            it.ThumbPending = true;
            int px = (int)Math.Round(96 * VisualTreeHelper.GetDpi(this).DpiScaleX);
            _icons.RequestThumb(it.Path, px, () => _items.Contains(it), img => { it.ThumbPending = false; it.Thumb = img; });
        }

        void Strip_Click(object sender, MouseButtonEventArgs e)
        {
            var it = (e.OriginalSource as FrameworkElement)?.DataContext as MediaItem;
            if (it == null) return;
            int i = _items.IndexOf(it);
            if (i >= 0 && i != _index) ShowAt(i);
            e.Handled = true;
        }

        /// <summary>The wheel scrolls the strip sideways.</summary>
        void Strip_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            ScrollViewer sv = FindChild<ScrollViewer>(Strip);
            if (sv == null) return;
            sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
            e.Handled = true;
        }

        static T FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject c = VisualTreeHelper.GetChild(parent, i);
                if (c is T t) return t;
                T deeper = FindChild<T>(c);
                if (deeper != null) return deeper;
            }
            return null;
        }

        // =================================================================
        // Details
        // =================================================================

        void Details_Click(object sender, RoutedEventArgs e) { ToggleDetails(); }

        void ToggleDetails()
        {
            _settings.ShowDetails = !_settings.ShowDetails;
            _settings.Save();
            ApplyOptions();
        }

        void FillDetails(MediaItem it)
        {
            DetName.Text = it.Name;
            var props = new List<KeyValuePair<string, string>>();
            if (it.Jellyfin != null)
            {
                // not the stream's address: it holds the sign-in's key
                DetKind.Text = "Video from Jellyfin";
                if (_webServer != null) props.Add(new KeyValuePair<string, string>("Server", new Uri(_webServer).Host));
            }
            else if (it.IsStream)
            {
                DetKind.Text = "Network stream";
                props.Add(new KeyValuePair<string, string>("Address", it.Path));
            }
            else try
            {
                var fi = new FileInfo(it.Path);
                DetKind.Text = (it.Kind == MediaKind.Picture ? "Picture" : it.Kind == MediaKind.Video ? "Video" : "Sound") + " · " +
                               Path.GetExtension(it.Path).TrimStart('.').ToUpperInvariant() + " · " + Format.Size(fi.Length);
                props.Add(new KeyValuePair<string, string>("Modified", Format.Stamp(fi.LastWriteTime)));
                props.Add(new KeyValuePair<string, string>("Folder", fi.DirectoryName));
            }
            catch (Exception ex) { DetKind.Text = ex.Message; }
            _details = props;
            DetProps.ItemsSource = null;
            DetProps.ItemsSource = props;
        }

        List<KeyValuePair<string, string>> _details = new List<KeyValuePair<string, string>>();

        /// <summary>Add facts as they're found (a picture's size and camera, a video's length) - before the folder.</summary>
        void AddDetails(MediaItem it, IEnumerable<KeyValuePair<string, string>> more)
        {
            if (Current != it) return;
            var list = _details.Where(p => p.Key != "Folder" && p.Key != "Modified").ToList();
            foreach (var m in more) if (!list.Any(p => p.Key == m.Key) && !string.IsNullOrEmpty(m.Value)) list.Add(m);
            list.AddRange(_details.Where(p => p.Key == "Modified" || p.Key == "Folder"));
            _details = list;
            DetProps.ItemsSource = null;
            DetProps.ItemsSource = list;
        }

        void CopyPath_Click(object sender, RoutedEventArgs e) { CopyPath(); }

        void CopyPath()
        {
            if (Current == null || Current.Jellyfin != null) return;
            try { Clipboard.SetText(Current.Path); Flash("Copied its path"); } catch { }
        }

        void CopyFile()
        {
            if (Current == null) return;
            if (Current.IsStream) { CopyPath(); return; }
            try
            {
                var list = new System.Collections.Specialized.StringCollection { Current.Path };
                Clipboard.SetFileDropList(list);
                Flash("Copied - paste it in vXplorer or Explorer");
            }
            catch { }
        }

        void OpenWith_Click(object sender, RoutedEventArgs e)
        {
            if (Current == null || Current.Jellyfin != null) return;
            try { Process.Start(new ProcessStartInfo("rundll32.exe", "shell32.dll,OpenAs_RunDLL " + Current.Path) { UseShellExecute = false }); }
            catch (Exception ex) { Log.Write("Open with: " + ex.Message); }
        }

        /// <summary>A short note in the top bar (where the place in the folder shows), for two seconds.</summary>
        void Flash(string text)
        {
            string was = PlaceText.Text;
            PlaceText.Text = text;
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            t.Tick += (s, e) => { t.Stop(); if (PlaceText.Text == text) PlaceText.Text = was; };
            t.Start();
        }

        // =================================================================
        // Show in folder, delete
        // =================================================================

        void ShowInFolder_Click(object sender, RoutedEventArgs e) { ShowInFolder(); }

        /// <summary>The file in its folder: vXplorer if it's installed (it opens the folder), else Explorer with the file selected.</summary>
        void ShowInFolder()
        {
            if (Current == null || Current.IsStream) return;
            try
            {
                string vx = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "vXplorer", "vXplorer.exe");
                if (File.Exists(vx)) Process.Start(new ProcessStartInfo(vx, "\"" + Path.GetDirectoryName(Current.Path) + "\"") { UseShellExecute = false });
                else Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + Current.Path + "\"") { UseShellExecute = false });
            }
            catch (Exception ex) { Log.Write("Show in folder: " + ex.Message); }
        }

        void Delete_Click(object sender, RoutedEventArgs e) { DeleteCurrent(); }

        /// <summary>Only a fixed drive (an internal or external disk) has a Recycle Bin - not a network drive or share, a USB stick or a card.</summary>
        static bool HasRecycleBin(string path)
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(path));
                if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\")) return false;
                return new DriveInfo(root).DriveType == DriveType.Fixed;
            }
            catch { return false; }
        }

        async void DeleteCurrent()
        {
            MediaItem it = Current;
            if (it == null || it.IsStream) return;
            // a network or removable drive has no Recycle Bin: Windows would delete it for good without a word - always ask, and say so
            bool bin = HasRecycleBin(it.Path);
            if (!bin)
            {
                if (!Ask.YesNo(this, "Delete it for good?", "“" + it.Name + "” is on a drive with no Recycle Bin (a network or removable drive), so it can't be got back. Delete it for good?",
                               "Delete for good", "Cancel", false)) return;
            }
            else if (_settings.ConfirmDelete && !Ask.YesNo(this, "Move to the Recycle Bin?", "Move “" + it.Name + "” to the Recycle Bin? You can get it back from there.",
                                                      "Move to Recycle Bin", "Cancel", true)) return;
            int at = _index;
            await StopVideo();   // VLC lets go of the file
            ReleasePicture(it);
            string error = await Task.Run(() =>
            {
                try
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(it.Path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                                                                       bin ? Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin
                                                                           : Microsoft.VisualBasic.FileIO.RecycleOption.DeletePermanently);
                    return null;
                }
                catch (OperationCanceledException) { return ""; }
                catch (Exception ex) { return ex.Message; }
            });
            if (error != null)
            {
                if (error.Length > 0) Flash((bin ? "Couldn't move it to the Recycle Bin: " : "Couldn't delete it: ") + error);
                ShowAt(at);
                return;
            }
            Log.Write((bin ? "Recycled " : "Deleted for good (no Recycle Bin on that drive) ") + it.Path);
            _items.Remove(it);
            if (_items.Count == 0) { _index = -1; ShowEmpty("That was the last one in this folder."); return; }
            _index = -1;
            ShowAt(Math.Min(at, _items.Count - 1));
        }

        // =================================================================
        // Full screen and slideshow
        // =================================================================

        bool _fullScreen;
        WindowState _stateBefore;

        void FullScreen_Click(object sender, RoutedEventArgs e) { SetFullScreen(!_fullScreen); }

        void SetFullScreen(bool on)
        {
            if (on == _fullScreen) return;
            if (on)
            {
                _stateBefore = WindowState;
                _fullScreen = true;
                if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;   // so maximising again covers the taskbar
                WindowState = WindowState.Maximized;
            }
            else
            {
                _fullScreen = false;
                WindowState = WindowState.Normal;
                if (_stateBefore == WindowState.Maximized) WindowState = WindowState.Maximized;
            }
            ApplyOptions();
            UpdateMaximized();
        }

        readonly DispatcherTimer _slideTimer;
        bool _sliding;

        void Slideshow_Click(object sender, RoutedEventArgs e) { if (_sliding) StopSlideshow(); else StartSlideshow(); }

        void StartSlideshow()
        {
            if (_items.Count < 2) return;
            _sliding = true;
            SlideBtn.Content = "";
            SlideBtn.ToolTip = "Stop the slideshow (Esc)";
            SetFullScreen(true);
            ArmSlide();
        }

        void StopSlideshow()
        {
            if (!_sliding) return;
            _sliding = false;
            _slideTimer.Stop();
            SlideBtn.Content = "";
            SlideBtn.ToolTip = "Slideshow (F5)";
        }

        /// <summary>The next step: a picture waits the set seconds; a video plays to its end first (VideoEnded calls SlideNext).</summary>
        void ArmSlide()
        {
            _slideTimer.Stop();
            if (!_sliding || Current == null) return;
            if (Current.Kind != MediaKind.Picture && _settings.SlideVideos) return;
            _slideTimer.Interval = TimeSpan.FromSeconds(Current.Kind == MediaKind.Picture ? _settings.SlideSeconds : 0.1);
            _slideTimer.Start();
        }

        void SlideNext()
        {
            _slideTimer.Stop();
            if (!_sliding) return;
            for (int step = 1; step <= _items.Count; step++)
            {
                int i = _index + step;
                if (i >= _items.Count)
                {
                    if (!_settings.SlideRepeat) { StopSlideshow(); return; }
                    i %= _items.Count;
                }
                if (_items[i].Kind == MediaKind.Picture || _settings.SlideVideos) { ShowAt(i); ArmSlide(); return; }
            }
            StopSlideshow();
        }

        // =================================================================
        // Options, the menu, keys
        // =================================================================

        /// <summary>The settings into the window: the strip, the details, full screen hides the bars.</summary>
        public void ApplyOptions()
        {
            if (_theme != null && _blur != _settings.ThemeOpacity) ApplyTheme();   // Use the theme's opacity changed
            bool full = _fullScreen;
            TopRow.Height = new GridLength(full ? 0 : 44);
            TopBar.Visibility = full ? Visibility.Collapsed : Visibility.Visible;
            UpdateStrip();
            UpdateDetailsCard();
            UpdateMusicButtons();
            // Options > Audio: the playlist beside a song, the classic player's look, the equaliser (where songs play: the next song)
            UpdatePlaylistColumn();
            ClassicLayout();
            ClassicEq();
            ApplyEq();
            LayoutPicture();
            UpdateVideoBar();
            CheckJellyfinServer();
        }

        /// <summary>The details beside the picture (not over Jellyfin's page, nor in full screen).</summary>
        void UpdateDetailsCard()
        {
            bool details = _settings.ShowDetails && !_fullScreen && !_jfShown;
            DetailsCard.Visibility = details ? Visibility.Visible : Visibility.Collapsed;
            DetailsCol.Width = new GridLength(details ? 268 : 0);
            DetailsBtn.Background = details ? (Brush)FindResource("vx.Selected") : Brushes.Transparent;
        }

        /// <summary>The folder's strip: for pictures only - a video, sound or stream gets the whole window.</summary>
        void UpdateStrip()
        {
            bool picture = Current == null || (Current.Kind == MediaKind.Picture && !Current.IsStream);
            Strip.Visibility = _settings.ShowStrip && !_fullScreen && !_jfShown && picture ? Visibility.Visible : Visibility.Collapsed;
        }

        void JellyfinBack_Click(object sender, RoutedEventArgs e) { ShowJellyfin(); }

        void AppMenu_Click(object sender, RoutedEventArgs e)
        {
            ContextMenu menu = BuildMenu(false);
            menu.PlacementTarget = AppMenuBtn;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        /// <summary>
        /// The menu from the icon at the top left, and (onWindow) on a right-click in the window or on a playing video - with play /
        /// pause, the audio track and subtitles too, as VLC's.
        /// </summary>
        ContextMenu BuildMenu(bool onWindow)
        {
            var menu = new ContextMenu();
            MediaItem it = Current;
            bool media = it != null && it.Kind != MediaKind.Picture;
            if (onWindow && media)
            {
                menu.Items.Add(Item(_vPlaying ? "Pause" : "Play", _vPlaying ? "\uE769" : "\uE768", "Space", TogglePlay));
                var audio = Item("Audio track", "\uE8D6", null, null);
                var subs = Item("Subtitles", "\uE7F0", null, null);
                FillTrackMenu(audio, false);
                FillTrackMenu(subs, true);
                menu.Items.Add(audio);
                menu.Items.Add(subs);
                menu.Items.Add(Item(_fullScreen ? "Leave full screen" : "Full screen", _fullScreen ? "\uE73F" : "\uE740", "F11", () => SetFullScreen(!_fullScreen)));
                if (it.Jellyfin != null)
                {
                    menu.Items.Add(Item("Play the next one", "", "Page Down", () => JellyfinNextPrevious(true)));
                    menu.Items.Add(Item("Back to Jellyfin", "", "Esc", ShowJellyfin));
                }
                menu.Items.Add(new Separator());
            }
            menu.Items.Add(Item("Open file…", "\uE8E5", "Ctrl+O", OpenDialog));
            menu.Items.Add(Item("Open folder…", "", "Ctrl+Shift+O", OpenFolderDialog));
            menu.Items.Add(Item("Open network stream…", "\uE774", "Ctrl+N", OpenStreamDialog));
            menu.Items.Add(JellyfinMenu());
            menu.Items.Add(new Separator());
            bool file = it != null && !it.IsStream;
            menu.Items.Add(Item("Show in its folder", "\uE838", null, ShowInFolder, file));
            menu.Items.Add(Item("Copy", "\uE8C8", "Ctrl+C", CopyFile, file));
            menu.Items.Add(Item(it != null && it.IsStream ? "Copy address" : "Copy path", "\uE71B", "Ctrl+Shift+C", CopyPath, it != null && it.Jellyfin == null));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Options", "\uE713", "Ctrl+,", () => ShowOptions(false)));
            if (!onWindow)
            {
                menu.Items.Add(Item("Check for updates", "\uE895", null, () => UpdateCenter.CheckNow(true), !UpdateCenter.Busy && !UpdateCenter.Installing));
                menu.Items.Add(Item("About vView", "\uE946", null, () => ShowOptions(true)));
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Close", "\uE8BB", "Alt+F4", Close));
            return menu;
        }

        /// <summary>The menu's Jellyfin: opens to the list of servers (the one showing ticked), then Options > Jellyfin to add one.</summary>
        MenuItem JellyfinMenu()
        {
            var servers = _settings.JellyfinValid().ToList();
            if (servers.Count == 0) return Item("Jellyfin", "", "Ctrl+J", ShowJellyfin);
            MenuItem jf = Item("Jellyfin", "", null, null);
            JellyfinEntry pick = _jfShown ? servers.FirstOrDefault(e => e.Base == _webServer) : null;
            if (pick == null) pick = _settings.JellyfinPick();
            foreach (JellyfinEntry s in servers)
            {
                JellyfinEntry entry = s;
                jf.Items.Add(Item(s.Label, s == pick ? "" : null, s == pick ? "Ctrl+J" : null, () => ShowJellyfin(entry)));
            }
            jf.Items.Add(new Separator());
            jf.Items.Add(Item("Add or change servers…", "", null, () =>
            {
                ShowOptions(false);
                OptionsWindow.Current?.ShowPageNamed(OptionsWindow.PgJellyfin);
            }));
            return jf;
        }

        void Stage_RightClick(object sender, MouseButtonEventArgs e)
        {
            ContextMenu menu = BuildMenu(true);
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
            e.Handled = true;
        }

        MenuItem Item(string header, string glyph, string gesture, Action click, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, InputGestureText = gesture ?? "", IsEnabled = enabled };
            if (glyph != null) mi.Icon = new TextBlock { Text = glyph, Style = (Style)FindResource("vx.MenuGlyph") };
            if (click != null) mi.Click += (s, e) => click();
            return mi;
        }

        void ShowOptions(bool about)
        {
            // from the classic player: over it (this window is hidden, and what it owns hides with it)
            OptionsWindow.Open(Front, _settings, _themeSource, _theme, _blur,
                               () => { foreach (MainWindow w in Application.Current.Windows.OfType<MainWindow>()) w.ApplyOptions(); }, about);
        }

        void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            ModifierKeys mods = Keyboard.Modifiers;
            bool none = mods == ModifierKeys.None, ctrl = mods == ModifierKeys.Control, shift = mods == ModifierKeys.Shift;
            bool media = Current != null && Current.Kind != MediaKind.Picture;
            bool jellyfin = Current != null && Current.Jellyfin != null;
            // Jellyfin's page has the keys (only the shortcuts it can't use come here) - except vView's own few
            if (_jfShown && !(ctrl && (key == Key.O || key == Key.N || key == Key.J || key == Key.OemComma)) &&
                !(none && (key == Key.F11 || (key == Key.Escape && _fullScreen)))) return;
            if (PlaylistKey(key)) { e.Handled = true; return; }
            // the playlist has the focus: Up / Down move through it (not the volume)
            if (none && (key == Key.Up || key == Key.Down) && FocusedList != null) return;
            bool handled = true;
            if (media && none && (key == Key.PageDown || key == Key.PageUp)) PlayNeighbour(key == Key.PageDown);
            else if (media && none && (key == Key.N || key == Key.P)) PlayNeighbour(key == Key.N);
            // a video or sound: the arrows (and J / L) move through it, as in other players - Page Up / Down still go to the next file
            else if (media && none && (key == Key.Right || key == Key.L)) SeekBy(10);
            else if (media && none && (key == Key.Left || key == Key.J)) SeekBy(-10);
            else if (media && (ctrl || shift) && key == Key.Right) SeekBy(ctrl ? 60 : 10);
            else if (media && (ctrl || shift) && key == Key.Left) SeekBy(ctrl ? -60 : -10);
            else if (media && none && key == Key.K) TogglePlay();
            else if (media && none && key == Key.Home) SeekToFraction(0);
            else if (media && none && key >= Key.D1 && key <= Key.D9) SeekToFraction((key - Key.D0) / 10.0);
            else if (media && none && key >= Key.NumPad1 && key <= Key.NumPad9) SeekToFraction((key - Key.NumPad0) / 10.0);
            else if (none && (key == Key.Right || key == Key.PageDown)) Next();
            else if (none && (key == Key.Left || key == Key.PageUp)) Prev();
            else if (none && key == Key.Home) ShowAt(0);
            else if (none && key == Key.End) ShowAt(_items.Count - 1);
            else if (none && key == Key.Space) { if (media) TogglePlay(); else Next(); }
            else if (none && (key == Key.F11 || key == Key.F)) SetFullScreen(!_fullScreen);
            else if (none && key == Key.Escape)
            {
                if (_sliding) StopSlideshow();
                if (_fullScreen) SetFullScreen(false);
                else if (jellyfin) ShowJellyfin();
            }
            else if (none && key == Key.F5) { if (_sliding) StopSlideshow(); else StartSlideshow(); }
            else if (none && key == Key.Delete) DeleteCurrent();
            else if (ctrl && key == Key.O) OpenDialog();
            else if (mods == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.O) OpenFolderDialog();
            else if (ctrl && key == Key.N) OpenStreamDialog();
            else if (ctrl && key == Key.J) ShowJellyfin();
            else if (ctrl && key == Key.OemComma) ShowOptions(false);
            else if (ctrl && key == Key.C) CopyFile();
            else if (mods == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.C) CopyPath();
            else if (none && key == Key.I) ToggleDetails();
            else if (!media && (none || ctrl) && (key == Key.OemPlus || key == Key.Add)) ZoomBy(1.25, null);
            else if (!media && (none || ctrl) && (key == Key.OemMinus || key == Key.Subtract)) ZoomBy(0.8, null);
            else if (!media && (none || ctrl) && (key == Key.D0 || key == Key.NumPad0)) FitPicture();
            else if (!media && none && (key == Key.D1 || key == Key.NumPad1)) ActualSize();
            else if (!media && none && key == Key.R) Rotate(90);
            else if (!media && shift && key == Key.R) Rotate(-90);
            else if (media && none && key == Key.M) ToggleMute();
            else if (media && none && key == Key.Up) SetVolume(_settings.Volume + 5);
            else if (media && none && key == Key.Down) SetVolume(_settings.Volume - 5);
            else handled = false;
            if (handled) e.Handled = true;
        }
    }

    public static class Format
    {
        /// <summary>A date and time as vView shows it (12-hour).</summary>
        public static string Stamp(DateTime d)
        {
            if (d == DateTime.MinValue) return "";
            return d.ToString("d") + " " + d.ToString("h:mm tt");
        }

        public static string Size(long bytes)
        {
            string[] units = { "bytes", "KB", "MB", "GB", "TB" };
            double v = bytes;
            int u = 0;
            while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
            return (u == 0 ? v.ToString("N0") : v.ToString(v < 10 ? "0.0" : "N0")) + " " + units[u];
        }

        public static string Time(long ms)
        {
            if (ms < 0) ms = 0;
            var t = TimeSpan.FromMilliseconds(ms);
            return t.TotalHours >= 1 ? ((int)t.TotalHours) + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00") : t.Minutes + ":" + t.Seconds.ToString("00");
        }
    }
}
