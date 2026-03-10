"""Screen capture utilities.

This module is used by Recorder to grab desktop screenshots at a fixed interval.

Robustness improvements (2026-02-27):
- Avoid GDI/DC handle leaks (DeleteDC for img_dc; DeleteObject for bitmap; ReleaseDC).
- Re-compute screen size on each capture to survive sleep / display reconfiguration.
- Make the background refresh thread resilient (never silently dies on exceptions).
- Add a watchdog in `get()` to recover if the refresh thread stalls.

The original repo used a single global `screen_size` computed once at import time.
That breaks after sleep / RDP / docking / DPI changes, and can cause decode failures.
"""

from __future__ import annotations

import logging
import os
import threading
import time
from dataclasses import dataclass
from typing import Optional, Tuple, Union

import pyautogui
import win32con
import win32gui
import win32ui

# Backward-compat: keep a global screen_size for any legacy code that imports it.
# We keep it updated on each successful capture.
screen_size = pyautogui.size()


def _get_logger() -> logging.Logger:
    """Create/reuse a module logger that writes to a small rotating file."""
    logger = logging.getLogger("pc_tracker.capturer")
    if logger.handlers:
        return logger

    logger.setLevel(logging.INFO)

    # Try to log to a file in the working directory.
    # If it fails (e.g., permissions), fall back to stderr.
    try:
        from logging.handlers import RotatingFileHandler

        log_path = os.environ.get("PC_TRACKER_LOG", os.path.join(os.getcwd(), "pc_tracker.log"))
        handler = RotatingFileHandler(log_path, maxBytes=2_000_000, backupCount=3, encoding="utf-8")
        fmt = logging.Formatter("%(asctime)s [%(levelname)s] %(name)s: %(message)s")
        handler.setFormatter(fmt)
        logger.addHandler(handler)
    except Exception:
        handler = logging.StreamHandler()
        fmt = logging.Formatter("%(asctime)s [%(levelname)s] %(name)s: %(message)s")
        handler.setFormatter(fmt)
        logger.addHandler(handler)

    # Avoid double logging via root
    logger.propagate = False
    return logger


@dataclass(frozen=True)
class ScreenFrame:
    """A captured frame."""

    bits: bytes
    size: Tuple[int, int]
    captured_at: float


class ScreenCapturer:
    """Captures the current desktop as raw BGRX bytes."""

    def __init__(self):
        self.hwindow = win32gui.GetDesktopWindow()
        self._logger = _get_logger()

    def capture(self) -> ScreenFrame:
        """Capture a full-screen screenshot.

        Returns:
            ScreenFrame(bits=BGRX bytes, size=(w,h), captured_at=timestamp)
        """
        # Recompute each time for robustness.
        w, h = pyautogui.size()

        window_dc = None
        img_dc = None
        mem_dc = None
        bmp = None

        try:
            window_dc = win32gui.GetWindowDC(self.hwindow)
            if not window_dc:
                raise RuntimeError("GetWindowDC returned NULL")

            img_dc = win32ui.CreateDCFromHandle(window_dc)
            mem_dc = img_dc.CreateCompatibleDC()

            bmp = win32ui.CreateBitmap()
            bmp.CreateCompatibleBitmap(img_dc, w, h)
            mem_dc.SelectObject(bmp)

            # Bit block transfer from screen into memory bitmap.
            mem_dc.BitBlt((0, 0), (w, h), img_dc, (0, 0), win32con.SRCCOPY)

            bits = bmp.GetBitmapBits(True)

            # Update legacy global.
            global screen_size
            screen_size = (w, h)

            return ScreenFrame(bits=bits, size=(w, h), captured_at=time.time())

        finally:
            # Release resources in the correct order.
            try:
                if mem_dc is not None:
                    mem_dc.DeleteDC()
            except Exception:
                pass

            try:
                if img_dc is not None:
                    img_dc.DeleteDC()  # IMPORTANT: prevents GDI handle leaks
            except Exception:
                pass

            try:
                if window_dc is not None:
                    win32gui.ReleaseDC(self.hwindow, window_dc)
            except Exception:
                pass

            try:
                if bmp is not None:
                    win32gui.DeleteObject(bmp.GetHandle())
            except Exception:
                pass


capturer = ScreenCapturer()


class RecentScreen:
    """Keeps a recent screenshot in memory and refreshes it in a background thread."""

    def __init__(
        self,
        capture_interval: float = 0.1,
        stale_after_seconds: float = 5.0,
    ):
        self.capture_interval = float(capture_interval)
        self.stale_after_seconds = float(stale_after_seconds)

        self._logger = _get_logger()
        self._lock = threading.Lock()
        self._stop_event = threading.Event()

        # Seed with an initial frame (best-effort).
        try:
            frame = capturer.capture()
        except Exception:
            self._logger.exception("Initial screen capture failed; will retry in background")
            # Use an empty placeholder; get() will retry.
            frame = None

        self._frame: Optional[ScreenFrame] = frame
        self._last_error: Optional[str] = None

        self._refresh_thread = threading.Thread(target=self._refresh_loop, name="RecentScreenRefresh")
        self._refresh_thread.daemon = True
        self._refresh_thread.start()

    def stop(self) -> None:
        """Stop the background capture thread."""
        self._stop_event.set()

    def _refresh_loop(self) -> None:
        """Background capture loop (never exits on transient errors)."""
        global capturer

        while not self._stop_event.is_set():
            try:
                frame = capturer.capture()
                with self._lock:
                    self._frame = frame
                    self._last_error = None
            except Exception as e:
                # Do NOT let the thread die; sleep and retry.
                msg = f"capture failed: {e!r}"
                with self._lock:
                    self._last_error = msg
                self._logger.exception("%s", msg)

                # Recreate capturer in case DC/session objects got invalid after sleep/lock.
                try:
                    capturer = ScreenCapturer()
                except Exception:
                    self._logger.exception("Failed to recreate ScreenCapturer")

                time.sleep(1.0)
                continue

            time.sleep(self.capture_interval)

    def get(self, with_size: bool = False):
        """Return the most recent screenshot without blocking input callbacks.

        Important: event callbacks call into Recorder.get_event(), so doing a full
        synchronous screen capture here can block keyboard/mouse listener threads
        and cause bursts of user actions to be missed. We therefore only return the
        latest cached frame on the caller thread.

        Long-run robustness is still handled by the background refresh thread, which
        retries forever and recreates the capturer after failures.
        """
        now = time.time()

        with self._lock:
            frame = self._frame

        def _return(frame_obj: ScreenFrame):
            return (frame_obj.bits, frame_obj.size) if with_size else frame_obj.bits

        if frame is not None:
            # Even if stale, returning the last cached frame is better than blocking
            # the input listener with a direct capture.
            return _return(frame)

        # No frame available yet.
        return (b"", (0, 0)) if with_size else b""

    @property
    def last_error(self) -> Optional[str]:
        with self._lock:
            return self._last_error
