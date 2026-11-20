"""Download manifest-listed icons once; verify sources before enabling a provider.

This script is intentionally inactive for unknown providers. It will not scrape
websites or place unlicensed assets into the repository.
"""

from __future__ import annotations

import json
import urllib.request
from pathlib import Path


MANIFEST = Path(__file__).resolve().parent.parent / "assets" / "icons" / "manifest.json"
OUTPUT_DIR = MANIFEST.parent


def download() -> int:
    if not MANIFEST.exists():
        print("No verified icon manifest exists.")
        print("Create assets/icons/manifest.json only after checking source terms.")
        return 1

    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    for item in manifest["icons"]:
        if not item.get("verified", False):
            print(f"SKIPPED {item['id']}: source is not verified")
            continue
        url = item["url"]
        target = OUTPUT_DIR / f"{item['id']}.png"
        request = urllib.request.Request(
            url,
            headers={"User-Agent": "RustToolkit/Phase1"},
        )
        with urllib.request.urlopen(request, timeout=15) as response:
            target.write_bytes(response.read())
        print(f"DOWNLOADED {item['id']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(download())
