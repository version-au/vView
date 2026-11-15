using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using vView.Core;
using Windows.Media;
using Windows.Storage.Streams;

namespace vView.Ui
{
    /// <summary>
    /// Windows' media controls (SystemMediaTransportControls, one per window): what vView plays shows in vShell's Now playing, Windows'
    /// volume pop-up and anything else that reads Windows' media sessions, and the media keys / those buttons work it. Songs, videos and
    /// streams from this PC - not a Jellyfin video: vShell's Jellyfin plugin shows those (with the poster), and two sources would clash.
    /// Kept in step from the video bar's timer (MediaControlsTick, 4 times a second while something plays) and StopVideo.
    /// </summary>
    public partial class MainWindow
    {
        SystemMediaTransportControls _smtc;
        bool _smtcFailed, _smtcShown;
        string _smtcItem = "";
        MediaPlaybackStatus _smtcStatus = MediaPlaybackStatus.Closed;
        long _smtcLength = -1, _smtcPos = -1, _smtcPosAt;
        bool _smtcErrorLogged;

        SystemMediaTransportControls Smtc()
        {
            if (_smtc != null || _smtcFailed) return _smtc;
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
                SystemMediaTransportControls s = SystemMediaTransportControlsInterop.GetForWindow(hwnd);
                s.IsPlayEnabled = s.IsPauseEnabled = s.IsStopEnabled = true;
                s.ButtonPressed += Smtc_ButtonPressed;
                s.PlaybackPositionChangeRequested += Smtc_PositionChangeRequested;
                _smtc = s;
            }
            catch (Exception ex)
            {
                _smtcFailed = true;
                Log.Write("Windows' media controls: " + ex.Message);
            }
            return _smtc;
        }

        /// <summary>What's playing, playing or paused, and where it's up to - told to Windows when it changes.</summary>
        void MediaControlsTick()
        {
            MediaItem it = _playing;
            bool show = it != null && it.Jellyfin == null && (it.Kind == MediaKind.Audio || it.Kind == MediaKind.Video);
            SystemMediaTransportControls s = show ? Smtc() : _smtc;
            if (s == null) return;
            try
            {
                if (!show)
                {
                    if (!_smtcShown) return;
                    _smtcShown = false;
                    _smtcItem = "";
                    _smtcStatus = MediaPlaybackStatus.Closed;
                    _smtcLength = _smtcPos = -1;
                    s.PlaybackStatus = MediaPlaybackStatus.Closed;
                    s.DisplayUpdater.ClearAll();
                    s.DisplayUpdater.Update();
                    s.IsEnabled = false;
                    return;
                }
                if (!_smtcShown) { _smtcShown = true; s.IsEnabled = true; }

                // what it is (again when the song's tags or cover arrive - they load after it starts)
                string item = it.Path + "|" + it.Title + "|" + it.Artist + "|" + (it.Info?.Album ?? "") + "|" +
                              (it.Cover != null ? RuntimeHelpers.GetHashCode(it.Cover) : 0) + "|" + _items.Count;
                if (item != _smtcItem)
                {
                    _smtcItem = item;
                    SystemMediaTransportControlsDisplayUpdater d = s.DisplayUpdater;
                    d.ClearAll();
                    if (it.Kind == MediaKind.Audio)
                    {
                        d.Type = MediaPlaybackType.Music;
                        d.MusicProperties.Title = it.IsStream ? it.Name : it.Title;
                        d.MusicProperties.Artist = it.Artist;
                        d.MusicProperties.AlbumTitle = it.Info?.Album ?? "";
                    }
                    else
                    {
                        d.Type = MediaPlaybackType.Video;
                        d.VideoProperties.Title = it.IsStream ? it.Name : Path.GetFileNameWithoutExtension(it.Path);
                    }
                    RandomAccessStreamReference cover = CoverStream(it.Cover);
                    if (cover != null) d.Thumbnail = cover;
                    d.Update();
                    s.IsNextEnabled = s.IsPreviousEnabled = _items.Count > 1;
                    _smtcPos = -1;   // and where it is, for the new one
                }

                // playing / paused / at its end
                MediaPlaybackStatus st = _vPlaying ? MediaPlaybackStatus.Playing : _ended ? MediaPlaybackStatus.Stopped : MediaPlaybackStatus.Paused;
                bool timeline = false;
                if (st != _smtcStatus) { _smtcStatus = st; s.PlaybackStatus = st; timeline = true; }

                // where it's up to: when the length is known or changes, after a jump (a seek), on play / pause, and every 5 s
                long length = Interlocked.Read(ref _vLength), time = _ended ? length : Interlocked.Read(ref _vTime), now = Environment.TickCount64;
                if (length <= 0) return;
                long expected = _smtcPos < 0 ? -1 : _smtcPos + (st == MediaPlaybackStatus.Playing ? now - _smtcPosAt : 0);
                if (timeline || length != _smtcLength || expected < 0 || Math.Abs(expected - time) > 1500 || now - _smtcPosAt > 5000)
                {
                    _smtcLength = length;
                    _smtcPos = time;
                    _smtcPosAt = now;
                    s.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
                    {
                        StartTime = TimeSpan.Zero,
                        MinSeekTime = TimeSpan.Zero,
                        EndTime = TimeSpan.FromMilliseconds(length),
                        MaxSeekTime = TimeSpan.FromMilliseconds(length),
                        Position = TimeSpan.FromMilliseconds(Math.Max(0, Math.Min(length, time)))
                    });
                }
            }
            catch (Exception ex)
            {
                if (!_smtcErrorLogged) { _smtcErrorLogged = true; Log.Write("Windows' media controls: " + ex.Message); }
            }
        }

        System.Windows.Threading.DispatcherTimer _smtcLater;

        /// <summary>
        /// After a stop: in 1.5 s, if nothing plays by then, Windows (and the taskbar button's progress) hear that nothing does - the next
        /// song usually starts first, so they don't blink between songs.
        /// </summary>
        void MediaControlsLater()
        {
            if (_smtcLater == null)
            {
                _smtcLater = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
                _smtcLater.Tick += (s, e) => { _smtcLater.Stop(); MediaControlsTick(); TaskbarProgress(); };
            }
            _smtcLater.Stop();
            _smtcLater.Start();
        }

        System.Windows.Shell.TaskbarItemProgressState _tbState = System.Windows.Shell.TaskbarItemProgressState.None;
        double _tbFraction = -1;

        /// <summary>
        /// Where it's up to on the taskbar button (this window's and the classic player's): playing / paused, as downloads show theirs -
        /// vShell's taskbar and Windows' both draw it. Everything with a length: songs, videos, Jellyfin; not a live stream or a picture.
        /// </summary>
        void TaskbarProgress()
        {
            MediaItem it = _playing;
            long length = Interlocked.Read(ref _vLength), time = Interlocked.Read(ref _vTime);
            var state = it == null || length <= 0 || _ended ? System.Windows.Shell.TaskbarItemProgressState.None
                      : _vPlaying ? System.Windows.Shell.TaskbarItemProgressState.Normal : System.Windows.Shell.TaskbarItemProgressState.Paused;
            double f = state == System.Windows.Shell.TaskbarItemProgressState.None ? 0 : Math.Max(0, Math.Min(1, (double)time / length));
            if (state == _tbState && Math.Abs(f - _tbFraction) < 0.002) return;   // a pixel or so: no need to tell the taskbar
            _tbState = state;
            _tbFraction = f;
            foreach (Window w in new Window[] { this, _classic })
            {
                if (w == null) continue;
                if (w.TaskbarItemInfo == null) w.TaskbarItemInfo = new System.Windows.Shell.TaskbarItemInfo();
                w.TaskbarItemInfo.ProgressState = state;
                w.TaskbarItemInfo.ProgressValue = f;
            }
        }

        /// <summary>The song's cover for Windows (a PNG in memory), or null.</summary>
        static RandomAccessStreamReference CoverStream(ImageSource cover)
        {
            if (!(cover is BitmapSource bmp)) return null;
            try
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                var ms = new MemoryStream();
                enc.Save(ms);
                ms.Position = 0;
                return RandomAccessStreamReference.CreateFromStream(ms.AsRandomAccessStream());
            }
            catch { return null; }
        }

        // Windows' buttons (the media keys, vShell's Now playing, the volume pop-up) arrive on another thread
        void Smtc_ButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs e)
        {
            SystemMediaTransportControlsButton b = e.Button;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_playing == null || _playing.Jellyfin != null) return;
                switch (b)
                {
                    case SystemMediaTransportControlsButton.Play: if (!_vPlaying) TogglePlay(); break;
                    case SystemMediaTransportControlsButton.Pause: if (_vPlaying) TogglePlay(); break;
                    case SystemMediaTransportControlsButton.Stop: StopSong(); break;
                    case SystemMediaTransportControlsButton.Next: PlayNeighbour(true); break;
                    case SystemMediaTransportControlsButton.Previous: PlayNeighbour(false); break;
                }
                ClassicRefresh();
            }));
        }

        void Smtc_PositionChangeRequested(SystemMediaTransportControls sender, PlaybackPositionChangeRequestedEventArgs e)
        {
            double ms = e.RequestedPlaybackPosition.TotalMilliseconds;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                long length = Interlocked.Read(ref _vLength);
                if (_playing != null && _playing.Jellyfin == null && length > 0) SeekToFraction(Math.Max(0, Math.Min(1, ms / length)));
            }));
        }
    }
}
