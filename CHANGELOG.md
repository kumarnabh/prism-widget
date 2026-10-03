# Change log

## 1.9.0 — Capacity intelligence

- Local account/window-scoped history and conservative burn-rate, sustainable pace, reset projection and exhaustion estimates.
- Chronological reset details, limiting-window status and cached tray capacity view.
- Opt-in predictive notifications with persistent deduplication and shared delivery cooldown.
- Window history selection, gaps, reset markers and optional estimated trajectory.
- English, Hindi, Spanish and French additions; estimates can be hidden entirely.
- Preserve 1.8 aggregate history separately; future history schemas are read-only.
- Fixed same-capture history persistence, account-switch collection races, capture-time projection drift, cross-account notification suppression and frozen tray values.
- No new dependencies, endpoints, prompts, analytics or cloud storage.

## 1.8.0 — Your workspace, your way

- Added system tray monitoring, background quota notifications and optional Windows startup.
- Independent refresh schedules/countdowns and configurable low-quota alerts.
- Local daily/weekly/30-day allowance history with recording controls and explicit clearing.
- Hide/reorder metrics, opacity, saved layout profiles, edge snapping and remembered monitor placement.
- Manual GitHub update checks and verified ZIP downloads; no automatic execution or installation.
- English, Hindi, Spanish and French core interface with 12/24-hour clocks.
- New settings tabs keep controls out of the compact dashboard.
- Fixed recovered-alert persistence, malformed profile clock labels, stale readings on long schedules, and full-transfer update timeouts.
- Added isolated feature checks and 160 translated/12-hour/custom-metric layout cases.

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
