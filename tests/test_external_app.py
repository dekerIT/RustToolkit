from __future__ import annotations

import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from services.external_app import launch_battlemetrics_map, resolve_battlemetrics_map_path


class ExternalAppTests(unittest.TestCase):
    def test_frozen_map_is_extracted_to_persistent_directory(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            bundled_dir = root / "assets" / "RustMap"
            bundled_dir.mkdir(parents=True)
            (bundled_dir / "battlemetricsmap.exe").write_bytes(b"map")
            (bundled_dir / "gup.xml").write_bytes(b"configuration")
            (bundled_dir / "libcurl.dll").write_bytes(b"dependency")

            with (
                patch.object(sys, "_MEIPASS", str(root), create=True),
                patch.object(Path, "home", return_value=root),
            ):
                executable = resolve_battlemetrics_map_path()

            self.assertTrue(executable.is_file())
            self.assertEqual(executable.read_bytes(), b"map")
            self.assertEqual((executable.parent / "gup.xml").read_bytes(), b"configuration")
            self.assertEqual((executable.parent / "libcurl.dll").read_bytes(), b"dependency")
            self.assertEqual(executable.parent.parent.name, "BattleMetricsMap")

    def test_launch_uses_map_directory_as_working_directory(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            executable = Path(temp_dir) / "battlemetricsmap.exe"
            with (
                patch("services.external_app.resolve_battlemetrics_map_path", return_value=executable),
                patch("services.external_app.subprocess.Popen") as popen,
            ):
                launch_battlemetrics_map()

            popen.assert_called_once_with([str(executable)], cwd=str(executable.parent))


if __name__ == "__main__":
    unittest.main()
