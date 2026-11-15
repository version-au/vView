using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using vView.Core;

namespace vView.Ui
{
    /// <summary>
    /// Tests only: with VVIEW_TEST_HOOKS=1 a test drives vView by WM_COPYDATA (kind TestKind, UTF-16 text) without it being in front.
    /// </summary>
    static class TestHooks
    {
        public const int TestKind = 0x76560002;
        public static readonly bool On = Environment.GetEnvironmentVariable("VVIEW_TEST_HOOKS") == "1";

        [StructLayout(LayoutKind.Sequential)]
        struct COPYDATASTRUCT { public IntPtr dwData; public int cbData; public IntPtr lpData; }

        public static string Received(IntPtr lParam)
        {
            if (!On || lParam == IntPtr.Zero) return null;
            var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
            if (cds.dwData.ToInt64() != TestKind || cds.lpData == IntPtr.Zero) return null;
            return Marshal.PtrToStringUni(cds.lpData, Math.Max(0, cds.cbData / 2)).TrimEnd('\0');
        }
    }

    public partial class MainWindow
    {
        /// <summary>open &lt;file&gt; | next | prev | zoom &lt;factor&gt; | fit | actual | rotate | play | seek &lt;seconds&gt; | details | full |
        /// slideshow | options [page] | dump &lt;file&gt;</summary>
        void RunTestCommand(string command)
        {
            string verb = command.Split(' ')[0].ToLowerInvariant();
            string arg = command.Length > verb.Length ? command.Substring(verb.Length + 1) : "";
            try
            {
                switch (verb)
                {
                    case "open": Open(arg); break;
                    case "stream": OpenStream(arg); break;
                    case "jellyfin":   // the page (or Esc's Back to Jellyfin while one of its videos plays); jellyfin <n>: the nth server (0 = first)
                        if (arg.Length > 0) ShowJellyfin(_settings.JellyfinValid().ElementAtOrDefault(int.Parse(arg))); else ShowJellyfin();
                        break;
                    case "jfsignout": ForgetJellyfin(_settings.JellyfinValid().ElementAtOrDefault(int.Parse(arg))?.Base); break;   // jfsignout <n>
                    case "jfnext": JellyfinNextPrevious(true); break;
                    // Up next / Skip intro without a server: jffake <file> plays a file as if from Jellyfin; jfupnext title|series|image;
                    // jfsegments <introStart> <introEnd> <outroStart> (ms, -1 = none); jfprompt <intro|next|cancel> clicks the prompt's button
                    case "jffake":
                    {
                        var jp = new JellyfinPlay { Url = new Uri(arg).AbsoluteUri, Title = Path.GetFileNameWithoutExtension(arg), ItemId = "fake" };
                        if (Current != null) Current.IsCurrent = false;
                        _items.Clear();
                        _items.Add(MediaItem.FromJellyfin(jp));
                        _index = -1;
                        ShowAt(0);
                        break;
                    }
                    case "jfupnext":
                        if (_playing?.Jellyfin != null) { var p = arg.Split('|'); _playing.Jellyfin.NextTitle = p[0]; _playing.Jellyfin.NextSeries = p.Length > 1 ? p[1] : ""; _playing.Jellyfin.NextImage = p.Length > 2 ? p[2] : null; }
                        break;
                    case "jfsegments":
                        if (_playing?.Jellyfin != null) { var p = arg.Split(' '); _playing.Jellyfin.IntroStart = long.Parse(p[0]); _playing.Jellyfin.IntroEnd = long.Parse(p[1]); _playing.Jellyfin.OutroStart = long.Parse(p[2]); }
                        break;
                    case "jfprompt": if (arg == "intro") SkipJellyfinIntro(); else if (arg == "next") JellyfinPlayNext(); else CancelUpNext(); break;
                    case "jfjs": _web?.CoreWebView2?.ExecuteScriptAsync(arg); break;   // run script in the page (tests drive the fake server's page)
                    case "updatecheck": UpdateCenter.CheckNow(true); break;
                    case "regtypes": if (Environment.GetEnvironmentVariable("VVIEW_TEST_REGROOT") != null) DefaultApp.Register(); break;
                    case "unregtypes": if (Environment.GetEnvironmentVariable("VVIEW_TEST_REGROOT") != null) DefaultApp.Unregister(); break;
                    case "updateinstall": UpdateCenter.Install(UpdateCenter.Available); break;
                    case "tracks":   // tracks <file>: the audio tracks and subtitles VLC reports, and the ones playing
                        if (_mp != null)
                        {
                            string file = arg;
                            Vlc(() =>
                            {
                                var sb = new StringBuilder();
                                sb.AppendLine("audio (" + _mp.AudioTrack + "): " + string.Join(" | ", (_mp.AudioTrackDescription ?? new LibVLCSharp.Shared.Structures.TrackDescription[0]).Select(t => t.Id + "=" + t.Name)));
                                sb.AppendLine("subs (" + _mp.Spu + "): " + string.Join(" | ", (_mp.SpuDescription ?? new LibVLCSharp.Shared.Structures.TrackDescription[0]).Select(t => t.Id + "=" + t.Name)));
                                File.WriteAllText(file, sb.ToString());
                            });
                        }
                        break;
                    case "sub": { int id = int.Parse(arg); Vlc(() => _mp.SetSpu(id)); break; }
                    case "drop":   // drop <path>|<path>…: as if dropped from Explorer
                    {
                        var data = new DataObject();
                        var list = new System.Collections.Specialized.StringCollection();
                        list.AddRange(arg.Split('|'));
                        data.SetFileDropList(list);
                        DropData(data);
                        break;
                    }
                    case "frames":   // frames <folder>: the animation's frames as PNGs, as vView shows them
                        if (_frames != null)
                            for (int i = 0; i < _frames.Count; i++)
                                using (var fs = File.Create(Path.Combine(arg, "frame" + i.ToString("00") + ".png")))
                                {
                                    var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                                    enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(_frames[i]));
                                    enc.Save(fs);
                                }
                        break;
                    // music: repeat <Off|All|One> | shuffle | ended (as if the song ended) | plremove <n> | plmove <n> <to> | pladd <path>|… |
                    // plsave <file> | plsort <title|artist|album|name> | plwidth (the playlist toggled) | plplay <n> (n from 1)
                    case "repeat": SetRepeat(arg); break;
                    case "shuffle": SetShuffle(!_settings.MusicShuffle); break;
                    case "ended": VideoEnded(); break;
                    case "plremove": RemoveSongs(new[] { _items[int.Parse(arg) - 1] }); break;
                    case "plmove": { var p = arg.Split(' '); MoveSong(_items[int.Parse(p[0]) - 1], int.Parse(p[1]) - 1); break; }
                    case "pladd": InsertSongs(arg.Split('|'), -1); break;
                    case "plsave": SavePlaylist(arg); break;
                    case "plsort": SortSongs(arg == "artist" ? (Func<MediaItem, string>)(i => i.Artist) : arg == "album" ? i => i.Info?.Album : arg == "name" ? i => i.Name : i => i.Title, arg); break;
                    case "pltoggle": PlaylistToggle_Click(null, null); break;
                    case "plplay": PlaySong(int.Parse(arg) - 1); break;
                    // the classic player and the equaliser: classic | classicoff | shade | unshade | cleq | cllist | clpin | stop |
                    // eqon | eqoff | eqpreset <n> | eqband <band (-1 = preamp)> <dB> | openfolder <path>
                    case "classic": EnterClassic(true); break;
                    case "classicoff": LeaveClassic(true); break;
                    case "shade": SetClassicShade(true); break;
                    case "unshade": SetClassicShade(false); break;
                    case "cleq": _settings.ClassicShowEq = !_settings.ClassicShowEq; ClassicLayout(); break;
                    case "cllist": _settings.ClassicShowList = !_settings.ClassicShowList; ClassicLayout(); break;
                    case "clpin": _settings.ClassicOnTop = !_settings.ClassicOnTop; ClassicLayout(); break;
                    case "stop": StopSong(); break;
                    case "eqon": SetEqOn(true); break;
                    case "eqoff": SetEqOn(false); break;
                    case "eqpreset": ChooseEqPreset(int.Parse(arg)); break;
                    case "eqband": { var p = arg.Split(' '); EqSliderChanged(int.Parse(p[0]), double.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture), true); break; }
                    case "openfolder": OpenFolder(arg); break;
                    case "droptext": { var data = new DataObject(); data.SetText(arg); DropData(data); break; }   // a link dragged from a browser
                    case "next": Next(); break;
                    case "prev": Prev(); break;
                    case "zoom": ZoomBy(double.Parse(arg, System.Globalization.CultureInfo.InvariantCulture), null); break;
                    case "fit": FitPicture(); break;
                    case "actual": ActualSize(); break;
                    case "rotate": Rotate(90); break;
                    case "play": TogglePlay(); break;
                    case "click": VideoOverlay_MouseLeftButtonDown(VideoOverlay, new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent }); break;
                    case "pausedfor": if (_pausedAt != 0) _pausedAt -= long.Parse(arg) * 1000; break;   // pausedfor <s>: as if paused that long
                    case "stall": System.Threading.Interlocked.Exchange(ref _vTimeAt, Environment.TickCount64 - 120000); break;   // as if stuck
                    case "mnext": PlayNeighbour(true); break;   // the bar's next / back
                    case "mback": PlayNeighbour(false); break;
                    case "seekto": SeekToFraction(double.Parse(arg, System.Globalization.CultureInfo.InvariantCulture)); break;
                    case "seek": SeekBy(int.Parse(arg)); break;
                    case "details": ToggleDetails(); break;
                    case "full": SetFullScreen(!_fullScreen); break;
                    case "slideshow": if (_sliding) StopSlideshow(); else StartSlideshow(); break;
                    case "options":
                        ShowOptions(false);
                        if (arg.Length > 0 && OptionsWindow.Current != null) OptionsWindow.Current.ShowPageNamed(arg);
                        break;
                    case "dump":
                    {
                        var sb = new StringBuilder();
                        MediaItem it = Current;
                        sb.AppendLine("current: " + (it == null ? "-" : it.Name + " (" + it.Kind + ")") + "  " + (_index + 1) + " of " + _items.Count);
                        sb.AppendLine("title: " + NameText.Text + " | " + PlaceText.Text);
                        sb.AppendLine("picture: " + (_bmp == null ? "-" : _bmp.PixelWidth + "x" + _bmp.PixelHeight) + " zoom=" + Math.Round(_zoom, 3) + " fit=" + _fit + " angle=" + Angle + " pan=" + Math.Round(_panX) + "," + Math.Round(_panY));
                        sb.AppendLine("video: " + (_mp == null ? "-" : "playing=" + _vPlaying + " time=" + _vTime + " length=" + _vLength + " ended=" + _ended + " paused=" + (_pausedAt != 0) + " reopens=" + _reopens) + " bar=" + VideoBar.Visibility);
                        sb.AppendLine("animation: " + (_frames == null ? "-" : _frames.Count + " frames, now " + _frame + ", running=" + (_animTimer?.IsEnabled == true) + " delays=" + string.Join(",", _delays) + " shown=" + (Pic.Source == _frames[_frame])));
                        sb.AppendLine("spectrum: " + SpectrumState());
                        sb.AppendLine("music: " + MusicState());
                        sb.AppendLine("classic: " + ClassicState());
                        sb.AppendLine("prompt: " + PromptState());
                        sb.AppendLine("eq: " + EqState());
                        sb.AppendLine("bar: slide=" + SlideBtn.Visibility + " classicBtn=" + ClassicBtn.Visibility + " shuffle=" + ShuffleBtn.Visibility + " audio=" + AudioBtn.Visibility);
                        sb.AppendLine("items: " + string.Join(" | ", _items.Select(i => i.Name)));
                        sb.AppendLine("empty: " + Empty.Visibility + " " + EmptyText.Text);
                        sb.AppendLine("details: " + string.Join(" | ", _details.Select(p => p.Key + "=" + p.Value)));
                        sb.AppendLine("thumbs: " + _items.Count(i => i.Thumb != null) + " of " + _items.Count);
                        sb.AppendLine("update: available=" + (UpdateCenter.Available == null ? "-" : UpdateCenter.VersionText(UpdateCenter.Available)) + " busy=" + UpdateCenter.Busy + " installing=" + UpdateCenter.Installing + " error=" + UpdateCenter.LastError + UpdateCenter.InstallError + " cant=" + (Updater.CantUpdateReason ?? "-"));
                        sb.AppendLine("jellyfin: shown=" + _jfShown + " server=" + (_webServer ?? "-") + " page=" + (_web?.CoreWebView2?.Source ?? "-") +
                                      " playing=" + (it?.Jellyfin == null ? "-" : it.Jellyfin.Title + " done=" + it.Jellyfin.Done) + " back=" + JellyfinBtn.Visibility);
                        sb.AppendLine("full=" + _fullScreen + " sliding=" + _sliding + " strip=" + Strip.Visibility + " detailsCard=" + DetailsCard.Visibility);
                        File.WriteAllText(arg, sb.ToString());
                        break;
                    }
                }
            }
            catch (Exception ex) { Log.Write("Test command '" + command + "': " + ex); }
        }
    }
}
