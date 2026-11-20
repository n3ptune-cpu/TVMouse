# TvRemote

A tiny phone-friendly remote for a Windows machine that drives a TV (for example a VM or a media PC with its output on the TV). Open a web page on your phone and use it as a touchpad, a keyboard for typing, and a set of media buttons.

Built with ASP.NET Core minimal APIs and Win32 `SendInput`. It serves its own web UI from a single self-contained `.exe`.

## Features

- Touchpad: drag to move the cursor, tap to click, plus left and right click buttons
- Text input (types literal text, then presses Enter)
- Media controls: seek back/forward, next, play/pause, back
- Global volume down, mute and volume up
- Listens only on your Tailscale address, so it isn't exposed to your LAN

## Requirements

- Windows
- [Tailscale](https://tailscale.com/) running on the PC and your phone
- .NET 10 SDK (to build from source)

## Build and run

```
dotnet run
```

Or publish a single self-contained exe:

```
dotnet publish -c Release
```

The exe is in `bin/Release/net10.0/win-x64/publish/`. On start it prints the address, for example `http://100.x.y.z:8765`. Open it on your phone.

## Configuration

Edit the constants at the top of `Program.cs`:

- `Port`: listening port (default 8765)
- `TargetProcess`: process name that key commands are sent to (default `stremio`)

Add new buttons by adding an entry to `commands` in `Program.cs` and a button with `data-cmd` in `wwwroot/index.html`.

## Running at startup

Use Task Scheduler with the trigger "At log on" and the option "Run only when user is logged on". It must run in the desktop session, because services can't send input to the screen. To control apps running as administrator, TvRemote has to run elevated too.

## Security

There is no authentication. Anyone who can reach the Tailscale address can control the mouse and keyboard, so only run this on a tailnet you trust and don't bind it to other interfaces.
