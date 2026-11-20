from __future__ import annotations

from pathlib import Path

from PySide6.QtCore import Qt
from PySide6.QtGui import QPixmap
from PySide6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QLabel, QLineEdit, QGridLayout,
    QScrollArea, QPushButton, QFrame, QTableWidget, QTableWidgetItem,
)

from services.raid_data import RaidDataStore


class IconTile(QFrame):
    def __init__(self, structure: dict, icon_dir: Path, parent: QWidget | None = None) -> None:
        super().__init__(parent)
        self.structure = structure
        self.setFixedSize(132, 112)
        self.setStyleSheet(
            "QFrame { background: #20242a; border: 1px solid #30353d; border-radius: 8px; }"
            "QFrame:hover { border: 1px solid #d86f32; }"
        )

        layout = QVBoxLayout(self)
        layout.setContentsMargins(8, 8, 8, 8)
        layout.setSpacing(4)

        image = QLabel()
        image.setAlignment(Qt.AlignmentFlag.AlignCenter)
        icon_path = icon_dir / f"{structure['icon']}.png"
        if icon_path.exists():
            pixmap = QPixmap(str(icon_path)).scaled(
                72, 72, Qt.AspectRatioMode.KeepAspectRatio,
                Qt.TransformationMode.SmoothTransformation,
            )
            image.setPixmap(pixmap)
        else:
            image.setText("Icon\nnot verified")
            image.setAlignment(Qt.AlignmentFlag.AlignCenter)
            image.setStyleSheet(
                "color: #9aa3af; background: #181b20; border: 1px dashed #30353d;"
                "border-radius: 6px;"
            )
            image.setFixedSize(72, 72)

        name = QLabel(structure["name"])
        name.setAlignment(Qt.AlignmentFlag.AlignCenter)
        name.setWordWrap(True)
        layout.addWidget(image, 0, Qt.AlignmentFlag.AlignCenter)
        layout.addWidget(name)

        button = QPushButton("Select")
        button.setFixedHeight(24)
        button.setStyleSheet("QPushButton { padding: 2px; font-size: 9pt; }")
        layout.addWidget(button)
        self.select_button = button


class RaidCalculatorPage(QWidget):
    def __init__(self) -> None:
        super().__init__()
        self.data = RaidDataStore().load()
        self.selected: dict | None = None
        self.tiles: list[IconTile] = []
        self.setup_ui()

    def setup_ui(self) -> None:
        root = QHBoxLayout(self)
        root.setContentsMargins(18, 18, 18, 18)

        left = QVBoxLayout()
        title = QLabel("Raid Calculator")
        title.setStyleSheet("font-size: 24px; font-weight: 700;")
        left.addWidget(title)

        warning = QLabel(self.data["metadata"].get("warning", "Data verification is incomplete."))
        warning.setWordWrap(True)
        warning.setStyleSheet("color: #e0b45b; padding: 8px;")
        left.addWidget(warning)

        search = QLineEdit()
        search.setPlaceholderText("Search structures...")
        search.textChanged.connect(self.filter_tiles)
        left.addWidget(search)

        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        grid_host = QWidget()
        self.grid = QGridLayout(grid_host)
        self.grid.setAlignment(Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignLeft)
        scroll.setWidget(grid_host)
        left.addWidget(scroll, 1)

        right = QFrame()
        right.setFixedWidth(360)
        right_layout = QVBoxLayout(right)
        right_layout.setContentsMargins(14, 14, 14, 14)
        right_layout.setSpacing(10)
        right_layout.addWidget(QLabel("Raid Details"))

        self.selection_label = QLabel("No structure selected")
        self.selection_label.setWordWrap(True)
        right_layout.addWidget(self.selection_label)

        self.side_label = QLabel("Soft side: Not verified\nHard side: Not verified")
        self.side_label.setWordWrap(True)
        right_layout.addWidget(self.side_label)

        table_label = QLabel("Raid list")
        right_layout.addWidget(table_label)
        self.table = QTableWidget(0, 3)
        self.table.setHorizontalHeaderLabels(["Method", "Qty", "Resources"])
        self.table.horizontalHeader().setStretchLastSection(True)
        right_layout.addWidget(self.table)

        totals = QLabel(
            "Explosives: 0\nRaw resources: Not verified\n"
            "Cheapest method: Not verified"
        )
        totals.setWordWrap(True)
        totals.setStyleSheet("padding: 10px; background: #20242a; border-radius: 6px;")
        right_layout.addWidget(totals)

        root.addLayout(left, 1)
        root.addWidget(right)
        self.populate_tiles()

    def populate_tiles(self) -> None:
        for index in range(self.grid.count()):
            item = self.grid.itemAt(index)
            if item and item.widget():
                item.widget().deleteLater()
        self.tiles.clear()

        icon_dir = Path(__file__).resolve().parent.parent / "assets" / "icons"
        for structure in self.data["structures"]:
            tile = IconTile(structure, icon_dir)
            tile.select_button.clicked.connect(lambda checked=False, s=structure: self.select_structure(s))
            self.tiles.append(tile)
        self.filter_tiles("")

    def filter_tiles(self, text: str) -> None:
        query = text.strip().lower()
        for index in reversed(range(self.grid.count())):
            item = self.grid.itemAt(index)
            if item and item.widget():
                self.grid.removeWidget(item.widget())
                item.widget().hide()

        visible = [
            tile for tile in self.tiles
            if query in tile.structure["name"].lower()
            or query in tile.structure["category"].lower()
        ]
        for index, tile in enumerate(visible):
            row, column = divmod(index, 5)
            self.grid.addWidget(tile, row, column)
            tile.show()

    def select_structure(self, structure: dict) -> None:
        self.selected = structure
        self.selection_label.setText(
            f"{structure['name']} — UNVERIFIED\n"
            f"Durability: {structure.get('durability') or 'Not verified'}"
        )
        self.side_label.setText(
            f"Soft side: {structure['soft_side']}\n"
            f"Hard side: {structure['hard_side']}"
        )
