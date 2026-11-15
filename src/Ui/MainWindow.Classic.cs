using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using vView.Core;

namespace vView.Ui
{
    /// <summary>
    /// The classic player: songs in a small window of their own (ClassicPlayer) - the player, the equaliser and the playlist stacked,
    /// or one line on top of everything. This window hides while it shows; the music, the playlist (_items) and the keys stay here.
    /// "Classic" is remembered (MusicView): the next song opens in it too. A picture or a video brings the full window back.
    /// </summary>
    public partial class MainWindow
    {
        ClassicPlayer _classic;
        bool _clSeeking;

        bool ClassicShowing { get { return _classic != null && _classic.IsVisible; } }

        /// <summary>
        /// The window dialogs belong to: the classic player while it shows (this window is hidden then, and a dialog of a hidden window
        /// could open behind the classic player kept on top), else this one.
        /// </summary>
        internal Window Front { get { return ClassicShowing ? (Window)_classic : this; } }

        void Classic_Click(object sender, RoutedEventArgs e) { EnterClassic(true); }

        ClassicPlayer EnsureClassic()
        {
            if (_classic != null) return _classic;
            var c = new ClassicPlayer(this);
            _classic = c;
            c.CloseRequested += () => Close();
            c.TitleDoubleClicked += () => SetClassicShade(!_settings.ClassicShade);
            c.IsVisibleChanged += (s, e) => UpdateSpectrumListening();
            c.PreviewKeyDown += ClassicKey;
            c.Drop += OnDrop;
            c.DragOver += OnDragOver;
            c.LocationChanged += (s, e) => { if (c.IsVisible) { _settings.ClassicLeft = c.Left; _settings.ClassicTop = c.Top; } };
            _barSets.Add(new BarSet(c.Bars, 28, 4, 2, 34, true));   // 166 px: inside the display's 174
            _barSets.Add(new BarSet(c.ShadeBars, 10, 3, 2, 16, false));

            // the title row and the shade
            c.AppMenuBtn.Click += (s, e) =>
            {
                ContextMenu menu = BuildMenu(false);
                menu.PlacementTarget = c.AppMenuBtn;
                menu.Placement = PlacementMode.Bottom;
                menu.IsOpen = true;
            };
            c.OptionsBtn.Click += (s, e) => { ShowOptions(false); OptionsWindow.Current?.ShowPageNamed("Audio"); };
            c.PinBtn.Click +=(s, e) => { _settings.ClassicOnTop = !_settings.ClassicOnTop; _settings.Save(); ClassicLayout(); };
            c.ShadeBtn.Click += (s, e) => SetClassicShade(true);
            c.UnshadeBtn.Click += (s, e) => SetClassicShade(false);
            c.FullBtn.Click += (s, e) => LeaveClassic(true);
            c.MinBtn.Click += (s, e) => c.WindowState = WindowState.Minimized;
            c.CloseBtn.Click += (s, e) => Close();
            c.ShadeCloseBtn.Click += (s, e) => Close();

            // playing
            c.PlayBtn.Click += (s, e) => TogglePlay();
            c.ShadePlayBtn.Click += (s, e) => TogglePlay();
            c.StopBtn.Click += (s, e) => StopSong();
            c.PrevBtn.Click += (s, e) => PlayNeighbour(false);
            c.ShadePrevBtn.Click += (s, e) => PlayNeighbour(false);
            c.NextBtn.Click += (s, e) => PlayNeighbour(true);
            c.ShadeNextBtn.Click += (s, e) => PlayNeighbour(true);
            c.ShuffleBtn.Click += (s, e) => SetShuffle(!_settings.MusicShuffle);
            c.RepeatBtn.Click += (s, e) => SetRepeat(_settings.MusicRepeat == "Off" ? "All" : _settings.MusicRepeat == "All" ? "One" : "Off");
            c.MuteBtn.Click += (s, e) => ToggleMute();
            c.BigTime.MouseLeftButtonUp += (s, e) => { _settings.ClassicTimeLeft = !_settings.ClassicTimeLeft; _settings.Save(); ClassicRefresh(); };
            c.EqChip.Click += (s, e) => { _settings.ClassicShowEq = c.EqChip.IsChecked == true; _settings.Save(); ClassicLayout(); };
            c.ListChip.Click += (s, e) => { _settings.ClassicShowList = c.ListChip.IsChecked == true; _settings.Save(); ClassicLayout(); };
            MouseWheelEventHandler wheel = (s, e) => { SetVolume(_settings.Volume + (e.Delta > 0 ? 5 : -5)); e.Handled = true; };
            c.VolumeBar.MouseWheel += wheel;
            ((UIElement)c.BigTime.Parent).MouseWheel += wheel;
            c.ShadeRow.MouseWheel += wheel;

            // the seek and volume bars: click or drag
            c.SeekBar.MouseLeftButtonDown += (s, e) => { c.SeekBar.CaptureMouse(); _clSeeking = true; ClassicSeekAt(e.GetPosition(c.SeekBar).X, false); e.Handled = true; };
            c.SeekBar.MouseMove += (s, e) => { if (c.SeekBar.IsMouseCaptured) ClassicSeekAt(e.GetPosition(c.SeekBar).X, false); };
            c.SeekBar.MouseLeftButtonUp += (s, e) => { if (!c.SeekBar.IsMouseCaptured) return; c.SeekBar.ReleaseMouseCapture(); _clSeeking = false; ClassicSeekAt(e.GetPosition(c.SeekBar).X, true); };
            c.VolumeBar.MouseLeftButtonDown += (s, e) => { c.VolumeBar.CaptureMouse(); ClassicVolumeAt(e.GetPosition(c.VolumeBar).X); e.Handled = true; };
            c.VolumeBar.MouseMove += (s, e) => { if (c.VolumeBar.IsMouseCaptured) ClassicVolumeAt(e.GetPosition(c.VolumeBar).X); };
            c.VolumeBar.MouseLeftButtonUp += (s, e) => { if (c.VolumeBar.IsMouseCaptured) { c.VolumeBar.ReleaseMouseCapture(); _settings.Save(); } };

            // the equaliser
            c.BuildEq(EqSliderChanged);
            c.EqOnChip.Click += (s, e) => SetEqOn(c.EqOnChip.IsChecked == true);
            c.PresetChip.Click += (s, e) => { c.PresetChip.IsChecked = false; EqPresetMenu(c.PresetChip); };

            // the playlist: the same list as the main window's, with the same mouse, drag and keys
            c.List.ItemsSource = _items;
            c.List.MouseDoubleClick += Playlist_DoubleClick;
            c.List.PreviewMouseLeftButtonDown += Playlist_PreviewMouseDown;
            c.List.PreviewMouseMove += Playlist_PreviewMouseMove;
            c.List.DragOver += Playlist_DragOver;
            c.List.Drop += Playlist_Drop;
            c.List.ContextMenuOpening += Playlist_ContextMenuOpening;
            c.List.Height = _settings.ClassicListHeight;
            c.ListGrip.DragDelta += (s, e) =>
            {
                double max = Math.Max(120, SystemParameters.WorkArea.Height - (c.ActualHeight - c.List.ActualHeight) - 16);
                c.List.Height = Math.Max(90, Math.Min(max, c.List.Height + e.VerticalChange));
            };
            c.ListGrip.DragCompleted += (s, e) => { _settings.ClassicListHeight = Math.Round(c.List.Height); _settings.Save(); };
            c.AddBtn.Click += AddSongs_Click;
            c.AddFolderBtn.Click += (s, e) => AddFolder();
            c.SaveBtn.Click += SavePlaylist_Click;

            if (_theme != null) c.ApplyLook(_theme, _blur);
            ClassicEq();
            return c;
        }

        /// <summary>Songs into the classic player (remembered when asked from its button): this window hides.</summary>
        void EnterClassic(bool remember)
        {
            if (!IsMusic) return;
            if (remember && _settings.MusicView != "Classic") { _settings.MusicView = "Classic"; _settings.Save(); }
            if (_fullScreen) SetFullScreen(false);
            ClassicPlayer c = EnsureClassic();
            PlaceClassic(c);
            ClassicLayout();
            ClassicSong(Current);
            ClassicButtons();
            bool was = c.IsVisible;
            c.Show();
            if (!was) c.Activate();
            Hide();
            ClassicRefresh();
        }

        /// <summary>The full window back (remembered when asked from the classic player's button).</summary>
        void LeaveClassic(bool remember)
        {
            if (remember && _settings.MusicView != "Full") { _settings.MusicView = "Full"; _settings.Save(); }
            if (_classic == null) return;
            bool was = _classic.IsVisible;
            if (!IsVisible) Show();
            if (was) { Activate(); _classic.Hide(); }
            UpdateMusicButtons();
        }

        /// <summary>After ShowAt / ShowEmpty: a song goes to the classic player if that's where songs play; anything else to this window.</summary>
        void SyncClassic()
        {
            if (IsMusic && _settings.MusicView == "Classic") EnterClassic(false);
            else if (ClassicShowing) LeaveClassic(false);
        }

        /// <summary>The window in front: the classic player while it shows, else this one (a file handed over from a second start).</summary>
        void ActivateFront()
        {
            if (ClassicShowing) { if (_classic.WindowState == WindowState.Minimized) _classic.WindowState = WindowState.Normal; _classic.Activate(); }
            else Activate();
        }

        /// <summary>A second start's file arriving at the classic player's window: handled as if it came here.</summary>
        internal IntPtr TakeCopyData(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) { return WndProc(hwnd, msg, wParam, lParam, ref handled); }

        void PlaceClassic(ClassicPlayer c)
        {
            if (c.IsVisible) return;
            Rect work = SystemParameters.WorkArea;
            double l = _settings.ClassicLeft ?? work.Right - c.Width - 24, t = _settings.ClassicTop ?? work.Bottom - 640;
            // somewhere on a screen (a monitor unplugged since)
            var all = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            if (!all.IntersectsWith(new Rect(l, t, c.Width, 60))) { l = work.Right - c.Width - 24; t = work.Top + 80; }
            c.Left = l;
            c.Top = Math.Max(all.Top, t);
        }

        void SetClassicShade(bool shade)
        {
            _settings.ClassicShade = shade;
            _settings.Save();
            ClassicLayout();
        }

        /// <summary>One line or the whole player; its equaliser and list; kept on top (always, as one line).</summary>
        void ClassicLayout()
        {
            ClassicPlayer c = _classic;
            if (c == null) return;
            c.SetShade(_settings.ClassicShade);
            c.ShowPanels(_settings.ClassicShowEq, _settings.ClassicShowList);
            c.Topmost = _settings.ClassicOnTop || _settings.ClassicShade;
            if (Math.Abs(c.List.Height - _settings.ClassicListHeight) > 0.5 && !c.ListGrip.IsDragging) c.List.Height = Math.Max(90, _settings.ClassicListHeight);
            c.PinBtn.Background = _settings.ClassicOnTop ? (Brush)FindResource("vx.Selected") : Brushes.Transparent;
            c.PinBtn.ToolTip = _settings.ClassicOnTop ? "Kept on top - click to let other windows over it" : "Keep on top";
            if (c.List.IsVisible && Current != null) c.List.ScrollIntoView(Current);
        }

        /// <summary>The song: "3. Title - Artist (4:12)" scrolling along, its format, the window's title.</summary>
        void ClassicSong(MediaItem it)
        {
            ClassicPlayer c = _classic;
            if (c == null || it == null || it.Kind != MediaKind.Audio) return;
            string who = it.Artist.Length > 0 ? " - " + it.Artist : "";
            string len = it.LengthText.Length > 0 ? " (" + it.LengthText + ")" : "";
            c.SetMarquee(it.Number + ". " + it.Title + who + len);
            c.ShadeTitle.Text = it.Title + who;
            c.Chips.ItemsSource = it.Info?.Format;
            c.Title = it.Title + who + " - vView";
            c.ListSummary.Text = "  " + PlaylistSummary.Text;
            if (c.List.IsVisible) c.List.ScrollIntoView(it);
        }

        void ClassicButtons()
        {
            ClassicPlayer c = _classic;
            if (c == null) return;
            Brush on = (Brush)FindResource("vx.Selected");
            string r = _settings.MusicRepeat;
            c.RepeatBtn.Content = r == "One" ? "" : "";
            c.RepeatBtn.ToolTip = r == "One" ? "Repeat: this song" : r == "All" ? "Repeat: the whole list" : "Repeat: off";
            c.RepeatBtn.Background = r == "Off" ? Brushes.Transparent : on;
            c.ShuffleBtn.Background = _settings.MusicShuffle ? on : Brushes.Transparent;
            c.ShuffleBtn.ToolTip = _settings.MusicShuffle ? "Shuffle: on" : "Shuffle: off";
        }

        /// <summary>Four times a second (with the bar): the time, where it's up to, playing or not, the volume.</summary>
        void ClassicRefresh()
        {
            ClassicPlayer c = _classic;
            if (c == null || !c.IsVisible) return;
            bool playing = _vPlaying;
            long length = Interlocked.Read(ref _vLength), time = _ended ? length : Interlocked.Read(ref _vTime);
            bool stopped = !playing && (time == 0 || _ended);
            string t = _settings.ClassicTimeLeft && length > 0 ? "-" + Format.Time(Math.Max(0, length - time)) : Format.Time(time);
            c.BigTime.Text = c.ShadeTime.Text = t;
            c.StateGlyph.Text = playing ? "" : stopped ? "" : "";
            c.PlayBtn.Content = c.ShadePlayBtn.Content = playing ? "" : "";
            c.PlayBtn.ToolTip = c.ShadePlayBtn.ToolTip = playing ? "Pause (Space)" : "Play (Space)";
            c.TimeText.Text = Format.Time(time);
            c.LengthText.Text = length > 0 ? Format.Time(length) : "";
            if (!_clSeeking)
            {
                double f = length > 0 ? Math.Max(0, Math.Min(1, (double)time / length)) : 0, w = c.SeekBar.ActualWidth;
                c.SeekFill.Width = w * f;
                c.SeekKnob.Margin = new Thickness(Math.Max(0, w * f - 6.5), 0, 0, 0);
            }
            int vol = _settings.Muted ? 0 : _settings.Volume;
            c.VolumeFill.Width = Math.Max(0, c.VolumeBar.ActualWidth * vol / 100.0);
            c.VolumeText.Text = vol.ToString();
            c.MuteBtn.Content = _settings.Muted || _settings.Volume == 0 ? "" : "";
        }

        void ClassicSeekAt(double x, bool done)
        {
            ClassicPlayer c = _classic;
            double f = Math.Max(0, Math.Min(1, x / Math.Max(1, c.SeekBar.ActualWidth)));
            c.SeekFill.Width = c.SeekBar.ActualWidth * f;
            c.SeekKnob.Margin = new Thickness(Math.Max(0, c.SeekBar.ActualWidth * f - 6.5), 0, 0, 0);
            long length = Interlocked.Read(ref _vLength);
            if (length > 0) c.TimeText.Text = Format.Time((long)(length * f));
            if (done) SeekToFraction(f);
        }

        void ClassicVolumeAt(double x)
        {
            SetVolume((int)Math.Round(Math.Max(0, Math.Min(1, x / Math.Max(1, _classic.VolumeBar.ActualWidth))) * 100));
        }

        /// <summary>Stop: paused at the start (the next play starts it from the beginning).</summary>
        void StopSong()
        {
            if (_mp == null || _playing == null) return;
            if (_vPlaying) PauseVideo(true);
            SeekToFraction(0);
            ClassicRefresh();
        }

        /// <summary>The equaliser's sliders and chips from the settings.</summary>
        void ClassicEq()
        {
            ClassicPlayer c = _classic;
            if (c == null || c.Preamp == null) return;
            c.EqOnChip.IsChecked = _settings.EqOn;
            c.EqOnChip.Content = _settings.EqOn ? "On" : "Off";
            c.PresetText.Text = _settings.EqPreset;
            c.Preamp.Value = _settings.EqPreamp;
            for (int i = 0; i < 10; i++) c.Bands[i].Value = i < _settings.EqBands.Count ? _settings.EqBands[i] : 0;
            double o = _settings.EqOn ? 1 : 0.45;
            c.EqGrid.Opacity = o;
        }

        async void EqPresetMenu(FrameworkElement under)
        {
            var menu = new ContextMenu { PlacementTarget = under, Placement = PlacementMode.Bottom };
            var names = await EqPresets();
            for (int i = 0; i < names.Count; i++)
            {
                int idx = i;
                var mi = new MenuItem { Header = names[i], IsCheckable = true, IsChecked = names[i] == _settings.EqPreset };
                mi.Click += (s, e) => ChooseEqPreset(idx);
                menu.Items.Add(mi);
            }
            if (names.Count == 0) menu.Items.Add(new MenuItem { Header = "VLC hasn't started", IsEnabled = false });
            menu.IsOpen = true;
        }

        /// <summary>Keys in the classic player: the main window's, less those for pictures and the full window.</summary>
        void ClassicKey(object sender, KeyEventArgs e)
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.F11 || key == Key.F || key == Key.F5 || key == Key.I || key == Key.Escape || key == Key.R ||
                key == Key.OemPlus || key == Key.OemMinus || key == Key.Add || key == Key.Subtract) return;
            OnPreviewKeyDown(sender, e);
        }

        string ClassicState()
        {
            ClassicPlayer c = _classic;
            if (c == null) return "none (view=" + _settings.MusicView + ")";
            return "shown=" + c.IsVisible + " main=" + IsVisible + " view=" + _settings.MusicView + " shade=" + c.Shaded + " top=" + c.Topmost +
                   " eq=" + c.EqPanel.Visibility + " list=" + c.ListPanel.Visibility + " size=" + Math.Round(c.ActualWidth) + "x" + Math.Round(c.ActualHeight) +
                   " | marquee=" + c.Marquee.Text + " | time=" + c.BigTime.Text + " state=" + (c.StateGlyph.Text == "" ? "playing" : c.StateGlyph.Text == "" ? "stopped" : "paused") +
                   " | chips=" + string.Join(",", (c.Chips.ItemsSource as System.Collections.Generic.IEnumerable<string>) ?? new string[0]) +
                   " | eqchip=" + c.EqOnChip.Content + " preset=" + c.PresetText.Text + " | " + c.ListSummary.Text.Trim();
        }
    }
}
