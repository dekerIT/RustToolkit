from __future__ import annotations

from __future__ import annotations

import ctypes
import sys

from PySide6.QtCore import Qt, QEvent
from PySide6.QtWidgets import (
    QMainWindow, QWidget, QHBoxLayout, QVBoxLayout, QFrame, QPushButton,
    QStackedWidget, QLabel, QSystemTrayIcon, QMenu, QApplication,
)
from PySide6.QtGui import QCloseEvent, QAction, QIcon

from ui.theme import app_stylesheet
from ui.raid_calculator_page import RaidCalculatorPage
from ui.alarm_page import AlarmPage
from ui.alarm_window import AlarmWindow
from ui.settings_page import SettingsPage
from ui.not_implemented_page import NotImplementedPage
from services.alarm_engine import AlarmEngine
from services.local_http_server import AlarmHTTPServer
from services.settings import SettingsStore


class MainWindow(QMainWindow):
    def __init__(self) -> None:
        super().__init__()
        self.setWindowTitle("RustToolkit")
        self.resize(1180, 720)
        self.setMinimumSize(900, 600)
        self.setStyleSheet(app_stylesheet())
        self.alarm_engine = AlarmEngine()
        self.settings_store = SettingsStore()
        initial = self.settings_store.ensure_defaults()
        self.alarm_engine.configure(
            initial["alarm_volume"],
            initial["repeat_alarm"],
            initial["keep_pc_awake"],
        )
        self.alarm_windows: list[AlarmWindow] = []
        self._allow_close = False
        self._hotkey_registered = False

        self.alarm_http = AlarmHTTPServer(
            trigger_callback=self.alarm_engine.trigger,
            stop_callback=self.alarm_engine.stop,
        )
        self.alarm_http.start()

        self.alarm_engine.triggered.connect(self.show_alarm_windows)
        self.alarm_engine.stopped.connect(self.close_alarm_windows)

        root = QWidget()
        layout = QHBoxLayout(root)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(0)
        self.setCentralWidget(root)

        sidebar = QFrame()
        sidebar.setObjectName("Sidebar")
        sidebar.setFixedWidth(220)
        sidebar_layout = QVBoxLayout(sidebar)
        sidebar_layout.setContentsMargins(10, 14, 10, 10)
        sidebar_layout.setSpacing(4)

        brand = QLabel("RUSTTOOLKIT")
        brand.setStyleSheet("font-size: 16px; font-weight: 800; padding: 8px 10px 18px 10px;")
        sidebar_layout.addWidget(brand)

        self.pages = QStackedWidget()
        self.nav_buttons: list[QPushButton] = []
        destinations = [
            ("Server Browser", NotImplementedPage(
                "Server Browser",
                "Not implemented yet. Phase 3 will add BattleMetrics live data and transparent ranking.",
            )),
            ("Raid Calculator", RaidCalculatorPage()),
            ("Raid Alarm", AlarmPage(self.alarm_engine)),
            ("Settings", SettingsPage(self.alarm_engine, QApplication.screens())),
            ("Decay", NotImplementedPage(
                "Decay",
                "Not implemented yet. This low-priority tool is reserved for cleanup in a later phase.",
            )),
        ]

        for index, (label, page) in enumerate(destinations):
            button = QPushButton(label)
            button.setObjectName("NavButton")
            button.setCheckable(True)
            button.clicked.connect(lambda checked=False, i=index: self.switch_page(i))
            sidebar_layout.addWidget(button)
            self.nav_buttons.append(button)
            self.pages.addWidget(page)

        sidebar_layout.addStretch()
        status = QLabel("Data status: Raid values unverified")
        status.setWordWrap(True)
        status.setStyleSheet("color: #e0b45b; font-size: 9pt; padding: 8px 10px;")
        sidebar_layout.addWidget(status)

        layout.addWidget(sidebar)
        layout.addWidget(self.pages)
        self.switch_page(1)
        self.setup_tray()

    def switch_page(self, index: int) -> None:
        self.pages.setCurrentIndex(index)
        for button_index, button in enumerate(self.nav_buttons):
            button.setChecked(button_index == index)

    def setup_tray(self) -> None:
        if not QSystemTrayIcon.isSystemTrayAvailable():
            return
        self.tray = QSystemTrayIcon(self)
        self.tray.setIcon(self.windowIcon() if not self.windowIcon().isNull() else QIcon())
        menu = QMenu(self)
        show_action = QAction("Show RustToolkit", self)
        show_action.triggered.connect(self.show_and_activate)
        test_action = QAction("Test Alarm", self)
        test_action.triggered.connect(lambda: self.alarm_engine.trigger("Tray test"))
        stop_action = QAction("Stop Alarm", self)
        stop_action.triggered.connect(self.alarm_engine.stop)
        quit_action = QAction("Quit", self)
        quit_action.triggered.connect(self.quit_application)
        menu.addAction(show_action)
        menu.addAction(test_action)
        menu.addAction(stop_action)
        menu.addSeparator()
        menu.addAction(quit_action)
        self.tray.setContextMenu(menu)
        self.tray.activated.connect(self.tray_activated)
        self.tray.show()

    def tray_activated(self, reason: QSystemTrayIcon.ActivationReason) -> None:
        if reason in (
            QSystemTrayIcon.ActivationReason.Trigger,
            QSystemTrayIcon.ActivationReason.DoubleClick,
        ):
            self.show_and_activate()

    def show_and_activate(self) -> None:
        self.show()
        self.showNormal()
        self.activateWindow()
        self.raise_()

    def quit_application(self) -> None:
        self.alarm_engine.stop()
        self.alarm_http.stop()
        if hasattr(self, "tray"):
            self.tray.hide()
        self._allow_close = True
        self.close()
        QApplication.instance().quit()

    def show_alarm_windows(self, reason: str) -> None:
        self.close_alarm_windows()
        screens = QApplication.screens()
        selected = set(self.settings_store.get("selected_monitors", []))
        for index, screen in enumerate(screens):
            if selected and index not in selected:
                continue
            window = AlarmWindow(reason, self.alarm_engine.stop)
            window.setGeometry(screen.availableGeometry())
            window.showFullScreen()
            window.raise_()
            window.activateWindow()
            self.alarm_windows.append(window)
        if not self.alarm_windows and screens:
            window = AlarmWindow(reason, self.alarm_engine.stop)
            window.setGeometry(screens[0].availableGeometry())
            window.showFullScreen()
            self.alarm_windows.append(window)

    def close_alarm_windows(self) -> None:
        for window in self.alarm_windows:
            window.close()
        self.alarm_windows.clear()

    def showEvent(self, event) -> None:
        super().showEvent(event)
        if hasattr(ctypes, "windll") and not self._hotkey_registered:
            hwnd = int(self.winId())
            result = ctypes.windll.user32.RegisterHotKey(hwnd, 1, 0, 0x77)
            self._hotkey_registered = bool(result)

    def nativeEvent(self, event_type, message):
        if hasattr(ctypes, "windll") and event_type == b"windows_generic_MSG":
            msg = ctypes.cast(message, ctypes.POINTER(ctypes.wintypes.MSG)).contents
            if msg.message == 0x0312 and msg.wParam == 1:
                self.alarm_engine.stop()
                return True, 0
        return super().nativeEvent(event_type, message)

    def closeEvent(self, event: QCloseEvent) -> None:
        if not self._allow_close:
            event.ignore()
            self.hide()
            if hasattr(self, "tray"):
                self.tray.showMessage(
                    "RustToolkit",
                    "RustToolkit is running in the system tray.",
                    QSystemTrayIcon.MessageIcon.Information,
                    3000,
                )
            return
        if hasattr(ctypes, "windll") and self._hotkey_registered:
            ctypes.windll.user32.UnregisterHotKey(int(self.winId()), 1)
        event.accept()
