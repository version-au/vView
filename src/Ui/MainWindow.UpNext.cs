using System;
using System.Threading;
using vView.Core;

namespace vView.Ui
{
    /// <summary>
    /// Jellyfin episodes, as Jellyfin's own player: Skip intro during the intro, and Up next near the end - the next one in the page's
    /// queue with a countdown, Play now and Cancel. The bridge sends the next item ("upnext") and the intro / credits times ("segments",
    /// from the server - Intro Skipper) after each "play". Up next shows at the start of the credits when the server knows them, else
    /// JellyfinUpNextSeconds before the end; the countdown is that long, then the next one plays (skipping the rest of the credits) - or,
    /// counting down to the very end, the end plays it as before. Cancel: the episode stops at its end instead. Options > Jellyfin.
    /// </summary>
    public partial class MainWindow
    {
        JellyfinPromptWindow _jfPrompt;

        JellyfinPromptWindow Prompt()
        {
            if (_jfPrompt == null)
            {
                _jfPrompt = new JellyfinPromptWindow(this, Stage) { SkipIntro = SkipJellyfinIntro, PlayNow = JellyfinPlayNext, Cancel = CancelUpNext };
                _jfPrompt.ApplyLook(_theme, _blur);
            }
            return _jfPrompt;
        }

        /// <summary>With the video bar (4 times a second while a video plays): what the prompt shows now.</summary>
        void JellyfinPromptTick()
        {
            JellyfinPlay jf = _playing?.Jellyfin;
            if (jf == null || jf.Done || _ended || _jfShown) { _jfPrompt?.Clear(); return; }
            long t = Interlocked.Read(ref _vTime), length = Interlocked.Read(ref _vLength);

            // Skip intro: while it's on (not after it was used)
            if (_settings.JellyfinSkipIntro && !jf.IntroDone && jf.IntroEnd > 0 && t >= Math.Max(0, jf.IntroStart) && t < jf.IntroEnd - 1000)
            {
                Prompt().ShowSkipIntro();
                return;
            }

            // Up next
            if (_settings.JellyfinUpNext && jf.NextTitle != null && !jf.UpNextCancelled && !jf.NextAsked && length > 0)
            {
                long span = _settings.JellyfinUpNextSeconds * 1000L;
                bool credits = jf.OutroStart > 0 && jf.OutroStart < length - 2000;
                long showAt = credits ? jf.OutroStart : length - span;
                long nextAt = credits ? Math.Min(length, jf.OutroStart + span) : length;
                if (t >= showAt)
                {
                    // the credits go on after the countdown: the next one plays then; counting to the end, the end plays it (as before)
                    bool early = nextAt < length - 1500;
                    if (_settings.JellyfinAutoNext && early && t >= nextAt) { JellyfinPlayNext(); return; }
                    string count = "";
                    if (_settings.JellyfinAutoNext)
                        count = "Plays in " + (long)Math.Ceiling(Math.Max(0, nextAt - t) / 1000.0) + " s" + (_vPlaying ? "" : " - paused");
                    Prompt().ShowUpNext(jf.NextTitle, jf.NextSeries, jf.NextImage, count);
                    return;
                }
            }
            _jfPrompt?.Clear();
        }

        void SkipJellyfinIntro()
        {
            JellyfinPlay jf = _playing?.Jellyfin;
            _jfPrompt?.Clear();
            if (jf == null || jf.IntroEnd <= 0) return;
            jf.IntroDone = true;
            long length = Interlocked.Read(ref _vLength);
            if (length > 0) SeekToFraction(Math.Min(1, jf.IntroEnd / (double)length));
            Log.Write("Jellyfin: skipped the intro (to " + Format.Time(jf.IntroEnd) + ")");
        }

        /// <summary>Play now (or the countdown ending): the page plays the next one in its queue.</summary>
        void JellyfinPlayNext()
        {
            JellyfinPlay jf = _playing?.Jellyfin;
            _jfPrompt?.Clear();
            if (jf == null || jf.Done || jf.NextAsked) return;
            jf.NextAsked = true;
            Log.Write("Jellyfin: up next - " + jf.NextTitle);
            JellyfinNextPrevious(true);
        }

        /// <summary>Cancel: no Up next for this one, and it stops at its end (not going on to the next).</summary>
        void CancelUpNext()
        {
            JellyfinPlay jf = _playing?.Jellyfin;
            _jfPrompt?.Clear();
            if (jf != null) jf.UpNextCancelled = true;
        }

        /// <summary>At the end of a Jellyfin video: false = stop there rather than let the page play the next one.</summary>
        bool JellyfinGoesOn(JellyfinPlay jf)
        {
            if (jf.UpNextCancelled) return false;
            if (_settings.JellyfinUpNext && !_settings.JellyfinAutoNext && jf.NextTitle != null) return false;
            return true;
        }

        string PromptState()
        {
            JellyfinPlay jf = _playing?.Jellyfin;
            return "mode=" + (_jfPrompt?.Mode ?? "-") + " shown=" + (_jfPrompt?.IsVisible == true) +
                   (jf == null ? "" : " next=" + (jf.NextTitle ?? "-") + " intro=" + jf.IntroStart + "-" + jf.IntroEnd + " outro=" + jf.OutroStart +
                                      " cancelled=" + jf.UpNextCancelled + " asked=" + jf.NextAsked + " introDone=" + jf.IntroDone);
        }
    }
}
