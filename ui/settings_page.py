from __future__ import annotations

import shutil
import sys
import winreg
from pathlib import Path

from PySide6.QtGui import QDesktopServices
from PySide6.QtCore import QUrl
from PySide6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QLabel, QSlider, QCheckBox,
    QPushButton, QGroupBox, QFrame,
)
from PySide6.QtCore import Qt

from services.settings import SettingsStore


class SettingsPage(QWidget):
    def __init__(self, engine, screens) -> None:
        super().__init__()
        self.engine = engine
        self.store = SettingsStore()
        self.settings = self.store.ensure_defaults()
        self.screens = screens
        self.setup_ui()

    def setup_ui(self) -> None:
        root = QVBoxLayout(self)
        root.setContentsMargins(18, 18, 18, 18)
        root.setSpacing(12)

        title = QLabel("Settings")
        title.setStyleSheet("font-size: 24px; font-weight: 700;")
        root.addWidget(title)

        alarm_box = QGroupBox("Raid Alarm")
        alarm_layout = QVBoxLayout(alarm_box)

        volume_row = QHBoxLayout()
        volume_row.addWidget(QLabel("Volume"))
        self.volume = QSlider(Qt.Orientation.Horizontal)
        self.volume.setRange(0, 100)
        self.volume.setValue(int(self.settings["alarm_volume"]))
        self.volume.valueChanged.connect(self.save_alarm_options)
        self.volume_label = QLabel(f"{self.volume.value()}%")
        self.volume.valueChanged.connect(lambda value: self.volume_label.setText(f"{value}%"))
        volume_row.addWidget(self.volume)
        volume_row.addWidget(self.volume_label)
        alarm_layout.addLayout(volume_row)

        self.repeat = QCheckBox("Repeat siren until acknowledged")
        self.repeat.setChecked(bool(self.settings["repeat_alarm"]))
        self.repeat.toggled.connect(self.save_alarm_options)
        alarm_layout.addWidget(self.repeat)

        self.keep_awake = QCheckBox("Keep PC awake while alarm is active")
        self.keep_awake.setChecked(bool(self.settings["keep_pc_awake"]))
        self.keep_awake.toggled.connect(self.save_alarm_options)
        alarm_layout.addWidget(self.keep_awake)

        monitors_label = QLabel("Flash monitors")
        alarm_layout.addWidget(monitors_label)
        self.monitor_checks: list[QCheckBox] = []
        selected = set(self.settings["selected_monitors"])
        for index, screen in enumerate(self.screens):
            name = screen.name() or f"Monitor {index + 1}"
            check = QCheckBox(name)
            check.setChecked(index in selected or not selected)
            check.toggled.connect(self.save_alarm_options)
            alarm_layout.addWidget(check)
            self.monitor_checks.append(check)
        root.addWidget(alarm_box)

        system_box = QGroupBox("System")
        system_layout = QVBoxLayout(system_box)
        self.startup = QCheckBox("Start RustToolkit with Windows")
        self.startup.setChecked(bool(self.settings["start_with_windows"]))
        self.startup.toggled.connect(self.set_startup)
        system_layout.addWidget(self.startup)

        clear_cache = QPushButton("Clear RustToolkit Cache")
        clear_cache.clicked.connect(self.clear_cache)
        system_layout.addWidget(clear_cache)

        open_config = QPushButton("Open Local Configuration Folder")
        open_config.clicked.connect(self.open_config)
        system_layout.addWidget(open_config)
        root.addWidget(system_box)

        root.addStretch()
        self.save_alarm_options()

    def save_alarm_options(self) -> None:
        selected = [
            index for index, check in enumerate(self.monitor_checks)
            if check.isChecked()
        ]
        volume = self.volume.value()
        repeat = self.repeat.isChecked()
        keep_awake = self.keep_awake.isChecked()
        self.store.set("alarm_volume", volume)
        self.store.set("selected_monitors", selected)
        self.store.set("repeat_alarm", repeat)
        self.store.set("keep_pc_awake", keep_awake)
        self.engine.configure(volume, repeat, keep_awake)

    def set_startup(self, enabled: bool) -> None:
        self.store.set("start_with_windows", enabled)
        executable = str(Path(sys.executable).resolve())
        if getattr(sys, "frozen", False):
            executable = str(Path(sys.argv[0]).resolve())

        key_path = r"Software\Microsoft\Windows\CurrentVersion\Run"
        try:
            with winreg.CreateKey(winreg.HKEY_CURRENT_USER, key_path) as key:
                if enabled:
                    winreg.SetValueEx(key, "RustToolkit", 0, winreg.REG_SZ, f'"{executable}"')
                else:
                    try:
                        winreg.DeleteValue(key, "RustToolkit")
                    except FileNotFoundError:
                        pass
        except OSError as exc:
            self.startup.setChecked(False)
            self.store.set("start_with_windows", False)
            self.status_label = QLabel(f"Startup setting failed: {exc}")

    def open_config(self) -> None:
        QDesktopServices.openUrl(QUrl.fromLocalFile(str(self.store.path.parent)))

    def clear_cache(self) -> None:
        targets = [
            Path.home() / "AppData" / "Local" / "RustToolkit" / "cache",
            Path(__import__("tempfile").gettempdir()) / "RustToolkit",
        ]
        removed = 0
        for target in targets:
            if target.exists():
                try:
                    shutil.rmtree(target)
                    removed += 1
                except OSError:
                    pass
        self.store.save(self.store.ensure_defaults())
