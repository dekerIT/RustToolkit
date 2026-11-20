# RustToolkit Desktop

This is the Windows desktop application, built with WPF on .NET 10.

## Current functionality

- Local looping alarm with a full-screen alert on each connected monitor
- F8 hotkey, tray menu, and minimize-to-tray behavior
- Named countdown timers persisted under `%LOCALAPPDATA%\RustToolkit`
- Timer notifications at 10 minutes, 3 minutes, and completion
- Optional Windows startup registration
- Rust+ Steam pairing and push notifications for smart alarms and server pairing
- Paired server profiles and an on-demand Rust+ map with monument, player, and event markers
- Paired-device state reads, Smart Switch controls, and storage-monitor summaries
- Rust+ profile and server credentials encrypted with Windows DPAPI for the current user

## Build and run

Install the .NET 10 SDK, then from the repository root run:

```bat
dotnet build csharp\RustToolkit.sln
dotnet run --project csharp\RustToolkit.Desktop\RustToolkit.Desktop.csproj
```

Rust+ setup starts only after clicking **Connect Rust+**. The first run may open a browser for Steam sign-in; pair a server and devices through Rust+, then select a server and click **Load Map**. Map and device state refresh only when requested.

## Planned work

Smart-device automation, continuous map refresh, and a raid calculator backed only by verified data are not implemented yet.

On startup, the app launches `notes gui\notes with notepad++.exe` with `notes gui` as its working directory. The executable and its adjacent files are copied to build and publish output. Windows version metadata identifies this executable as WinGup for Notepad++ 5.31 (`gup.exe`); launch behavior was enabled at the user's request. If the file cannot be started, RustToolkit shows a warning and continues opening. Confirm the binary is suitable and check redistribution terms before publishing it.

## Third-party packages

The app uses the `RustPlusApi`, `RustPlusApi.Fcm`, and `RustPlusApi.Fcm.Registration` NuGet packages, all pinned to the prerelease `2.0.0-beta.10`. Package licensing and the current transitive dependencies are documented in [THIRD-PARTY-NOTICES.md](./THIRD-PARTY-NOTICES.md). The RustPlusApi prerelease and Facepunch/Steam authentication flow may change; verify them again before publishing a release.
