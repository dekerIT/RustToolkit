from PySide6.QtWidgets import QWidget, QVBoxLayout, QLabel
from PySide6.QtCore import Qt


class NotImplementedPage(QWidget):
    def __init__(self, title: str, detail: str) -> None:
        super().__init__()
        layout = QVBoxLayout(self)
        layout.setAlignment(Qt.AlignmentFlag.AlignCenter)
        heading = QLabel(title)
        heading.setStyleSheet("font-size: 24px; font-weight: 700;")
        heading.setAlignment(Qt.AlignmentFlag.AlignCenter)
        message = QLabel(detail)
        message.setWordWrap(True)
        message.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.addWidget(heading)
        layout.addWidget(message)
