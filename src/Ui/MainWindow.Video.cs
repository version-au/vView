using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Shell;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using vView.Core;

namespace vView.Ui
{
    /// <summary>
    /// Video and sound through VLC's engine (LibVLCSharp): it plays practically every format. VLC draws the picture into a window of
    /// its own (the VideoView), so nothing of vView's is drawn over a video - its controls sit in the bar underneath.
    /// - **Never call VLC from the window's thread**: VLC's video window is a child of ours, so VLC's threads sometimes wait for our
    ///   thread - while ours waited for VLC (asking the time, stopping), both stood still (a video stuck on its first frame). Every call
    ///   goes through one worker thread (Vlc), in order; the bar reads what VLC's events reported (time, length, playing).
    /// - VLC's engine starts in the background when vView starts; libvlc\win-x64\plugins\plugins.dat (made at build time, see the
    ///   csproj) makes that take a fraction of a second instead of reading ~280 plugin files.
    /// </summary>
    public partial class MainWindow
    {
        LibVLC _vlc;
        MediaPlayer _mp;
        LibVLCSharp.Shared.Media _media;
        MediaItem _playing;
        string _vlcError;
        bool _ended, _detailsAdded;
        volatile bool _subsOffPending;
        readonly DispatcherTimer _videoTick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        Task<LibVLC> _vlcStarting;

        // what VLC's events said (written on VLC's threads, read by the bar)
        long _vTime, _vLength;
        volatile bool _vPlaying;

        // ---- a stream that lost its connection (a long pause, the PC asleep, the server dropping it) is opened again where it was ----
        /// <summary>When the time last moved (or play was pressed) - Environment.TickCount64.</summary>
        long _vTimeAt;
        /// <summary>When it was paused (0 = not paused by us).</summary>
        long _pausedAt;
        /// <summary>Tries since it last played on properly; where the last one started.</summary>
        int _reopens;
        long _reopenFrom;
        /// <summary>Gave up opening it again: stopped watching until play is pressed (which tries again).</summary>
        bool _streamLost;
        /// <summary>Subtitle files added to what's playing (added again when it's opened again).</summary>
        readonly List<string> _subSlaves = new List<string>();
        /// <summary>VLC's warnings and errors written to the log for this video (capped).</summary>
        int _vlcLines;
        /// <summary>After a pause this long, a stream is opened again instead of carried on (its connection has likely gone).</summary>
        const long LongPauseMs = 2 * 60 * 1000;
        /// <summary>Playing but the time hasn't moved for this long (plus the network caching): opened again.</summary>
        const long StallMs = 15 * 1000;
        const int MaxReopens = 3;

        // ---- the worker every VLC call goes through ----
        readonly BlockingCollection<Action> _vlcQueue = new BlockingCollection<Action>();

        /// <summary>Run a VLC call on the worker, after the ones before it.</summary>
        Task Vlc(Action call)
        {
            var done = new TaskCompletionSource<bool>();
            if (_vlcQueue.IsAddingCompleted) { done.SetResult(false); return done.Task; }
            _vlcQueue.Add(() =>
            {
                try { call(); }
                catch (Exception ex) { Log.Write("VLC call: " + ex.Message); }
                done.TrySetResult(true);
            });
            return done.Task;
        }

        void InitVideo()
        {
            _videoTick.Tick += (s, e) => { UpdateVideoBar(); JellyfinTick(); JellyfinPromptTick(); WatchStream(); MediaControlsTick(); TaskbarProgress(); };
            // the click layer is shown in a window of its own over the video, which LibVLCSharp leaves up when the video is hidden: empty,
            // it lets the mouse through (it took every click on Jellyfin's page after Back, and on a picture after a video)
            Video.IsVisibleChanged += (s, e) => VideoOverlay.Visibility = Video.IsVisible ? Visibility.Visible : Visibility.Collapsed;
            VideoOverlay.Visibility = Visibility.Collapsed;
            var worker = new Thread(() => { foreach (Action a in _vlcQueue.GetConsumingEnumerable()) a(); }) { IsBackground = true, Name = "VLC calls" };
            worker.Start();
            // VLC's engine: started now in the background, so the first video opens as fast as the rest
            bool hw = _settings.HardwareDecoding;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            _vlcStarting = Task.Run(() =>
            {
                LibVLCSharp.Shared.Core.Initialize();   // finds libvlc\win-x64 beside vView.exe
                var vlc = new LibVLC("--avcodec-hw=" + (hw ? "any" : "none"), "--no-video-title-show", "--no-snapshot-preview");
                // VVIEW_VLC_LOG=1: VLC's own messages into vview.log (for working out why something won't play)
                if (Environment.GetEnvironmentVariable("VVIEW_VLC_LOG") == "1")
                    vlc.Log += (s, e) => Log.Write("VLC " + e.Level + " " + e.Module + ": " + e.Message);
                else   // otherwise its errors and network warnings - not the routine ones (VlcLog)
                    vlc.Log += (s, e) =>
                    {
                        if (!VlcLog.WorthLogging(e.Level.ToString(), e.Module, e.Message)) return;
                        if (Interlocked.Increment(ref _vlcLines) <= 40) Log.Write("VLC " + e.Level + " " + e.Module + ": " + e.Message);
                    };
                Log.Write("VLC ready in " + watch.ElapsedMilliseconds + " ms");
                return vlc;
            });
        }

        /// <summary>The player, once VLC's engine has started (waited for without holding up the window).</summary>
        async Task<bool> EnsurePlayer()
        {
            if (_mp != null) return true;
            if (_vlcError != null) return false;
            try
            {
                _vlc = await _vlcStarting;
                if (_mp != null) return true;
                var mp = new MediaPlayer(_vlc) { EnableHardwareDecoding = _settings.HardwareDecoding };
                mp.TimeChanged += (s, e) =>
                {
                    if (Interlocked.Exchange(ref _vTime, e.Time) != e.Time) Interlocked.Exchange(ref _vTimeAt, Environment.TickCount64);
                };
                mp.LengthChanged += (s, e) => Interlocked.Exchange(ref _vLength, e.Length);
                mp.Playing += (s, e) =>
                {
                    _vPlaying = true;
                    // Jellyfin chose no subtitles: off, in case VLC picked one the file marks as its default (not from VLC's own event)
                    if (_subsOffPending) { _subsOffPending = false; Dispatcher.BeginInvoke(new Action(() => Vlc(() => _mp.SetSpu(-1)))); }
                };
                mp.Paused += (s, e) => _vPlaying = false;
                mp.Stopped += (s, e) => _vPlaying = false;
                mp.EndReached += (s, e) => { _vPlaying = false; Dispatcher.BeginInvoke(new Action(VideoEnded)); };
                mp.EncounteredError += (s, e) =>
                {
                    _vPlaying = false;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_playing == null) return;
                        // a stream that had been playing: most likely its connection went - opened again where it was
                        if (_playing.IsStream && Interlocked.Read(ref _vTime) > 0) { ReopenStream("VLC reported an error"); return; }
                        Log.Write("VLC couldn't play " + _playing.Name);
                        Flash(_playing.Jellyfin != null ? "VLC couldn't play this from Jellyfin" : "VLC couldn't play this file");
                        JellyfinError();
                    }));
                };
                _mp = mp;
                ApplyEq();   // the equaliser as it was left
                return true;
            }
            catch (Exception ex)
            {
                _vlcError = ex.Message;
                Log.Write("VLC player: " + ex);
                return false;
            }
        }

        async void PlayMedia(MediaItem it, int v)
        {
            _ = StopVideo();
            if (!await EnsurePlayer())
            {
                if (v != _showVersion) return;
                Empty.Visibility = Visibility.Visible;
                EmptyGlyph.Text = "";
                EmptyText.Text = "vView can't play videos: VLC's engine didn't start (" + _vlcError + ").";
                return;
            }
            if (v != _showVersion) return;   // moved on while VLC was starting
            _playing = it;
            _ended = false;
            _detailsAdded = false;
            Interlocked.Exchange(ref _vTime, 0);
            Interlocked.Exchange(ref _vLength, 0);
            Interlocked.Exchange(ref _vTimeAt, Environment.TickCount64);
            Interlocked.Exchange(ref _vlcLines, 0);
            _pausedAt = 0;
            _reopens = 0;
            _streamLost = false;
            _subSlaves.Clear();
            if (it.Jellyfin != null && it.Jellyfin.SubUrl != null) _subSlaves.Add(it.Jellyfin.SubUrl);
            Video.Visibility = it.Kind == MediaKind.Video ? Visibility.Visible : Visibility.Collapsed;
            AudioCard.Visibility = it.Kind == MediaKind.Audio ? Visibility.Visible : Visibility.Collapsed;
            if (Video.MediaPlayer == null)
            {
                // first video: laid out first, so its host window exists before VLC is given it
                UpdateLayout();
                Video.MediaPlayer = _mp;
            }
            UpdateSpectrumListening();   // asked again now VLC has it (the window showing it may have come first)
            bool playOn = _playNext;   // the next song, after one that was playing
            _playNext = false;
            _videoTick.Start();
            UpdateVideoBar();
            // start once the video area is laid out and its window exists (VLC draws into it)
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (v != _showVersion || _playing != it) return;
                string path = it.Path;
                bool stream = it.IsStream;
                int caching = _settings.NetworkCaching;
                bool paused = !_settings.AutoPlay && !_sliding && !stream && !playOn;
                int volume = _settings.Volume;
                bool muted = _settings.Muted;
                JellyfinPlay jf = it.Jellyfin;
                _subsOffPending = jf != null && jf.SubsOff;
                Vlc(() =>
                {
                    // a stream by its address (VLC buffers network-caching ms first); a file by its path
                    var m = stream ? new LibVLCSharp.Shared.Media(_vlc, path, FromType.FromLocation, ":network-caching=" + caching)
                                   : new LibVLCSharp.Shared.Media(_vlc, new Uri(path));
                    if (paused) m.AddOption(":start-paused");
                    if (jf != null)
                    {
                        // from Jellyfin: where you got to, and the audio and subtitles chosen in Jellyfin
                        if (jf.StartMs > 0) m.AddOption(":start-time=" + (jf.StartMs / 1000.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                        if (jf.AudioTrack >= 0) m.AddOption(":audio-track=" + jf.AudioTrack);
                        if (jf.SubTrack >= 0) m.AddOption(":sub-track=" + jf.SubTrack);
                    }
                    _media = m;
                    _mp.Play(m);
                    _mp.Volume = volume;
                    _mp.Mute = muted;
                    if (jf != null && jf.SubUrl != null) _mp.AddSlave(MediaSlaveType.Subtitle, jf.SubUrl, true);
                });
            }));
        }

        // With Blur / Frosted, the bars beside a video show the glass (VLC paints them see-through) - the theme's look. Tried and dropped:
        // VLC's host window layered (solid black bars, but videos often stuck on their first frame with the click layer over them) and
        // the window going solid while a video shows (the bars then showed the windows behind).
        /// <summary>Stop what's playing and let go of the file (the task ends when VLC has).</summary>
        Task StopVideo()
        {
            _videoTick.Stop();
            _playing = null;
            _vPlaying = false;
            Video.Visibility = Visibility.Collapsed;
            AudioCard.Visibility = Visibility.Collapsed;
            UpdateVideoBar();
            MediaControlsLater();   // Windows' media controls and the taskbar progress: cleared in a moment if nothing else starts
            _jfPrompt?.Clear();     // Skip intro / Up next
            if (_mp == null) return Task.CompletedTask;
            return Vlc(() =>
            {
                if (_media == null) return;
                _mp.Stop();
                _mp.Media = null;
                _media.Dispose();
                _media = null;
            });
        }

        void CloseVideo()
        {
            Task stopped = StopVideo();
            stopped.Wait(3000);
            Vlc(() => { try { _mp?.Dispose(); _vlc?.Dispose(); } catch { } }).Wait(3000);
            _vlcQueue.CompleteAdding();
        }

        void VideoEnded()
        {
            if (_playing == null) return;
            // a stream that "ended" well before its end lost its connection - not watched to the end
            long length = Interlocked.Read(ref _vLength), time = Interlocked.Read(ref _vTime);
            if (_playing.IsStream && length > 0 && time < length - 30000)
            {
                ReopenStream("it stopped at " + Format.Time(time) + " of " + Format.Time(length));
                return;
            }
            if (_playing.IsStream) Log.Write("Stream: played to the end (" + _playing.Name + ")");
            if (JellyfinEnded()) return;
            if (_sliding && _settings.SlideVideos) { SlideNext(); return; }
            // a song: the next one in the list (shuffle and repeat as set); at the end of the list it stops
            if (_playing.Kind == MediaKind.Audio && !_playing.IsStream)
            {
                if (_playing == Current && MusicStep(true, true)) return;
            }
            else if (_settings.Loop) { Restart(); return; }
            _ended = true;
            UpdateVideoBar();
        }

        void Restart()
        {
            if (_mp == null || _playing == null) return;
            _ended = false;
            Vlc(() => { if (_media == null) return; _mp.Stop(); _mp.Play(); });
        }

        void TogglePlay()
        {
            if (_mp == null || _playing == null) return;
            if (_ended) { Restart(); return; }
            PauseVideo(_vPlaying);
        }

        /// <summary>
        /// Pause or carry on (Space, a click on the video, the bar, Jellyfin's page). A stream paused for a long time is opened again where
        /// it was instead: its connection has likely gone (the server or the PC dropped it), and VLC would sit there without playing.
        /// </summary>
        void PauseVideo(bool pause)
        {
            if (_mp == null || _playing == null) return;
            MediaItem it = _playing;
            long now = Environment.TickCount64;
            if (pause)
            {
                _vPlaying = false;   // the button changes now; VLC's event confirms
                _pausedAt = now;
                Vlc(() => _mp.SetPause(true));
                if (it.IsStream) Log.Write("Stream: paused at " + Format.Time(Interlocked.Read(ref _vTime)));
            }
            else
            {
                long paused = _pausedAt == 0 ? 0 : now - _pausedAt;
                _pausedAt = 0;
                _vPlaying = true;
                Interlocked.Exchange(ref _vTimeAt, now);
                if (it.IsStream && (paused >= LongPauseMs || _streamLost))
                {
                    _reopens = 0;
                    bool lost = _streamLost;
                    _streamLost = false;
                    ReopenStream(lost ? "play pressed after the connection was lost" : "play pressed after " + Format.Time(paused) + " paused");
                }
                else
                {
                    Vlc(() => _mp.SetPause(false));
                    if (it.IsStream) Log.Write("Stream: playing again" + (paused > 0 ? " after " + Format.Time(paused) + " paused" : ""));
                }
            }
            UpdateVideoBar();
        }

        /// <summary>Every quarter second: a stream that says it's playing but whose time hasn't moved for a while is opened again.</summary>
        void WatchStream()
        {
            MediaItem it = _playing;
            // not "VLC says it's playing": a stream opened again that never gets going is caught too (streams never start paused)
            if (it == null || !it.IsStream || _pausedAt != 0 || _ended || _streamLost || _mp == null) return;
            long quiet = Environment.TickCount64 - Interlocked.Read(ref _vTimeAt);
            if (quiet > StallMs + _settings.NetworkCaching) ReopenStream("stuck for " + quiet / 1000 + " s");
            else if (_reopens > 0 && Interlocked.Read(ref _vTime) > _reopenFrom + 5000) _reopens = 0;   // playing properly again
        }

        /// <summary>
        /// Open the stream that's playing again, where it was (with the same audio track and subtitles) - in place: the page isn't shown and
        /// Jellyfin isn't told anything new, it just carries on. After a few tries in a row it gives up and says so.
        /// </summary>
        void ReopenStream(string why)
        {
            MediaItem it = _playing;
            if (it == null || !it.IsStream || _mp == null) return;
            long at = Interlocked.Read(ref _vTime), length = Interlocked.Read(ref _vLength);
            if (_streamLost) return;   // already gave up (a late error from the last try)
            if (++_reopens > MaxReopens)
            {
                Log.Write("Stream: " + why + " - gave up after " + MaxReopens + " tries (" + it.Name + ")");
                _streamLost = true;
                _vPlaying = false;
                UpdateVideoBar();
                Flash(it.Jellyfin != null ? "Lost the connection to Jellyfin" : "Lost the connection to the stream");
                JellyfinError();
                return;
            }
            Log.Write("Stream: " + why + " - opening it again at " + Format.Time(at) + " (try " + _reopens + ")");
            _reopenFrom = at;
            _streamLost = false;
            _ended = false;
            _pausedAt = 0;
            _vPlaying = true;
            Interlocked.Exchange(ref _vTimeAt, Environment.TickCount64);
            string path = it.Path;
            int caching = _settings.NetworkCaching, volume = _settings.Volume;
            bool muted = _settings.Muted, seek = length > 0 && at > 0;   // a live stream just starts again
            string[] slaves = _subSlaves.ToArray();
            Vlc(() =>
            {
                int audio = _mp.AudioTrack, spu = _mp.Spu;
                var m = new LibVLCSharp.Shared.Media(_vlc, path, FromType.FromLocation, ":network-caching=" + caching);
                if (seek) m.AddOption(":start-time=" + (at / 1000.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                if (audio >= 0) m.AddOption(":audio-track-id=" + audio);
                if (spu >= 0) m.AddOption(":sub-track-id=" + spu);
                else _subsOffPending = true;
                LibVLCSharp.Shared.Media old = _media;
                _media = m;
                _mp.Play(m);
                old?.Dispose();
                _mp.Volume = volume;
                _mp.Mute = muted;
                foreach (string s in slaves) _mp.AddSlave(MediaSlaveType.Subtitle, s, false);
            });
            UpdateVideoBar();
        }

        /// <summary>A subtitle file for what's playing (kept, so it comes back if the stream is opened again).</summary>
        void AddSubtitleSlave(string uri)
        {
            if (_mp == null || _playing == null || uri == null) return;
            _subSlaves.Add(uri);
            Vlc(() => _mp.AddSlave(MediaSlaveType.Subtitle, uri, true));
        }


        /// <summary>Jump to a point (0 = the start, 0.5 = halfway) - Home and the number keys.</summary>
        void SeekToFraction(double f)
        {
            if (_mp == null || _playing == null) return;
            long length = Interlocked.Read(ref _vLength);
            if (length <= 0) return;
            long to = (long)(length * Math.Max(0, Math.Min(1, f)));
            bool ended = _ended;
            _ended = false;
            Interlocked.Exchange(ref _vTime, to);
            Vlc(() =>
            {
                if (ended) { _mp.Stop(); _mp.Play(); }
                if (_mp.IsSeekable) _mp.Time = to;
            });
            UpdateVideoBar();
        }

        void PlayPause_Click(object sender, RoutedEventArgs e) { TogglePlay(); }
        void BackMedia_Click(object sender, RoutedEventArgs e) { PlayNeighbour(false); }
        void NextMedia_Click(object sender, RoutedEventArgs e) { PlayNeighbour(true); }

        /// <summary>
        /// The bar's next / back (and Page Down / Up): a Jellyfin episode - the page plays the next or previous one; a file - the next or
        /// previous video (or sound) in its folder. A network stream has none (the buttons are hidden).
        /// </summary>
        void PlayNeighbour(bool next)
        {
            MediaItem it = _playing ?? Current;
            if (it == null || it.Kind == MediaKind.Picture) return;
            if (it.Jellyfin != null) { JellyfinNextPrevious(next); return; }
            if (it.IsStream) return;
            if (it.Kind == MediaKind.Audio && it == Current) { MusicStep(next, false); return; }   // the playlist
            string path = it.Path;
            Task.Run(() => vView.Core.Media.Neighbour(path, next)).ContinueWith(t =>
            {
                if (Current == null || Current.Path != path) return;   // moved on meanwhile
                if (t.Result != null) Open(t.Result);
                else Flash(next ? "That's the last one in this folder" : "That's the first one in this folder");
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void SeekBy(int seconds)
        {
            if (_mp == null || _playing == null) return;
            long length = Interlocked.Read(ref _vLength), to = Math.Max(0, Interlocked.Read(ref _vTime) + seconds * 1000L);
            if (length > 0) to = Math.Min(length - 500, to);
            Interlocked.Exchange(ref _vTime, to);
            Vlc(() => { if (_mp.IsSeekable) _mp.Time = to; });
            UpdateVideoBar();
        }

        void Loop_Click(object sender, RoutedEventArgs e)
        {
            // a song: repeat off -> the whole list -> this song -> off
            if (IsMusic) { SetRepeat(_settings.MusicRepeat == "Off" ? "All" : _settings.MusicRepeat == "All" ? "One" : "Off"); return; }
            _settings.Loop = !_settings.Loop;
            _settings.Save();
            ApplyOptions();
        }

        void Mute_Click(object sender, RoutedEventArgs e) { ToggleMute(); }

        void ToggleMute()
        {
            _settings.Muted = !_settings.Muted;
            bool m = _settings.Muted;
            if (_mp != null) Vlc(() => _mp.Mute = m);
            UpdateVideoBar();
        }

        void SetVolume(int v)
        {
            _settings.Volume = Math.Max(0, Math.Min(100, v));
            _settings.Muted = false;
            int vol = _settings.Volume;
            if (_mp != null) Vlc(() => { _mp.Volume = vol; _mp.Mute = false; });
            UpdateVideoBar();
        }

        /// <summary>The bar under a video or sound: the time, where it's up to, the volume. Only what VLC's events reported - no VLC calls.</summary>
        void UpdateVideoBar()
        {
            ClassicRefresh();
            bool show = _playing != null;
            VideoBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            // next / back: Jellyfin's episodes, or the folder's videos - not a network stream
            Visibility nb = show && (_playing.Jellyfin != null || !_playing.IsStream) ? Visibility.Visible : Visibility.Collapsed;
            if (BackMediaBtn.Visibility != nb) BackMediaBtn.Visibility = NextMediaBtn.Visibility = nb;
            VolumeFill.Width = Math.Max(0, VolumeBar.ActualWidth * (_settings.Muted ? 0 : _settings.Volume) / 100.0);
            MuteBtn.Content = _settings.Muted || _settings.Volume == 0 ? "" : "";
            if (!show) return;
            bool playing = _vPlaying;
            PlayBtn.Content = playing ? "" : "";
            PlayBtn.ToolTip = playing ? "Pause (Space)" : "Play (Space)";
            long length = Interlocked.Read(ref _vLength), time = _ended ? length : Interlocked.Read(ref _vTime);
            TimeText.Text = Format.Time(time);
            // a live stream has no length: "Live", and nothing to drag along
            bool live = _playing.IsStream && length <= 0;
            LengthText.Text = live ? "Live" : length > 0 ? Format.Time(length) : "";
            SeekBar.IsEnabled = !live;
            SeekBar.Opacity = live ? 0.35 : 1;
            double f = length > 0 ? Math.Max(0, Math.Min(1, (double)time / length)) : 0;
            if (!_seeking)
            {
                double w = SeekBar.ActualWidth;
                SeekFill.Width = w * f;
                SeekKnob.Margin = new Thickness(Math.Max(0, w * f - 6), 0, 0, 0);
            }
            if (!_detailsAdded && length > 0)
            {
                _detailsAdded = true;
                MediaItem it = _playing;
                Vlc(() =>
                {
                    uint vw = 0, vh = 0;
                    bool sized = it.Kind == MediaKind.Video && _mp.Size(0, ref vw, ref vh) && vw > 0;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var more = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("Length", Format.Time(length)) };
                        if (sized) more.Add(new KeyValuePair<string, string>("Dimensions", vw + " × " + vh));
                        AddDetails(it, more);
                    }));
                });
            }
        }

        // ---- audio tracks and subtitles (the bar's buttons, and the right-click menu) ----

        void AudioTracks_Click(object sender, RoutedEventArgs e) { TrackMenu((FrameworkElement)sender, false); }
        void Subtitles_Click(object sender, RoutedEventArgs e) { TrackMenu((FrameworkElement)sender, true); }

        void TrackMenu(FrameworkElement under, bool subtitles)
        {
            var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = under, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
            FillTrackMenu(menu, subtitles);
            menu.IsOpen = true;
        }

        /// <summary>
        /// The video's audio tracks or subtitles (VLC asked on its worker - never from this thread), the one playing ticked; subtitles also
        /// "Off" and "Add a subtitle file…". A subtitle file with the video's name beside it is picked up by VLC by itself.
        /// </summary>
        void FillTrackMenu(System.Windows.Controls.ItemsControl menu, bool subtitles)
        {
            menu.Items.Clear();
            menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "Looking…", IsEnabled = false });
            if (_mp == null || _playing == null) return;
            Vlc(() =>
            {
                LibVLCSharp.Shared.Structures.TrackDescription[] tracks = subtitles ? _mp.SpuDescription : _mp.AudioTrackDescription;
                int current = subtitles ? _mp.Spu : _mp.AudioTrack;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    menu.Items.Clear();
                    bool any = false;
                    if (subtitles)
                    {
                        var off = new System.Windows.Controls.MenuItem { Header = "Off", IsCheckable = true, IsChecked = current < 0 };
                        off.Click += (s, e) => Vlc(() => _mp.SetSpu(-1));
                        menu.Items.Add(off);
                    }
                    foreach (var t in tracks ?? new LibVLCSharp.Shared.Structures.TrackDescription[0])
                    {
                        if (t.Id < 0) continue;   // VLC's own "Disable"
                        int id = t.Id;
                        any = true;
                        var mi = new System.Windows.Controls.MenuItem { Header = string.IsNullOrWhiteSpace(t.Name) ? "Track " + id : t.Name, IsCheckable = true, IsChecked = id == current };
                        mi.Click += (s, e) => Vlc(() => { if (subtitles) _mp.SetSpu(id); else _mp.SetAudioTrack(id); });
                        menu.Items.Add(mi);
                    }
                    if (!any) menu.Items.Add(new System.Windows.Controls.MenuItem { Header = subtitles ? "No subtitles in this video" : "No sound in this one", IsEnabled = false });
                    if (subtitles && !_playing.IsStream)
                    {
                        menu.Items.Add(new System.Windows.Controls.Separator());
                        var add = new System.Windows.Controls.MenuItem { Header = "Add a subtitle file…" };
                        add.Click += (s, e) => AddSubtitleFile();
                        menu.Items.Add(add);
                    }
                }));
            });
        }

        void AddSubtitleFile()
        {
            if (_playing == null || _mp == null) return;
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Add subtitles to " + _playing.Name,
                Filter = "Subtitles|*.srt;*.ass;*.ssa;*.vtt;*.sub;*.idx;*.smi;*.txt|All files|*.*"
            };
            try { dlg.InitialDirectory = System.IO.Path.GetDirectoryName(_playing.Path); } catch { }
            if (dlg.ShowDialog(this) != true) return;
            AddSubtitleSlave(new Uri(dlg.FileName).AbsoluteUri);
            Flash("Subtitles added");
        }

        // ---- clicks on the video itself (the see-through layer LibVLCSharp keeps over VLC's window) ----

        /// <summary>A click pauses or plays; a double-click goes full screen (its second click undoes the first's pause, as in other players).</summary>
        void VideoOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Activate();   // the keys keep working (the layer is a window of its own)
            TakeKeys();
            if (e.ClickCount <= 2) TogglePlay();
            if (e.ClickCount == 2) SetFullScreen(!_fullScreen);
            e.Handled = true;
        }

        /// <summary>The wheel over a video: volume, as in VLC.</summary>
        void VideoOverlay_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            SetVolume(_settings.Volume + (e.Delta > 0 ? 5 : -5));
            e.Handled = true;
        }

        // ---- the seek and volume bars: click or drag along them ----

        bool _seeking;

        void Bar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var bar = (FrameworkElement)sender;
            bar.CaptureMouse();
            if (bar == SeekBar) _seeking = true;
            BarAt(bar, e.GetPosition(bar).X);
            e.Handled = true;
        }

        void Bar_MouseMove(object sender, MouseEventArgs e)
        {
            var bar = (FrameworkElement)sender;
            if (bar.IsMouseCaptured) BarAt(bar, e.GetPosition(bar).X);
        }

        void Bar_MouseUp(object sender, MouseButtonEventArgs e)
        {
            var bar = (FrameworkElement)sender;
            if (!bar.IsMouseCaptured) return;
            bar.ReleaseMouseCapture();
            if (bar == SeekBar)
            {
                _seeking = false;
                float f = (float)Math.Max(0, Math.Min(1, e.GetPosition(bar).X / Math.Max(1, bar.ActualWidth)));
                if (_mp != null && _playing != null)
                {
                    bool ended = _ended;
                    _ended = false;
                    Interlocked.Exchange(ref _vTime, (long)(Interlocked.Read(ref _vLength) * f));
                    Vlc(() =>
                    {
                        if (ended) { _mp.Stop(); _mp.Play(); }
                        if (_mp.IsSeekable) _mp.Position = f;
                    });
                }
            }
            UpdateVideoBar();
        }

        void BarAt(FrameworkElement bar, double x)
        {
            double f = Math.Max(0, Math.Min(1, x / Math.Max(1, bar.ActualWidth)));
            if (bar == VolumeBar) { SetVolume((int)Math.Round(f * 100)); return; }
            // while dragging: the knob follows and the time shows where it would go; it jumps there on release
            SeekFill.Width = bar.ActualWidth * f;
            SeekKnob.Margin = new Thickness(Math.Max(0, bar.ActualWidth * f - 6), 0, 0, 0);
            long length = Interlocked.Read(ref _vLength);
            if (length > 0) TimeText.Text = Format.Time((long)(length * f));
        }
    }
}
