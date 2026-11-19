"""Raid Toolkit application entry point."""

from __future__ import annotations

import sys

from PySide6.QtWidgets import QApplication

from ui.main_window import MainWindow
from services.logging_config import configure_logging
from services.external_app import resolve_external_app_path, launch_external_app
from services.settings import SettingsStore


def main() -> int:
    configure_logging()
    app = QApplication(sys.argv)
    app.setApplicationName("Raid Toolkit")
    app.setOrganizationName("RaidToolkit")
    window = MainWindow()
    window.show()
    settings = SettingsStore()
    if settings.get("launch_external_app", True):
        launch_external_app(resolve_external_app_path(settings.get("external_app_path", "")))
    return app.exec()


if __name__ == "__main__":
    raise SystemExit(main())
