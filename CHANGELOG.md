# Change log

## 1.7.2 — Adaptive action row

- Consolidated Free RAM, Refresh and Settings into one row; labels collapse to accessible vector icons in narrower/shorter layouts.
- Compact mode keeps the same actions in its header without adding a row. Restore/close remain in the More menu and keyboard shortcuts.
- Fixed narrow layouts shrinking the entire widget when charts appeared without enough vertical room. Charts now require enough space for the whole grid.
- Added 480 forward/reverse breakpoint checks, including long clock labels, action visibility, non-overlap and label restoration.

## 1.7.1 — Toolbar fit control

- Moved Fit height to a crisp vector icon in the top toolbar in both full and compact layouts, with an accessible name and hover description.
- Removed the text button beside Free RAM. Refresh intervals are unchanged.

## 1.7.0 — Public release

- MIT license, installation/upgrade instructions, privacy/security guidance, contributor docs and issue templates.
- Isolated setup for Python 3.11–3.14 x64; self-contained Windows packaging, checksums and explicit file boundaries.
- Copyable diagnostics without identities, credentials or paths.
- Offline native checks, two-version Python CI, browser regressions and Git-history secret scanning.
- Codex active-account verification takes priority; historical fallback is stale and cannot trigger alerts.
- Fixed first/removed-account selection in Claude browser sync.
- Reminder settings remain accessible on short displays; checks use effective dimensions and actual 4× export resolution.
- Smallest compact layout retains unscaled text even for disconnected accounts.

## 1.6.0 — Reset reminders

- Configurable quiet reminders before known resets with >10% left, persistent deduplication and sample preview.

## 1.5.0 — World clocks

- Two configurable timezone clocks with daylight saving, date offsets and compact header integration.

## Earlier work

- Adaptive glass widget, automated quota adapters, Claude CLI, RAM controls, generated icon, vector charts, responsive presets and crisp DPI rendering.
