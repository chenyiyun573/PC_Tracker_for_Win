# PC Tracker Native

A Visual Studio-ready Windows desktop tracker focused on a single simple flow:

- one mode only
- Start recording
- Stop and save
- choose your own output folder
- global mouse and keyboard capture
- per-action screenshots
- idle / wait snapshots
- JSONL export written during recording
- Markdown export written during recording and finalized on stop
- modern capture first (`Windows.Graphics.Capture`), automatic GDI fallback
- crash / exception logging written into the chosen output folder under `logs`
- no `%LocalAppData%` settings file

## Open and build

1. Open `PcTrackerNative.sln` in Visual Studio 2022 or newer.
2. Restore NuGet packages.
3. Build `Release | x64`.
4. Run `PcTrackerNative.App.exe`.

## Requirements

- Windows 10 version 2004 or later recommended
- Visual Studio with desktop .NET / WPF support
- x64 build target

## What changed in this version

- kept the single-mode workflow and auto-save stop flow
- removed settings persistence to `%LocalAppData%`
- settings now live only in the GUI during the current run
- logs now go under the selected output root instead of `%LocalAppData%`
- `trajectory.md` still grows during recording instead of only being created after stop
- added generated app icon assets and applied them to the window / executable
- kept local try/catch guards around capture, raw input, timer, writer loop, and WndProc paths to reduce silent exits
- kept plain keyboard text capture for ordinary typing

## Files written per session

- `trajectory.jsonl`
- `trajectory.md`
- `session.json`
- `images\*.png`

## Other files under the output root

- `logs\app-YYYYMMDD.log`

## Notes

- The app writes partial JSONL and Markdown while recording, so if the process is interrupted you should still have a usable timeline file.
- The recorder filters out this app's own clicks / key presses by checking the foreground process.
- The main window requests `WDA_EXCLUDEFROMCAPTURE` so the tracker UI can stay out of screenshots on supported Windows versions.
- IME-heavy text input is still a known limitation.
