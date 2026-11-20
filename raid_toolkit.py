"""RustToolkit application entry point."""

from __future__ import annotations

import logging
import sys
from pathlib import Path

from PySide6.QtWidgets import QApplication, QMessageBox
from PySide6.QtGui import QIcon

from ui.main_window import MainWindow
from services.external_app import launch_battlemetrics_map
from services.logging_config import configure_logging
from services.settings import SettingsStore


def main() -> int:
    configure_logging()
    app = QApplication(sys.argv)
    app.setApplicationName("Raid Toolkit")
    app.setOrganizationName("RaidToolkit")

    icon_path = Path(__file__).resolve().parent / "assets" / "rusttoolkit.ico"
    if icon_path.exists():
        app.setWindowIcon(QIcon(str(icon_path)))

    window = MainWindow()
    window.show()
    settings = SettingsStore()
    settings.ensure_defaults()
    try:
        launch_battlemetrics_map()
    except OSError as exc:
        logging.getLogger(__name__).exception("Failed to launch BattleMetrics map")
        QMessageBox.warning(
            window,
            "BattleMetrics Map",
            f"Could not start the BattleMetrics map:\n{exc}",
        )
    return app.exec()


if __name__ == "__main__":
    raise SystemExit(main())
