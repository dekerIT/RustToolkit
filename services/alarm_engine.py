from __future__ import annotations

import ctypes
import logging
import math
import tempfile
import threading
import wave
from pathlib import Path

from PySide6.QtCore import QObject, Signal


class AlarmEngine(QObject):
    triggered = Signal(str)
    stopped = Signal()

    def __init__(self) -> None:
        super().__init__()
        self._active = False
        self._volume = 70
        self._repeat = True
        self._keep_awake = False
        self._mixer_ready = False
        self._wake_state = 0
        self._generate_siren()
        self._init_audio()

    @property
    def active(self) -> bool:
        return self._active

    def configure(self, volume: int, repeat: bool, keep_awake: bool) -> None:
        self._volume = max(0, min(100, int(volume)))
        self._repeat = repeat
        self._keep_awake = keep_awake
        if self._mixer_ready:
            try:
                import pygame
                pygame.mixer.music.set_volume(self._volume / 100)
            except Exception as exc:
                logging.getLogger(__name__).warning("Could not set alarm volume: %s", exc)

    def trigger(self, reason: str = "Manual alarm") -> None:
        reason = reason.strip() or "Alarm"
        self._active = True
        self.triggered.emit(reason)
        self._set_keep_awake(True)
        self._play_siren()

    def stop(self) -> None:
        if not self._active:
            return
        self._active = False
        self._stop_siren()
        self._set_keep_awake(False)
        self.stopped.emit()

    def _generate_siren(self) -> None:
        cache_dir = Path(tempfile.gettempdir()) / "RustToolkit"
        cache_dir.mkdir(parents=True, exist_ok=True)
        self.siren_path = cache_dir / "rusttoolkit_siren.wav"
        if self.siren_path.exists():
            return

        sample_rate = 22050
        duration = 1.2
        frames = int(sample_rate * duration)
        with wave.open(str(self.siren_path), "wb") as output:
            output.setnchannels(1)
            output.setsampwidth(2)
            output.setframerate(sample_rate)
            data = bytearray()
            for index in range(frames):
                time = index / sample_rate
                frequency = 650 + 350 * (0.5 + 0.5 * math.sin(2 * math.pi * 1.2 * time))
                value = int(18000 * math.sin(2 * math.pi * frequency * time))
                data += int(value).to_bytes(2, byteorder="little", signed=True)
            output.writeframes(bytes(data))

    def _init_audio(self) -> None:
        try:
            import pygame
            pygame.mixer.init(frequency=22050, size=-16, channels=1)
            pygame.mixer.music.set_volume(self._volume / 100)
            pygame.mixer.music.load(str(self.siren_path))
            self._mixer_ready = True
        except Exception as exc:
            logging.getLogger(__name__).warning("Alarm audio unavailable: %s", exc)

    def _play_siren(self) -> None:
        if not self._mixer_ready:
            return
        try:
            import pygame
            pygame.mixer.music.stop()
            pygame.mixer.music.play(-1 if self._repeat else 0)
        except Exception as exc:
            logging.getLogger(__name__).warning("Could not play alarm sound: %s", exc)

    def _stop_siren(self) -> None:
        if not self._mixer_ready:
            return
        try:
            import pygame
            pygame.mixer.music.stop()
        except Exception as exc:
            logging.getLogger(__name__).warning("Could not stop alarm sound: %s", exc)

    def _set_keep_awake(self, enabled: bool) -> None:
        if not self._keep_awake:
            return
        if not hasattr(ctypes, "windll"):
            return
        es_continuous = 0x80000000
        es_system_required = 0x00000001
        es_display_required = 0x00000002
        try:
            if enabled:
                ctypes.windll.kernel32.SetThreadExecutionState(
                    es_continuous | es_system_required | es_display_required
                )
            else:
                ctypes.windll.kernel32.SetThreadExecutionState(es_continuous)
        except Exception as exc:
            logging.getLogger(__name__).warning("Could not prevent sleep: %s", exc)
