# vView  (v0.1)

A picture, video and sound viewer for Windows 11 in **vShell's** look. It reads the theme from vShell's settings and follows it live -
colours, the Wallpaper Theme and the Blur / Frosted background. C# / .NET 10 + WPF, the same stack as vShell. It works with or without
vShell running (without vShell installed it uses vShell's default theme). Part of the **vSuite** apps with vShell and
[vXplorer](https://vsuite.vrsion.com/).

Pictures are read by Windows' own picture support (Windows Imaging Component) - whatever Windows can open, vView can. Videos and sounds
play with VLC's engine (libVLC, through LibVLCSharp), so practically every format works.

## Install

Download `vView-setup.exe` (always the newest: https://vsuite.vrsion.com/vview/vView-setup.exe) (or click **Install** in vShell's
*Settings > Updates > vSuite apps*). It installs for your account only (no administrator rights), with a Start menu shortcut, an
optional desktop shortcut, and vView offered in "Open with" and *Settings > Default apps* for pictures, videos and sounds. It doesn't
take any file types over by itself - *Options > General > Default app* does that.

## What it does

### Pictures

* Fit to the window, zoom at the mouse (wheel, + / -), drag around, actual size, turn (R) - turned the way the camera says.
* **The folder along the bottom:** thumbnails of the other pictures in the folder; Left / Right, the wheel or the side buttons go through
  them, and the pictures either side load ahead.
* **Details** beside the picture (I): size, dimensions, when it was taken, camera, exposure.
* **Slideshow** (F5), with its own timing.

### Videos and sounds

* Opening one just plays it, as VLC does: play / pause, the timeline, volume (or the mouse wheel over the video), mute, play again.
* **Audio track and subtitles** under the video: choose the track, choose or turn off subtitles, or add a subtitle file (one with the
  video's name is picked up by itself).
* **Network streams** (Ctrl+N): rtsp://, rtmp://, http(s)://, udp:// … addresses, with the recent ones remembered, and how much to hold
  before playing (*Options > Video*).
* Graphics card decoding, autoplay and repeat in Options.

### Everywhere

* **Full screen** (F11 or a double-click), right-click menu (play / pause, audio track, subtitles, full screen, open, copy),
  **move to the Recycle Bin** (Delete), **show in its folder** (in vXplorer when it's installed), copy / copy path, open with.
* **One vView:** a file opened while vView is open shows in the same window, or a new one (*Options > General*).
* **Options** (laid out like vShell's Settings): the strip, details, the mouse wheel, small pictures, autoplay, repeat, decoding,
  slideshow timing, network streams, the theme's opacity, the default app, and updates.
* **Updates:** checks https://vsuite.vrsion.com a minute after starting and then daily, shows a notice with what's
  new, and installs and restarts with one click - putting the old version back if the new one won't start. vShell's *Settings > Updates*
  can update it too.

## The vShell look

The window takes vShell's background (solid or gradient, or the Wallpaper Theme's colours), text and accents, its widget corners and
outline, and its Blur / Frosted / off background: your wallpaper behind the window under the theme colour at vShell's opacity, as
vShell's bars draw it. *Options > Look > Use the theme's opacity* off = a solid window. The icon (a soft play shape holding vShell's three
dots) is drawn in your theme's colours.

## Build and run

Needs the .NET 10 SDK on Windows. After cloning, fetch the shared library: `git submodule update --init`.

```
dotnet build vView.csproj -c Release
bin\Release\net10.0-windows\win-x64\vView.exe
```

For a release, double-click **build.cmd**: `dist\vView\` (self-contained: vView.exe, .NET and VLC's engine in `libvlc\`) and in
`dist\site\vview\` the update site's files - `vView.zip`, `latest.json` (each file's SHA-256), `changelog.md` and, with [Inno Setup 6](https://jrsoftware.org/isinfo.php), the installer
`vView-setup-<version>.exe` (`installer\vView.iss`; `build.cmd nosetup` skips it). vView isn't a single exe: VLC's DLLs must stay
separate files (LGPL). With VLC installed, the build also makes VLC's plugin cache, so videos start in a blink.

Settings: `%APPDATA%\vView\settings.json`. Log: `%APPDATA%\vView\vview.log`.

To try a theme without touching vShell's real settings, point `VVIEW_VSHELL_DIR` at a folder holding a `settings.json`.

## vSuite.Core

What vShell, vXplorer and vView share - the log, the updater, reading vShell's theme (and the Wallpaper Theme), the glass background,
themed questions, the Options pages, shell thumbnails - lives in **vSuite.Core** (https://github.com/version-au/vSuite.Core), here
as the git submodule `vSuite.Core\`. A fix there reaches all three apps.

libVLC and LibVLCSharp (VideoLAN, LGPL 2.1 or later), Vanara, NAudio and WebView2 - see `THIRD-PARTY.txt`, which comes with every build.

## Licence

Copyright (C) 2026 Steven (version-au). vView is free software: you can redistribute it and/or modify it under the terms of the
GNU General Public License, version 3 - see [LICENSE](LICENSE). It comes with no warranty. The shared library vSuite.Core is MIT, and
the parts by others are listed in [THIRD-PARTY.txt](THIRD-PARTY.txt).

Source: https://github.com/version-au/vView · Downloads and updates: https://vsuite.vrsion.com
