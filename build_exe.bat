@echo off
setlocal
py -3.11 -m venv .venv
call .venv\Scripts\activate.bat
python -m pip install --upgrade pip
pip install -r requirements.txt
pip install pyinstaller
pyinstaller --noconsole --clean --onefile --name RaidToolkit --add-data "data;data" --add-data "assets;assets" raid_toolkit.py
pause
