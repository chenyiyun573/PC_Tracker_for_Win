"""Event recorder.

Robustness improvements (2026-02-27):
- Store screenshot size together with screenshot bytes to survive resolution changes.
- Do not rely on a module-level `screen_size` inside multiprocessing workers.
- Add defensive error handling around async screenshot saving.
"""

from __future__ import annotations

import json
import multiprocessing
import os
from datetime import datetime
from typing import Any, Dict, Optional, Tuple

from PIL import Image, ImageDraw

from capturer import RecentScreen
from fs import delete_file, ensure_folder, hide_folder
from utils import get_current_time

MARK_IMAGE = False


class Recorder:
    def __init__(self, task=None, buffer_len: int = 1, directory: str = "events"):
        # Using a dedicated context improves Windows/pyinstaller reliability.
        try:
            ctx = multiprocessing.get_context("spawn")
        except Exception:
            ctx = multiprocessing

        # maxtasksperchild helps avoid long-run worker memory growth (PIL, etc.).
        self.pool = ctx.Pool(maxtasksperchild=200)

        self.task = task
        self.buffer_len = int(buffer_len)
        self.directory = directory
        self.screenshot_dir = os.path.join(directory, "screenshot")

        self.buffer = []  # [(event, rect)]
        self.saved_cnt = 0

        self.timestamp_str = datetime.now().strftime("%Y%m%d_%H%M%S")

        # Ensure directories exist
        ensure_folder(self.directory)
        ensure_folder(self.screenshot_dir)

        # Hide directory
        hide_folder(self.directory)

        # Generate filename prefix
        if self.task is not None:
            index = self.task.id
            prefix = f"task{index}" if index != 0 else "free_task"
        else:
            prefix = "events"

        self.event_filename = os.path.join(self.directory, f"{prefix}_{self.timestamp_str}.jsonl")
        self.md_filename = os.path.join(self.directory, f"{prefix}_{self.timestamp_str}.md")

        self.recent_screen = RecentScreen()
        self.screenshot_f_list = []

    def get_event(self, action=None) -> Dict[str, Any]:
        timestamp = get_current_time()
        screenshot, size = self.recent_screen.get(with_size=True)

        event: Dict[str, Any] = {
            "timestamp": timestamp,
            "action": action,
            "screenshot": screenshot,  # bytes until saved; later becomes filename
            "screenshot_size": list(size),  # JSON-friendly; removed/kept as needed
        }
        return event

    def record_event(self, event: Dict[str, Any], rect=None) -> None:
        self.buffer.append((event, rect))
        if len(self.buffer) > self.buffer_len:
            ev, rec = self.buffer.pop(0)
            self.save(ev, rec)

    def record_action(self, action, rect=None) -> None:
        event = self.get_event(action)
        self.record_event(event, rect)

    def get_last_action(self):
        if self.buffer and len(self.buffer) > 0:
            event, _ = self.buffer[-1]
            return event.get("action")
        return None

    def change_last_action(self, action) -> None:
        if self.buffer:
            event, rect = self.buffer.pop()
            event["action"] = action
            self.buffer.append((event, rect))
        else:
            print("WARNING: No record to change in the buffer!")

    def save(self, event: Dict[str, Any], rect) -> None:
        self.saved_cnt += 1

        timestamp = event["timestamp"].replace(":", "").replace("-", "")
        action = event["action"]

        screenshot_filename = os.path.join(self.screenshot_dir, f"{timestamp}_{self.saved_cnt}.png")

        point = {"x": getattr(action, "kwargs", {}).get("x"), "y": getattr(action, "kwargs", {}).get("y")}
        if None in point.values():
            point = None

        screenshot_bytes: bytes = event.get("screenshot") or b""
        size_list = event.get("screenshot_size") or [0, 0]
        size: Tuple[int, int] = (int(size_list[0]), int(size_list[1]))

        # Async save screenshot; fall back to sync on failures.
        try:
            self.pool.apply_async(save_screenshot, (screenshot_filename, screenshot_bytes, size, rect, point))
        except Exception:
            # Pool might be closed/terminated; do sync save.
            save_screenshot(screenshot_filename, screenshot_bytes, size, rect, point)

        # Write jsonl record (replace bytes with path)
        event["screenshot"] = screenshot_filename
        event["action"] = str(action)
        try:
            event["element"] = action.get_element()
        except Exception:
            event["element"] = "Unknown"
        event["rect"] = rect

        with open(self.event_filename, "a", encoding="utf-8") as f:
            json.dump(event, f, ensure_ascii=False)
            f.write("\n")

        self.screenshot_f_list.append(screenshot_filename)

    def wait(self) -> None:
        # Save all buffered events
        for event, rect in self.buffer:
            self.save(event, rect)
        self.buffer.clear()

        # Stop background screenshot refresh thread before shutdown.
        try:
            self.recent_screen.stop()
        except Exception:
            pass

        # Close process pool
        try:
            self.pool.close()
            self.pool.join()
        except Exception:
            try:
                self.pool.terminate()
            except Exception:
                pass

    def generate_md(self, task=None) -> None:
        if task is not None:
            self.task = task

        prompt = (
            "Given the screenshot as below.\n"
            "What's the next step that you will do to help with the task?"
        )

        with open(self.event_filename, "r", encoding="utf-8") as file:
            lines = file.readlines()

        markdown_content = []
        if self.task is not None:
            index = self.task.id
            description = self.task.description
            level = self.task.level
            if index == 0:
                markdown_content.append("# Free Task\n")
            else:
                markdown_content.append(f"# Task {index}\n")
            markdown_content.append(f"**Description:** {description}\n\n")
            markdown_content.append(f"**Level:** {level}\n\n")
        else:
            markdown_content.append("# Non task-oriented events\n")

        for line in lines:
            event = json.loads(line.strip())
            timestamp = event.get("timestamp", "")
            action = event.get("action", "")
            screenshot_path = event.get("screenshot", "")
            # remove the first directory so md can be moved around a bit easier
            screenshot_path = "\\".join(screenshot_path.split("\\")[1:])

            markdown_content.append(f"### {timestamp}\n")
            markdown_content.append(f"**Input:** \n\n{prompt}\n\n")
            markdown_content.append("\n\n")
            markdown_content.append(f"**Output:** \n\n{action}\n\n")

        with open(self.md_filename, "w", encoding="utf-8") as md_file:
            md_file.writelines(markdown_content)

    def discard(self) -> None:
        delete_file(self.event_filename)
        delete_file(self.md_filename)
        for f in self.screenshot_f_list:
            delete_file(f)


def save_screenshot(
    save_filename: str,
    screenshot: bytes,
    size: Tuple[int, int],
    rect=None,
    point=None,
) -> None:
    """Decode raw BGRX bytes into a PNG.

    Note: this function is called inside multiprocessing worker processes.
    Avoid relying on globals that may become stale (e.g., screen_size).
    """

    w, h = size
    if not screenshot or w <= 0 or h <= 0:
        # Nothing to save.
        return

    try:
        image = Image.frombuffer("RGB", (w, h), screenshot, "raw", "BGRX", 0, 1)
        if MARK_IMAGE:
            mark_image(image, rect, point)
        image.save(save_filename)
    except Exception as e:
        # Avoid crashing workers; optionally write a small marker file.
        try:
            with open(save_filename + ".error.txt", "w", encoding="utf-8") as f:
                f.write(repr(e))
        except Exception:
            pass


def mark_image(image: Image.Image, rect, point) -> None:
    if rect is not None:
        draw = ImageDraw.Draw(image)
        draw.rectangle(
            [(rect["left"], rect["top"]), (rect["right"], rect["bottom"])],
            outline="red",
            width=3,
        )

    if point is not None:
        draw = ImageDraw.Draw(image)
        radius = 6
        left = point["x"] - radius
        top = point["y"] - radius
        right = point["x"] + radius
        bottom = point["y"] + radius
        draw.ellipse([(left, top), (right, bottom)], fill="red")
