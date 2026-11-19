# Raid Toolkit

## Phase 2

Includes the alarm timer, local HTTP endpoint, cross-monitor flashing, siren, tray and Windows startup setting.

### Alarm tests

1. Open **Raid Alarm**.
2. Click **Test Alarm**.
3. Verify the screen flashes and the siren plays.
4. Press **F8** or double-click the flashing screen to stop it.
5. Test timer from the countdown controls.
6. Test HTTP endpoints in PowerShell:

```powershell
Invoke-WebRequest -Method POST -Uri "http://127.0.0.1:47800/alarm?reason=Test+raid"
Invoke-WebRequest -Method POST -Uri "http://127.0.0.1:47800/stop"
```

Settings are stored in `%LOCALAPPDATA%\RaidToolkit\config.json`. Logs are stored in `%LOCALAPPDATA%\RaidToolkit\logs\raid_toolkit.log`.

## Phase 1

The app uses PySide6/Qt. The Raid Calculator, application shell, English UI, JSON data, and icon-cache structure are included.

### Important status

- Raid values are **not verified**. `data/raid_data.json` currently carries prototype values and marks `last_verified` as `null`.
- Real item/building icons are **not bundled** until a source is verified for provenance and reuse rights.
- Server Browser, Raid Alarm, Settings, and Decay are visibly marked "Not implemented in Phase 1".
- The Wipe tab is removed.

## Run on Windows

```bat
py -3.11 -m venv .venv
.venv\Scripts\activate
python -m pip install --upgrade pip
pip install -r requirements.txt
python raid_toolkit.py
```

## Download icons later

```bat
python scripts\download_icons.py
```

The script deliberately performs no downloads until `VERIFIED_ICON_SOURCES` is populated with sources whose terms have been checked.

## Build one EXE

```bat
py -3.11 -m venv .venv
.venv\Scripts\activate
pip install -r requirements.txt
pip install pyinstaller
pyinstaller --noconsole --onefile --name RaidToolkit raid_toolkit.py
```

The EXE is written to `dist\RaidToolkit.exe`.

## PyInstaller resource fix

One-file builds extract resources to `_MEIPASS`. Source and EXE paths must therefore be resolved through `sys._MEIPASS`; raid JSON and `assets/` are included in the EXE by:

```text
datas=[('data', 'data'), ('assets', 'assets')]
```

The application also shows "DATA STATUS: UNAVAILABLE" instead of crashing if bundled raid data is missing.

## Phase 3

Server Browser uses the public BattleMetrics Rust server listing.

- The HTTP request has a 12-second timeout.
- HTTP 429 responses are retried with `Retry-After` or bounded exponential delay.
- A successful response is cached in `%LOCALAPPDATA%\RaidToolkit\cache\battlemetrics_rust.json`.
- Network/API failures show cached data or an explicit offline/error status.
- A token is optional and can be saved in Settings.
- Map preview is disabled until a verified and legally usable source is configured.
- Ping uses Windows `ping` and may return unavailable when ICMP is blocked.
- Population history is stored locally as refresh snapshots.
- Ranking weights are visible and editable in Settings.

The BattleMetrics API response schema may change. Unknown fields are displayed as `Unknown`, `?`, or `Not measured`.
