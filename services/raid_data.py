from __future__ import annotations

import json
from pathlib import Path
from typing import Any


class RaidDataStore:
    def __init__(self, base_dir: Path | None = None) -> None:
        self.base_dir = base_dir or Path(__file__).resolve().parent.parent
        self.path = self.base_dir / "data" / "raid_data.json"

    def load(self) -> dict[str, Any]:
        if not self.path.exists():
            return {
                "metadata": {
                    "status": "missing",
                    "source": "",
                    "last_verified": None,
                    "warning": "raid_data.json is missing.",
                },
                "structures": [],
                "methods": [],
            }
        try:
            return json.loads(self.path.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, OSError) as exc:
            return {
                "metadata": {
                    "status": "invalid",
                    "warning": f"Raid data could not be loaded: {exc}",
                },
                "structures": [],
                "methods": [],
            }
