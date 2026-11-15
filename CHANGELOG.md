# Changelog

## 0.1.29 · release
- vview.log keeps VLC's real problems only, not the messages it gives in normal playing (they filled the log).
- Behind the scenes: the update notice's corner window is the shared library's (the same as vTerm's).

## 0.1.28 · pre-release
- The update notice's progress bar follows the download, instead of standing still until the end (vSuite.Core 1.0.24).
- Options: the scrollbar is the new one too, and keeps clear of the window's resize edge.

## 0.1.27
- Scrollbars easier to grab - never a tiny thumb in a long playlist, wider when you point at them (vSuite.Core 1.0.23).

## 0.1.26 · release
- Fixed: Jellyfin's Up next could show the episode that's playing (and the series' picture) instead of the next one.

## 0.1.25
- The suite's shared library brought up to date (nothing you'll notice in vView).

## 0.1.24
- Jellyfin episodes: "Up next" near the end - the next episode with a countdown, Play now and Cancel - as Jellyfin's own player.
- Jellyfin episodes: a Skip intro button during the intro (with Intro Skipper on the server).
- Options > Jellyfin > Watching episodes: Up next on / off, how long it counts down, whether the next one plays by itself, Skip intro.

## 0.1.23
- Songs and videos playing in vView show in vShell's Now playing and Windows' volume pop-up, and the media keys work them (Jellyfin videos stay with vShell's Jellyfin plugin).
- vView's taskbar button shows how far through a song or video it is (Jellyfin too), and when it's paused.

## 0.1.22
- Opening a song while vView is open (Open in a new window on) no longer leaves a large blank window beside the classic player.
- A song opened while the classic player is playing goes into that player, not a second one on top of it.

## 0.1.21
- The update notice shows over a playing video and the Jellyfin page, not underneath them.

## 0.1.20
- Playing from Jellyfin (or another network stream): no slideshow, Show in its folder or Delete buttons at the top.

## 0.1.19
- The classic player's vView icon (top left) opens the vView menu, as in the full window: open a file, folder or stream, Jellyfin, Options …
- An Add a folder button on the classic player's playlist, beside Add songs.
- Jellyfin from the classic player (its menu or Ctrl+J) brings the full window back with the page.
- Open, Add songs, Save the list and the stream box open over the classic player, not behind it.

## 0.1.18
- The first release from vsuite.vrsion.com only - a test that updating from the site works.

## 0.1.17
- Updates now come from vsuite.vrsion.com instead of GitHub - no more "GitHub is limiting requests".
- The update notice lists the changes of every version since yours, not just the newest one.
- Options > Updates: "Releases page" is now "vSuite website" (vSuite.Core 1.0.18).

## 0.1.16
- Animated GIFs and WebPs play (they showed their first frame only), at their own speed, over and over; Details shows how many frames and how long.
- Drop a folder on vView to go through its pictures (or play its first video or sound), drop several pictures to go through just those, or drop a link from your browser to play it as a network stream.
- Dropping a file on a playing video opens it (the video took the drop before); a file dropped on Jellyfin's page no longer replaces the page.
- A sound shows a visualiser: bars that move with the music, from vView's own sound only (other apps, system sounds and calls don't show in it).
- Music: a song shows "now playing" - its cover (the picture in the file, or a cover.jpg / folder.jpg beside it), title, artist, album, year and format - with a playlist beside it.
- The playlist starts with the song's folder; songs play one after another. Shuffle and repeat (off / the whole list / this song) are in the bar; previous goes back to the start of a song if it's more than 3 seconds in.
- Change the playlist: drag songs up and down, drop songs from Explorer onto it, Add songs, right-click to remove or sort by title / artist / album / file name; Delete there takes a song off the list (it stays on your PC). Save it as a playlist (.m3u8), and open .m3u / .m3u8 playlists.
- Drop several songs (or a folder of them) on vView to play just those. The list button hides the playlist; in a narrow window it shows the playlist instead of now playing.
- The classic player: a small window for music (the button at the top, beside full screen) - a display with the time (click it for the time left) and the visualiser, the song scrolling along, play / stop / previous / next, shuffle and repeat, the volume - with an equaliser and the playlist under it (EQ and List). Songs keep opening in it until you go back to the full window; a picture or a video opens in the full window.
- One line: the classic player's arrow (or a double-click on its title) folds it into one line that stays on top of other windows. The pin keeps the whole player on top.
- The classic player's playlist can be made longer or shorter: drag its bottom edge. Its gear opens Options.
- Options > Audio: whether songs play in the full window or the classic player (a song double-clicked in Explorer then opens straight in the classic player), the playlist beside songs, the classic player's equaliser, list, time left and keep-on-top, and the equaliser on or off.
- An equaliser: ten bands and a preamp, with VLC's presets (Rock, Pop, Classical, Club …); it stays as you set it for every song.
- Open folder… (Ctrl+Shift+O, in the menu and on the empty window): a folder's pictures, or its songs as the playlist. The playlist's right-click menu can add a folder too.
- The empty window says "Open a media file, or drop one here". The slideshow button only shows for pictures.

## 0.1.15
- The installer is now called vView-setup.exe in every release (no version in the name), so the latest one always has the same download link. (shared library 1.0.17)

## 0.1.14
- Previous and next buttons beside play: a Jellyfin video goes to the previous / next episode; a video or sound on your PC to the previous / next one in its folder. Also Page Up / Page Down, or P / N.
- The strip's thumbnails come from Windows' cache first and show faster (shared library 1.0.16).

## 0.1.13
- Jellyfin and other streams: a video paused for a long time (or left while the PC slept) plays again when you press play - vView reconnects in place, where you were, with the same audio and subtitles.
- Streams: a video that stops moving while playing (the connection dropped) reconnects by itself; if the server has gone, it says so, and play tries again.
- Space pauses a Jellyfin video (the hidden page no longer keeps the keyboard).
- Click a video to pause or play it; double-click still goes full screen.
- Video keys: Left / Right (or J / L) go back / forward 10 seconds, Ctrl+Left / Right a minute, K plays or pauses, Home goes to the start, 1-9 jump to 10%-90%.
- The log notes pauses, reconnects and VLC's errors for videos, to help track down playing problems.

## 0.1.12
- The see-through background and the Wallpaper Theme always read the picture each monitor really shows (shared library 1.0.14).

## 0.1.11
- Jellyfin: more than one server - add them in Options > Jellyfin (each with a name of your own); Jellyfin in vView's menu lists them, Ctrl+J opens the last one. Each server keeps its own sign-in.
- Jellyfin: after going back from a video, the page takes clicks again (you couldn't pick another video).
- Jellyfin: the window resizes from its sides and bottom while the page shows.

## 0.1.10
- Jellyfin: the page no longer stops at the Jellyfin logo - it opens to the sign-in.

## 0.1.9
- Jellyfin: browse your Jellyfin server on its own page inside vView (Ctrl+J, or Jellyfin in the menu; the address goes in Options > Jellyfin).
- Videos you play there play in vView with VLC, from where you left off, with the audio and subtitles chosen in Jellyfin - and where you got to and what you've watched go back to Jellyfin.
- Esc or the arrow at the top left goes back to Jellyfin; Page Down plays the next episode; when one ends, Jellyfin's next one plays (if it's set to).

## 0.1.8
- Updates: Update in vShell's Settings no longer leaves an install waiting for later if the check fails, never installs a version that was skipped or didn't start, and a release without its checksum isn't installed.
- Questions: Enter presses the button that has the focus (Tab moves it).
- The see-through background asks Windows about the wallpaper less often, and a plain-colour desktop shows that colour (not the old picture).

## 0.1.7

- Include pre-releases (Options > Updates) can be changed from vShell's Settings > Updates > vSuite apps - the switch follows straight away.

## 0.1.6

- vShell's Settings > System > vSuite apps can update vView: it checks and installs straight away (started with --update, or the open vView is asked).
- With a plain-colour desktop the see-through window shows that colour, not black.
- Options looks like vShell's settings: the (i) opens a help card, and the buttons and other controls are the same as vShell's.

## 0.1.5

- Options has a Look page (as vXplorer's) with Use the theme's opacity: switch it off for a solid window.
- With Blur and Frosted off, the window shows your wallpaper (not blurred) through the theme colour at your opacity, matching the bars and widgets - it was solid.

## 0.1.4

- Blur and Frosted are drawn as vShell's bars draw them - your blurred wallpaper under the theme colour - so vShell's opacity setting now shows (Windows' Mica hid it). (Beside a playing video the bars are the theme colour.)

## 0.1.3

- Follows vShell's Wallpaper Theme: with it on, vView takes the colours vShell works out from your wallpaper.
- Picks up vShell's theme when vShell is installed while vView is running, without a restart.
- Built on vSuite.Core, the library vView now shares with vShell and vXplorer (the theme, glass, questions, log, updates and Options are the same code in each).

## 0.1.2

- Options > General > Default app: make vView the default for pictures, videos and sounds (it signs itself up for all 80 types and opens Windows' Default apps page on vView).
- The installer offers vView for all the types it opens, not just the common ones.
- Options > General: the folder along the bottom says it's for pictures only.

## 0.1.1

- Updates, as in vXplorer: vView checks GitHub a minute after it starts and then about once a day, shows a notice when there's a new version, and installs it with one click (it goes back to this version if the new one won't start). Options > Updates, and Check for updates in the menu.
- An installer: vView in the Start menu, and in "Open with" and Settings > Default apps for pictures, videos and sounds.
- A new icon: a soft play shape holding vShell's three dots, like vXplorer's folder - in your theme's colours.
- Network streams: Open network stream (Ctrl+N) plays rtsp://, rtmp://, http(s)://, udp:// … addresses - from the menu at the top left or a right-click; recent ones are listed. A live stream shows "Live".
- Right-click in the window or on a playing video: a menu with play / pause, audio track, subtitles, full screen, open and copy.
- Audio track and subtitle buttons under a video: choose the audio track, choose or turn off subtitles, or add a subtitle file (one with the video's name is picked up by itself).
- Opening a video or a sound just plays it, as VLC does; the strip along the bottom is for pictures only, and shows only pictures.
- Options > General: a file opened while vView is open can show in this window or open a new one.
- Options > Video: how much of a network stream to hold before it plays (0.3, 1 or 3 seconds).
- The mouse wheel over a video changes the volume; a double-click on it goes full screen.

## 0.1.0

- First version: pictures, videos and sounds in your vShell theme, following it as it changes (Blur / Frosted glass too).
- Pictures with Windows' own picture support: fit to the window, zoom at the mouse (wheel, + / -), drag around, actual size, turn (R), turned the way the camera says.
- Videos and sounds with VLC's engine: practically every format; play / pause, the timeline, volume, mute, play again from the start.
- The folder along the bottom with thumbnails; Left / Right, the wheel or the side buttons go through it; the pictures either side load ahead.
- Details beside the picture (I): size, dimensions, when it was taken, camera, exposure; a video's length.
- Full screen (F11 or a double-click), slideshow (F5), move to the Recycle Bin (Delete), show in its folder (vXplorer if installed), copy / copy path, open with.
- Options in vShell's style: the strip, details, the mouse wheel, small pictures, autoplay, repeat, graphics card decoding, slideshow timing.
- Opening another file while vView is open shows it in the same window.
