# RustToolkit Desktop

This is the Windows desktop application, built with WPF on .NET 10.

## Current functionality

- Local looping alarm with a full-screen alert on each connected monitor
- F8 hotkey, tray menu, and minimize-to-tray behavior
- Named countdown timers persisted under `%LOCALAPPDATA%\RustToolkit`
- Timer notifications at 10 minutes, 3 minutes, and completion
- Redesigned Rust Notes app with Obsidian-inspired graph views, bundled with the installer and launched with RustToolkit
- Optional Windows startup registration
- Optional keep-awake mode that prevents system sleep while RustToolkit is running
- RustHelp-backed item search with recipe details, research costs, listed loot sources, item images, and persistent favorites
- Crafting calculator with batch-aware recursive ingredient breakdown and workbench requirements
- Raid planner with hard/soft sides, custom target health, owned explosives, and cost/time comparisons
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

On first launch, RustToolkit also opens the bundled Rust Notes app, which uses the shared visual style and includes graph views inspired by Obsidian. Notes data is stored under `%LOCALAPPDATA%\RustToolkit\Notes`; it can also be opened from its Start Menu shortcut. Choose **Connect Steam / Rust+** to start setup or **I'll sign in later — continue** to use local tools without an account. Rust+ setup can also be started later from its sidebar page. Steam sign-in opens a browser; the connected Steam ID and next pairing step are shown in the app. **Disconnect** removes the saved Rust+ account and paired server/device tokens from this PC, then returns to the welcome screen. Pair a server and Smart Alarm/devices through Rust+, then select a server and click **Load Map**. Map and device state refresh only when requested. The Raid Alarm's **Test siren** button checks only the local siren; real alerts arrive through Rust+ Smart Alarm notifications.

## Data sources and limitations

The crafting catalog, item images, recipe, research, and listed loot-source data come from [RustHelp](https://rusthelp.com/). RustHelp pages are read in an embedded WebView2 browser because direct HTTP access may be blocked by its CDN. The catalog is cached under `%LOCALAPPDATA%\RustToolkit\rusthelp`; item-page details are cached in memory. The Microsoft Edge WebView2 Runtime must be installed. If RustHelp is unavailable, the saved catalog remains searchable, but live item details and recipe calculations require a successful page load.

The raid planner continues to use the [Rustly API](https://rustly.com/developers/api/) for raid costs and estimates. RustHelp is not currently used for raid planning.

Rustly provides standard full-health raid counts, but not per-hit damage. Custom-health and owned-explosive results therefore linearly estimate required explosives, remaining health, material cost, and time; overkill and random damage can change actual results. Material totals are in-game costs, not market prices.

Map and device data refresh only when requested. Smart-device automation and continuous map refresh are not implemented yet.

## Planned work

- Validate sign-in, disconnect, Smart Alarm notifications, server pairing, map loading, and device controls with a real Rust+ account/server.
- Improve Rust+ reconnect behavior and add clearer map marker controls or optional refresh.
- Add user-configurable smart-device automations and first-use pairing guidance.
- Validate the current RustHelp recipe and loot data against in-game values.
- Prepare and test a distributable Windows release.

## Third-party packages

The app uses the `RustPlusApi`, `RustPlusApi.Fcm`, and `RustPlusApi.Fcm.Registration` NuGet packages, all pinned to the prerelease `2.0.0-beta.10`. Package licensing and the current transitive dependencies are documented in [THIRD-PARTY-NOTICES.md](./THIRD-PARTY-NOTICES.md). The RustPlusApi prerelease and Facepunch/Steam authentication flow may change; verify them again before publishing a release.
