# Stream PIP Viewer

Stream PIP Viewer is a lightweight desktop application for displaying two RTSP/H.264 feeds with a main view and a picture-in-picture overlay.

The app was built to prioritize low CPU usage and good image quality. It uses native video playback instead of converting camera feeds to MJPEG or pushing raw frames through the application.
Each stream runs in its own `CctvPip.StreamHost` process, and the main app embeds those helper windows into the MainWindow and PIP surfaces. This keeps audio state isolated per stream when the native playback backend exposes process-wide audio behavior.

## Purpose

The application is intended for a monitoring screen where two CCTV feeds should stay visible at the same time:

- `CCTV1` starts as the main full-size feed.
- `CCTV2` starts as the smaller PIP feed.
- Both streams start muted.
- Feeds can be swapped at runtime.
- PIP position, size, and border color are configurable.

## Host Architecture

The application has two executable projects:

- `CctvPip.App` is the shell application. It owns the main window, PIP layout, context menu, fullscreen behavior, settings dialog, config file, and MainWindow/PIP role mapping.
- `CctvPip.StreamHost` is a single-stream player. One process is launched for `CCTV1`, and a second process is launched for `CCTV2`.

The main app starts each host with `--hosted`, finds the host window by process ID, embeds that window into the matching video surface, and sends mute, unmute, restart, and shutdown commands to that process.

This design exists because some native playback backends can share audio behavior inside one process. Isolating each stream into its own process makes `Mute Main Stream`, `Mute PIP Stream`, `Mute All`, and `Unmute All` behave independently.

Hosted startup is equivalent to:

```powershell
CctvPip.StreamHost.exe --hosted --stream cctv1 --label CCTV1 --config "path\to\cctv-pip.properties"
CctvPip.StreamHost.exe --hosted --stream cctv2 --label CCTV2 --config "path\to\cctv-pip.properties"
```

## Default Streams

Stream URLs example:

```properties
cctv1.url=rtsp://192.168.0.999:554/cam/realmonitor?channel=1&subtype=0&unicast=true&proto=Onvif
cctv2.url=rtsp://192.168.0.998:554/cam/realmonitor?channel=1&subtype=0&unicast=true&proto=Onvif
```

The defaults use `subtype=0`, which is the higher-quality stream for these cameras.

## Controls

Right-click the video window to open the context menu:

```text
Settings
----------
Mute All
Unmute All
----------
Unmute Main Stream / Mute Main Stream
Unmute PIP Stream / Mute PIP Stream
----------
Swap feeds
Fullscreen / Exit Fullscreen
```

Audio controls:

- `Mute All` mutes both streams.
- `Unmute All` unmutes both streams.
- `Unmute Main Stream / Mute Main Stream` toggles the stream currently displayed in the main video area.
- `Unmute PIP Stream / Mute PIP Stream` toggles the stream currently displayed in the PIP overlay.
- Mute state follows the stream through reconnects and source reloads during the current app session.
- Main/PIP audio commands are sent to separate stream-host processes so muting or unmuting one stream does not change the other stream.

Other controls:

- Double-click the main video area to enter/exit fullscreen.
- Double-click the PIP video to swap feeds.
- Press `Esc` to exit fullscreen.

## Settings

Open `Settings` from the context menu.

Available settings:

- CCTV1 source URL.
- CCTV2 source URL.
- PIP size.
- PIP X position.
- PIP Y position.
- PIP border color.
- Swap streams.
- Open active config file.

Settings behavior:

- PIP size, position, and border color preview immediately in the main window.
- Source URL and setting changes are written to config only after clicking `Save`.
- `Cancel` restores the config-backed view and does not write changes.
- If either source URL changes on `Save`, both streams reload.

## Config File

The app creates or loads:

```text
cctv-pip.properties
```

Typical settings:

```properties
cctv1.url=rtsp://192.168.0.999:554/cam/realmonitor?channel=1&subtype=0&unicast=true&proto=Onvif
cctv2.url=rtsp://192.168.0.998:554/cam/realmonitor?channel=1&subtype=0&unicast=true&proto=Onvif
pip.border.color=#FFFFFF
pip.x=1450
pip.y=40
pip.scale=0.28
reconnect.attempts=3
reconnect.delay.ms=1500
player.backend=libvlc
libvlc.options=--no-video-title-show,--avcodec-hw=any
cctv1.player.options=:rtsp-tcp,:network-caching=300,:live-caching=300
cctv2.player.options=:rtsp-tcp,:network-caching=300,:live-caching=300
```

Use `Open Config` in Settings to open the exact config file used by the running app.

## No-Signal And Reconnect Behavior

Each stream reconnects independently.

Before a stream connects:

```text
NO SIGNAL
```

If initial connection fails after retries:

```text
NO SIGNAL - Failed to connect
```

If a stream was live and later reconnect fails:

```text
NO SIGNAL. Reconnection failed
```

The app shows the stream label and a short technical error reason when available.

## Run

```powershell
dotnet run
```

Build:

```powershell
dotnet build
```

Building the main app also builds `CctvPip.StreamHost` and copies the helper executable to:

```text
bin\Debug\net8.0-windows\StreamHost\CctvPip.StreamHost.exe
```

The helper can also be launched directly for diagnostics.

## Run Stream Hosts Directly

Build the app first:

```powershell
dotnet build
```

Then run a stream host as a normal standalone window by omitting `--hosted`:

```powershell
.\bin\Debug\net8.0-windows\StreamHost\CctvPip.StreamHost.exe --stream cctv1 --config ".\bin\Debug\net8.0-windows\cctv-pip.properties"
.\bin\Debug\net8.0-windows\StreamHost\CctvPip.StreamHost.exe --stream cctv2 --config ".\bin\Debug\net8.0-windows\cctv-pip.properties"
```

You can also provide a stream URL directly:

```powershell
.\bin\Debug\net8.0-windows\StreamHost\CctvPip.StreamHost.exe --label "Test Camera" --url "rtsp://camera.example/stream"
```

Useful host arguments:

- `--stream cctv1` or `--stream cctv2` selects which config keys to read.
- `--config <path>` reads stream URL, LibVLC options, and media options from a properties file.
- `--url <rtsp-url>` overrides the configured stream URL.
- `--label <text>` changes the window/status label.
- `--unmuted` starts the host with audio enabled. Without this flag, hosts start muted.
- `--libvlc-options <comma-separated-options>` overrides `libvlc.options`.
- `--media-options <comma-separated-options>` overrides the selected stream's media options.

Standalone host controls:

- Right-click opens the host context menu.
- `Mute` / `Unmute` toggles that one host process.
- `Restart` reloads that stream.
- Press `M` to toggle mute.
- Press `R` to restart playback.
