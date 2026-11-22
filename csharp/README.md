# RustToolkit Desktop

This is the Windows desktop application, built with WPF on .NET 10.

## Current functionality

- Local looping alarm with a full-screen alert on each connected monitor
- F8 hotkey, tray menu, and minimize-to-tray behavior
- Named countdown timers persisted under `%LOCALAPPDATA%\RustToolkit`
- Timer notifications at 10 minutes, 3 minutes, and completion
- Optional Windows startup registration
- Optional keep-awake mode that prevents system sleep while RustToolkit is running
- Crafting calculator with recipe search, item icons, batch-aware recursive ingredient breakdown, workbench requirements, and persistent favorites
- First-run welcome with an optional Steam/Rust+ sign-in and a path to continue without an account
- Rust+ Steam pairing and push notifications for smart alarms and server pairing
- Rust+ disconnect that removes the local profile and paired server/device tokens
- Paired server profiles and an on-demand Rust+ map with monument, player, and event markers
- Paired-device state reads, Smart Switch controls, and storage-monitor summaries
- Rust+ profile and server credentials encrypted with Windows DPAPI for the current user

## Build and run

Install the .NET 10 SDK, then from the repository root run:

```bat
dotnet build csharp\RustToolkit.sln
dotnet run --project csharp\RustToolkit.Desktop\RustToolkit.Desktop.csproj
```

On first launch, choose **Connect Steam / Rust+** to start setup or **I'll sign in later — continue** to use local tools without an account. Rust+ setup can also be started later from its sidebar page. Steam sign-in opens a browser; the connected Steam ID and next pairing step are shown in the app. **Disconnect** removes the saved Rust+ account and paired server/device tokens from this PC, then returns to the welcome screen. Pair a server and Smart Alarm/devices through Rust+, then select a server and click **Load Map**. Map and device state refresh only when requested. The Raid Alarm's **Test siren** button checks only the local siren; real alerts arrive through Rust+ Smart Alarm notifications.

## Data sources and limitations

Crafting recipes and item PNGs are fetched from the unofficial [Rust Items API](https://rust-api.tafu.casa/). The catalog and requested icons are cached under `%LOCALAPPDATA%\RustToolkit\crafting`, so a saved catalog remains usable when the API is unavailable. The calculator totals game materials, not market prices; the third-party API may change or become unavailable.

Map and device data refresh only when requested. Smart-device automation, continuous map refresh, and a separate raid-cost calculator backed only by verified data are not implemented yet.

The app copies the root-level `notes.exe` into `notes\notes.exe` beside the desktop app and launches it at startup. The notes app's working directory is `%LOCALAPPDATA%\RustToolkit\Notes`, so files it stores relative to its working directory remain outside the build folder. If `notes.exe` is missing or fails to start, RustToolkit shows a warning and continues. Confirm the notes app's source and redistribution terms before publishing a release; see [THIRD-PARTY-NOTICES.md](./THIRD-PARTY-NOTICES.md).

## Planned work

- Validate sign-in, disconnect, Smart Alarm notifications, server pairing, map loading, and device controls with a real Rust+ account/server.
- Improve Rust+ reconnect behavior and add clearer map marker controls or optional refresh.
- Add user-configurable smart-device automations and first-use pairing guidance.
- Add a separate raid-cost calculator only after every value has a verified source.
- Verify the notes app and its storage behavior, then prepare a tested Windows release.

## Third-party packages

The app uses the `RustPlusApi`, `RustPlusApi.Fcm`, and `RustPlusApi.Fcm.Registration` NuGet packages, all pinned to the prerelease `2.0.0-beta.10`. Package licensing and the current transitive dependencies are documented in [THIRD-PARTY-NOTICES.md](./THIRD-PARTY-NOTICES.md). The RustPlusApi prerelease and Facepunch/Steam authentication flow may change; verify them again before publishing a release.
