using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;

namespace vView.Ui
{
    /// <summary>
    /// The classic player (ClassicPlayer.xaml): the window, its look, the scrolling song, one line (the shade) and the equaliser's
    /// sliders. Everything it does goes through the main window (MainWindow.Classic.cs), which plays the music and holds the playlist.
    /// </summary>
    public partial class ClassicPlayer : Window
    {
        readonly MainWindow _main;
        WindowIcons _icons;
        /// <summary>Closing for real (the main window is closing); otherwise closing this window closes vView.</summary>
        internal bool AllowClose;
        internal event Action CloseRequested;
        internal event Action TitleDoubleClicked;

        internal ClassicPlayer(MainWindow main)
        {
            _main = main;
            InitializeComponent();
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 36, ResizeBorderThickness = new Thickness(0), CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false
            });
            SourceInitialized += (s, e) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle).AddHook(WndProc);
            // Alt+F4 / the taskbar's Close: vView closes - once this window's own closing is over (the main window closes this one)
            Closing += (s, e) =>
            {
                if (AllowClose) return;
                e.Cancel = true;
                Dispatcher.BeginInvoke(new Action(() => CloseRequested?.Invoke()));
            };
            _marqueeTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(30) };
            _marqueeTimer.Tick += (s, e) => StepMarquee();
            MarqueeBox.SizeChanged += (s, e) => ResetMarquee();
            IsVisibleChanged += (s, e) => ResetMarquee();
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_NCLBUTTONDBLCLK = 0xA3, WM_COPYDATA = 0x4A;
            if (msg == WM_NCLBUTTONDBLCLK) { handled = true; TitleDoubleClicked?.Invoke(); }
            // a second vView start handing over its file (this window carries vView's mark too): the main window takes it
            else if (msg == WM_COPYDATA) return _main.TakeCopyData(hwnd, msg, wParam, lParam, ref handled);
            return IntPtr.Zero;
        }

        internal void ApplyLook(Theme t, bool blur)
        {
            if (t == null) return;
            WindowEffects.Look(this, t, blur);
            if (_icons == null) _icons = new WindowIcons(this);
            _icons.Apply(t);
            try { AppMark.Source = ShadeMark.Source = AppIcon.Image(t, true); } catch { }
        }

        // ---- one line ----

        internal bool Shaded { get; private set; }

        internal void SetShade(bool shade)
        {
            Shaded = shade;
            TitleRow.Visibility = PlayerPanel.Visibility = shade ? Visibility.Collapsed : Visibility.Visible;
            ShadeRow.Visibility = shade ? Visibility.Visible : Visibility.Collapsed;
            WindowChrome.GetWindowChrome(this).CaptionHeight = shade ? 40 : 36;
            ResetMarquee();
        }

        internal void ShowPanels(bool eq, bool list)
        {
            EqPanel.Visibility = eq && !Shaded ? Visibility.Visible : Visibility.Collapsed;
            ListPanel.Visibility = list && !Shaded ? Visibility.Visible : Visibility.Collapsed;
            EqChip.IsChecked = eq;
            ListChip.IsChecked = list;
        }

        // ---- the song, scrolling along when it's longer than its box ----

        readonly DispatcherTimer _marqueeTimer;
        double _marqueeX;
        int _marqueeWait;

        internal void SetMarquee(string text)
        {
            if (Marquee.Text == text) return;
            Marquee.Text = text;
            ResetMarquee();
        }

        void ResetMarquee()
        {
            _marqueeX = 0;
            _marqueeWait = 60;   // stands still ~2 s first
            Canvas.SetLeft(Marquee, 0);
            Marquee.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            bool scroll = IsVisible && !Shaded && Marquee.DesiredSize.Width > MarqueeBox.ActualWidth;
            if (scroll) _marqueeTimer.Start(); else _marqueeTimer.Stop();
        }

        void StepMarquee()
        {
            if (_marqueeWait > 0) { _marqueeWait--; return; }
            double w = Marquee.DesiredSize.Width;
            _marqueeX -= 1;
            if (_marqueeX < -(w - MarqueeBox.ActualWidth) - 2) { _marqueeX = 0; _marqueeWait = 60; }   // at the end: back to the start
            Canvas.SetLeft(Marquee, _marqueeX);
        }

        // ---- the equaliser's sliders: the preamp, a gap, the ten bands ----

        internal EqSlider Preamp;
        internal EqSlider[] Bands = new EqSlider[10];
        static readonly string[] BandLabels = { "60", "170", "310", "600", "1K", "3K", "6K", "12K", "14K", "16K" };

        internal void BuildEq(Action<int, double, bool> changed)
        {
            EqGrid.Children.Clear();
            EqGrid.ColumnDefinitions.Clear();
            for (int i = 0; i < 12; i++) EqGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 1 ? new GridLength(14) : new GridLength(1, GridUnitType.Star) });
            for (int i = -1; i < 10; i++)
            {
                int band = i;
                var slider = new EqSlider { Height = 110, Width = 22, HorizontalAlignment = HorizontalAlignment.Center };
                slider.ValueChanged += (v, done) => changed(band, v, done);
                var label = new TextBlock { Text = band < 0 ? "Pre" : BandLabels[band], FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
                label.SetResourceReference(TextBlock.ForegroundProperty, "vx.FgDim");
                var col = new StackPanel();
                col.Children.Add(slider);
                col.Children.Add(label);
                Grid.SetColumn(col, band < 0 ? 0 : band + 2);
                EqGrid.Children.Add(col);
                if (band < 0) Preamp = slider; else Bands[band] = slider;
            }
        }
    }

    /// <summary>
    /// An upright slider for the equaliser, -20 to +20 dB: the track, the part from 0 dB to the value in the accent, the knob. Drag or
    /// click it, the wheel moves it 1 dB, a double-click puts it back to 0. ValueChanged(value, done) - done when the mouse lets go.
    /// </summary>
    sealed class EqSlider : FrameworkElement
    {
        public const double Min = -20, Max = 20;
        double _value;
        public event Action<double, bool> ValueChanged;

        public static readonly DependencyProperty TrackProperty = DependencyProperty.Register("Track", typeof(Brush), typeof(EqSlider), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty FillProperty = DependencyProperty.Register("Fill", typeof(Brush), typeof(EqSlider), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty KnobProperty = DependencyProperty.Register("Knob", typeof(Brush), typeof(EqSlider), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public EqSlider()
        {
            SetResourceReference(TrackProperty, "vx.Track");
            SetResourceReference(FillProperty, "vx.Accent");
            SetResourceReference(KnobProperty, "vx.Fg");
            Cursor = Cursors.Hand;
            Focusable = false;
        }

        public double Value
        {
            get { return _value; }
            set { _value = Math.Max(Min, Math.Min(Max, value)); ToolTip = (_value > 0 ? "+" : "") + _value.ToString("0.#") + " dB"; InvalidateVisual(); }
        }

        const double Knob = 14;
        double Top { get { return Knob / 2; } }
        double Span { get { return Math.Max(1, ActualHeight - Knob); } }
        double YOf(double v) { return Top + (Max - v) / (Max - Min) * Span; }

        protected override void OnRender(DrawingContext dc)
        {
            double x = ActualWidth / 2;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));   // the whole of it takes the mouse
            dc.DrawRoundedRectangle((Brush)GetValue(TrackProperty), null, new Rect(x - 2.5, Top, 5, Span), 2.5, 2.5);
            double y0 = YOf(0), y = YOf(_value);
            dc.DrawRoundedRectangle((Brush)GetValue(FillProperty), null, new Rect(x - 2.5, Math.Min(y0, y), 5, Math.Abs(y - y0)), 2.5, 2.5);
            dc.DrawEllipse((Brush)GetValue(KnobProperty), null, new Point(x, y), Knob / 2, Knob / 2);
        }

        void SetFromY(double y, bool done)
        {
            double v = Max - (y - Top) / Span * (Max - Min);
            Value = Math.Round(v * 2) / 2;   // half a dB
            ValueChanged?.Invoke(_value, done);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) { Value = 0; ValueChanged?.Invoke(0, true); e.Handled = true; return; }
            CaptureMouse();
            SetFromY(e.GetPosition(this).Y, false);
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (IsMouseCaptured) SetFromY(e.GetPosition(this).Y, false);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (!IsMouseCaptured) return;
            ReleaseMouseCapture();
            SetFromY(e.GetPosition(this).Y, true);
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            Value = _value + (e.Delta > 0 ? 1 : -1);
            ValueChanged?.Invoke(_value, true);
            e.Handled = true;
        }
    }
}
