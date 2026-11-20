from __future__ import annotations

import hashlib
import shutil
import subprocess
import sys
from pathlib import Path


def resolve_battlemetrics_map_path() -> Path:
    if hasattr(sys, "_MEIPASS"):
        root = Path(sys._MEIPASS)
    else:
        root = Path(__file__).resolve().parent.parent

    bundled_dir = root / "assets" / "RustMap"
    executable = bundled_dir / "battlemetricsmap.exe"
    if not executable.is_file():
        raise FileNotFoundError(f"BattleMetrics map executable not found: {executable}")

    if not hasattr(sys, "_MEIPASS"):
        return executable

    digest = hashlib.sha256()
    for source in sorted(path for path in bundled_dir.iterdir() if path.is_file()):
        digest.update(source.name.encode("utf-8"))
        with source.open("rb") as file:
            for chunk in iter(lambda: file.read(1024 * 1024), b""):
                digest.update(chunk)

    destination_dir = (
        Path.home()
        / "AppData"
        / "Local"
        / "RustToolkit"
        / "BattleMetricsMap"
        / digest.hexdigest()
    )
    if not destination_dir.exists():
        shutil.copytree(bundled_dir, destination_dir)

    executable = destination_dir / "battlemetricsmap.exe"
    if not executable.is_file():
        raise FileNotFoundError(f"Extracted BattleMetrics map executable not found: {executable}")
    return executable


def launch_battlemetrics_map() -> None:
    executable = resolve_battlemetrics_map_path()
    subprocess.Popen([str(executable)], cwd=str(executable.parent))
