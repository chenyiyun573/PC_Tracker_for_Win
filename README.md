# PC Tracker Native

A Visual Studio-ready Windows desktop tracker that mirrors the core flow of the original Python PC_Tracker app:

- Given Task mode
- Free Task mode
- Non-Task mode
- global mouse and keyboard capture
- per-action screenshots
- idle / wait snapshots
- JSONL export
- Markdown export with embedded screenshots
- modern capture first (`Windows.Graphics.Capture`), automatic GDI fallback

## Open and build

1. Open `PcTrackerNative.sln` in Visual Studio 2022 or newer.
2. Restore NuGet packages.
3. Build `Release | x64`.
4. Run `PcTrackerNative.App.exe`.

## Requirements

- Windows 10 version 2004 or later recommended
- Visual Studio with desktop .NET / WPF support
- x64 build target

## What is inside

- `Windows.Graphics.Capture` for the primary screenshot path
- Raw Input for global keyboard / mouse monitoring
- fallback `Graphics.CopyFromScreen` capture path if WGC startup fails
- task list stored in `tasks.json` beside the exe
- recordings stored under `%LocalAppData%\PcTrackerNative\events`

## Notes

- This repo intentionally keeps the UI simple so it is easy to extend.
- The recorder filters out this app's own clicks / key presses by checking the foreground process.
- The main window requests `WDA_EXCLUDEFROMCAPTURE` so the tracker UI can stay out of screenshots on supported Windows versions.
- IME-heavy text input is still a known limitation, similar to the original tracker.

## Default controls

- Start from the tab you want.
- The window minimizes after recording starts.
- Restore it from the taskbar when you want to finish, fail, or discard.

## Files written per session

- `trajectory.jsonl`
- `trajectory.md`
- `session.json`
- `images\*.png`

## License notes

This repo includes adapted helper code based on Microsoft screen capture samples and the MIT-licensed `WindowsCapture` helper project. See `THIRD_PARTY_NOTICES.md`.


## 2026-03 build fix

This repo was updated to remove the explicit `Microsoft.Windows.CsWinRT` package reference and the `WindowsSdkPackageVersion` override. For .NET 8 desktop apps, the Windows-specific TFM already pulls in the appropriate Windows SDK targeting pack. These two settings were making builds depend on local `Platform.xml` files from a specific Windows SDK installation.
