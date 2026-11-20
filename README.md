<div align="center">

# 🛠️ RustToolkit

**A modern Windows desktop toolkit for Rust players.**
Raid calculator · Raid alarm · BattleMetrics server tools

![Platform](https://img.shields.io/badge/platform-Windows%2010%2B-0078D6?logo=windows&logoColor=white)
![Python](https://img.shields.io/badge/python-3.11%2B-3776AB?logo=python&logoColor=white)
![UI](https://img.shields.io/badge/UI-PySide6-41CD52?logo=qt&logoColor=white)
![Status](https://img.shields.io/badge/status-in%20development-orange)

</div>

---

RustToolkit is a free, fan-made companion app for the game **Rust**. It runs locally on your PC, lives in the system tray, and keeps everything on your machine: no accounts, no cloud, no telemetry.

> [!NOTE]
> The project is under active development. See the [Roadmap](#-roadmap) for what works today and what is coming.

<!-- Replace with real screenshots once available:
<p align="center">
  <img src="assets/screenshots/main.png" width="720" alt="RustToolkit main window">
</p>
-->

## ✨ Features

| Feature | Status |
| --- | --- |
| 🧮 **Raid calculator** framework (structures, categories, explosives) | ✅ Available (data being verified) |
| 🚨 **Raid alarm**: full-screen flashing alert + siren on selected monitors | ✅ Available |
| 🔌 **Local HTTP API** to trigger and stop alarms from scripts or other tools | ✅ Available |
| 🖥️ **System tray** controls, global stop hotkey, Windows startup option | ✅ Available |
| 🌐 **Server browser** powered by BattleMetrics | 🚧 Planned |
| 🏆 **Transparent server ranking** | 🚧 Planned |

### 🚨 Raid Alarm

- Manual test alarm and countdown alarm
- Full-screen flashing on the monitors you choose
- Siren sound with adjustable volume and repeat
- **F8** global hotkey or **double-click** the alarm window to acknowledge
- Minimizes to tray instead of closing, so it keeps watching in the background
- Optional launch with Windows

### 🧮 Raid Calculator

A data-driven calculator framework for structures and explosives.

> [!IMPORTANT]
> Durability, soft/hard side and explosive quantities in `data/raid_data.json` are **deliberately marked unverified** until each value is confirmed from a cited source. Structure names and categories are currently placeholders. No numbers are shown as fact before they are verified.

## 🚀 Quick start

### Option A: Download the EXE

Grab `RustToolkit.exe` from the [Releases](../../releases) page and run it. No installation needed.

### Option B: Run from source

Requirements: **Windows 10 or newer** and **Python 3.11+**.

```bat
py -3.11 -m venv .venv
call .venv\Scripts\activate.bat
python -m pip install --upgrade pip
pip install -r requirements.txt
python raid_toolkit.py
```

### Build your own EXE

```bat
build_exe.bat
```

The result is written to `dist\RustToolkit.exe`.

## 🔌 Raid Alarm HTTP API

RustToolkit listens on a **loopback-only** endpoint, `127.0.0.1:47800`. It never accepts connections from other machines.

| Action | Request |
| --- | --- |
| Trigger an alarm | `POST http://127.0.0.1:47800/alarm?reason=Base+raided` |
| Stop the alarm | `POST http://127.0.0.1:47800/stop` |

**PowerShell**

```powershell
Invoke-WebRequest -Method POST -Uri "http://127.0.0.1:47800/alarm?reason=Base+raided"
Invoke-WebRequest -Method POST -Uri "http://127.0.0.1:47800/stop"
```

**curl**

```bash
curl -X POST "http://127.0.0.1:47800/alarm?reason=Base+raided"
curl -X POST "http://127.0.0.1:47800/stop"
```

This makes it easy to hook the alarm into your own scripts or automation.

## ⚙️ Configuration and privacy

| What | Where |
| --- | --- |
| Settings | `%LOCALAPPDATA%\RustToolkit\config.json` |
| Logs | `%LOCALAPPDATA%\RustToolkit\logs\` |

- Everything is stored **locally**.
- The **BattleMetrics token is optional**. It is saved locally and is never included in source code or builds.

## 🧪 Development

```bat
python -m compileall -q raid_toolkit.py ui services data scripts tests
python -m unittest discover -s tests -v
python scripts\download_icons.py
```

The icon script reports that no verified manifest exists until a legitimate, licensed icon source is configured. Missing icons are shown as an English fallback label.

To test the alarm manually, open **Raid Alarm** and click **Test Alarm**. Press **F8** or double-click the alarm window to stop it.

<details>
<summary><b>Project structure</b></summary>

```
raid_toolkit.py     # application entry point
ui/                 # PySide6 interface
services/           # alarm system, local API, integrations
data/               # raid data (unverified until sourced)
assets/             # images and resources
tests/              # unit tests
build_exe.bat       # EXE build script
RaidToolkit.spec    # PyInstaller spec
requirements.txt
```

</details>

## 🗺️ Roadmap

- [x] Application shell and raid calculator data framework
- [x] Local alarm system, tray controls and HTTP API
- [ ] Verified raid data with cited sources
- [ ] Licensed item and building icons
- [ ] BattleMetrics server browser (including map preview)
- [ ] Transparent server ranking
- [ ] Screenshots and first public release

## 🤝 Contributing

Issues and pull requests are welcome. If you contribute raid data, please include a **source** for every value. Unverified numbers will not be merged.

## 📜 Disclaimer

RustToolkit is a fan-made project and is **not affiliated with or endorsed by Facepunch Studios**. Rust and related names are trademarks or properties of their respective owners.
