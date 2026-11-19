# RustToolkit

RustToolkit is a Windows desktop application for Rust players, built with Python and PySide6.

## Features

- Raid Calculator
- BattleMetrics-based Server Browser
- Configurable server ranking
- Countdown and manual raid alarms
- Local HTTP alarm endpoint
- Flashing full-screen alarms
- System tray support
- Windows startup option
- Local settings and cache
- JSON-based game data

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

## Build EXE

```bat
pip install pyinstaller
pyinstaller --noconsole --clean --onefile --name RustToolkit ^
  --add-data "data;data" ^
  --add-data "assets;assets" ^
  raid_toolkit.py
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

Raid values in `data/raid_data.json` are currently marked unverified.

Real item and building icons are not bundled until their source, provenance, and reuse terms are verified.

The Server Browser relies on public BattleMetrics data. Availability and response fields may change. Map preview is unavailable until a verified public source is configured.

## Disclaimer

This project is a fan-made tool and is not affiliated with or endorsed by Facepunch Studios.

Rust and related names are trademarks or properties of their respective owners.

Не добавляйте эти строки: иначе RAID-данные и assets с иконками исчезнут из репозитория.
