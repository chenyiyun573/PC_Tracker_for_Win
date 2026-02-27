# PC_Tracker_for_Win – Robust Screenshot Patch

This patch fixes the common long-run failure mode where screenshots become *stuck* (all later screenshots are identical) after several hours, often triggered by Windows sleep/lock, display reconfiguration (dock/undock), or GDI resource exhaustion.

## What changed

### `tracker/capturer.py`
- Fixes a GDI/DC handle leak (`img_dc.DeleteDC()` was missing).
- Re-computes screen size on each capture (instead of caching it once at import time).
- Makes the background refresh thread resilient (exceptions no longer kill it silently).
- Adds a watchdog in `RecentScreen.get()` to recover if the refresh thread stalls.
- Adds lightweight rotating log file output (`pc_tracker.log` in the working directory).

### `tracker/recorder.py`
- Captures and stores screenshot size for each event.
- Passes size into the multiprocessing worker so PNG decoding is correct even after resolution changes.
- Adds defensive error handling around async saving.

## How to apply

1. Unzip your existing `PC_Tracker_for_Win` project.
2. Copy the two patched files from this zip into your project, overwriting:

- `tracker/capturer.py`
- `tracker/recorder.py`

3. Rebuild your executable if needed:

```powershell
cd tracker
.\package.ps1
```

## Notes
- This patch does not change your event JSONL format except for adding `screenshot_size` (a `[w, h]` list) to each event.
- If your workflow depends on an exact schema, you can remove that field after saving, but it is useful for debugging.
