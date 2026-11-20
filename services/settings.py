from __future__ import annotations

import json
from copy import deepcopy
from pathlib import Path
from typing import Any


class SettingsStore:
    DEFAULTS = {
        "battlemetrics_token": "",
        "alarm_volume": 70,
        "selected_monitors": [],
        "keep_pc_awake": False,
        "repeat_alarm": True,
        "start_with_windows": False,
        "score_weights": {
            "population": 30,
            "queue": 20,
            "wipe_age": 20,
            "map_size": 10,
            "ping": 20,
        },
    }

    def __init__(self) -> None:
        self.path = Path.home() / "AppData" / "Local" / "RustToolkit" / "config.json"
        self.path.parent.mkdir(parents=True, exist_ok=True)

    def ensure_defaults(self) -> dict[str, Any]:
        if not self.path.exists():
            self.save(deepcopy(self.DEFAULTS))
        data = self.load()
        changed = False
        for key, value in self.DEFAULTS.items():
            if key not in data:
                data[key] = deepcopy(value)
                changed = True
        if changed:
            self.save(data)
        return data

    def load(self) -> dict[str, Any]:
        if not self.path.exists():
            return deepcopy(self.DEFAULTS)
        try:
            return json.loads(self.path.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, OSError):
            return deepcopy(self.DEFAULTS)

    def get(self, key: str, default: Any = None) -> Any:
        return self.load().get(key, default)

    def set(self, key: str, value: Any) -> None:
        data = self.load()
        data[key] = value
        self.save(data)

    def save(self, data: dict[str, Any]) -> None:
        temp = self.path.with_suffix(".tmp")
        temp.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
        temp.replace(self.path)
