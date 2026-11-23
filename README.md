<div align="center">

# RUSTTOOLKIT

### A focused desktop companion for Rust.

Crafting plans, Rust+ alerts, server maps, timers, and practical Windows controls — in one native app.

[**Explore the website**](https://dekerit.github.io/RustToolkit/) ·
[**Download the latest release**](https://github.com/dekerIT/RustToolkit/releases/latest) ·
[View source](https://github.com/dekerIT/RustToolkit)

<br>

[![Windows 10 and 11](https://img.shields.io/badge/WINDOWS-10%20%2F%2011-F4F4F5?style=flat-square&labelColor=151517)](#download)
[![.NET 10](https://img.shields.io/badge/.NET-10-F4F4F5?style=flat-square&labelColor=151517)](#build-from-source)
[![Latest release](https://img.shields.io/github/v/release/dekerIT/RustToolkit?style=flat-square&labelColor=151517&color=F4F4F5)](https://github.com/dekerIT/RustToolkit/releases/latest)

</div>

---

RustToolkit is an independent Windows application for players who want useful Rust tools close at hand. Local features work without Steam sign-in; connect to Rust+ only for live notifications, paired servers, maps, and devices.

## At a glance

| Local tools — no account needed | Rust+ tools — sign in and pair in-game |
| --- | --- |
| Crafting calculator with recipe icons and favorites | Smart Alarm notifications and full-screen alerts |
| Saved countdown timers and Rust notes organized in folders | Paired server map and markers |
| Siren test, Windows startup, and keep-awake option | Smart-device status and Smart Switch controls |

<p align="center">
  <img src="assets/screenshots/crafting.png" width="88%" alt="RustToolkit crafting calculator">
</p>
<p align="center">
  <img src="assets/screenshots/raid-alarm.png" width="49%" alt="RustToolkit raid alarm">
  <img src="assets/screenshots/timers.png" width="49%" alt="RustToolkit countdown timers">
</p>
<p align="center">
  <img src="assets/screenshots/rustplus.png" width="49%" alt="RustToolkit Rust+ connection and server map">
  <img src="assets/screenshots/settings.png" width="49%" alt="RustToolkit Windows startup and keep-awake settings">
</p>

## Download

Choose the setup experience or a portable archive from the [latest GitHub release](https://github.com/dekerIT/RustToolkit/releases/latest):

- **Installer — `RustToolkit-Setup-vX.Y.Z.exe`:** per-user setup, Start Menu shortcut, optional desktop shortcut, and uninstall support.
- **Portable — `RustToolkit-vX.Y.Z-win-x64.zip`:** extract the archive and run `RustToolkit.Desktop.exe`. Keep the extracted files together.

## Get started

1. Install RustToolkit and launch it.
2. Choose **I'll sign in later — continue** to explore the local tools, or connect Steam / Rust+.
3. For Rust+ features, sign in and pair a server and its Smart Alarm or smart devices in Rust+ in-game.
4. Open **Rust+** in the app and select a paired server. Load the map or request a device's current state.

Steam sign-in alone does not pair a server. Disconnect from the **Rust+** page to remove the locally saved account and pairing data.

## What it does

- **Crafting:** search Rust items, choose an amount, and see craft batches, nested ingredients, workbench requirements, base-material totals, and favorites.
- **Raid Alarm:** real Smart Alarm events can play a looping siren and show a full-screen alert on connected monitors. **Test siren** only tests the local sound; press **F8** to stop an alert.
- **Timers:** named countdowns persist across app restarts and notify at 10 minutes, 3 minutes, and completion.
- **Notes:** a companion notes window opens with RustToolkit; organize Rust notes into folders.
- **Desktop controls:** minimize to the system tray, optionally start with Windows, and optionally prevent Windows from sleeping while the app runs.
- **Rust+:** connect or disconnect Steam, receive pairing and alarm notifications, view a paired map, read smart-device states, and manually control paired Smart Switches.

## Build from source

Requirements: Windows and the **.NET 10 SDK**. From the repository root, run:

```powershell
dotnet run --project .\csharp\RustToolkit.Desktop\RustToolkit.Desktop.csproj
```

Build the solution:

```powershell
dotnet build .\csharp\RustToolkit.sln
```

See [`csharp/README.md`](./csharp/README.md) for development notes.

## Data, privacy, and limitations

- Crafting recipes and item icons are fetched from the unofficial [Rust Items API](https://rust-api.tafu.casa/) and cached under `%LOCALAPPDATA%\RustToolkit\crafting` for offline use. The calculator reports game ingredients, not market prices.
- Maps and device states refresh when requested. Automatic smart-device routines and a separate raid-cost calculator are not currently available.
- Rust+ credentials are protected for the current Windows user with DPAPI. App settings, timers, and cached recipes are stored locally under `%LOCALAPPDATA%\RustToolkit`.
- Rust+ pairing depends on Steam, Facepunch, and the third-party RustPlusApi package; their services and APIs may change.

## License

RustToolkit's original code and assets are proprietary. The repository's [`LICENSE`](./LICENSE) describes the limited permissions for viewing the source and using unmodified releases. Third-party dependencies retain their own licenses; see [`csharp/THIRD-PARTY-NOTICES.md`](./csharp/THIRD-PARTY-NOTICES.md).

## Roadmap

- [ ] Validate Steam sign-in, disconnect, Smart Alarm notifications, pairing, map loading, and device controls with a real Rust+ account and server.
- [ ] Improve Rust+ reconnect behavior and map-marker controls.
- [ ] Add optional smart-device automations and clearer first-use pairing guidance.
- [ ] Consider a separate raid-cost calculator only when its data sources can be verified.

---

RustToolkit is an independent fan-made project and is not affiliated with or endorsed by Facepunch Studios. Rust and related names are trademarks or properties of their respective owners.
