# PC Stats Tray

[![Build and Release](https://github.com/Avaxerrr/pc-stats-tray/actions/workflows/build-release.yml/badge.svg)](https://github.com/Avaxerrr/pc-stats-tray/actions/workflows/build-release.yml)
[![GitHub Pages](https://img.shields.io/badge/Site-GitHub%20Pages-121013)](https://avaxerrr.github.io/pc-stats-tray/)
![Windows](https://img.shields.io/badge/Windows-10%20%2F%2011-0078D4)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)
![License](https://img.shields.io/badge/License-MIT-green)

PC Stats Tray is a small Windows app that shows hardware stats in simple places you can actually see:

- in the tray icon
- on the desktop
- inside games, if you want that

If you want a lightweight monitor instead of a big dashboard always open, this is what the app is for.

## What it looks like

### Desktop overlay

![Desktop overlay](PCStatsTray/assets/screenshots/overlay-screenshot.png)

### OSD settings

![OSD settings](PCStatsTray/assets/screenshots/OSD-settings.png)

## Start here

To use the app, these are the main things to know:

- `Website`: [Open the landing page](https://avaxerrr.github.io/pc-stats-tray/)
- `App download`: [Download the latest release](https://github.com/Avaxerrr/pc-stats-tray/releases/latest)
- `PawnIO`: [Official site](https://pawnio.eu/)
- `.NET 10 Desktop Runtime`: [Download from Microsoft](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- `RTSS`: [Download RivaTuner Statistics Server](https://www.guru3d.com/download/rtss-rivatuner-statistics-server-download/)

What these mean:

- `App download` is the ready-to-run exe from the GitHub Releases page.
- `PawnIO` may be required for full CPU temperature, clock, and power sensors on some systems.
- `.NET 10 Desktop Runtime` is only needed if this PC does not already have it installed.
- `RTSS` is only needed if you want the overlay to appear inside games.

If you only want the tray icon and desktop overlay, you do not need RTSS.

## Quick setup

1. Install `PawnIO` from the official site if you want full CPU temperature, clock, and power sensors.
2. Install the `.NET 10 Desktop Runtime` only if this PC does not already have it.
3. Run `PCStatsTray.exe`.
4. Right-click the tray icon.
5. Open `OSD Settings`.
6. Choose the metrics you want.
7. Turn on `Desktop OSD` if you want stats on your desktop.
8. Turn on `RTSS OSD (Games)` only if RTSS is installed and open.
9. Changes apply immediately.

## First run and CPU sensors

On some systems, the app can read `CPU Load` right away but `CPU Temp`, `CPU Clock`, and `CPU Power` stay blank.

If that happens, PC Stats Tray shows a startup note explaining that full CPU sensors may require PawnIO, which is the official low-level backend used by LibreHardwareMonitor on some systems.

- Official PawnIO site: [pawnio.eu](https://pawnio.eu/)
- The app keeps running even if you do not install it.
- You can reopen the explanation any time from `CPU Sensor Setup...` in the tray menu.

## Want stats inside games?

Do this:

1. Install RTSS.
2. Open RTSS.
3. In RTSS, make sure `Show On-Screen Display` is turned on.
4. Open PC Stats Tray.
5. In `OSD Settings`, turn on `RTSS OSD (Games)`.

Simple explanation:

- PC Stats Tray gets the temperatures and usage data.
- RTSS is the thing that draws those stats inside the game.
- If `Show On-Screen Display` is off in RTSS, nothing will appear in-game even if PC Stats Tray is working correctly.

## Want stats only on the desktop?

Do this:

1. Run PC Stats Tray.
2. Open `OSD Settings`.
3. Turn on `Desktop OSD`.
4. Leave `RTSS OSD (Games)` off.

## If the app does not open

The most common reason is that the `.NET 10 Desktop Runtime` is not installed yet.

Install that first, then try again.

## What the app can do

- show a live temperature in the tray icon
- show a desktop OSD with the metrics you choose
- send the same metrics to RTSS for supported in-game overlays
- let you change font, size, shadow, outline, position, and visibility
- support hotkeys for toggling the OSD and opening settings

## Technical details

PC Stats Tray is built with:

- .NET 10
- WinForms
- LibreHardwareMonitor-related binaries for hardware data collection

## Credits

PC Stats Tray uses LibreHardwareMonitor-related binaries for hardware monitoring support.

Credit and third-party notice files are included in:

- `PCStatsTray/docs/LibreHardwareMonitor-LICENSE.txt`
- `PCStatsTray/docs/LibreHardwareMonitor-THIRD-PARTY-NOTICES.txt`

Project folders:

- `PCStatsTray/` main Windows app
- `PCStatsTray.Tests/` unit tests
- `PCStatsTray/lib/` local dependency DLLs used by the app
- `PCStatsTray/docs/` third-party license and notice files

## Build from source

```powershell
dotnet build .\PCStatsTray\PCStatsTray.csproj
dotnet test .\PCStatsTray.Tests\PCStatsTray.Tests.csproj
```

## Publish

Current publish command:

```powershell
dotnet publish .\PCStatsTray\PCStatsTray.csproj -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=false
```

Output:

```text
PCStatsTray/bin/Release/net10.0-windows/win-x64/publish/PCStatsTray.exe
```

This is a single-file publish, but it still requires the .NET 10 Desktop Runtime on the target PC.

## License

This project is released under the MIT License. See [LICENSE](LICENSE).

The MIT license applies to PC Stats Tray itself. Third-party components keep their own upstream notices and license terms.
