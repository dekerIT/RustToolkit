# RustToolkit

RustToolkit is a Windows desktop application for Rust players, built with Python and PySide6.

## Phase 1

Phase 1 contains the application shell and a raid calculator data framework.

The structure names and categories are placeholders. Durability, soft/hard side and
explosive quantities are **not provided** until verified from a cited source.
Missing icons are shown as an English fallback label.

## Phase 2

Phase 2 adds the local alarm system:

- Manual alarm test
- Countdown alarm
- Local POST endpoint on `127.0.0.1:47800`
- Full-screen flashing alarms on selected monitors
- Pygame siren
- F8 global stop hotkey
- Double-click acknowledgment
- System tray controls
- Alarm volume, repeat, monitor and Windows startup settings

The alarm endpoint is loopback-only. It does not accept remote network connections.

Start a local alarm:

```powershell
Invoke-WebRequest -Method POST -Uri "http://127.0.0.1:47800/alarm?reason=Base+raided"
```

Stop it:

```powershell
Invoke-WebRequest -Method POST -Uri "http://127.0.0.1:47800/stop"
```

## Screenshots

Screenshots will be added after visual polish.

## Requirements

- Windows 10 or newer
- Python 3.11 or newer

## Run from source

```bat
py -3.11 -m venv .venv
call .venv\Scripts\activate.bat
python -m pip install --upgrade pip
pip install -r requirements.txt
python raid_toolkit.py
```

## Test Phase 1

```bat
python -m compileall -q raid_toolkit.py ui services data scripts tests
python -m unittest discover -s tests -v
python scripts\download_icons.py
```

The icon script is expected to report that no verified manifest exists until a
legitimate, licensed icon source is configured.

## Test Phase 2

```bat
python -m unittest discover -s tests -v
```

Then click **Raid Alarm** and use **Test Alarm**. Press F8 or double-click the
alarm window to stop it. Closing the main window keeps the application in the tray.

## Build EXE

```bat
build_exe.bat
```

The EXE is generated in:

```text
dist\RustToolkit.exe
```

## Raid Alarm HTTP API

```text
POST http://127.0.0.1:47800/alarm?reason=Base+raided
POST http://127.0.0.1:47800/stop
```

Examples:

```powershell
Invoke-WebRequest -Method POST -Uri "http://127.0.0.1:47800/alarm?reason=Base+raided"
Invoke-WebRequest -Method POST -Uri "http://127.0.0.1:47800/stop"
```

Press `F8` or double-click the flashing alarm window to acknowledge it.

## Configuration

Settings are stored locally in:

```text
%LOCALAPPDATA%\RustToolkit\config.json
```

Logs are stored in:

```text
%LOCALAPPDATA%\RustToolkit\logs\
```

The BattleMetrics token is optional. It is saved locally and is never included in source code or builds.

## Project structure

```text
raid_toolkit.py
ui/
services/
data/
assets/
tests/
build_exe.bat
requirements.txt
RustToolkit.spec
```

## Data and icon status

Raid values in `data/raid_data.json` are deliberately marked unverified.

Real item and building icons are not bundled until each source, provenance and reuse
terms are verified. The app shows a fallback label for every missing icon.

The Server Browser is not implemented yet. The bundled BattleMetrics map is a
separate application launched alongside RustToolkit; map preview inside the
Server Browser is not implemented.

## Disclaimer

This project is a fan-made tool and is not affiliated with or endorsed by Facepunch Studios.

Rust and related names are trademarks or properties of their respective owners.
