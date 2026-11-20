from PySide6.QtGui import QColor


COLORS = {
    "bg": "#111317",
    "panel": "#181b20",
    "panel_alt": "#20242a",
    "text": "#f2f4f7",
    "muted": "#9aa3af",
    "accent": "#d86f32",
    "accent_hover": "#ec8747",
    "border": "#30353d",
    "danger": "#e45757",
    "success": "#68bd82",
    "warning": "#e0b45b",
}


def app_stylesheet() -> str:
    return f"""
    QWidget {{
        background: {COLORS["bg"]};
        color: {COLORS["text"]};
        font-family: "Segoe UI";
        font-size: 10pt;
    }}
    QFrame#Sidebar {{
        background: {COLORS["panel"]};
        border-right: 1px solid {COLORS["border"]};
    }}
    QPushButton#NavButton {{
        text-align: left;
        padding: 10px 14px;
        border: none;
        border-radius: 6px;
        color: {COLORS["muted"]};
    }}
    QPushButton#NavButton:hover {{
        background: {COLORS["panel_alt"]};
        color: {COLORS["text"]};
    }}
    QPushButton#NavButton:checked {{
        background: {COLORS["accent"]};
        color: white;
        font-weight: 600;
    }}
    QLineEdit, QComboBox, QSpinBox, QDoubleSpinBox {{
        background: {COLORS["panel_alt"]};
        border: 1px solid {COLORS["border"]};
        border-radius: 6px;
        padding: 7px;
        selection-background-color: {COLORS["accent"]};
    }}
    QTableWidget {{
        background: {COLORS["panel"]};
        border: 1px solid {COLORS["border"]};
        border-radius: 8px;
        gridline-color: {COLORS["border"]};
    }}
    QHeaderView::section {{
        background: {COLORS["panel_alt"]};
        padding: 8px;
        border: none;
        border-right: 1px solid {COLORS["border"]};
        border-bottom: 1px solid {COLORS["border"]};
    }}
    QPushButton {{
        background: {COLORS["accent"]};
        color: white;
        border: none;
        border-radius: 6px;
        padding: 8px 12px;
        font-weight: 600;
    }}
    QPushButton:hover {{ background: {COLORS["accent_hover"]}; }}
    """
