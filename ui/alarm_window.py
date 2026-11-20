from __future__ import annotations

from PySide6.QtCore import Qt, QTimer
from PySide6.QtGui import QMouseEvent
from PySide6.QtWidgets import QWidget, QVBoxLayout, QLabel


class AlarmWindow(QWidget):
    def __init__(self, reason: str, ack_callback) -> None:
        super().__init__()
        self.reason = reason
        self.ack_callback = ack_callback
        self._dark = False
        self.setWindowFlags(
            Qt.WindowType.FramelessWindowHint
            | Qt.WindowType.WindowStaysOnTopHint
            | Qt.WindowType.Tool
        )
        self.setAttribute(Qt.WidgetAttribute.WA_DeleteOnClose)
        self.setCursor(Qt.CursorShape.BlankCursor)
        self.setStyleSheet("background: #a91d1d;")

        layout = QVBoxLayout(self)
        layout.setContentsMargins(40, 40, 40, 40)
        label = QLabel("RAID ALARM")
        label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        label.setStyleSheet("color: white; font-size: 56px; font-weight: 900;")
        reason_label = QLabel(reason)
        reason_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        reason_label.setStyleSheet("color: white; font-size: 22px;")
        hint = QLabel("Press F8 or double-click to acknowledge")
        hint.setAlignment(Qt.AlignmentFlag.AlignCenter)
        hint.setStyleSheet("color: white; font-size: 16px;")
        layout.addStretch()
        layout.addWidget(label)
        layout.addWidget(reason_label)
        layout.addWidget(hint)
        layout.addStretch()

        self.flash_timer = QTimer(self)
        self.flash_timer.timeout.connect(self.toggle_flash)
        self.flash_timer.start(450)

    def toggle_flash(self) -> None:
        self._dark = not self._dark
        self.setStyleSheet(
            "background: #111317;" if self._dark else "background: #a91d1d;"
        )

    def mouseDoubleClickEvent(self, event: QMouseEvent) -> None:
        self.ack_callback()

    def closeEvent(self, event) -> None:
        self.flash_timer.stop()
        event.accept()
