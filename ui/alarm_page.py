from __future__ import annotations

from PySide6.QtCore import QTimer
from PySide6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QLabel, QPushButton,
    QSpinBox, QGroupBox,
)

from ui.theme import COLORS


class AlarmPage(QWidget):
    def __init__(self, engine) -> None:
        super().__init__()
        self.engine = engine
        self._remaining = 0
        self._countdown_timer = QTimer(self)
        self._countdown_timer.timeout.connect(self._tick)
        self.setup_ui()

    def setup_ui(self) -> None:
        root = QVBoxLayout(self)
        root.setContentsMargins(18, 18, 18, 18)

        title = QLabel("Raid Alarm")
        title.setStyleSheet("font-size: 24px; font-weight: 700;")
        root.addWidget(title)

        status = QGroupBox("Status")
        status_layout = QVBoxLayout(status)
        self.status_label = QLabel("Idle")
        self.status_label.setStyleSheet("font-size: 18px;")
        status_layout.addWidget(self.status_label)
        root.addWidget(status)

        countdown = QGroupBox("Countdown")
        countdown_layout = QHBoxLayout(countdown)
        self.minutes = QSpinBox()
        self.minutes.setRange(0, 1440)
        self.minutes.setValue(5)
        self.minutes.setSuffix(" min")
        self.seconds = QSpinBox()
        self.seconds.setRange(0, 59)
        self.seconds.setValue(0)
        self.seconds.setSuffix(" sec")
        countdown_layout.addWidget(QLabel("Delay"))
        countdown_layout.addWidget(self.minutes)
        countdown_layout.addWidget(self.seconds)
        start_countdown = QPushButton("Start Countdown")
        start_countdown.clicked.connect(self.start_countdown)
        countdown_layout.addWidget(start_countdown)
        stop_countdown = QPushButton("Cancel Countdown")
        stop_countdown.clicked.connect(self.cancel_countdown)
        countdown_layout.addWidget(stop_countdown)
        root.addWidget(countdown)

        controls = QGroupBox("Manual Trigger")
        controls_layout = QHBoxLayout(controls)
        test_button = QPushButton("Test Alarm")
        test_button.clicked.connect(lambda: self.engine.trigger("Manual test"))
        stop_button = QPushButton("Stop Alarm")
        stop_button.clicked.connect(self.engine.stop)
        controls_layout.addWidget(test_button)
        controls_layout.addWidget(stop_button)
        root.addWidget(controls)

        endpoint = QLabel(
            "Local endpoint: 127.0.0.1:47800\n"
            "POST /alarm?reason=Base+raided\n"
            "POST /stop"
        )
        endpoint.setWordWrap(True)
        endpoint.setStyleSheet(
            f"color: {COLORS['muted']}; background: {COLORS['panel_alt']}; "
            "padding: 12px; border-radius: 6px;"
        )
        root.addWidget(endpoint)
        root.addStretch()

        self.engine.triggered.connect(self._engine_triggered)
        self.engine.stopped.connect(self._engine_stopped)

    def _engine_triggered(self, reason: str) -> None:
        self.status_label.setText(f"ACTIVE — {reason}")
        self.status_label.setStyleSheet("font-size: 18px; color: #e45757;")

    def _engine_stopped(self) -> None:
        self.status_label.setText("Idle")
        self.status_label.setStyleSheet("font-size: 18px;")

    def start_countdown(self) -> None:
        self._remaining = self.minutes.value() * 60 + self.seconds.value()
        if self._remaining <= 0:
            self.status_label.setText("Countdown must be longer than zero")
            return
        self.status_label.setText(f"Countdown — {self._remaining} seconds")
        self._countdown_timer.start(1000)

    def cancel_countdown(self) -> None:
        self._countdown_timer.stop()
        self._remaining = 0
        if not self.engine.active:
            self.status_label.setText("Idle")

    def _tick(self) -> None:
        self._remaining -= 1
        if self._remaining <= 0:
            self._countdown_timer.stop()
            self.engine.trigger("Countdown alarm")
            return
        self.status_label.setText(f"Countdown — {self._remaining} seconds")
