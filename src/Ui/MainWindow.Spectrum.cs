using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Threading;
using vView.Core;

namespace vView.Ui
{
    /// <summary>
    /// The visualiser: 32 bands from 50 Hz to 16 kHz, from vView's own sound only (<see cref="SoundTap"/>), rising at once and falling
    /// slowly, with a cap that holds the peak a moment. Drawn as bars in each <see cref="BarSet"/> - now playing, the classic player's
    /// display and its one-line shade. It listens only while one of them shows a song.
    /// </summary>
    public partial class MainWindow
    {
        const int Bands = 32, FftSize = 2048;
        readonly SoundTap _tap = new SoundTap();
        readonly double[] _level = new double[Bands], _peak = new double[Bands];
        readonly int[] _peakHold = new int[Bands];
        readonly float[] _samples = new float[FftSize];
        readonly double[] _re = new double[FftSize], _im = new double[FftSize];
        readonly List<BarSet> _barSets = new List<BarSet>();
        DispatcherTimer _specTimer, _stopLater;

        void InitSpectrum()
        {
            _barSets.Add(new BarSet(Spectrum, Bands, 10, 3, 120, true));
            _specTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
            _specTimer.Tick += (s, e) => DrawSpectrum();
            // between two songs the card hides for a moment: it stops listening only when nothing shows the bars for 2 s
            _stopLater = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _stopLater.Tick += (s, e) => { _stopLater.Stop(); if (!SpectrumWanted) _tap.Stop(); };
            AudioCard.IsVisibleChanged += (s, e) => UpdateSpectrumListening();
        }

        /// <summary>A song shows somewhere: now playing in this window, or the classic player.</summary>
        bool SpectrumWanted
        {
            // the song open (Current), not the one VLC has started (_playing): started straight in the classic player, that shows before
            // VLC is ready - asked then, the bars never started
            get { return AudioCard.IsVisible || (_classic != null && _classic.IsVisible && Current != null && Current.Kind == MediaKind.Audio); }
        }

        void UpdateSpectrumListening()
        {
            if (SpectrumWanted) { _stopLater.Stop(); _ = _tap.StartAsync(); _specTimer.Start(); }
            else
            {
                _specTimer.Stop();
                _stopLater.Start();
                Array.Clear(_level);
                Array.Clear(_peak);
                foreach (BarSet b in _barSets) b.Draw(_level, _peak);
            }
        }

        void DrawSpectrum()
        {
            if (!_tap.Running) return;
            _tap.Snapshot(_samples);
            for (int i = 0; i < FftSize; i++)
            {
                double w = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1));   // Hann window
                _re[i] = _samples[i] * w;
                _im[i] = 0;
            }
            Fft.Transform(_re, _im);
            // the bars show the sound as it is, not how loud vView's volume makes it
            double comp = _settings.Volume > 0 ? 100.0 / _settings.Volume : 1;
            double binHz = (double)_tap.SampleRate / FftSize;
            for (int b = 0; b < Bands; b++)
            {
                double lo = 50 * Math.Pow(16000.0 / 50, (double)b / Bands), hi = 50 * Math.Pow(16000.0 / 50, (double)(b + 1) / Bands);
                int i0 = Math.Max(1, (int)Math.Floor(lo / binHz)), i1 = Math.Max(i0, (int)Math.Ceiling(hi / binHz) - 1);
                double mag = 0;
                for (int k = i0; k <= i1 && k < FftSize / 2; k++) mag = Math.Max(mag, Math.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]));
                double amp = mag * 4 / FftSize * comp;   // a full-scale sine = 1 (2/N, and 2 for the Hann window's loss)
                double db = 20 * Math.Log10(amp + 1e-9);
                double v = Math.Clamp((db + 60) / 60, 0, 1);   // -60 dB .. 0 dB
                _level[b] = v > _level[b] ? v : Math.Max(v, _level[b] - 0.05);
                if (_level[b] >= _peak[b]) { _peak[b] = _level[b]; _peakHold[b] = 20; }
                else if (_peakHold[b] > 0) _peakHold[b]--;
                else _peak[b] = Math.Max(_level[b], _peak[b] - 0.015);
            }
            foreach (BarSet b in _barSets) if (b.Canvas.IsVisible) b.Draw(_level, _peak);
        }

        /// <summary>For the test hook's dump.</summary>
        string SpectrumState()
        {
            return "tap=" + (_tap.Running ? "on" : "off") + " packets=" + _tap.Packets + " error=" + (_tap.Error ?? "-") +
                   " max=" + Math.Round(_level.Max(), 2) + " avg=" + Math.Round(_level.Average(), 2) +
                   " loudest=" + Array.IndexOf(_level, _level.Max());
        }
    }

    /// <summary>One drawing of the visualiser: count bars (each the loudest of its share of the 32 bands) in a canvas, caps optional.</summary>
    sealed class BarSet
    {
        public readonly Canvas Canvas;
        readonly Rectangle[] _bars, _caps;
        readonly double _height;

        public BarSet(Canvas canvas, int count, double barWidth, double gap, double height, bool caps)
        {
            Canvas = canvas;
            _height = height;
            canvas.Width = count * (barWidth + gap) - gap;
            canvas.Height = height;
            _bars = new Rectangle[count];
            _caps = caps ? new Rectangle[count] : null;
            double r = Math.Min(2, barWidth / 3);
            for (int i = 0; i < count; i++)
            {
                var bar = new Rectangle { Width = barWidth, Height = 2, RadiusX = r, RadiusY = r };
                bar.SetResourceReference(Shape.FillProperty, "vx.Accent");
                Canvas.SetLeft(bar, i * (barWidth + gap));
                Canvas.SetTop(bar, height - 2);
                canvas.Children.Add(bar);
                _bars[i] = bar;
                if (caps)
                {
                    var cap = new Rectangle { Width = barWidth, Height = Math.Max(2, height / 40), RadiusX = 1, RadiusY = 1, Opacity = 0 };
                    cap.SetResourceReference(Shape.FillProperty, "vx.Fg");
                    Canvas.SetLeft(cap, i * (barWidth + gap));
                    canvas.Children.Add(cap);
                    _caps[i] = cap;
                }
            }
        }

        public void Draw(double[] level, double[] peak)
        {
            int n = _bars.Length, bands = level.Length;
            for (int i = 0; i < n; i++)
            {
                int b0 = i * bands / n, b1 = Math.Max(b0 + 1, (i + 1) * bands / n);
                double lv = 0, pk = 0;
                for (int b = b0; b < b1; b++) { lv = Math.Max(lv, level[b]); pk = Math.Max(pk, peak[b]); }
                double h = Math.Max(2, lv * _height);
                _bars[i].Height = h;
                Canvas.SetTop(_bars[i], _height - h);
                if (_caps == null) continue;
                Canvas.SetTop(_caps[i], Math.Max(0, _height - Math.Max(2, pk * _height) - _caps[i].Height - 2));
                _caps[i].Opacity = pk < 0.04 ? 0 : 1;   // at rest: just the bars, no second line of caps
            }
        }
    }
}
