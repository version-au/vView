using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace vView.Core
{
    /// <summary>
    /// vView's own sound, as Windows mixes it, for the visualiser: Windows' process loopback capture (Windows 10 2004+), asked for this
    /// process (and its children) only - other apps, system sounds and calls aren't in it. VLC still plays as before; this only listens.
    /// The latest samples (mono) are kept in a ring for <see cref="Snapshot"/>.
    /// </summary>
    sealed class SoundTap
    {
        const int RingSize = 8192;
        readonly float[] _ring = new float[RingSize];
        readonly object _lock = new object();
        int _pos;
        long _packets;
        WasapiRecorder _rec;
        bool _starting;
        int _channels = 2;
        bool _float = true;

        public int SampleRate { get; private set; } = 48000;
        public bool Running { get { return _rec != null; } }
        public long Packets { get { return Interlocked.Read(ref _packets); } }
        public string Error { get; private set; }

        public async Task StartAsync()
        {
            if (_rec != null || _starting) return;
            _starting = true;
            try
            {
                WasapiRecorder rec = await Task.Run(() => new WasapiRecorderBuilder()
                    .WithProcessLoopback((uint)Environment.ProcessId, ProcessLoopbackMode.IncludeTargetProcessTree)
                    .WithFormat(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2))
                    .BuildAsync());
                WaveFormat f = rec.WaveFormat;
                SampleRate = f.SampleRate;
                _channels = Math.Max(1, f.Channels);
                _float = f.Encoding == WaveFormatEncoding.IeeeFloat || (f.Encoding == WaveFormatEncoding.Extensible && f.BitsPerSample == 32);
                if (!_float && f.BitsPerSample != 16) throw new NotSupportedException("sound format " + f);
                rec.DataAvailable += (buffer, flags, devicePosition, qpcPosition) => Take(buffer, (Convert.ToInt32(flags) & 2) != 0);
                rec.StartRecording();
                _rec = rec;
                Error = null;
                Log.Write("Visualiser: listening to vView's sound (" + f + ")");
            }
            catch (Exception ex)
            {
                Error = ex.GetType().Name + ": " + ex.Message;
                Log.Write("Visualiser: can't listen to vView's sound - " + Error);
            }
            finally { _starting = false; }
        }

        public void Stop()
        {
            WasapiRecorder rec = _rec;
            _rec = null;
            if (rec == null) return;
            lock (_lock) { Array.Clear(_ring); }
            Task.Run(() =>
            {
                try { rec.StopRecording(); } catch { }
                try { rec.Dispose(); } catch { }
            });
        }

        void Take(ReadOnlySpan<byte> buffer, bool silent)
        {
            Interlocked.Increment(ref _packets);
            int frameBytes = _channels * (_float ? 4 : 2);
            int frames = buffer.Length / frameBytes;
            lock (_lock)
            {
                if (silent)
                {
                    for (int i = 0; i < frames; i++) { _ring[_pos] = 0; _pos = (_pos + 1) % RingSize; }
                    return;
                }
                if (_float)
                {
                    ReadOnlySpan<float> s = MemoryMarshal.Cast<byte, float>(buffer);
                    for (int i = 0; i < frames; i++)
                    {
                        float sum = 0;
                        for (int c = 0; c < _channels; c++) sum += s[i * _channels + c];
                        _ring[_pos] = sum / _channels;
                        _pos = (_pos + 1) % RingSize;
                    }
                }
                else
                {
                    ReadOnlySpan<short> s = MemoryMarshal.Cast<byte, short>(buffer);
                    for (int i = 0; i < frames; i++)
                    {
                        float sum = 0;
                        for (int c = 0; c < _channels; c++) sum += s[i * _channels + c] / 32768f;
                        _ring[_pos] = sum / _channels;
                        _pos = (_pos + 1) % RingSize;
                    }
                }
            }
        }

        /// <summary>The last dest.Length samples, oldest first.</summary>
        public void Snapshot(float[] dest)
        {
            lock (_lock)
            {
                int start = (_pos - dest.Length + RingSize) % RingSize;
                for (int i = 0; i < dest.Length; i++) dest[i] = _ring[(start + i) % RingSize];
            }
        }
    }

    /// <summary>A plain radix-2 FFT (in place; n a power of two).</summary>
    static class Fft
    {
        public static void Transform(double[] re, double[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2 * Math.PI / len;
                double wr = Math.Cos(ang), wi = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1, ci = 0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = i + k + len / 2;
                        double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - tr; im[b] = im[a] - ti;
                        re[a] += tr; im[a] += ti;
                        double ncr = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr;
                        cr = ncr;
                    }
                }
            }
        }
    }
}
