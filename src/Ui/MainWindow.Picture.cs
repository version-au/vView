using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using vView.Core;

namespace vView.Ui
{
    /// <summary>
    /// Pictures: decoded by Windows' own decoders (WIC) on a worker, turned the way the camera says (EXIF orientation), fitted to the
    /// window or at actual size, zoomed at the mouse, dragged around when bigger than the window, turned with R. The pictures either
    /// side are decoded ahead, so Left / Right are instant.
    /// </summary>
    public partial class MainWindow
    {
        sealed class Decoded
        {
            public BitmapSource Bitmap;
            /// <summary>Degrees the camera says to turn it (EXIF orientation).</summary>
            public int Turn;
            public List<KeyValuePair<string, string>> Props = new List<KeyValuePair<string, string>>();
            public string Error;
            /// <summary>An animated GIF / WebP: every frame, whole (Frames[0] = Bitmap), and how long each shows, in ms. Null for a still.</summary>
            public List<BitmapSource> Frames;
            public List<int> Delays;
        }

        BitmapSource _bmp;
        double _zoom = 1, _panX, _panY;
        bool _fit = true;
        int _baseTurn, _turn;
        int Angle { get { return ((_baseTurn + _turn) % 360 + 360) % 360; } }

        /// <summary>Recently decoded pictures (path -> picture), the newest last; a few are kept for going back and forth.</summary>
        readonly List<KeyValuePair<string, Task<Decoded>>> _cache = new List<KeyValuePair<string, Task<Decoded>>>();
        const int CacheSize = 5;

        Task<Decoded> Decode(string path)
        {
            for (int i = 0; i < _cache.Count; i++)
                if (string.Equals(_cache[i].Key, path, StringComparison.OrdinalIgnoreCase))
                {
                    var hit = _cache[i];
                    _cache.RemoveAt(i);
                    _cache.Add(hit);
                    return hit.Value;
                }
            Task<Decoded> t = Task.Run(() => DecodeNow(path));
            _cache.Add(new KeyValuePair<string, Task<Decoded>>(path, t));
            while (_cache.Count > CacheSize) _cache.RemoveAt(0);
            return t;
        }

        /// <summary>Forget a picture (it's about to be deleted or changed).</summary>
        void ReleasePicture(MediaItem it)
        {
            if (it == null) return;
            _cache.RemoveAll(c => string.Equals(c.Key, it.Path, StringComparison.OrdinalIgnoreCase));
            if (Current == it) { StopAnimation(); Pic.Source = null; _bmp = null; }
        }

        static Decoded DecodeNow(string path)
        {
            var d = new Decoded();
            try
            {
                // OnLoad reads it all now and lets go of the file; ReadWrite / Delete sharing so other apps aren't blocked meanwhile
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    BitmapDecoder dec = BitmapDecoder.Create(fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    BitmapFrame frame = dec.Frames[0];
                    BitmapSource bmp = frame;
                    // a huge picture (a panorama, a scan): decoded smaller, at most 12000 px across
                    if ((long)frame.PixelWidth * frame.PixelHeight > 120000000L)
                    {
                        fs.Position = 0;
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.StreamSource = fs;
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        if (frame.PixelWidth >= frame.PixelHeight) bi.DecodePixelWidth = 12000; else bi.DecodePixelHeight = 12000;
                        bi.EndInit();
                        bmp = bi;
                    }
                    bmp.Freeze();
                    d.Bitmap = bmp;
                    if (dec.Frames.Count > 1) ReadAnimation(dec, path, d);
                    d.Props.Add(new KeyValuePair<string, string>("Dimensions", d.Bitmap.PixelWidth + " × " + d.Bitmap.PixelHeight));
                    if (d.Frames != null) d.Props.Add(new KeyValuePair<string, string>("Animation", d.Frames.Count + " frames · " + (d.Delays.Sum() / 1000.0).ToString("0.#") + " s"));
                    ReadMetadata(frame.Metadata as BitmapMetadata, d);
                }
            }
            catch (Exception ex)
            {
                d.Error = ex.Message;
                Log.Write("Picture " + path + ": " + ex.GetType().Name + ": " + ex.Message);
            }
            return d;
        }

        // ---- animated GIF / WebP ----

        /// <summary>Frames held for one animation at most (all of them decoded, whole): a bigger one shows its first frame only.</summary>
        const long AnimationBytes = 256L * 1024 * 1024;

        /// <summary>
        /// The frames of an animated GIF or WebP, whole and frozen, with their times. A GIF's frames are pieces (a rectangle each, then
        /// kept, cleared or put back - its "disposal") and are put together here, as a browser does; Windows' WebP decoder gives whole
        /// frames already but no times, so those come from the file's own ANMF chunks. Other files with frames (a TIFF's pages, an
        /// icon's sizes) aren't animations.
        /// </summary>
        static void ReadAnimation(BitmapDecoder dec, string path, Decoded d)
        {
            bool gif = dec is GifBitmapDecoder;
            bool webp = !gif && string.Equals(Path.GetExtension(path), ".webp", StringComparison.OrdinalIgnoreCase);
            if (!gif && !webp) return;
            try
            {
                int n = dec.Frames.Count;
                var frames = new List<BitmapSource>(n);
                var delays = new List<int>(n);
                if (webp)
                {
                    int w = dec.Frames[0].PixelWidth, h = dec.Frames[0].PixelHeight;
                    if ((long)w * h * 4 * n > AnimationBytes) { Log.Write("Animation " + path + ": too big to animate (" + n + " frames)"); return; }
                    List<int> times = WebPDurations(path);
                    for (int i = 0; i < n; i++)
                    {
                        BitmapSource f = dec.Frames[i];
                        if (f.PixelWidth != w || f.PixelHeight != h) return;   // not whole frames: leave it a still
                        if (f.CanFreeze && !f.IsFrozen) f.Freeze();
                        frames.Add(f);
                        delays.Add(Delay(times != null && i < times.Count ? times[i] : 100));
                    }
                }
                else
                {
                    var md = dec.Metadata;
                    int w = Int(Q(md, "/logscrdesc/Width"), dec.Frames[0].PixelWidth), h = Int(Q(md, "/logscrdesc/Height"), dec.Frames[0].PixelHeight);
                    if (w <= 0 || h <= 0 || (long)w * h * 4 * n > AnimationBytes) { Log.Write("Animation " + path + ": too big to animate (" + n + " frames)"); return; }
                    int stride = w * 4;
                    var canvas = new byte[stride * h];
                    for (int i = 0; i < n; i++)
                    {
                        BitmapFrame f = dec.Frames[i];
                        var fm = f.Metadata as BitmapMetadata;
                        int fx = Int(Q(fm, "/imgdesc/Left"), 0), fy = Int(Q(fm, "/imgdesc/Top"), 0);
                        int disposal = Int(Q(fm, "/grctlext/Disposal"), 0);
                        byte[] before = disposal == 3 ? (byte[])canvas.Clone() : null;
                        var src = new FormatConvertedBitmap(f, PixelFormats.Bgra32, null, 0);
                        int fw = src.PixelWidth, fh = src.PixelHeight;
                        var px = new byte[fw * 4 * fh];
                        src.CopyPixels(px, fw * 4, 0);
                        for (int y = 0; y < fh; y++)
                        {
                            int ty = fy + y;
                            if (ty < 0 || ty >= h) continue;
                            for (int x = 0; x < fw; x++)
                            {
                                int tx = fx + x;
                                if (tx < 0 || tx >= w) continue;
                                int s = (y * fw + x) * 4;
                                if (px[s + 3] == 0) continue;   // a GIF's see-through pixel: what was there shows
                                Buffer.BlockCopy(px, s, canvas, ty * stride + tx * 4, 4);
                            }
                        }
                        BitmapSource whole = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, canvas, stride);
                        whole.Freeze();
                        frames.Add(whole);
                        delays.Add(Delay(Int(Q(fm, "/grctlext/Delay"), 0) * 10));
                        if (disposal == 2)   // cleared to see-through where this frame was
                            for (int y = Math.Max(0, fy); y < Math.Min(h, fy + fh); y++)
                                Array.Clear(canvas, y * stride + Math.Max(0, fx) * 4, (Math.Min(w, fx + fw) - Math.Max(0, fx)) * 4);
                        else if (before != null) canvas = before;   // put back as it was before this frame
                    }
                }
                d.Bitmap = frames[0];
                d.Frames = frames;
                d.Delays = delays;
            }
            catch (Exception ex) { Log.Write("Animation " + path + ": " + ex.GetType().Name + ": " + ex.Message); }
        }

        /// <summary>Browsers' rule: a frame time of 10 ms or less (often 0, "as fast as you can") plays as 100 ms.</summary>
        static int Delay(int ms) { return ms <= 10 ? 100 : ms; }

        static int Int(object o, int fallback)
        {
            try { return o == null ? fallback : Convert.ToInt32(o); } catch { return fallback; }
        }

        /// <summary>An animated WebP's frame times in ms (its ANMF chunks, in order), or null.</summary>
        static List<int> WebPDurations(string path)
        {
            try
            {
                byte[] b;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    b = new byte[fs.Length];
                    fs.ReadExactly(b, 0, b.Length);
                }
                if (b.Length < 12 || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F' || b[8] != 'W' || b[9] != 'E' || b[10] != 'B' || b[11] != 'P') return null;
                var list = new List<int>();
                int p = 12;
                while (p + 8 <= b.Length)
                {
                    int size = BitConverter.ToInt32(b, p + 4);
                    if (size < 0) break;
                    if (b[p] == 'A' && b[p + 1] == 'N' && b[p + 2] == 'M' && b[p + 3] == 'F' && p + 8 + 15 <= b.Length)
                        list.Add(b[p + 20] | b[p + 21] << 8 | b[p + 22] << 16);   // payload: X, Y, width, height (3 bytes each), then duration
                    p += 8 + size + (size & 1);
                }
                return list.Count > 0 ? list : null;
            }
            catch { return null; }
        }

        List<BitmapSource> _frames;
        List<int> _delays;
        int _frame;
        DispatcherTimer _animTimer;

        void StartAnimation(Decoded d)
        {
            StopAnimation();
            if (d.Frames == null || d.Frames.Count < 2) return;
            _frames = d.Frames;
            _delays = d.Delays;
            _frame = 0;
            if (_animTimer == null)
            {
                _animTimer = new DispatcherTimer(DispatcherPriority.Render);
                _animTimer.Tick += (s, e) =>
                {
                    if (_frames == null) { _animTimer.Stop(); return; }
                    _frame = (_frame + 1) % _frames.Count;
                    Pic.Source = _frames[_frame];
                    _animTimer.Interval = TimeSpan.FromMilliseconds(_delays[_frame]);
                };
            }
            _animTimer.Interval = TimeSpan.FromMilliseconds(_delays[0]);
            _animTimer.Start();
        }

        void StopAnimation()
        {
            _animTimer?.Stop();
            _frames = null;
            _delays = null;
        }

        static object Q(BitmapMetadata md, string query)
        {
            try { return md.ContainsQuery(query) ? md.GetQuery(query) : null; } catch { return null; }
        }

        /// <summary>The camera's orientation, when it was taken, the camera and the exposure.</summary>
        static void ReadMetadata(BitmapMetadata md, Decoded d)
        {
            if (md == null) return;
            try
            {
                object o = Q(md, "System.Photo.Orientation");
                if (o != null)
                    switch (Convert.ToInt32(o))
                    {
                        case 3: case 4: d.Turn = 180; break;
                        case 6: case 5: d.Turn = 90; break;
                        case 8: case 7: d.Turn = 270; break;
                    }
            }
            catch { }
            Action<string, string> add = (k, v) => { if (!string.IsNullOrWhiteSpace(v)) d.Props.Add(new KeyValuePair<string, string>(k, v.Trim())); };
            try
            {
                DateTime taken;
                if (!string.IsNullOrEmpty(md.DateTaken) && DateTime.TryParse(md.DateTaken, out taken)) add("Taken", Format.Stamp(taken));
            }
            catch { }
            try
            {
                string make = md.CameraManufacturer ?? "", model = md.CameraModel ?? "";
                add("Camera", model.StartsWith(make, StringComparison.OrdinalIgnoreCase) || make.Length == 0 ? model : make + " " + model);
            }
            catch { }
            try
            {
                var parts = new List<string>();
                object exp = Q(md, "System.Photo.ExposureTime"), f = Q(md, "System.Photo.FNumber"), iso = Q(md, "System.Photo.ISOSpeed");
                if (exp != null)
                {
                    double e = Convert.ToDouble(exp);
                    parts.Add(e >= 1 ? e.ToString("0.#") + " s" : "1/" + Math.Round(1 / e) + " s");
                }
                if (f != null) parts.Add("f/" + Convert.ToDouble(f).ToString("0.#"));
                if (iso != null) parts.Add("ISO " + iso);
                add("Exposure", string.Join(" · ", parts));
                object fl = Q(md, "System.Photo.FocalLength");
                if (fl != null) add("Lens", Convert.ToDouble(fl).ToString("0.#") + " mm");
            }
            catch { }
        }

        async void ShowPicture(MediaItem it, int v)
        {
            Video.Visibility = Visibility.Collapsed;
            AudioCard.Visibility = Visibility.Collapsed;
            PicCanvas.Visibility = Visibility.Visible;
            StopAnimation();
            Decoded d = await Decode(it.Path);
            if (v != _showVersion) return;
            if (d.Bitmap == null)
            {
                string ext = Path.GetExtension(it.Path).ToLowerInvariant();
                string hint = ext == ".heic" || ext == ".heif" ? " Windows needs the HEIF Image Extensions (Microsoft Store) to read it."
                            : ext == ".avif" ? " Windows needs the AV1 Video Extension (Microsoft Store) to read it."
                            : ".dng.cr2.cr3.nef.nrw.arw.orf.rw2.raf.pef.srw.3fr.erf.kdc.mrw.x3f".Contains(ext) ? " Windows needs the Raw Image Extension (Microsoft Store) to read it."
                            : "";
                Pic.Source = null;
                _bmp = null;
                Empty.Visibility = Visibility.Visible;
                EmptyGlyph.Text = "";
                EmptyText.Text = "vView can't show this picture." + hint;
                return;
            }
            _bmp = d.Bitmap;
            Pic.Source = _bmp;
            _baseTurn = d.Turn;
            _turn = 0;
            _fit = true;
            _panX = _panY = 0;
            LayoutPicture();
            StartAnimation(d);
            AddDetails(it, d.Props);
            if (_sliding) ArmSlide();
        }

        void HidePicture()
        {
            StopAnimation();
            PicCanvas.Visibility = Visibility.Collapsed;
            Pic.Source = null;
            _bmp = null;
        }

        /// <summary>Decode the pictures either side ahead, so moving to them is instant.</summary>
        void PreloadAround()
        {
            if (_items.Count < 2) return;
            foreach (int step in new[] { 1, -1 })
            {
                MediaItem it = _items[((_index + step) % _items.Count + _items.Count) % _items.Count];
                if (it.Kind == MediaKind.Picture && it != Current) Decode(it.Path);
            }
        }

        /// <summary>Place the picture: its size at this zoom, turned, centred and moved by the pan (kept so it doesn't leave the window).</summary>
        void LayoutPicture()
        {
            if (_bmp == null) return;
            double sw = Stage.ActualWidth, sh = Stage.ActualHeight;
            if (sw <= 0 || sh <= 0) return;
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            double w = _bmp.PixelWidth / dpi, h = _bmp.PixelHeight / dpi;   // 100% = one picture pixel per screen pixel
            bool sideways = Angle == 90 || Angle == 270;
            double bw = sideways ? h : w, bh = sideways ? w : h;
            double fit = Math.Min(sw / bw, sh / bh);
            if (_settings.SmallPictures != "Fill") fit = Math.Min(1, fit);
            if (_fit) _zoom = fit;
            Pic.Width = w;
            Pic.Height = h;
            var tg = new TransformGroup();
            tg.Children.Add(new ScaleTransform(_zoom, _zoom));
            tg.Children.Add(new RotateTransform(Angle));
            Pic.RenderTransform = tg;
            double dw = bw * _zoom, dh = bh * _zoom;
            double mx = Math.Max(0, (dw - sw) / 2), my = Math.Max(0, (dh - sh) / 2);
            _panX = Math.Max(-mx, Math.Min(mx, _panX));
            _panY = Math.Max(-my, Math.Min(my, _panY));
            Canvas.SetLeft(Pic, (sw - w) / 2 + _panX);
            Canvas.SetTop(Pic, (sh - h) / 2 + _panY);
            // pixels showing as pixels (or bigger): no smoothing; smaller: high-quality down-scaling
            RenderOptions.SetBitmapScalingMode(Pic, _zoom >= 2 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
            ZoomText.Content = (_fit ? "Fit · " : "") + Math.Round(_zoom * 100) + "%";
            Stage.Cursor = (dw > sw + 1 || dh > sh + 1) ? (_dragging ? Cursors.SizeAll : Cursors.Hand) : null;
        }

        void ZoomBy(double factor, Point? at)
        {
            if (_bmp == null) return;
            double old = _zoom, z = Math.Max(0.02, Math.Min(32, _zoom * factor));
            // the point under the mouse stays put
            Point c = new Point(Stage.ActualWidth / 2, Stage.ActualHeight / 2);
            Point p = at ?? c;
            _panX = (p.X - c.X) - (p.X - c.X - _panX) * z / old;
            _panY = (p.Y - c.Y) - (p.Y - c.Y - _panY) * z / old;
            _zoom = z;
            _fit = false;
            LayoutPicture();
        }

        void FitPicture() { _fit = true; _panX = _panY = 0; LayoutPicture(); }
        void ActualSize() { _fit = false; _zoom = 1; _panX = _panY = 0; LayoutPicture(); }
        void Rotate(int degrees) { if (_bmp == null) return; _turn += degrees; _fit = true; _panX = _panY = 0; LayoutPicture(); }

        void ZoomIn_Click(object sender, RoutedEventArgs e) { ZoomBy(1.25, null); }
        void ZoomOut_Click(object sender, RoutedEventArgs e) { ZoomBy(0.8, null); }
        void ZoomText_Click(object sender, RoutedEventArgs e) { if (_fit) ActualSize(); else FitPicture(); }
        void RotateLeft_Click(object sender, RoutedEventArgs e) { Rotate(-90); }
        void RotateRight_Click(object sender, RoutedEventArgs e) { Rotate(90); }

        void Stage_SizeChanged(object sender, SizeChangedEventArgs e) { LayoutPicture(); }

        void Stage_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Current == null) return;
            if (Current.Kind == MediaKind.Audio)   // a song: the volume, as over a video (the playlist scrolls itself)
            {
                if (!PlaylistList.IsMouseOver) { SetVolume(_settings.Volume + (e.Delta > 0 ? 5 : -5)); e.Handled = true; }
                return;
            }
            bool zoom =Current.Kind == MediaKind.Picture && (_settings.Wheel == "Zoom" || Keyboard.Modifiers == ModifierKeys.Control);
            if (zoom) ZoomBy(e.Delta > 0 ? 1.2 : 1 / 1.2, e.GetPosition(Stage));
            else if (e.Delta < 0) Next(); else Prev();
            e.Handled = true;
        }

        // ---- dragging a picture that's bigger than the window; a double-click goes full screen ----

        bool _dragging;
        Point _dragFrom;
        double _dragPanX, _dragPanY;

        void Stage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) { SetFullScreen(!_fullScreen); e.Handled = true; return; }
            if (_bmp == null || Stage.Cursor == null) return;
            _dragging = true;
            _dragFrom = e.GetPosition(Stage);
            _dragPanX = _panX;
            _dragPanY = _panY;
            Stage.CaptureMouse();
            Stage.Cursor = Cursors.SizeAll;
        }

        void Stage_MouseMove(object sender, MouseEventArgs e)
        {
            ShowSideButtons(true);
            if (!_dragging) return;
            Point p = e.GetPosition(Stage);
            _panX = _dragPanX + (p.X - _dragFrom.X);
            _panY = _dragPanY + (p.Y - _dragFrom.Y);
            LayoutPicture();
        }

        void Stage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            Stage.ReleaseMouseCapture();
            LayoutPicture();
        }

        void Stage_MouseEnter(object sender, MouseEventArgs e) { ShowSideButtons(true); }
        void Stage_MouseLeave(object sender, MouseEventArgs e) { ShowSideButtons(false); }

        /// <summary>The previous / next buttons show while the mouse is over the picture.</summary>
        void ShowSideButtons(bool show)
        {
            double o = show ? 1 : 0;
            if (PrevBtn.Opacity != o) { PrevBtn.Opacity = o; NextBtn.Opacity = o; }
        }
    }
}
