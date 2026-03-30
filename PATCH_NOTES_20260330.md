# Patch notes - 2026-03-30

This follow-up source patch changes the single-mode native tracker in the following ways:

1. No `%LocalAppData%` dependency
   - removed settings persistence to `%LocalAppData%`
   - default output root is now `Documents\PC_Tracker_Output`
   - settings stay in the GUI for the current app run only

2. Output-root logging
   - exception and app logs now go to `logs\app-YYYYMMDD.log` under the chosen output folder
   - the main window previews the current log file path directly in the GUI

3. Cleaner compact UI
   - kept a one-window card layout instead of multiple modes or tabs
   - tightened the layout around Start / Stop, output folder, settings, status, and timer
   - added explicit text that nothing is written to `%LocalAppData%`

4. App icon assets
   - added generated icon files for the app
   - applied the icon to the executable and the window header

5. Kept the important recording fix from the previous patch
   - `trajectory.md` is still created at recording start
   - every action still appends to markdown as it is recorded
   - final stop still rewrites a clean final markdown file
