// vView's bridge into Jellyfin's web page (put in every page vView's Jellyfin panel loads, before the page's own scripts).
// Jellyfin's page looks for window.NativeShell - the hook its own desktop app uses - and loads the players it lists. vView lists one
// "vView" player for videos: when you press Play, the page asks it to play a stream, and it hands the address to vView (VLC plays it);
// vView reports back where it's up to, and the page tells the server (resume points, watched). Music stays in the page's own player.
// Messages go through WebView2's window.chrome.webview. Only on vView's Jellyfin server (__VVIEW_HOST__ is filled in by vView).
(function () {
    'use strict';
    const HOST = __VVIEW_HOST__, VERSION = __VVIEW_VERSION__, DEVICE = __VVIEW_DEVICE__;
    if (location.hostname.toLowerCase() !== HOST || !window.chrome || !window.chrome.webview || window.NativeShell) return;
    const webview = window.chrome.webview;
    const send = (m) => { try { webview.postMessage(m); } catch (e) { console.error('[vView] ' + e); } };

    // what this page can do in vView (as a browser's, without exit / fullscreen / chromecast - vView does those)
    const features = ['filedownload', 'displaylanguage', 'htmlaudioautoplay', 'htmlvideoautoplay', 'externallinks', 'clientsettings',
        'remotecontrol', 'remotevideo', 'displaymode', 'screensaver', 'fileinput', 'targetblank', 'subtitleappearancesettings', 'subtitleburnsettings'];

    // VLC plays any video as it is - the server only converts when it must (a slow connection, the bitrate set in the page)
    const subtitles = ['srt', 'subrip', 'ass', 'ssa', 'vtt', 'webvtt', 'sub', 'smi', 'ttml'].reduce((list, f) =>
        list.concat([{ Format: f, Method: 'External' }, { Format: f, Method: 'Embed' }]), [])
        .concat(['pgssub', 'pgs', 'dvdsub', 'dvbsub'].map(f => ({ Format: f, Method: 'Embed' })));

    function vlcProfile(p) {
        p = Object.assign({}, p);
        p.Name = 'vView';
        p.MaxStaticBitrate = 1000000000;
        p.DirectPlayProfiles = [{ Type: 'Video' }].concat((p.DirectPlayProfiles || []).filter(x => x.Type !== 'Video'));
        p.ContainerProfiles = (p.ContainerProfiles || []).filter(x => x.Type !== 'Video');
        p.CodecProfiles = (p.CodecProfiles || []).filter(x => x.Type !== 'Video' && x.Type !== 'VideoAudio');
        p.SubtitleProfiles = subtitles;
        return p;
    }

    /** A stream's place among the file's own tracks of its type (0 = the first), as VLC numbers them; -1 if it isn't one. */
    function trackNumber(streams, index, type) {
        let n = 0;
        for (const s of streams) {
            if (s.Type !== type || s.IsExternal) continue;
            if (s.Index === index) return n;
            n++;
        }
        return -1;
    }

    function titleOf(item) {
        if (!item) return 'Jellyfin';
        if (item.Type === 'Episode') {
            const se = (item.ParentIndexNumber != null ? 'S' + item.ParentIndexNumber : '') + (item.IndexNumber != null ? 'E' + item.IndexNumber : '');
            return [item.SeriesName, se, item.Name].filter(x => x).join(' · ');
        }
        return item.ProductionYear && item.Type === 'Movie' ? item.Name + ' (' + item.ProductionYear + ')' : (item.Name || 'Jellyfin');
    }

    let active = null;   // the player (the page makes one)
    webview.addEventListener('message', (e) => { if (active && e.data && e.data.type) active.fromVView(e.data); });

    class VViewPlayer {
        constructor({ events, appHost, playbackManager }) {
            this.events = events;
            this.appHost = appHost;
            this.playbackManager = playbackManager;
            this.name = 'vView';
            this.type = 'mediaplayer';
            this.id = 'vviewplayer';
            this.syncPlayWrapAs = 'htmlvideoplayer';
            this.priority = -2;              // before the page's own video player
            this.useFullSubtitleUrls = true; // subtitle files with the server's address, for VLC
            this.isLocalPlayer = true;
            this.isFetching = false;
            this._src = null;
            this._options = null;
            this._time = 0;
            this._duration = null;
            this._started = false;
            this._paused = false;
            this._volume = 100;
            this._muted = false;
            this._full = false;
            this._stopFromVView = false;
            this._accepted = null;
            active = this;
            send({ type: 'ready', version: VERSION });
        }

        canPlayMediaType(mediaType) { return (mediaType || '').toLowerCase() === 'video'; }
        canPlayItem(item) { return this.canPlayMediaType(item.MediaType); }
        supportsPlayMethod() { return true; }
        async getDeviceProfile(item, options) { return vlcProfile(await this.appHost.getDeviceProfile(item, options)); }
        supports() { return false; }

        play(options) {
            const source = options.mediaSource || {};
            const streams = source.MediaStreams || [];
            const transcode = options.playMethod === 'Transcode';
            this._src = options.url;
            this._options = options;
            this._time = 0;
            this._duration = source.RunTimeTicks ? source.RunTimeTicks / 10000 : null;
            this._started = false;
            this._paused = false;
            const audio = source.DefaultAudioStreamIndex;
            const sub = source.DefaultSubtitleStreamIndex == null ? -1 : source.DefaultSubtitleStreamIndex;
            const m = {
                type: 'play', url: options.url, title: titleOf(options.item), itemId: (options.item && options.item.Id) || '',
                // a converted stream that starts part-way already starts there (the page adds the offset to the time)
                startMs: options.transcodingOffsetTicks ? 0 : Math.floor((options.playerStartPositionTicks || 0) / 10000),
                audioTrack: transcode ? 0 : (audio != null && audio >= 0 ? trackNumber(streams, audio, 'Audio') : -1),
                subTrack: -1, subUrl: null, subsOff: sub < 0
            };
            Object.assign(m, this.subtitle(sub));
            console.debug('[vView] play ' + m.title + ' (' + options.playMethod + ')');
            send(m);
            this.extras(options.item);
            // done when vView has taken it (it says "accepted"); the page then reports that playback started
            return new Promise((resolve) => {
                this._accepted = resolve;
                setTimeout(() => { if (this._accepted === resolve) { this._accepted = null; resolve(); } }, 10000);
            });
        }

        /**
         * For vView's Up next and Skip intro, after "play": the next one in the page's queue (what it plays at the end) and the item's
         * intro / credits - the server's media segments (Jellyfin 10.10+, filled in by Intro Skipper), else Intro Skipper's own older API.
         * Each message carries the item's id, so vView never takes them for another one.
         */
        async extras(item) {
            if (!item || !item.Id) return;
            const id = item.Id, api = window.ApiClient;
            const still = () => this._options && this._options.item && this._options.item.Id === id;
            try {
                const pm = this.playbackManager;
                const list = (pm.getPlaylist ? await pm.getPlaylist(this) : null) || [];
                // where this item is in the queue: the page moves its index on only after play() has returned, so while the next
                // episode starts the index is still the one before - then "the one after" was this very episode (Up next showed the
                // episode playing). Found by its id; the index only picks between copies of it.
                let i = pm.getCurrentPlaylistIndex ? pm.getCurrentPlaylistIndex(this) : -1;
                if (!(i >= 0 && list[i] && list[i].Id === id)) {
                    let at = -1;
                    for (let k = 0; k < list.length; k++) if (list[k] && list[k].Id === id && (at < 0 || k > i)) { at = k; if (k > i) break; }
                    i = at;
                }
                const next = i >= 0 ? list[i + 1] : null;
                if (next && next.Id && next.Id !== id && still()) {
                    let image = null;
                    if (api) {
                        if (next.ImageTags && next.ImageTags.Primary)
                            image = api.getScaledImageUrl(next.Id, { type: 'Primary', maxWidth: 400, tag: next.ImageTags.Primary });
                        else if (next.ParentThumbItemId && next.ParentThumbImageTag)
                            image = api.getScaledImageUrl(next.ParentThumbItemId, { type: 'Thumb', maxWidth: 400, tag: next.ParentThumbImageTag });
                        else if (next.ParentBackdropItemId && next.ParentBackdropImageTags && next.ParentBackdropImageTags.length)
                            image = api.getScaledImageUrl(next.ParentBackdropItemId, { type: 'Backdrop', maxWidth: 400, tag: next.ParentBackdropImageTags[0] });
                    }
                    const se = next.Type === 'Episode'
                        ? [(next.ParentIndexNumber != null ? 'S' + next.ParentIndexNumber : '') + (next.IndexNumber != null ? ' E' + next.IndexNumber : ''), next.Name].filter(x => x && x.trim()).join(' · ')
                        : (next.Name || '');
                    send({ type: 'upnext', itemId: id, title: se || titleOf(next), series: next.SeriesName || '', image: image });
                }
            } catch (e) { console.debug('[vView] up next: ' + e); }
            if (!api) return;
            let intro = null, outro = null;
            try {
                const r = await api.getJSON(api.getUrl('MediaSegments/' + id));
                for (const s of (r && r.Items) || []) {
                    const seg = { start: s.StartTicks / 10000, end: s.EndTicks / 10000 };
                    if (s.Type === 'Intro' && !intro) intro = seg;
                    if (s.Type === 'Outro' && !outro) outro = seg;
                }
            } catch (e) { }
            if (!intro && !outro) {
                try {
                    const r = await api.getJSON(api.getUrl('Episode/' + id + '/IntroSkipperSegments'));
                    const seg = (x) => {
                        if (!x || x.Valid === false) return null;
                        const a = x.Start != null ? x.Start : x.IntroStart, b = x.End != null ? x.End : x.IntroEnd;
                        return b > 0 ? { start: a * 1000, end: b * 1000 } : null;
                    };
                    intro = seg(r && (r.Introduction || r.Intro));
                    outro = seg(r && (r.Credits || r.Outro));
                } catch (e) { }
            }
            if ((intro || outro) && still())
                send({ type: 'segments', itemId: id, introStart: intro ? intro.start : -1, introEnd: intro ? intro.end : -1, outroStart: outro ? outro.start : -1 });
        }

        /** How vView shows Jellyfin's subtitle stream index: a file of its own (subUrl), one of the video's (subTrack), or none. */
        subtitle(index) {
            const options = this._options || {};
            const streams = (options.mediaSource && options.mediaSource.MediaStreams) || [];
            if (index == null || index < 0) return { subTrack: -1, subUrl: null, subsOff: true };
            const s = streams.find(x => x.Index === index);
            if (s && s.DeliveryMethod === 'External' && s.DeliveryUrl) return { subTrack: -1, subUrl: s.DeliveryUrl, subsOff: false };
            if (s && s.DeliveryMethod === 'Encode') return { subTrack: -1, subUrl: null, subsOff: true };   // drawn into the picture by the server
            if (options.playMethod === 'Transcode') return { subTrack: 0, subUrl: null, subsOff: false };
            return { subTrack: trackNumber(streams, index, 'Subtitle'), subUrl: null, subsOff: false };
        }

        /** A message from vView. */
        fromVView(m) {
            switch (m.type) {
                case 'accepted':
                    if (this._accepted) { const r = this._accepted; this._accepted = null; r(); }
                    break;
                case 'state':
                    if (!this._src) return;
                    if (m.d > 0) this._duration = m.d;
                    this._time = m.t;
                    this._full = !!m.full;
                    if (m.volume !== this._volume || m.muted !== this._muted) {
                        this._volume = m.volume;
                        this._muted = m.muted;
                        this.events.trigger(this, 'volumechange');
                    }
                    if (m.playing && !this._started) {
                        this._started = true;
                        this.events.trigger(this, 'playing');
                    } else if (this._started && m.playing === this._paused) {
                        this._paused = !m.playing;
                        this.events.trigger(this, this._paused ? 'pause' : 'unpause');
                        if (!this._paused) this.events.trigger(this, 'playing');
                    }
                    this.events.trigger(this, 'timeupdate');
                    break;
                case 'ended':   // played to the end: the page goes on to the next one if there is one
                    if (this._src) this.stopped();
                    break;
                case 'stop':    // stopped in vView (Back to Jellyfin, another file, closing)
                    if (!this._src) return;
                    if (m.t != null) this._time = m.t;
                    this._stopFromVView = true;
                    this.playbackManager.stop(this);
                    break;
                case 'error':
                    if (this._src) this.events.trigger(this, 'error', [{ type: 'mediadecodeerror' }]);
                    break;
                case 'next': this.playbackManager.nextTrack(this); break;
                case 'previous': this.playbackManager.previousTrack(this); break;
            }
        }

        stopped() {
            const src = this._src;
            this.events.trigger(this, 'stopped', [{ src: src }]);
            this._src = null;
            this._options = null;
            this._started = false;
        }

        stop(destroyPlayer) {
            if (this._src) {
                if (!this._stopFromVView) send({ type: 'stop' });
                this.stopped();
            }
            this._stopFromVView = false;
            if (destroyPlayer) this.destroy();
            return Promise.resolve();
        }

        destroy() { }
        currentSrc() { return this._src; }

        // where it's up to, in ms (the page reads it for the reports to the server)
        currentTime(ms) {
            if (ms != null) { this._time = ms; send({ type: 'seek', ms: ms }); return; }
            return this._time;
        }
        duration() { return this._duration; }
        seekable() { return !!this._duration; }
        getBufferedRanges() { return []; }

        paused() { return this._paused; }
        pause() { send({ type: 'pause' }); }
        unpause() { send({ type: 'unpause' }); }
        resume() { send({ type: 'unpause' }); }
        playPause() { send({ type: this._paused ? 'unpause' : 'pause' }); }

        getVolume() { return this._volume; }
        setVolume(v) { v = Math.max(0, Math.min(100, Math.round(Number(v)))); if (!isNaN(v)) { this._volume = v; send({ type: 'volume', v: v }); } }
        volumeUp() { this.setVolume(this._volume + 2); }
        volumeDown() { this.setVolume(this._volume - 2); }
        setMute(mute) { this._muted = !!mute; send({ type: 'mute', on: !!mute }); }
        isMuted() { return this._muted; }

        canSetAudioStreamIndex() { return true; }
        setAudioStreamIndex(index) {
            const o = this._options || {};
            const streams = (o.mediaSource && o.mediaSource.MediaStreams) || [];
            send({ type: 'audio', track: o.playMethod === 'Transcode' ? 0 : trackNumber(streams, index, 'Audio') });
        }
        setSubtitleStreamIndex(index) { send(Object.assign({ type: 'subtitle' }, this.subtitle(index))); }
        setSecondarySubtitleStreamIndex() { }
        resetSubtitleOffset() { }
        enableShowingSubtitleOffset() { }
        disableShowingSubtitleOffset() { }
        isShowingSubtitleOffsetEnabled() { return false; }
        setSubtitleOffset() { }
        getSubtitleOffset() { return 0; }

        isFullscreen() { return this._full; }
        toggleFullscreen() { send({ type: 'fullscreen' }); }
        getPlaybackRate() { return 1; }
        setPlaybackRate() { }
        getSupportedPlaybackRates() { return [{ name: '1x', id: 1 }]; }
        getSupportedAspectRatios() { return []; }
        getAspectRatio() { return 'auto'; }
        setAspectRatio() { }
        setPictureInPictureEnabled() { }
        isPictureInPictureEnabled() { return false; }
        togglePictureInPicture() { }
        isAirPlayEnabled() { return false; }
        setAirPlayEnabled() { }
        toggleAirPlay() { }
        setBrightness() { }
        getBrightness() { return 100; }
        getStats() { return Promise.resolve({ categories: [] }); }
    }

    window.vViewPlayer = () => VViewPlayer;

    window.NativeShell = {
        getPlugins() { return ['vViewPlayer']; },
        openUrl(url) { send({ type: 'openurl', url: url }); },
        openClientSettings() { send({ type: 'options' }); },
        AppHost: {
            init() { return Promise.resolve({ deviceName: DEVICE, appName: 'vView', appVersion: VERSION }); },
            // as a browser's: 'auto' would make the page's layoutManager ask again and again (it hung on the logo)
            getDefaultLayout() { return 'modern'; },
            supports(command) { return features.includes((command || '').toLowerCase()); },
            // for the page's own players (music): what Edge plays; vView's player changes it for VLC
            getDeviceProfile(profileBuilder) { return profileBuilder({ enableMkvProgressive: false, disableHlsVideoAudioCodecs: [] }); },
            appName() { return 'vView'; },
            appVersion() { return VERSION; },
            deviceName() { return DEVICE; },
            exit() { }
        }
    };
})();
