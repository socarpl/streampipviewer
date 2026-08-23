# Stream PIP Viewer

Stream PIP Viewer is a lightweight desktop application for displaying two RTSP/H.264 feeds with a main view and a picture-in-picture overlay.

The app was built to prioritize low CPU usage and good image quality. It uses native video playback instead of converting camera feeds to MJPEG or pushing raw frames through the application.

## Purpose

The application is intended for a monitoring screen where two CCTV feeds should stay visible at the same time:

- `CCTV1` starts as the main full-size feed.
- `CCTV2` starts as the smaller PIP feed.
- Feeds can be swapped at runtime.
- PIP position, size, and border color are configurable.

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
────────
Swap feeds
Fullscreen / Exit Fullscreen
```

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
