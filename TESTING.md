# Testing

## 0.1.33 - signed with the Certum certificate
1. Before building: SimplySign Desktop connected (its tray icon > Connect, the code from the phone). `build.cmd` says "Signed: vView.exe",
   then Inno Setup signs the setup and its uninstaller. Not connected: a local build says "Building unsigned"; `build.cmd upload` stops.
2. dist\setup\vView-setup.exe and the app: right-click > Properties > Digital Signatures - the Certum certificate, with a timestamp.
   Run the setup: Windows names the publisher (not "Unknown publisher"). After installing, the uninstaller in the app's folder is signed too.
3. An update from the update site still installs (the site's SHA-256 is of the signed files).

## 0.1.32: snap layouts on the maximise button
1. Windows Settings > System > Multitasking > Snap windows on, "Show snap layouts when I hover over a window's maximize button" ticked: rest the pointer on vView's maximise button - Windows' layouts menu opens under it; pick one and the window snaps there. The button lights up while the pointer is on it and goes back when it leaves (also when it leaves fast, off the window).
2. Click it: maximises; again: restores (the glyph follows). Maximised and on the other monitor: the menu opens at the button.
3. Untick the setting with vView open: within a few seconds the button is the plain one again (its own hover and tooltip, no menu). Minimise, close and moving the window: as before.
4. Checked before release: a test copy (its own VVIEW_DATA_DIR) answers WM_NCHITTEST with 9 (HTMAXBUTTON) only over the maximise button. Automatic: vSuite.Tests `SnapLayoutsTests`.

## 0.1.31: the glass across monitors
1. With the window see-through (Blur, Frosted or the wallpaper as it is): drag it slowly from one monitor onto the next and back - smooth, each part showing its own monitor's wallpaper, moving with the window.
2. Drag it half off a screen's edge: the background keeps moving with it (it used to stop at the edge).
3. Drag it around one monitor: smooth; let go - it's live at once (hover highlights), text as sharp as before.

## 0.1.30: huge pictures, pictures that changed
1. A picture over 120 megapixels (13000 × 10000): it opens (at 12000 px across) and vView's memory stays in the hundreds of MB, not well over a GB; Dimensions and the camera details still show. Going through a folder of big scans: memory levels off (about 700 MB of pictures kept, the newest two always).
2. Normal pictures, animated GIF / WebP: as before.
3. View a picture, change and save it in another program, go to the next and back: the new picture shows. Open a picture while it is still being copied in ("can't show this picture"), go away and back once the copy has finished: it shows.

## 0.1.30: Delete where there is no Recycle Bin
1. A picture on a network drive (S:\…), a \\server\share path or a USB stick, Delete: "Delete it for good? … is on a drive with no Recycle Bin …" - also with "Ask before deleting" off. Delete for good: gone, the next one shows, the log says "Deleted for good". Cancel: nothing happens.
2. A picture on C: or another internal / external disk: as before - "Move to the Recycle Bin?" (or straight away with asking off), and it is in the Recycle Bin.

## 0.1.29: VLC's messages in the log
1. Play a few videos and a Jellyfin episode, seek, go to the next one: vview.log has no "failed to create audio output", timestamp,
   "avcodec_send_packet" or TLS-session lines. A stream that won't open still says so ("Failed to open …").
2. `dotnet test tests\vView.Tests`: passes.

## 0.1.28: the update's progress bar (vSuite.Core 1.0.24)
1. Update to a newer release from this version (the running copy downloads it): the notice's bar and percentage move steadily with the
   download, and the window stays responsive during "Checking and installing…". (Updating TO this version still shows the old behaviour -
   it's the version doing the download that matters.)
2. Settings / Options made small enough to scroll: its scrollbar is the thin one that widens when pointed at, with a ~40 px thumb, and sits a
   little in from the window's right edge - grabbing it never starts a resize.

## 0.1.27: scrollbars (vSuite.Core 1.0.23)
1. A long playlist / Options: the scrollbar thumb stays about 40 px tall and widens when pointed at.

## 0.1.26: Up next names the next episode
1. Play an episode from a series in Jellyfin and let it reach Up next: it shows the next episode (S1 E2 · its name, its picture).
2. Let the next episode play by itself (or Play now): at the end of that one, Up next shows the episode after it - not the one playing.
   (Before: the page moved its queue on only after vView took the new episode, so "the one after the current" was the episode itself.)
3. The last episode of a season / a film: no Up next.

## 0.1.25: vSuite.Core 1.0.20
Only the library moved on (it added TermLink, which vView doesn't use). Check that vView starts, follows a theme change, plays a video and
that Options > Updates still checks.

## 0.1.24: Up next and Skip intro for Jellyfin episodes
Needs Jellyfin 10.10+ with Intro Skipper (its intros / credits as media segments) for the intro and credits times.
1. Play an episode from a series in Jellyfin's page (with more episodes after it). During its intro: a Skip intro button at the bottom
   right. Click it: the video jumps to the end of the intro; the button goes. Let an intro play out: the button goes by itself.
2. At the credits: "Up next" with the next episode's picture, "S1 E4 · Name", the series and "Plays in 20 s" counting down. At 0 the next
   episode starts (the rest of the credits skipped), and Jellyfin's page shows it as playing.
3. Play now: the next one starts straight away. Cancel: the card goes; the episode plays to its end and stops there (back to Jellyfin's
   page, the episode marked watched) - it doesn't go on to the next.
4. An episode without credits times on the server: Up next shows 20 s before the end, counts to the end, and the next one plays.
5. Pause during the countdown: it says "- paused" and stops counting; play: it goes on.
6. Options > Jellyfin > Watching episodes: Up next off - no card (the next one still plays at the end, as before); Count down 10 / 30 s;
   "Play the next episode when it gets to 0" off - no countdown, and the episode stops at its end unless you press Play now; Skip intro off.
7. A movie, or the last episode in the queue: no Up next. Full screen: the card and button sit in the screen's bottom right corner.

## 0.1.23: Windows' media controls and taskbar progress
1. Play a song from your PC: vShell's Now playing shows its title, artist, album, cover and the time; Windows' volume pop-up shows it too.
2. Now playing's play / pause / next / previous (and the keyboard's media keys) work vView; seeking on its bar moves the song.
3. A song in the classic player: the same. A video from your PC: it shows with the file's name.
4. Next song (or the next one starting by itself): Now playing changes to it without blinking off between them. Stop / open a picture:
   Now playing lets go of vView within a couple of seconds.
5. A Jellyfin video: Now playing shows it as before (vShell's Jellyfin plugin, with the poster) - vView doesn't add a second entry.
6. vView's taskbar button (vShell's taskbar and Windows'): a bar along it shows how far through the song / video it is - Jellyfin videos
   too; paused: the paused colour. A picture, or a live stream: no bar. The classic player's button shows it as well.

## 0.1.22: songs opened while vView is open (Open in a new window on)
1. Options > General > Open in a new window on, songs in the classic player (Options > Audio). Open a picture or a video in vView.
2. Double-click a song in Explorer: only the classic player appears, playing it - no large empty window beside it. The picture / video
   window stays as it was.
3. Double-click another song: the same classic player plays it (its folder becomes the playlist) - no second player, no new window.
4. Close the classic player (its X): it closes; the picture / video window stays.
5. A picture or a video double-clicked while the classic player plays: it opens in a new window, as before.
6. Open in a new window off: a song goes to the classic player as before.

## 0.1.21: the update notice over videos and Jellyfin
1. With a newer vView on the site, play a video (from your PC or Jellyfin), then Check for updates (the menu, or Options > Updates): the
   notice shows at the bottom right of the picture area, over the video - not hidden under it. Same over the Jellyfin page.
2. Move and resize the window, go full screen and back: the notice stays in the picture area's bottom right corner.
3. Click its buttons (Later, What's new, Skip): they work, and the keys still go to vView afterwards (Space pauses the video).
4. Minimise vView, or switch to the classic player: the notice goes with the window; restore it: it's back.
5. Install and restart from it over a video: the progress shows in it, then vView restarts on the new version.

## 0.1.20: no file buttons for Jellyfin and streams
1. Play a video from Jellyfin: the top bar has no Slideshow, Show in its folder or Delete (Move to the Recycle Bin) buttons; Details and
   Full screen are still there.
2. Back to Jellyfin, then open a picture from your PC: all three are back. A video from your PC: Show in its folder and Delete show.
3. Open a network stream (Ctrl+N, or a link dropped on the window): the three buttons are hidden too.
4. A song: Show in its folder and Delete show, Slideshow doesn't (as before).

## 0.1.19: the classic player's menu and Add a folder
1. Play a song in the classic player. Click the vView icon at the top left: the vView menu opens under it (Open file…, Open folder…,
   Open network stream…, Jellyfin, Show in its folder, Copy, Options, Check for updates, About, Close). Dragging the title row still
   moves the player; double-clicking the title still folds it into one line.
2. Open file… / Open folder… with a song or a music folder: it plays in the classic player. With a picture or a video: the full window
   comes back with it.
3. Jellyfin (a server, or Ctrl+J): the full window comes back with the page. Play a song afterwards: it opens in the classic player again.
4. Keep the classic player on top (the pin), then Open file…, Add songs, Add a folder, Save the list and Open network stream…: each
   opens in front of the classic player, not behind it.
5. The playlist's new folder button (between + and Save): pick a folder - its songs go on the end of the list (subfolders too, as a
   drop). Cancel: nothing changes.

## 0.1.18: the first site-only release
1. On 0.1.17, with 0.1.18 copied to the site: Options > Updates > Check now (or the check at start): 0.1.18 is offered and the
   notice lists the changes of 0.1.18 and 0.1.17.
2. Install: vView restarts on 0.1.18; its log shows it read vsuite.vrsion.com, no api.github.com.
3. A copy older than 0.1.17 (GitHub only): it updates to 0.1.17 from GitHub, then to 0.1.18 from the site.

## 0.1.16: animated pictures, drag and drop
1. Open an animated GIF (one with a see-through background, and one that only redraws part of the picture each frame): it plays at
   its own speed, over and over, looking as it does in a browser. Same for an animated WebP. Details: "Animation: N frames · N s".
2. Zoom, drag and rotate while it plays; Right / Left to the next picture and back: it plays again from the start. A still GIF and a
   multi-page TIFF stay still.
3. Drop a folder from Explorer: its pictures, in Explorer's order, from the first. A folder of only videos: its first one plays.
4. Drop three pictures from different folders: "1 of 3", going through just those. Drop one picture: its whole folder, as before.
5. Drop a link from a browser's address bar (http(s)://…/video.mp4): it plays as a network stream. Dropping plain text does nothing.
6. Drop a picture onto a playing video (full screen too): the picture opens. Drop a text file: the top bar says "Nothing here vView
   can show" and the picture or video stays.
7. On Jellyfin's page, drop a file on the page: the page stays (nothing happens).
8. Play a song: the bars under its name move with it (bass on the left, treble on the right), each with a cap that holds the peak a
   moment. vview.log: "Visualiser: listening to vView's sound".
9. Pause it and play something in a browser or another player: the bars stay flat. Play again: only vView's song shows.
10. Turn vView's volume down: the bars stay about the same height (they show the song, not the volume). Mute: flat.
11. Open a picture or a video: the visualiser stops listening (no "listening" line until a sound shows again). Between songs it
    keeps listening (one "listening" line for a whole album).

### Music player
12. Open a song from an album folder: now playing shows its cover, title, "Artist · Album · Year", format chips (MP3 · 44.1 kHz ·
    Stereo · 320 kbps); the playlist lists the folder's songs with titles, artists and lengths, the one playing marked with a speaker.
    Top bar: "3 of 12 · folder". A song without tags shows its file name; one without a picture shows the note (or the folder's
    cover.jpg / folder.jpg).
13. Let a song end: the next plays. At the end of the list it stops (repeat off). Repeat (the bar's loop button) cycles off ->
    whole list (back to the first) -> this song (plays it again). Shuffle: every song once, at random, before any plays again;
    previous goes back the way it came.
14. Previous more than 3 s into a song: back to its start; in the first 3 s: the song before. Page Up / Down and P / N do the same.
15. Drag a song to another place in the list: the order and the numbers change, the one playing keeps playing. Drop songs from
    Explorer onto the list: added where they're dropped. + adds songs from a dialog. Right-click: remove, sort (title / artist /
    album / file name), save. Select a song and press Delete: off the list, not the Recycle Bin. Remove the one playing: the next
    one plays.
16. Save the list (the disk button), open the .m3u8: the same songs in the same order. Open an .m3u from another player (relative
    paths, accented names): its songs; ones it lists that aren't there are skipped.
17. Drop three songs from different folders: just those three. Drop an album folder: its songs.
18. The list button hides / shows the playlist (remembered). Make the window narrow: the cover goes above the words; the list
    button then shows the playlist instead of now playing.
20. Start vView with nothing: "Open a media file, or drop one here", with Open… and Open a folder…. Ctrl+Shift+O / the menu's Open
    folder…: a folder of songs -> the playlist; of pictures -> its pictures; with nothing vView shows -> says so. Playlist right-click
    > Add a folder…: its songs added at the end.
21. A song: no slideshow button; a picture: it's back.

### Classic player
22. A song playing, the classic-player button at the top (left of full screen; not there for pictures and videos): the full window goes, a small window shows the time (playing), the visualiser,
    the song scrolling (if long), format chips, the volume, the seek bar, the buttons and the playlist. Click the time: "-3:05"
    (time left); again: time gone. Drag the window by its title; it opens there next time.
23. Play / pause, stop (back to 0:00, the stop mark), previous / next, shuffle, repeat (off / all / one), the volume bar and the wheel
    over the display, mute. Space, Page Up / Down and the arrows work in it.
24. EQ: the equaliser under the player. Choose a preset (Rock, Club …): the sliders move, the sound changes, "On". Drag a slider:
    "Custom". Double-click one: 0. Off: the sound is back to normal, the sliders dim. Close and start vView: the same.
25. List: the playlist under the player (double-click plays, drag to reorder, drop files, Delete / right-click as in the full window).
26. The arrow (or double-click the title): one line - the time, small bars, the song, previous / play / next - on top of other
    windows. The down arrow: the whole player again. The pin: the whole player stays on top.
27. While in the classic player, open a song from Explorer: it plays in the classic player. Open a picture or a video: the full
    window comes back with it; the next song goes to the classic player again. The full-window button (top) goes back and stays.
28. Close the classic player (its X, Alt+F4 or the taskbar): vView closes (no error in vview.log).
29. The visualiser's bars stay inside the display's box.
30. Drag the line at the bottom of the classic player's list down: the list (and the window) get longer; up: shorter. Start vView
    again: the same length.
31. Options > Audio (or the classic player's gear, Ctrl+, in it): Songs play in the classic player; close vView; double-click a song
    in Explorer: only the classic player opens (the full window doesn't flash up). The full window: the next song opens there.
    The switches change the classic player at once (on top, equaliser, list, time left) and Use the equaliser turns it on / off.
32. Started straight in the classic player (step 31): its bars move with the music at once (no switching to the full window and
    back). Next song: still moving.
19. The wheel over now playing: the volume. After a song, open a picture: the strip, side buttons and zoom tools are back; a
    video: the audio-track and subtitle buttons are back, the loop button is "Play again from the start".

## 0.1.15: installer without the version in its name
1. build.cmd: dist\setup\ and dist\release\ have vView-setup.exe (no version number); run it - it installs / upgrades as before, and
   Programs and Features shows the right version.
2. Attach it to the release: https://github.com/version-au/vView-releases/releases/latest/download/vView-setup.exe downloads it.
3. vShell's Settings > Updates > vSuite apps with vView not installed: Install finds the new vView-setup.exe (and an older
   release's vView-setup-<version>.exe while the newest release still has the old name).

## 0.1.14

1. Open a video in a folder of episodes (ep 1, ep 2, ep 10): the bar shows previous / play / next. Next goes to ep 2, then ep 10 (Explorer's order); at the last one the top bar says "That's the last one in this folder". Previous goes back the same way. Page Down / Up and N / P do the same.
2. A Jellyfin episode: next plays the following episode in vView, previous the one before (Jellyfin's own queue).
3. A network stream (Ctrl+N): no previous / next buttons.
4. A sound: next / previous go through the folder's sounds only.

## 0.1.13

1. Play a Jellyfin video from the page, then press Space without clicking anything: it pauses; Space again plays.
2. Click the video: it pauses; click again: it plays. Double-click: full screen, still playing (or still paused).
3. Right / Left (and L / J): forward / back 10 s; Ctrl+Right / Left: a minute; Home: the start; 5: halfway; K: pause / play. Page Down / Up on a Jellyfin episode still go to the next / previous one.
4. Pause a Jellyfin video for 3+ minutes (or let the PC go to sleep), press play: after a second or so it carries on from the same spot, same audio and subtitles, without the page showing. vview.log: "Stream: play pressed after … paused - opening it again at …". Jellyfin's resume point keeps updating.
5. Pause for under a minute, play: it just carries on (log: "Stream: playing again after …").
6. A picture still: Left / Right go through the folder, 0 / 1 / R as before.

## 0.1.11

1. Updating from 0.1.10: Options > Jellyfin lists your server (moved over from the old setting); Ctrl+J opens it, still signed in.
2. Add a second server (a name, its address): the line under the address says if it isn't one, or is already listed. Jellyfin in the menu now opens to both, the open one ticked; pick the other - its page shows, the top bar says its name.
3. Sign out on one server's row: only that one asks you to sign in again. Remove a server: it goes from the list and the menu; if it was showing, the next one shows.
4. Play a video, then Back (Esc or the arrow): click another video in the page - it opens and plays.
5. With the page showing (not maximised), drag the window's left, right and bottom edges: it resizes.

## 0.1.10

1. Ctrl+J with a real Jellyfin server set: the page gets past the Jellyfin logo to the sign-in (or home) page, laid out as in a browser. Then the 0.1.9 steps.

## 0.1.9

1. Ctrl+J with no server set: Options opens on Jellyfin. Type your server's address (e.g. 192.168.1.20:8096) - the line under it shows Jellyfin's page address. Open Jellyfin: the page fills the window; sign in.
2. Browse to a film or episode with a resume point and press Play (or Resume): the page goes, the video plays in vView from that point, the title reads "Show · S1E2 · Name". Details (I) show "Video from Jellyfin" and the server - no address.
3. Pause, seek, then Esc (or the arrow at the top left): back on the page where you were; the item's resume point is where you stopped (open it again to check).
4. Play an episode to its end with "Play next episode automatically" on in Jellyfin's settings: the next one starts in vView; it's marked watched in Jellyfin.
5. An MKV / HEVC file: plays straight away (Jellyfin's dashboard > Activity shows Direct Play, not Transcoding). A video with subtitles chosen in Jellyfin shows them.
6. Music in the page plays in the page's own player (vView doesn't take it).
7. Close vView while a Jellyfin video plays: Jellyfin keeps the position. Options > Jellyfin > Sign out: the page asks you to sign in again.
8. A link out of Jellyfin (IMDb …) opens in your browser, not in vView.

## 0.1.7

1. vView open on Options > Updates; vShell > Settings > Updates > vSuite apps > vView > Include pre-releases: flip it - vView's switch follows. Flip vView's - vShell's follows.

## 0.1.6

0. With a newer release out: vShell > Settings > System > vSuite apps > vView > Update - vView shows its update notice and installs.
1. Options: hover an (i) (Look > Use the theme's opacity) - a help card opens under it. Buttons look like vShell's settings' buttons. Everything works as before.

## 0.1.5

0. Options > Look (new page) > Use the theme's opacity: off - vView's window goes solid at once; on - see-through again.
1. vShell > Appearance > Background: Off, opacity 50%: vView's window shows the wallpaper (sharp) through the theme colour, as a bar does. A playing video is unaffected.

## 0.1.4

1. vShell > Appearance > Background: Frosted, lower the opacity: vView's window shows more of the blurred wallpaper, like the bars.
2. Play a video: it plays as before; the bars beside it are the theme colour (no glass there - the video's own window covers it).
## 0.1.3

1. Everything looks and works as in 0.1.2: pictures, a video with its bar, Options (all pages), the update notice.
2. vShell > Wallpaper Theme on: within a few seconds vView takes the wallpaper's colours. Off: back to the saved colours.
3. Options > Updates > Check now: the line updates.

## 0.1.2

1. Options > General > Default app: the line under it says how many of the 80 types vView opens. "Make vView the default…" opens Settings > Default apps on vView's page; Set default there, come back to Options: the count goes up. A .jpg / .mkv / .mp3 double-clicked in Explorer opens in vView.
2. Install with dist\setup\vView-setup-0.1.2.exe: right-click a .mkv > Open with lists vView. Uninstall: vView leaves Open with and Default apps.
3. Options > General: "Show the folder along the bottom" reads "For pictures only - the folder's other pictures; click one to show it".

## 0.1.1

1a. Install with dist\setup\vView-setup-0.1.1.exe: vView in the Start menu; right-click a .jpg / .mp4 > Open with: vView is listed; Settings > Apps > Default apps > vView lists its file types. Uninstall from Settings: asks whether to keep the settings.
1b. Options > Updates: "You have vView 0.1.1 - the newest …" (or a newer one with Install). When a newer release is on GitHub: the notice over the picture, Install and restart - vView closes and comes back as the new version, from the same folder.
1. Open a picture: the strip shows only the folder's pictures (no videos or sounds). Open a video or a sound (from Explorer or Open…):
   it plays on its own - no strip, Left / Right do nothing, the top shows its folder.
2. Ctrl+N (or the menu at the top left > Open network stream…): paste an rtsp:// / rtmp:// / http(s):// address, Play. A camera or live
   stream plays and shows "Live" (the timeline can't be dragged); the address is in Recent next time; an address on the clipboard is
   filled in.
3. Right-click a playing video: Pause, Audio track ▸, Subtitles ▸, Full screen, Open file…, Open network stream…, Copy path. Right-click a
   picture: the same without the video items.
4. Under a video: the ♫ button lists the audio tracks (a film with several languages); CC lists the subtitles, Off, and Add a subtitle
   file… (an .srt with the video's name beside it shows by itself).
5. Mouse wheel over a video: volume. Double-click it: full screen.
6. Options > General > "Opening a file while vView is open": Open a new window - double-clicking another file in Explorer / vXplorer opens a
   second vView window; Use this window - it replaces what's showing.
7. The new icon (a play shape with the three dots) in the menu button, on the taskbar and pinned - in the theme's colours; the exe's own icon in vShell's default colours.

## 0.1.0

1. Open a photo with vView (drag it onto vView.exe, or Open… from the menu): it fits the window, in your vShell colours; the title shows
   "12 of 48 · folder"; the strip along the bottom shows the folder with this one ringed.
2. Left / Right, Page Up / Down, the side buttons (move the mouse over the picture) and clicking in the strip go through the folder; the
   next picture shows at once. Home / End: first / last.
3. Mouse wheel zooms at the mouse; drag the picture around when it's bigger than the window; "Fit · 38%" switches fit / actual size;
   + / - / 0 / 1; R and Shift+R turn it. A phone photo taken sideways shows the right way up.
4. I (or the ⓘ button): details - size, dimensions, taken, camera, exposure.
5. A video (MP4, MKV, MOV …): it plays straight away, the bar shows the time and length; Space pauses; click / drag the timeline; Shift+Left /
   Right jump 10 s; volume bar, M mutes, Up / Down volume; the repeat button plays it again when it ends. With Frosted glass on, the bars
   beside the video are black (not see-through).
6. A sound (MP3, FLAC, WAV): a note and its name, the same bar.
7. F11 or a double-click: full screen (Esc back). F5: slideshow (each picture for the set seconds; Esc stops).
8. Delete: asks, then the file goes to the Recycle Bin and the next one shows.
9. Show in its folder: vXplorer opens the folder (or Explorer, with the file selected, if vXplorer isn't installed).
10. Options (Ctrl+,): pages on the left; each switch / choice takes effect at once (the strip, details, wheel = next one, small pictures
    fill the window, autoplay off = a video opens paused).
11. Change the theme in vShell: vView follows straight away. Pin vView on vShell's taskbar: a sharp icon.
12. With vView open, double-click another picture set to open with vView: it shows in the same window.
13. A HEIC / AVIF / RAW file without its Microsoft Store extension: "vView can't show this picture. Windows needs …".

## Review fixes (0.1.8)
1. vShell's Update while offline: nothing installs later by itself.
2. Any question: Tab to the other button, Enter - that button's answer is taken.
3. Desktop set to a solid colour: the see-through background shows that colour, not the old picture.

## 0.1.12: the wallpaper read right (vSuite.Core 1.0.14)
1. vShell with a different wallpaper on each monitor, and its Wallpaper Theme on. Start vView with Blur / Frosted: on each monitor the
   background shows that monitor's own picture, blurred (not the one Windows has in the registry).
2. The colours follow the main monitor's picture; change it in vShell: within about 15 s vView takes the new colours.
3. Wallpaper Theme off, plain colours: nothing changes from before.

## 0.1.17: updates from vsuite.vrsion.com (the bridge release)
Publish: build.cmd. dist\release\ goes to GitHub one last time (copies before 0.1.17 only know GitHub); dist\site\ is copied over the
site's root (vview\latest.json, vView.zip, vView-setup.exe, changelog.md).
1. https://vsuite.vrsion.com/vview/latest.json shows 0.1.17 in a browser; .../vview/vView-setup.exe downloads.
2. A copy on the version before: its update (from GitHub) installs 0.1.17 as before.
3. On 0.1.17, Options > Updates > Check now: up to date, no GitHub error; click it many times: never "limiting requests". "vSuite
   website" opens the site.
4. A newer test build copied to the site: the notice comes up listing the changes of that version AND 0.1.17; Install: it restarts on
   the new version (and goes back if it won't start, as before).
5. build.cmd pre, copied up (it goes to vview\pre\): offered only with Include pre-releases on, marked as a pre-release.
6. vShell's Settings > Updates > vSuite apps shows the site's version for vView, and Update / Install work from it.
