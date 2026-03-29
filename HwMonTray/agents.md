# HwMonTray - AI Assistant Guide

This document (`agents.md`) serves as a set of rules, architectures, and commands for any future AI assistants modifying this project.

## 1. Project Overview & Architecture
- **Framework**: .NET 10.0 (Windows Forms).
- **Core Library**: `LibreHardwareMonitorLib` (LHM) is used to read physical CPU, GPU, RAM, and motherboard sensors.
- **Elevation Requirement**: Reading physical hardware sensors requires **Administrator Privileges**. Attempting to run `dotnet run` from a non-elevated shell will throw an "operation requires elevation" exception because of the app's embedded `app.manifest`.

## 2. Compilation & Publishing Commands

### Standard Build (Fast)
```powershell
dotnet build
```
Run the executable directly from an **elevated** PowerShell window:
```powershell
& ".\bin\Debug\net10.0-windows\HwMonTray.exe"
```

### Production Release Build
Use the standard .NET publish location and do not invent ad-hoc output folders unless the normal publish folder is locked by a running process.
Default publish output path:
```text
.\bin\Release\net10.0-windows\win-x64\publish\
```
To generate the framework-dependent release build, use:
```powershell
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=false
```
*Note: Do NOT omit `-p:SelfContained=false` on newer .NET versions, otherwise the build will pack the entire ~100MB .NET runtime into the executable.*
If publishing with `-r win-x64`, run a runtime-specific restore first when needed:
```powershell
dotnet restore -r win-x64
```
Run the published executable from:
```powershell
& ".\bin\Release\net10.0-windows\win-x64\publish\HwMonTray.exe"
```
Only create a root-level versioned one-file executable such as `HwMonTray_v0.3.1.exe` when the user explicitly says it is the final build/release. Otherwise, publish only to the standard .NET publish folder and do not refresh or create versioned root executables.

## 3. UI and Rendering Caveats

### OSD Overlay (`OverlayForm.cs`)
- **Transparency Mechanism**: Do NOT use standard WinForms `Opacity` or `TransparencyKey` to achieve round, transparent shapes, as they create aliased (jagged) hard edges and artifacting. The project uses raw Win32 `UpdateLayeredWindow` with an ARGB 32bpp bitmap for true per-pixel alpha blending (soft shadows, anti-aliased rounded corners).
- **Text Rendering**: When drawing on a transparent background, `ClearTypeGridFit` creates terrible red artifacts. Always use `g.TextRenderingHint = TextRenderingHint.AntiAlias`.

### Settings Layout (`OverlaySettingsForm.cs`)
- **TrackBars**: WinForms `TrackBar` controls have `AutoSize = true` by default, giving them a forced height of 45px. When placing them tightly in cards (like the custom `MakeSlider` method), `AutoSize` must be manually set to `false`, otherwise the bottom half of the trackbar overrides and visually deletes the labels underneath it.
- **Checkboxes in Dark Mode**: Standard WinForms `CheckBox` controls with `FlatStyle.Flat` draw tiny, essentially invisible checks on dark backgrounds. All checkboxes (like in the Metrics card) must be completely custom-drawn via their `.Paint` event to ensure the checkmark is visible with the UI Accent color.

## 4. Configuration Persistence
All settings are stored in `hwmon_config.json`. The persistence logic resides concurrently inside `Program.cs` (`SaveOverlayConfig`) and `DetailsForm.cs` (save config button). Do NOT refactor `OverlayConfig` properties without migrating or handling the existing `.json` deserialization safely.

## 5. UI Aesthetics
- **Color Palette**: Dark mode exclusively. Backgrounds are deep grays/blacks (`#1A1A1E`), borders are slightly lighter (`#2C2C30`), and interactive text is brightly colored with the Accent (Blue/Cyan/Green).
- **Typography**: Default to `Segoe UI`, but preserve support for the user-selectable OSD font list in settings. Use bold weights for numbers and uppercase labels.
