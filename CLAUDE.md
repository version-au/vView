# vView - notes for Claude Code

vView is a Windows 11 picture and video viewer (C# / .NET 10, WPF) in vShell's look, part of V's suite with vShell and vXplorer. It reads
vShell's theme from `%APPDATA%\vShell\settings.json` and follows it live, exactly as vXplorer does. Owner: V (Steven).
Repo: https://github.com/version-au/vView (branch `main`, GPL-3.0). This machine's notes (working folder, remotes): `CLAUDE.local.md` (not in git).
vXplorer (https://github.com/version-au/vXplorer) is the sister app: the theme, window effects, styles, Ask, Instance, AppIcon and ShellIcons were copied
from it (namespace `vView`) - when one of those changes there, copy the change here. Its CLAUDE.md has the background on all of them.

## The shared library: vSuite.Core (since 0.1.3)

- **The suite's design guide** is `vSuite.Core\docs\design-guide.md` (the window, theme, icon, controls, keys, words, Options, release): follow it for anything new here.
- **The changelog says which builds were which** (vSuite.Core 1.0.28): every build marks its version's heading in CHANGELOG.md " · pre-release" (`build.cmd pre`) or " · release" (a release replaces the pre-release mark) - commit CHANGELOG.md after each build. Write new headings without a mark.

- The theme, glass, styles, Ask, Instance, Log, updater, update notice, Options blocks, AppIcon / WindowIcons and ShellIcons come from
  **vSuite.Core** (https://github.com/version-au/vSuite.Core, MIT), here as the git submodule `vSuite.Core\` -
  read its CLAUDE.md. A fresh clone needs `git submodule update --init`. Global usings in `src/GlobalUsings.cs`; Program calls
  `App.Init("vView", "VVIEW", "https://vsuite.vrsion.com", InstallStyle.Folder, "libvlc")` and sets `AppIcon.Drawer = Brand.Draw`
  (`Ui/Brand.cs`, the play shape). vView's `OptionsWindow` derives from `OptionsWindowBase`; the notice is an `UpdateNotice` on the Stage.
- **A change to a shared piece is made in vSuite.Core** (with its tests), pushed there, then `git -C vSuite.Core pull` here and committed
  with vView's own version bump. The notes below about those pieces describe the library's code.

## Build and run

- `dotnet build vView.csproj -c Release` -> `bin\Release\net10.0-windows\win-x64\vView.exe` (+ `libvlc\win-x64\`). Close a running vView first.
- "Release build" = that **and** `build.cmd` (by full path on a network share - see `CLAUDE.local.md`): `dist\vView\` (self-contained
  folder: exe, .NET, libvlc) and `dist\vView-<ver>.zip`. Not a single exe: VLC's DLLs must stay separate files (LGPL), and its plugins
  can't load from inside a bundle. Updates and the installer: see "Releases and updates" below.
- **Before a release build the library must be current** (V, 2026-11-05): `build.cmd` runs `scripts\check-library.ps1`, which fetches
  vSuite.Core and stops the build if it's behind its repo, has unpushed / uncommitted changes, or the new pointer isn't committed in
  vView. So: `git -C vSuite.Core pull`, build, test, commit (in the unreleased version's changelog), then build.cmd.
- Log: `%APPDATA%\vView\vview.log`. Settings: `%APPDATA%\vView\settings.json` (`AppSettings`).
- Tests: `VVIEW_DATA_DIR` (settings + log) and `VVIEW_VSHELL_DIR` (theme) to scratch folders; `VVIEW_TEST_HOOKS=1` + WM_COPYDATA kind
  0x76560002 (`TestHooks.cs`: open / next / prev / zoom / fit / actual / rotate / play / seek / details / full / slideshow / options [page] /
  dump <file>); `VVIEW_TEST_ASK=yes|no` answers Ask. `VVIEW_VLC_LOG=1` copies VLC's own messages into the log. Stop only the test's
  own process by its PID - V may be running vView.

## Releases and updates

- Releases are on the update site https://vsuite.vrsion.com (since 0.1.17 - GitHub before; 0.1.17 was the bridge, the last GitHub
  release - build.cmd makes nothing for GitHub now; the zip is dist\vView.zip): build.cmd writes `dist\site\vview\` (vView.zip, the setup, latest.json with each file's
  SHA-256, changelog.md = CHANGELOG.md - the notice lists every version since yours; `build.cmd pre` -> `vview\pre\`) via vSuite.Core's
  `scripts\site-release.ps1`; V copies `dist\site\` over the site's root by hand (only the newest release is kept there). The setup is **in every release** (V, 2026-11-03; `nosetup` skips it;
  `installer\vView.iss`, per user, %LOCALAPPDATA%\Programs\vView, "Open with" + Default apps registration). Don't run the installer in tests.
- Updater (`Core/Updater.cs`, `Ui/UpdateCenter.cs` + `MainWindow.Update.cs` from vXplorer, Options > Updates): vView is a folder, so an update
  replaces the folder: zip downloaded + SHA-256 checked, unpacked beside the install as `vView.update`, then a PowerShell watchdog
  (`update.ps1`, written to Log.Folder) waits for vView to exit, renames install -> `vView.old` and `vView.update` -> install (retrying while
  something holds the folder), copies `unins*` across, starts the new exe and waits for `update-ok.txt` (ReportStarted), else rolls back
  (`vView.failed`). Only a copy whose folder has `libvlc` and isn't under `\bin\` can update. Tests: `VVIEW_UPDATE_SITE` (a site serving vview/latest.json) + scratchpad (needs redoing for the site)
  `tupdate.ps1` (a TcpListener on 127.0.0.1 serving a fake release; hooks `updatecheck` / `updateinstall`) - passed: swap, and roll back.
- Default app (`Core/DefaultApp.cs`, Options > General): Windows won't let a program set itself as default (UserChoice hash), so vView
  registers (ProgIds vView.Picture / .Video / .Sound for every type in `Media.AllTypes`, Applications\vView.exe, Capabilities +
  RegisteredApplications, OpenWithProgids) and opens `ms-settings:defaultapps?registeredAppUser=vView`. The installer runs
  `vView.exe --register-types` / the uninstaller `--unregister-types` (one list, in Media.cs). **Tests: set `VVIEW_TEST_REGROOT`
  (e.g. Software\vViewTest) - never register against V's real Software\Classes**; hooks `regtypes` / `unregtypes` only work with it set.
- The notice sits over the picture (a playing video's own window would cover it).

## House rules (as in vShell and vXplorer)

- **Only make a release build when V asks.** Otherwise just make the change and commit it.
- Every user-visible change: bump `<Version>` in `vView.csproj` (0.1.N - next N once the current one has been given to V), add a section
  at the top of **CHANGELOG.md** (`## 0.1.N`, one short line per change), and test steps in **TESTING.md**.
- Commit messages: `0.1.N: what changed` (short, lower-case after the colon), one commit per change set.
- Times shown to the user are **12-hour** (`Format.Stamp`). UI text is plain, friendly English.
- Every colour in a style is a DynamicResource `vx.*` key (same keys as vXplorer - `Theme.Apply` sets them).

## Layout

- `src/Program.cs` - entry: `--make-icon`, one instance per settings folder (`Instance.HandOff`: a second start sends its file to the
  running window), app resources (`src/Ui/Controls.xaml`), settings, theme, `MainWindow`.
- `src/Core/` - `Media` (the file types: pictures = what WIC reads, videos and sounds = VLC; `Folder()` lists a file's folder in Explorer
  order), `MediaItem` (a strip entry), `AppSettings`, `VShellThemeSource`, `Instance`, `Log`.
- `src/Ui/MainWindow.xaml(.cs)` - top bar (menu, name, place "3 of 48", tools, caption buttons), the stage (picture / video / sound card /
  empty state, previous / next buttons), details card, video bar, the strip; opening, moving through the folder, delete (Recycle Bin via
  Microsoft.VisualBasic's FileSystem), show in folder (vXplorer if installed), full screen, slideshow, keys, the menu.
- `MainWindow.Picture.cs` - WIC decode on a worker (OnLoad, file shared Read/Write/Delete), EXIF orientation, metadata (taken, camera,
  exposure), a cache of 5 with the neighbours decoded ahead, fit / zoom at the mouse / drag to pan / rotate (`LayoutPicture`: 100% = one
  picture pixel per screen pixel).
- `MainWindow.Video.cs` - VLC (LibVLCSharp + VideoLAN.LibVLC.Windows, `VideoView`).
- `OptionsWindow.cs` - vShell-style pages (General, Pictures, Video, Slideshow, About) built from the same blocks as vXplorer's Options.
- `ShellIcons.cs` (from vXplorer) - the strip's thumbnails (`RequestThumb`, 4 STA threads).

## Things learned the hard way

- **Never call VLC from the window's thread.** VLC's video window is a child of ours, so VLC's threads sometimes wait on our thread;
  when ours waited on VLC (reading `Time` / `IsPlaying` in a timer, `Stop`), a video froze on its first frame. Every VLC call goes through
  the `Vlc(...)` worker queue, in order; the bar shows what VLC's events reported (`_vTime`, `_vLength`, `_vPlaying`). Never call VLC back
  from inside its own events either (EndReached -> Dispatcher -> worker).
- `Video.MediaPlayer` is set when a video is first shown (the VideoView visible), and `Play` waits for `DispatcherPriority.Loaded` - set
  while collapsed, VLC drew into a window of its own.
- **VLC's plugin cache**: without `libvlc\win-x64\plugins\plugins.dat` VLC loads all ~280 plugin DLLs at every start - 13 s from the
  network share. The csproj's `VlcPluginCache` target runs an installed VLC's `vlc-cache-gen.exe` from inside our libvlc folder (so it
  uses our libvlc version) after Build and Publish; with it VLC is ready in ~0.2 s. VLC also starts in the background at launch.
- Only x64 VLC is copied (`VlcWindowsX86Enabled` / `VlcWindowsArm64Enabled` false), and unused plugin folders are left out
  (`VlcWindowsX64ExcludeFiles`: gui, lua, visualization, stream_out, access_output, mux, services_discovery, control).
- With Blur / Frosted the window is glass and VLC's black is fully transparent - the wallpaper showed beside a video.
  `MakeVideoOpaque` makes the VideoView's host window layered at alpha 255 (as vXplorer's preview handler host).
- VLC writes `SetThumbNailClip failed` and h264 `get_buffer() failed` to stderr - harmless noise (no console in a WinExe).
- Clicks on a video: VLC's window takes the mouse, so the VideoView has **content** - a see-through Grid (`VideoOverlay`) that LibVLCSharp
  shows in an owned window over the video - for the right-click menu, double-click (full screen) and wheel (volume). It's owned, so
  vShell's taskbar doesn't list it. Its click calls Activate() so the keys keep coming to the main window.
- Blur / Frosted (0.1.4) is vSuite's `Glass` under the content (`WindowEffects.Look`), not DWM: the window is opaque to Windows, so the bars beside a video are the theme colour (VLC's window covers the glass layer) - no see-through bars, no layered host needed. **Don't** make VLC's host window layered (WS_EX_LAYERED): with the overlay window on top, videos often stuck on their first frame (1 in 3).
- Audio tracks / subtitles: `AudioTrackDescription` / `SpuDescription` read on the `Vlc` worker, chosen with SetAudioTrack / SetSpu;
  a subtitle file with AddSlave(Subtitle). Streams: `Media(…, FromType.FromLocation, ":network-caching=N")`; `MediaItem.Stream`, no folder.
- What vView goes through (`Media.Folder`): a picture brings its folder's pictures; a video plays alone (V: "like VLC"); a sound
  brings its folder's sounds - the playlist (0.1.16).
- **Music (0.1.16, `MainWindow.Music.cs`, `Core/MusicInfo.cs`)**: the playlist IS `_items` (so place text, Delete, ShowAt work);
  AudioCard is now playing + the playlist card (`vv.TrackItem`). Tags via VLC `Media.Parse(ParseLocal)` on a worker, one at a time,
  current first (`LoadInfo`); covers via IShellItemImageFactory THUMBNAILONLY on an STA thread, else cover/folder/front.jpg. Song end
  -> `MusicStep(next, auto)` (repeat Off/All/One, shuffle with `_shufflePlayed` / `_shuffleBack`); `_playNext` makes the next song
  play even with AutoPlay off. Playlist focus: Delete = off the list, Enter plays, Up/Down move. .m3u/.m3u8 read (UTF-8, else
  Latin-1) and saved as .m3u8. Hooks: repeat / shuffle / ended / plplay / plremove / plmove / pladd / plsave / plsort / pltoggle;
  dump `music:`.
- **Classic player (0.1.16, `ClassicPlayer.xaml/.cs` = view only, `MainWindow.Classic.cs` wires it)**: the main window Hides, the music
  / `_items` / keys stay in MainWindow (`ClassicKey` forwards, minus picture / full-window keys). `MusicView` "Classic" = songs open
  there (`SyncClassic` after ShowAt; a picture / video / ShowEmpty brings the main window back). Its X / Alt+F4 closes vView: its
  Closing cancels and BeginInvokes the main Close (closing the main window from inside its Closing threw). It carries vView's
  Instance mark (WindowIcons), so it forwards WM_COPYDATA to the main WndProc (`TakeCopyData`). Shared row styles in
  `src/Ui/Music.xaml` (merged in Program). The visualiser draws into `BarSet`s (now playing, the display, the shade).
- **Equaliser (`MainWindow.Eq.cs`)**: VLC's `Equalizer` (10 bands + preamp, its presets) set through the Vlc worker, latest values
  only while dragging; kept in settings (EqOn / EqPreset / EqPreamp / EqBands), applied when the player is made. Hooks: classic /
  classicoff / shade / unshade / cleq / cllist / clpin / stop / eqon / eqoff / eqpreset n / eqband b dB / openfolder; dump `classic:`
  `eq:` `bar:`.
- Several windows: `OpenInNewWindow` (Options > General) - a hand-off then opens `new MainWindow`; `ShutdownMode.OnLastWindowClose`;
  Options apply to every window. Each window has its own VLC engine and player.
- Test hooks also: `stream <url>`, `tracks <file>`, `sub <id>`. A stream test needs a server that answers range requests (an MP4 with its
  index at the end won't start otherwise) - an HttpListener on "localhost" isn't reached by VLC (it tries ::1); use 127.0.0.1.
- **Jellyfin (0.1.9, `MainWindow.Jellyfin.cs` + `src/Ui/jellyfin-bridge.js`, embedded)**: the server's own web page in a WebView2 (data in
  `Log.Folder\WebView2`, shared by the windows) over rows 1-3; the bridge script defines `window.NativeShell` (jellyfin-web's hook, as
  Jellyfin Desktop's `native/nativeshell.js` + `mpvVideoPlayer.js`) with one player plugin `vViewPlayer` for videos only. Its play() posts
  the stream URL + start ms + track numbers to vView (`MediaItem.FromJellyfin`), vView posts `state` every 0.5 s (from `_vTime` etc.),
  `ended`, `stop`, `error`; the page's playbackManager does the server reports / next episode. `JellyfinPlay.Done` = the page already
  knows it stopped (don't post `stop` again). Its device profile direct-plays any video (VLC). Only messages from the server's host are
  taken. The stream URL holds the api key - never shown or copied. Tests: scratchpad `fakejf` (ASP.NET page that loads NativeShell plugins
  as jellyfin-web does) on 127.0.0.1 + hooks `jellyfin` / `jfjs <script>` / `jfnext`. Starting a test vView from the share with PowerShell's
  Start-Process hangs (zone prompt) - start it from bash. The fake page doesn't run jellyfin-web's startup, so also check a real server:
  `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=N` and read the page's console over CDP. `AppHost.getDefaultLayout()`
  must not return 'auto' (0.1.10: the layoutManager asked for it again forever - the page stuck on the logo).
- Jellyfin servers (0.1.11): `AppSettings.JellyfinServers` (Name + Address, `JellyfinLast` = Ctrl+J's) - the old `JellyfinServer` string
  moves in on load. One WebView per window; switching navigates it. Sign out / Remove clears one origin only (CDP
  `Storage.clearDataForOrigin`). Hooks `jellyfin <n>` / `jfsignout <n>`. Signed-in tests: copy `%APPDATA%\vView\WebView2` into the test
  data folder (V's vView closed), delete it after.
- **LibVLCSharp leaves the VideoView's content window (VideoOverlay) up when the video is collapsed** - it took every click on what showed
  next (Jellyfin's page after Back). VideoOverlay is collapsed with the video (`Video.IsVisibleChanged`): empty, the window lets clicks through.
- **A paused stream's connection dies** (0.1.13: V paused a Jellyfin video, the PC slept; play did nothing - VLC said "playing", the
  time never moved, no error). Streams are reopened in place (`ReopenStream`: same URL, `:start-time`, audio/sub track ids, subtitle
  slaves re-added) after a pause >= 2 min, when the time hasn't moved for 15 s + caching (`WatchStream`), on an early EndReached or
  an error after it had played; 3 tries, then "lost the connection" (`_streamLost`) until play. Test: scratchpad `freezesrv.py`
  (Python, 127.0.0.1, Range; a `freeze` flag file silences open connections) + hooks `pausedfor <s>` / `stall` / `click` / `seekto <f>`.
- **WebView2's window keeps the keyboard when hidden** - Space did nothing to a Jellyfin video. `TakeKeys` (SetFocus to our hwnd,
  only while active) on HideJellyfinPage, a click on the video and Activated.
- JellyfinHost has a 6 px margin (not maximised): the WebView's own window covered the resize border.
- **Visualiser (0.1.16, `Core/SoundTap.cs` + `MainWindow.Spectrum.cs`)**: Windows' process loopback capture (NAudio.Wasapi 3.x,
  `WasapiRecorderBuilder.WithProcessLoopback(own pid, IncludeTargetProcessTree)` + BuildAsync, 48 kHz float asked for) - only vView's
  sound, VLC untouched (V didn't want all of the PC's sound, nor vView playing the sound itself). On only while AudioCard shows; bars
  compensated for vView's volume. Test: quiet tones from vView vs a SoundPlayer in another process; dump's `spectrum:` line.
- **Up next / Skip intro (0.1.24, MainWindow.UpNext.cs, JellyfinPromptWindow)**: after "play" (which now carries itemId) the bridge's
  extras() posts upnext (the next item in playbackManager.getPlaylist after getCurrentPlaylistIndex: "S1 E4 · Name", series, a
  picture URL from ApiClient.getScaledImageUrl) and segments (/MediaSegments/{id} Intro / Outro - Intro Skipper on Jellyfin 10.10+;
  else Intro Skipper's /Episode/{id}/IntroSkipperSegments), matched to the playing item by id. JellyfinPromptTick (video tick): Skip
  intro while in the intro; Up next from the Outro start (else JellyfinUpNextSeconds before the end), counting that long, then
  JellyfinPlayNext (posts "next" - NextAsked, so the end doesn't post "ended" too) - or, counting to the end, the end plays it.
  Cancel / auto-next off: the end posts "stop" (not "ended"), so the page doesn't go on. Corner windows (owned, no-activate, over Stage's
  bottom right - above VLC / WebView2): CornerWindow in NoticeWindow.cs (also the update notice). Test hooks: jffake / jfupnext /
  jfsegments / jfprompt.
- **Windows' media controls (0.1.23, MainWindow.MediaControls.cs)**: SystemMediaTransportControls for the main window's hwnd
  (SystemMediaTransportControlsInterop.GetForWindow - hence TargetFramework net10.0-windows10.0.19041.0): title / artist / album /
  cover, playing / paused, the timeline; its buttons and seek work vView. Kept in step from _videoTick (MediaControlsTick, only on
  change; the timeline on play / pause / jumps and every 5 s); a stop clears it 1.5 s later if nothing else started (MediaControlsLater,
  no blink between songs). **Not for Jellyfin videos** - vShell's Jellyfin plugin (an IMediaHelper) shows those with the poster, and
  matches Windows' session by title. The same tick sets the taskbar button's progress (TaskbarProgress, TaskbarItemInfo on this
  window and the classic player - vShell's taskbar reads ITaskbarList), Jellyfin included. Test: scratchpad gsmtc.ps1 reads Windows' sessions.
- Animated GIF / WebP (0.1.16): GIF frames put together by hand (`/imgdesc` rect, `/grctlext` Delay / Disposal); WIC's WebP decoder
  gives whole frames but no times - those come from the file's ANMF chunks. Other multi-frame files (TIFF pages, icons) stay still.
- Pictures: Windows' own decoders only (V's call - no ImageMagick). HEIC / AVIF / camera RAW need the Microsoft Store extensions; the empty
  state says which one.
- The icon (`AppIcon`): a soft play triangle holding vShell's three dots (V chose it, 2026-11-03) - `vview.ico` remade with `--make-icon` when the drawing changes. The window icon is set with WM_SETICON at 64 / 16 px (`SetNativeIcons`) and `vShell.LiveIcon` counts from the time - as vXplorer.

## Tests

- `dotnet test tests\vView.Tests` (xunit) - the parts with no window: `VlcLog` (which of VLC's messages go in vview.log). Add tests there for new window-less code.
