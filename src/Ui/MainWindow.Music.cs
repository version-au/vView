using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using vView.Core;
using LibVLC = LibVLCSharp.Shared.LibVLC;
using Media = vView.Core.Media;

namespace vView.Ui
{
    /// <summary>
    /// Music: a song shows "now playing" (its cover, title, artist · album · year, format and the visualiser) with the playlist beside it.
    /// The playlist is the window's own list (_items) - opening a song brings its folder's songs, a .m3u its songs, a drop the dropped
    /// ones - so "3 of 11", Page Up / Down and Delete work as for pictures. Songs play on by themselves; shuffle and repeat (off / all /
    /// one) are in the bar. Tags come from VLC (read on a worker, the playing song first), covers from Windows' thumbnails or cover.jpg.
    /// </summary>
    public partial class MainWindow
    {
        bool IsMusic { get { return Current != null && Current.Kind == MediaKind.Audio && !Current.IsStream; } }

        /// <summary>Shuffle: what's been played since the list began (not picked again until all have), and the way back.</summary>
        readonly HashSet<MediaItem> _shufflePlayed = new HashSet<MediaItem>();
        readonly List<MediaItem> _shuffleBack = new List<MediaItem>();
        readonly Random _rnd = new Random();
        /// <summary>The next song starts playing even with Options > Video > Play straight away off (it was playing on).</summary>
        bool _playNext;
        /// <summary>A narrow window: the list button shows the playlist in place of now playing.</summary>
        bool _narrowList;

        void InitMusic()
        {
            PlaylistList.ItemsSource = _items;
            _items.CollectionChanged += (s, e) =>
            {
                for (int i = 0; i < _items.Count; i++) _items[i].Number = i + 1;
                if (e.Action == NotifyCollectionChangedAction.Reset) { _shufflePlayed.Clear(); _shuffleBack.Clear(); }
                if (IsMusic) UpdatePlaylistSummary();
            };
            AudioCard.SizeChanged += (s, e) => UpdatePlaylistColumn();
        }

        /// <summary>A song was opened (ShowAt): now playing, its tags and cover, the playlist and the bar's buttons.</summary>
        void ShowMusic(MediaItem it)
        {
            RenderNow(it);
            LoadInfo(it);
            if (!it.CoverTried)
            {
                it.CoverTried = true;
                int px = (int)Math.Round(340 * VisualTreeHelper.GetDpi(this).DpiScaleX);
                MusicInfo.CoverAsync(it.Path, px).ContinueWith(t =>
                {
                    it.Cover = t.Result;
                    if (Current == it) RenderNow(it);
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }
            PlaylistList.ScrollIntoView(it);
            UpdatePlaylistSummary();
            UpdatePlaylistColumn();
            UpdateMusicButtons();
        }

        void RenderNow(MediaItem it)
        {
            AudioName.Text = it.Title;
            TrackInfo info = it.Info;
            AudioArtist.Text = info == null ? "" : string.Join(" · ", new[] { info.Artist, info.Album, info.Year }.Where(s => !string.IsNullOrEmpty(s)));
            AudioArtist.Visibility = AudioArtist.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            AudioFormat.ItemsSource = info?.Format;
            CoverImg.Source = it.Cover;
            CoverGlyph.Visibility = it.Cover == null ? Visibility.Visible : Visibility.Collapsed;
            if (info != null && info.Title != null) Title = it.Title + (info.Artist != null ? " - " + info.Artist : "") + " - vView";
            ClassicSong(it);
        }

        // ---- tags, read one at a time on a worker: the song asked for first, then the rest of the list ----

        readonly List<MediaItem> _infoQueue = new List<MediaItem>();
        bool _infoRunning;

        async void LoadInfo(MediaItem first)
        {
            if (first != null && first.Info == null) { _infoQueue.Remove(first); _infoQueue.Insert(0, first); first.InfoQueued = true; }
            foreach (MediaItem i in _items)
                if (i.Kind == MediaKind.Audio && !i.IsStream && i.Info == null && !i.InfoQueued) { i.InfoQueued = true; _infoQueue.Add(i); }
            if (_infoRunning) return;
            _infoRunning = true;
            try
            {
                LibVLC vlc;
                try { vlc = await _vlcStarting; } catch { return; }
                while (_infoQueue.Count > 0)
                {
                    MediaItem it = _infoQueue[0];
                    _infoQueue.RemoveAt(0);
                    it.InfoQueued = false;
                    if (it.Info != null || !_items.Contains(it)) continue;
                    string path = it.Path;
                    TrackInfo ti = await Task.Run(() => MusicInfo.Read(vlc, path));
                    it.SetInfo(ti);
                    if (it == Current) RenderNow(it);
                    if (_infoQueue.Count % 10 == 0) UpdatePlaylistSummary();
                }
            }
            finally { _infoRunning = false; }
        }

        void UpdatePlaylistSummary()
        {
            int n = _items.Count;
            long ms = _items.Sum(i => i.Info?.DurationMs ?? 0);
            PlaylistSummary.Text = n + (n == 1 ? " song" : " songs") + (ms > 0 ? " · " + Format.Time(ms) : "");
            if (_classic != null) _classic.ListSummary.Text = "  " + PlaylistSummary.Text;
        }

        // ---- the layout: side by side when there's room, the cover above the words when not; the playlist when wide enough ----

        void NowArea_SizeChanged(object sender, SizeChangedEventArgs e) { LayoutNow(); }

        void LayoutNow()
        {
            double w = NowArea.ActualWidth, h = NowArea.ActualHeight;
            if (w <= 0 || h <= 0) return;
            bool side = w >= 700;
            double cover, textW;
            if (side)
            {
                cover = Math.Max(120, Math.Min(340, Math.Min(h - 48, w * 0.42)));
                textW = Math.Max(160, w - cover - 32 - 40);
            }
            else
            {
                cover = Math.Max(90, Math.Min(260, Math.Min(h * 0.42, w - 48)));
                textW = Math.Max(160, w - 40);
            }
            NowPanel.Orientation = side ? Orientation.Horizontal : Orientation.Vertical;
            CoverBox.Width = CoverBox.Height = cover;
            NowText.Margin = side ? new Thickness(32, 0, 0, 0) : new Thickness(0, 20, 0, 0);
            NowText.MaxWidth = Math.Min(520, textW);
            NowText.HorizontalAlignment = side ? HorizontalAlignment.Left : HorizontalAlignment.Center;
            TextAlignment ta = side ? TextAlignment.Left : TextAlignment.Center;
            AudioName.TextAlignment = AudioArtist.TextAlignment = ta;
            AudioName.FontSize = side ? 28 : 22;
            AudioFormat.HorizontalAlignment = Spectrum.HorizontalAlignment = NowLabel.HorizontalAlignment = side ? HorizontalAlignment.Left : HorizontalAlignment.Center;
            double s = Math.Min(1, NowText.MaxWidth / Math.Max(1, Spectrum.Width));
            Spectrum.LayoutTransform = new ScaleTransform(s, s);
            // short window: the visualiser goes first
            Spectrum.Visibility = (side ? h >= 220 : h >= cover + 240) ? Visibility.Visible : Visibility.Collapsed;
        }

        void UpdatePlaylistColumn()
        {
            double w = AudioCard.ActualWidth;
            bool narrow = w > 0 && w < 680;
            bool list = narrow ? _narrowList : _settings.ShowPlaylist;
            if (narrow && list)
            {
                NowArea.Visibility = Visibility.Collapsed;
                PlaylistCard.Margin = new Thickness(0, 6, 0, 6);
                Grid.SetColumn(PlaylistCard, 0);
                Grid.SetColumnSpan(PlaylistCard, 2);
                PlaylistCol.Width = new GridLength(0);
            }
            else
            {
                NowArea.Visibility = Visibility.Visible;
                PlaylistCard.Margin = new Thickness(8, 6, 0, 6);
                Grid.SetColumn(PlaylistCard, 1);
                Grid.SetColumnSpan(PlaylistCard, 1);
                PlaylistCol.Width = new GridLength(list ? Math.Min(380, Math.Max(280, w * 0.32)) : 0);
            }
            PlaylistCard.Visibility = list ? Visibility.Visible : Visibility.Collapsed;
            PlaylistBtn.Background = list ? (Brush)FindResource("vx.Selected") : Brushes.Transparent;
        }

        void PlaylistToggle_Click(object sender, RoutedEventArgs e)
        {
            if (AudioCard.ActualWidth > 0 && AudioCard.ActualWidth < 680) _narrowList = !_narrowList;
            else { _settings.ShowPlaylist = !_settings.ShowPlaylist; _settings.Save(); }
            UpdatePlaylistColumn();
        }

        /// <summary>The bar under a song: shuffle, repeat (off / all / one) and the list; no audio-track or subtitle buttons.</summary>
        void UpdateMusicButtons()
        {
            bool music = IsMusic || (_playing != null && _playing.Kind == MediaKind.Audio && !_playing.IsStream);
            ShuffleBtn.Visibility = PlaylistBtn.Visibility = music ? Visibility.Visible : Visibility.Collapsed;
            AudioBtn.Visibility = SubsBtn.Visibility = music ? Visibility.Collapsed : Visibility.Visible;
            // a Jellyfin video or another network stream: no file here - no slideshow, folder or Recycle Bin
            bool stream = Current != null && (Current.Jellyfin != null || Current.IsStream);
            SlideBtn.Visibility = music || stream ? Visibility.Collapsed : Visibility.Visible;   // the slideshow is for pictures
            FolderBtn.Visibility = DeleteBtn.Visibility = stream ? Visibility.Collapsed : Visibility.Visible;
            ClassicBtn.Visibility = music ? Visibility.Visible : Visibility.Collapsed;
            Brush on = (Brush)FindResource("vx.Selected");
            if (music)
            {
                string r = _settings.MusicRepeat;
                LoopBtn.Content = r == "One" ? "" : "";
                LoopBtn.ToolTip = r == "One" ? "Repeat: this song" : r == "All" ? "Repeat: the whole list" : "Repeat: off";
                LoopBtn.Background = r == "Off" ? Brushes.Transparent : on;
                ShuffleBtn.Background = _settings.MusicShuffle ? on : Brushes.Transparent;
                ShuffleBtn.ToolTip = _settings.MusicShuffle ? "Shuffle: on" : "Shuffle: off";
            }
            else
            {
                LoopBtn.Content = "";
                LoopBtn.ToolTip = "Play again from the start";
                LoopBtn.Background = _settings.Loop ? on : Brushes.Transparent;
            }
            ClassicButtons();
        }

        void Shuffle_Click(object sender, RoutedEventArgs e) { SetShuffle(!_settings.MusicShuffle); }

        void SetShuffle(bool on)
        {
            _settings.MusicShuffle = on;
            _settings.Save();
            _shufflePlayed.Clear();
            _shuffleBack.Clear();
            UpdateMusicButtons();
            Flash(on ? "Shuffle on" : "Shuffle off");
        }

        void SetRepeat(string r)
        {
            _settings.MusicRepeat = r == "All" || r == "One" ? r : "Off";
            _settings.Save();
            UpdateMusicButtons();
            Flash(r == "One" ? "Repeating this song" : r == "All" ? "Repeating the whole list" : "Repeat off");
        }

        // ---- moving through the list ----

        /// <summary>
        /// The next or previous song: by the list, or at random with shuffle (each once before any again). auto = the song ended: repeat
        /// "One" plays it again, and at the end of the list (repeat off) false - it just stops. Previous more than 3 s in: back to its start.
        /// </summary>
        bool MusicStep(bool next, bool auto)
        {
            MediaItem cur = Current;
            if (cur == null || _items.Count == 0) return false;
            string repeat = _settings.MusicRepeat;
            if (auto && repeat == "One") { Restart(); return true; }
            if (!next)
            {
                if (Interlocked.Read(ref _vTime) > 3000) { SeekToFraction(0); return true; }
                if (_settings.MusicShuffle)
                {
                    while (_shuffleBack.Count > 0)
                    {
                        MediaItem p = _shuffleBack[_shuffleBack.Count - 1];
                        _shuffleBack.RemoveAt(_shuffleBack.Count - 1);
                        int pi = _items.IndexOf(p);
                        if (pi >= 0) { PlaySong(pi); return true; }
                    }
                }
                if (_index > 0) { PlaySong(_index - 1); return true; }
                if (repeat == "All" && _items.Count > 1) { PlaySong(_items.Count - 1); return true; }
                Flash("That's the first one in the list");
                return true;
            }
            int to = -1;
            if (_settings.MusicShuffle)
            {
                _shufflePlayed.Add(cur);
                List<MediaItem> left = _items.Where(i => !_shufflePlayed.Contains(i)).ToList();
                if (left.Count == 0 && repeat == "All")
                {
                    _shufflePlayed.Clear();
                    _shufflePlayed.Add(cur);
                    left = _items.Where(i => i != cur).ToList();
                    if (left.Count == 0) left.Add(cur);
                }
                if (left.Count > 0) to = _items.IndexOf(left[_rnd.Next(left.Count)]);
                if (to >= 0) _shuffleBack.Add(cur);
            }
            else if (_index + 1 < _items.Count) to = _index + 1;
            else if (repeat == "All") to = 0;
            if (to < 0)
            {
                if (auto) return false;
                Flash("That's the last one in the list");
                return true;
            }
            PlaySong(to);
            return true;
        }

        void PlaySong(int index)
        {
            _playNext = true;
            if (index == _index) { Restart(); return; }
            ShowAt(index);
        }

        void Playlist_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ItemAt(e.OriginalSource) == null) return;
            PlaySelected(sender as ListBox);
        }

        /// <summary>The playlist with the keyboard: the main window's, or the classic player's.</summary>
        ListBox FocusedList
        {
            get
            {
                if (PlaylistList.IsKeyboardFocusWithin) return PlaylistList;
                if (_classic != null && _classic.List.IsKeyboardFocusWithin) return _classic.List;
                return null;
            }
        }

        void PlaySelected(ListBox list = null)
        {
            var it = (list ?? FocusedList ?? PlaylistList).SelectedItem as MediaItem;
            int i = it == null ? -1 : _items.IndexOf(it);
            if (i >= 0) PlaySong(i);
        }

        static MediaItem ItemAt(object source)
        {
            return (source as FrameworkElement)?.DataContext as MediaItem ?? (source as FrameworkContentElement)?.DataContext as MediaItem;
        }

        // ---- changing the list: add, remove, move, sort, save, open ----

        /// <summary>The songs at index (the end when -1), without moving off the one playing.</summary>
        void InsertSongs(IEnumerable<string> paths, int index)
        {
            MediaItem cur = Current;
            int at = index < 0 || index > _items.Count ? _items.Count : index;
            int added = 0;
            foreach (string p in paths)
            {
                if (Media.KindOf(p) != MediaKind.Audio) continue;
                _items.Insert(at++, new MediaItem(p));
                added++;
            }
            if (added == 0) { Flash("No songs in that"); return; }
            if (cur != null) _index = _items.IndexOf(cur);
            UpdatePlace();
            LoadInfo(null);
            Flash(added == 1 ? "Added a song" : "Added " + added + " songs");
        }

        void RemoveSongs(IList<MediaItem> songs)
        {
            if (songs.Count == 0) return;
            MediaItem cur = Current;
            int at = _index;
            foreach (MediaItem it in songs) { _items.Remove(it); _shufflePlayed.Remove(it); _shuffleBack.Remove(it); }
            if (_items.Count == 0) { _index = -1; ShowEmpty(null); return; }
            if (cur != null && songs.Contains(cur))
            {
                // the song playing went: the one after it (or the new last one) - playing if it was
                cur.IsCurrent = false;
                _index = -1;
                _playNext = _vPlaying;
                ShowAt(Math.Min(at, _items.Count - 1));
                return;
            }
            _index = cur == null ? -1 : _items.IndexOf(cur);
            UpdatePlace();
        }

        void MoveSong(MediaItem it, int to)
        {
            int from = _items.IndexOf(it);
            if (from < 0) return;
            to = Math.Max(0, Math.Min(_items.Count - 1, to));
            if (from == to) return;
            MediaItem cur = Current;
            _items.Move(from, to);
            if (cur != null) _index = _items.IndexOf(cur);
            PlaylistList.SelectedItem = it;
            UpdatePlace();
        }

        void SortSongs(Func<MediaItem, string> key, string what)
        {
            MediaItem cur = Current;
            List<MediaItem> sorted = _items.OrderBy(i => key(i) ?? "", StringComparer.CurrentCultureIgnoreCase)
                                           .ThenBy(i => i.Info?.Album ?? "").ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                int from = _items.IndexOf(sorted[i]);
                if (from != i) _items.Move(from, i);
            }
            if (cur != null) _index = _items.IndexOf(cur);
            UpdatePlace();
            Flash("Sorted by " + what);
        }

        /// <summary>"3 of 11 · folder" in the top bar, after the list changed.</summary>
        void UpdatePlace()
        {
            MediaItem it = Current;
            if (it == null) return;
            string folder = it.IsStream ? "" : Path.GetFileName(Path.GetDirectoryName(it.Path)?.TrimEnd('\\') ?? "");
            PlaceText.Text = it.Jellyfin != null ? "Jellyfin" : it.IsStream ? "Network stream" : _items.Count > 1 ? (_index + 1) + " of " + _items.Count + " · " + folder : folder;
        }

        void AddSongs_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Add songs to the playlist", Filter = Media.SoundFilter, Multiselect = true };
            try { if (Current != null && !Current.IsStream) dlg.InitialDirectory = Path.GetDirectoryName(Current.Path); } catch { }
            if (dlg.ShowDialog(Front) == true) InsertSongs(dlg.FileNames, -1);
        }

        void AddFolder()
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Add a folder's songs to the playlist" };
            try { if (Current != null && !Current.IsStream) dlg.InitialDirectory = Path.GetDirectoryName(Current.Path); } catch { }
            if (dlg.ShowDialog(Front) == true) InsertSongs(Media.Dropped(new[] { dlg.FolderName }), -1);
        }

        void SavePlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (_items.Count == 0) return;
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save the playlist",
                Filter = "Playlist (.m3u8)|*.m3u8|Playlist for older players (.m3u)|*.m3u",
                FileName = Path.GetFileName(Path.GetDirectoryName(_items[0].Path)?.TrimEnd('\\') ?? "") is string n && n.Length > 0 ? n : "Playlist"
            };
            try { dlg.InitialDirectory = Path.GetDirectoryName(_items[0].Path); } catch { }
            if (dlg.ShowDialog(Front) != true) return;
            SavePlaylist(dlg.FileName);
        }

        void SavePlaylist(string file)
        {
            try { Media.WritePlaylist(file, _items); Flash("Playlist saved"); }
            catch (Exception ex) { Log.Write("Save playlist " + file + ": " + ex.Message); Flash("Couldn't save the playlist"); }
        }

        /// <summary>A .m3u / .m3u8: its songs, from the first.</summary>
        void OpenPlaylist(string path)
        {
            List<string> songs;
            try { songs = Media.ReadPlaylist(path); }
            catch (Exception ex) { Log.Write("Playlist " + path + ": " + ex.Message); ShowEmpty("vView can't read this playlist."); return; }
            if (songs.Count == 0) { ShowEmpty("There are no songs vView can find in this playlist."); return; }
            _settings.LastFolder = Path.GetDirectoryName(path) ?? "";
            SetList(songs, 0);
        }

        /// <summary>Go through just these files (dropped pictures, a playlist's songs), from index.</summary>
        void SetList(IEnumerable<string> paths, int index)
        {
            LeaveJellyfinVideo();
            _items.Clear();
            foreach (string f in paths) _items.Add(new MediaItem(f));
            _index = -1;
            ShowAt(index);
        }

        // ---- the list's right-click menu ----

        void Playlist_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            ListBox list = sender as ListBox ?? PlaylistList;
            ContextMenu menu = list.ContextMenu;
            menu.Items.Clear();
            List<MediaItem> sel = list.SelectedItems.Cast<MediaItem>().ToList();
            Action<string, Action, bool> add = (text, act, enabled) =>
            {
                var mi = new MenuItem { Header = text, IsEnabled = enabled };
                mi.Click += (s, a) => act();
                menu.Items.Add(mi);
            };
            add("Play", () => PlaySelected(list), sel.Count == 1);
            add(sel.Count > 1 ? "Remove " + sel.Count + " songs from the list" : "Remove from the list", () => RemoveSongs(sel), sel.Count > 0);
            add("Add songs…", () => AddSongs_Click(null, null), true);
            add("Add a folder…", AddFolder, true);
            menu.Items.Add(new Separator());
            add("Sort by title", () => SortSongs(i => i.Title, "title"), _items.Count > 1);
            add("Sort by artist", () => SortSongs(i => i.Artist, "artist"), _items.Count > 1);
            add("Sort by album", () => SortSongs(i => i.Info?.Album, "album"), _items.Count > 1);
            add("Sort by file name", () => SortSongs(i => i.Name, "file name"), _items.Count > 1);
            menu.Items.Add(new Separator());
            add("Save the list…", () => SavePlaylist_Click(null, null), _items.Count > 0);
        }

        // ---- dragging songs up and down the list, and files onto it ----

        Point _dragFromPt;
        MediaItem _dragSong;
        ListBox _dragList;
        const string TrackFormat = "vView.Track";

        void Playlist_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _dragList = sender as ListBox ?? PlaylistList;
            _dragFromPt = e.GetPosition(_dragList);
            _dragSong = ItemAt(e.OriginalSource);
        }

        void Playlist_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragSong == null || _dragList == null || e.LeftButton != MouseButtonState.Pressed) return;
            Vector d = e.GetPosition(_dragList) - _dragFromPt;
            if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            MediaItem it = _dragSong;
            _dragSong = null;
            DragDrop.DoDragDrop(_dragList, new DataObject(TrackFormat, it), DragDropEffects.Move);
        }

        void Playlist_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(TrackFormat) ? DragDropEffects.Move : DroppedFiles(e.Data) != null ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        void Playlist_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            int index = DropIndex(e, sender as ListBox ?? PlaylistList);
            if (e.Data.GetDataPresent(TrackFormat))
            {
                var it = e.Data.GetData(TrackFormat) as MediaItem;
                if (it == null) return;
                int from = _items.IndexOf(it);
                if (from < 0) return;
                if (index < 0) index = _items.Count;
                MoveSong(it, from < index ? index - 1 : index);
                return;
            }
            string[] files = DroppedFiles(e.Data);
            if (files != null) { Activate(); InsertSongs(Media.Dropped(files), index); }
        }

        /// <summary>Where a drop on the list goes: before the song under the mouse (after it on its lower half); -1 = the end.</summary>
        int DropIndex(DragEventArgs e, ListBox list)
        {
            var row = ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (row == null || !(row.DataContext is MediaItem it)) return -1;
            int i = _items.IndexOf(it);
            if (i < 0) return -1;
            return e.GetPosition(row).Y > row.ActualHeight / 2 ? i + 1 : i;
        }

        /// <summary>Keys while the playlist has the focus: Delete takes the songs off the list (not the Recycle Bin), Enter plays.</summary>
        bool PlaylistKey(Key key)
        {
            ListBox list = FocusedList;
            if (list == null || Keyboard.Modifiers != ModifierKeys.None) return false;
            if (key == Key.Delete) { RemoveSongs(list.SelectedItems.Cast<MediaItem>().ToList()); return true; }
            if (key == Key.Enter) { PlaySelected(list); return true; }
            return false;
        }

        string MusicState()
        {
            MediaItem it = Current;
            return "title=" + AudioName.Text + " | artist=" + AudioArtist.Text + " | format=" + string.Join(",", (AudioFormat.ItemsSource as IEnumerable<string>) ?? new string[0]) +
                   " | cover=" + (CoverImg.Source == null ? "none" : "yes") + " | repeat=" + _settings.MusicRepeat + " shuffle=" + _settings.MusicShuffle +
                   " | list=" + PlaylistCard.Visibility + " col=" + Math.Round(PlaylistCol.Width.Value) + " | " + PlaylistSummary.Text +
                   " | layout=" + NowPanel.Orientation + " cover=" + Math.Round(CoverBox.Width) + " | buttons shuffle=" + ShuffleBtn.Visibility +
                   " | order=" + string.Join(",", _items.Select(i => i.Number + ":" + Path.GetFileNameWithoutExtension(i.Path)));
        }
    }
}
