using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;

namespace vView.Ui
{
    /// <summary>
    /// The equaliser: VLC's own (a preamp and ten bands, -20 to +20 dB, and its presets - Flat, Rock, Pop, Classical …), set on the
    /// player through the VLC worker. The settings keep it, so it's the same for every song and both views.
    /// </summary>
    public partial class MainWindow
    {
        List<string> _eqPresets;
        Equalizer _eqObj;
        int _eqQueued;
        bool _eqWantOn;
        float _eqWantPre;
        float[] _eqWantBands = new float[10];

        /// <summary>Put the settings' equaliser on the player (drag-fast: only the latest values are sent).</summary>
        void ApplyEq()
        {
            if (_mp == null) return;
            _eqWantOn = _settings.EqOn;
            _eqWantPre = (float)_settings.EqPreamp;
            _eqWantBands = Enumerable.Range(0, 10).Select(i => (float)(i < _settings.EqBands.Count ? _settings.EqBands[i] : 0)).ToArray();
            if (Interlocked.Exchange(ref _eqQueued, 1) == 1) return;
            Vlc(() =>
            {
                Interlocked.Exchange(ref _eqQueued, 0);
                if (!_eqWantOn) { _mp.UnsetEqualizer(); return; }
                var eq = new Equalizer();
                eq.SetPreamp(_eqWantPre);
                float[] b = _eqWantBands;
                for (uint i = 0; i < b.Length && i < eq.BandCount; i++) eq.SetAmp(b[i], i);
                _mp.SetEqualizer(eq);
                Equalizer old = _eqObj;
                _eqObj = eq;
                old?.Dispose();
            });
        }

        /// <summary>VLC's preset names (once VLC has started).</summary>
        async Task<List<string>> EqPresets()
        {
            if (_eqPresets != null) return _eqPresets;
            try { await _vlcStarting; } catch { return new List<string>(); }
            var names = new List<string>();
            await Vlc(() =>
            {
                using (var e = new Equalizer())
                    for (uint i = 0; i < e.PresetCount; i++) names.Add(e.PresetName(i));
            });
            _eqPresets = names;
            return names;
        }

        /// <summary>A preset's values into the settings, the equaliser on.</summary>
        async void ChooseEqPreset(int index)
        {
            List<string> names = await EqPresets();
            if (index < 0 || index >= names.Count) return;
            float pre = 0;
            var amps = new float[10];
            await Vlc(() =>
            {
                using (var e = new Equalizer((uint)index))
                {
                    pre = e.Preamp;
                    for (uint i = 0; i < 10 && i < e.BandCount; i++) amps[i] = e.Amp(i);
                }
            });
            _settings.EqPreset = names[index];
            _settings.EqPreamp = Math.Round(pre, 1) + 0.0;   // + 0.0: no "-0"
            _settings.EqBands = amps.Select(a => Math.Round((double)a, 1) + 0.0).ToList();
            _settings.EqOn = true;
            _settings.Save();
            ApplyEq();
            ClassicEq();
        }

        /// <summary>A slider moved (band -1 = the preamp): the equaliser on, "Custom".</summary>
        void EqSliderChanged(int band, double v, bool done)
        {
            if (band < 0) _settings.EqPreamp = v;
            else { while (_settings.EqBands.Count < 10) _settings.EqBands.Add(0); _settings.EqBands[band] = v; }
            _settings.EqPreset = "Custom";
            bool wasOn = _settings.EqOn;
            _settings.EqOn = true;
            ApplyEq();
            if (done) _settings.Save();
            if (!wasOn || done) ClassicEq();
        }

        void SetEqOn(bool on)
        {
            _settings.EqOn = on;
            _settings.Save();
            ApplyEq();
            ClassicEq();
        }

        string EqState()
        {
            return "on=" + _settings.EqOn + " preset=" + _settings.EqPreset + " pre=" + _settings.EqPreamp + " bands=" + string.Join(",", _settings.EqBands) +
                   " player=" + (_eqObj == null ? "-" : "set");
        }
    }
}
