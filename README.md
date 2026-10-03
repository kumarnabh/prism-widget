# Prism

<img src="assets/prism-icon.png" width="112" alt="Prism glass icon">

**Your computer and AI capacity, at a glance.** A free, open-source Windows desktop widget with luminous glass, live system metrics, automatic subscription quota readings, world clocks and quiet reset reminders.

[Download](https://github.com/kumarnabh/prism-widget/releases/latest) · [Troubleshooting](TROUBLESHOOTING.md) · [Privacy](PRIVACY.md) · [Contribute](CONTRIBUTING.md)

![Checks](https://github.com/kumarnabh/prism-widget/actions/workflows/ci.yml/badge.svg)
![License: MIT](https://img.shields.io/badge/license-MIT-88dfd0)
![Platform: Windows x64](https://img.shields.io/badge/platform-Windows%20x64-a9d2ff)

## Features

- CPU/RAM usage, free system-drive space, download/upload rate and short history charts.
- Optional GPU percentage, dedicated VRAM, Windows-reported CPU frequency, battery and disk throughput. Multi-GPU and local-disk details live in **⋯ → System capacity**.
- Minimize to a taskbar button with live capacity bars in its hover/Peek preview, or use **⋯ → Taskbar ribbon** for a persistent compact view near the taskbar.
- Automatic Codex, Cursor Individual, OpenCode Go and Claude subscription quota adapters.
- Resizable dashboard, compact grid and slim ribbon. Labels disappear before metrics; the widget never scrolls.
- Two optional global timezone clocks with daylight saving, date rollover and UTC-offset tooltips. Defaults: US Eastern and US Pacific.
- Quiet reset reminders when **more than 10% remains**, with configurable lead time and persistent deduplication.
- Pinning, remembered position, size presets, keyboard controls and selected-app RAM trimming.
- Per-monitor DPI support, vector gauges/charts, and 4× exports with outlined text.
- Copyable dependency diagnostics without account details, credentials or machine paths.
- System tray monitoring, background notifications and optional per-user Windows startup.
- Independent provider schedules, next-check countdowns and configurable low-quota alerts.
- Local window-level allowance history, cautious burn-rate estimates, safe pace and an upcoming-reset timeline.
- A cached tray capacity view and optional predictive alerts; no model calls or cloud analytics.
- Hide/reorder metrics, adjust opacity and save Work, Gaming, Presentation or custom layout profiles.
- Show/hide every metric, including an empty layout, and optionally show only AI providers with valid quota windows. Stale readings retain their state; this filter does not verify paid entitlement.
- Screen-edge snapping and monitor-aware position restoration.
- English, Hindi, Spanish and French core interface, with 12/24-hour clocks.
- On-demand update checks and size/SHA-256-verified ZIP downloads from this repository.

Prism is an independent community project, not affiliated with OpenAI, Cursor, Anthropic or OpenCode. Percentages represent reported subscription allowance, **not exact spendable token counts**. Provider interface changes may require adapter updates. The core widget and new settings support English, Hindi, Spanish and French; provider messages, connection diagnostics and some advanced dialogs remain English. Timezone selection is global.

## Install

1. Use **Windows 10/11 x64** and install [64-bit Python 3.11–3.14](https://www.python.org/downloads/windows/), including its launcher. Windows 11 is the locally tested platform.
2. Download `Prism-<version>-win-x64.zip` and `SHA256SUMS.txt` from [Releases](https://github.com/kumarnabh/prism-widget/releases/latest). Compare the ZIP hash using `Get-FileHash .\Prism-<version>-win-x64.zip -Algorithm SHA256`.
3. Extract into a writable folder, such as Documents. Keep the files together. Do not run inside the ZIP or install in Program Files.
4. Double-click **Setup.cmd**. It creates a local `.venv`, installs pinned terminal dependencies and launches Prism. Internet is needed for this first dependency installation.
5. Open **⋯ → Connections** for your accounts. System readings work independently.

The release includes .NET; no SDK or administrator access is needed. Executables are currently **unsigned**. Windows may show an unknown-publisher warning; verify the source and checksum before deciding whether to run it. Windows startup is off by default and can be enabled in Settings. Update checks are manual; downloads never install or replace files automatically.

**Upgrade:** close Prism, extract a new release into a separate folder, and run Setup. Privately copy the old `data` folder before launching if you want to retain settings/caches. Never share it. Reconnect optional browser/status-line integration after changing the installation path. Keep the old installation until the upgrade works. If startup was enabled, turn it off in the old installation before enabling it in the new one.

## Connect accounts

| Service | Setup | Reading source |
| --- | --- | --- |
| Codex | Install and sign in to Codex CLI | Active-account quota through its local app server; historical telemetry is only a stale fallback after connection failure. |
| Cursor Individual | Sign in to Cursor; enable reuse in Prism | Read-only usage endpoint using the existing local sign-in. Team plans are unsupported. |
| OpenCode Go | Configure Go in OpenCode; enable reuse | Read-only Go usage endpoint. A Zen/API key without Go entitlement is insufficient. |
| Claude subscription | Install Claude Code; choose **Sign in to Claude** and complete first-run setup | Built-in interactive `/usage` panel every five minutes, with hooks/MCP disabled and no model prompt. |

Cursor and Go credential reuse is opt-in. Prism never asks for pasted tokens, never refreshes raw credentials, and sends them only to their own fixed HTTPS usage endpoints, refusing redirects. Authentication is managed by each provider's app/CLI.

Optional Claude browser sync and status-line feed setup is in the included [connection guide](README.html). Multiple browser accounts require an explicit choice. API-key Claude sessions do not provide subscription quota.

## Readings and controls

AI percentages show the **lowest remaining allowance among valid reported windows**: any window may limit use. Hover for all windows, source, capture time and known resets. A dash means unknown, never full. Status dots: green current, amber stale, pink error, gray unavailable. Capacity turns amber at 25% and coral at 10%.

System readings update every two seconds. Default AI checks: Codex every 60 seconds; Cursor, OpenCode Go and Claude every five minutes. Settings → Refresh rates lets you choose 1/5/10/15/30/60 minutes per provider. A five-second scheduler launches only due adapters; hover a provider or the footer for its countdown. F5 requests all providers immediately but still respects provider caches and error backoff. Readings older than ten minutes are marked stale, even with a longer schedule. Network is aggregate Ethernet/Wi-Fi throughput and may include virtual adapters. Charts retain up to 72 seconds. Disk bars show **free** space.

Drag the title to move and the lower-right grip to resize. The top toolbar's vertical-arrow icon fits height to content; double-clicking the grip does the same. The icon is available in full and compact layouts. The **⋯** menu remains accessible at every size. Short layouts combine metrics into two, four or eight columns. Hover for labels and details.

Free RAM, Refresh and Settings share one action row. Below 430 pixels wide or 760 pixels tall, labels collapse into icons with tooltips and accessible names. In compact mode, these actions share the header with Fit height and More. Use More or Ctrl+M to restore details; More or Esc closes Prism. Charts appear only when the entire grid fits without shrinking the dashboard.

| Shortcut | Action |
| --- | --- |
| F5 | Refresh |
| Ctrl+P | Pin / unpin |
| Ctrl+M | Compact / expanded |
| Ctrl+, | Settings |
| Esc | Close |

**Free RAM** asks Windows to trim eligible working-set pages from selected apps. It does not close apps, delete files or clear conversations. Memory may return immediately and apps may briefly slow down; this is not a memory-leak repair.

## Clocks and reminders

**⋯ → World clocks** enables two clocks with custom labels. US Eastern/Pacific follow daylight saving, rather than fixed EST/PST year-round. Hover for dates and UTC offsets. Compact clocks reuse the header without extra rows.

**⋯ → Reset reminders** offers 5/15/30/60/120 minutes, enable/disable and a preview. Default: enabled, 30 minutes. A quiet 12-second popup requires a current reading no older than ten minutes, a known future reset and **strictly more than 10% left**. Each provider/window/reset alerts once, even across restarts. Prism must be running. Visible widgets show a popup; hidden/minimized widgets use the system tray notification. Windows notification settings may suppress tray alerts. No model calls or automatic spending. Claude CLI currently has no parsed reset timestamp; browser/feed readings with timestamps can qualify.

## Make it yours

Open the **Settings** icon or **Ctrl+,**. The dashboard remains free of scrolling; settings pages may scroll on smaller screens.

| Settings page | Controls |
| --- | --- |
| General | Tray, optional Windows startup, edge snapping, 12/24-hour clock, language, opacity, connections and timezone settings |
| Refresh rates | Separate provider intervals; caches and provider backoff remain authoritative |
| Metrics | Check visible metrics, select a row and move it up/down; at least one metric must remain |
| Alerts | Low-quota threshold, reset reminders, local history, capacity estimates and opt-in predictive alerts |
| Profiles | Save/replace, load or delete up to 20 layouts; built-in Work/Gaming/Presentation starters |
| Updates | Check GitHub, download a verified ZIP and open the downloads folder |

**Tray:** use More → Hide to tray or minimize. Click the tray icon for CPU/RAM, provider capacity/status and the nearest known current reset; choose Show Prism to restore. Right-click offers Show, Refresh, Settings, History, Capacity & resets and Exit. Opening the view uses cached readings and never starts account collection. Closing the widget or pressing Esc exits Prism. Startup launches into the tray. Notifications depend on Windows settings and Prism remaining open.

**Low quota:** enabled at 20% by default; select a threshold from 5–50%. Each provider/window alerts once until it recovers above the threshold or starts a new reset cycle. Manual, stale, future or missing readings cannot trigger alerts. If no reset timestamp exists, recovery is needed to rearm that window.

**History:** More → Usage history shows 24 hours, 7 days or 30 days. Choose a quota window to see current-account samples, gaps, reset markers and an optional estimated trajectory. Fresh captures are compacted into five-minute buckets for up to 30 days. Old 1.8 history remains available as **Legacy aggregate**; it mixed accounts and windows, so it is never used for forecasts. New histories use installation-local opaque scopes, not account names. Token rotation may start a new series. Disable recording in Alerts; Clear history erases both formats. Data is collected only while Prism runs and is not backfilled.

**Capacity:** More → Capacity & resets orders provider-reported reset times and shows remaining allowance, state and limiting windows. Tooltips and details add Prism-calculated recent pace, sustainable pace and estimated capacity at reset when sufficient valid history exists. Percentages per hour mean **percentage points of allowance**, not tokens. Estimates require at least six distinct samples over 30 minutes, a verified account scope and an unbroken quota cycle. Corrections, gaps, stale readings and rolling replenishment suppress projections. Unknown resets stay unknown. The measured percentage remains provider-reported; estimates do not replace it.

Turn off **Show capacity estimates** to hide forecasting. **Predictive alerts are off by default**; enable them in Alerts for sustained estimates of exhaustion at least 15 minutes before reset. Two qualifying captures at least five minutes apart are required. All capacity/low/reset deliveries share a two-minute cooldown and persistent deduplication. Rapid-burn notifications are deferred until a dependable personal baseline can be established. See [forecast method and limitations](docs/FORECASTING.md).

**Layouts:** profiles include size, compact mode, pinning, opacity, metric visibility/order and clocks. They exclude sign-ins, alert settings and refresh intervals. Compact mode follows the complete chosen order; the full dashboard keeps CPU/RAM and disk/network paired and orders its AI cards. Hidden metrics still collect and can alert. Drag near a work-area edge to snap; saved monitor offsets restore with an on-screen fallback when a monitor disappears.

**Updates:** checking contacts the public GitHub release API only on request. Downloads remain in `data/downloads`, match the expected release filename/size and published SHA-256, and are never executed or extracted automatically. These checks detect corruption; they are not a publisher signature. Releases remain unsigned. Use the upgrade procedure above.

## Privacy and support

No Prism backend, analytics, crash uploads or remote control. Preferences/caches stay local. The browser extension keeps organization choices in browser storage but sends only quota values into Prism. Read [PRIVACY.md](PRIVACY.md).

Use **⋯ → Diagnostics** for a shareable dependency report. [Report bugs](https://github.com/kumarnabh/prism-widget/issues/new/choose) with version, Windows/display scale, reproduction and that report. Never upload `data/`, credentials, CLI transcripts or private screenshots. Report vulnerabilities privately via [SECURITY.md](SECURITY.md).

## Build and test

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), Python 3.11–3.14 x64, and Node.js 22+ for browser tests. In a source checkout:

```powershell
./Build.ps1
./.venv/Scripts/python.exe -m unittest discover -s tests -p 'test_*.py'
node --test tests/popup.test.cjs
./scripts/verify-native.ps1
./Prism.exe
```

Close Prism before rebuilding. `Build.ps1 -SkipDependencies` skips setup. Native checks use **offline account fixtures** by default, plus real system sensors. They cover controls, quota rules, reminders, clocks, 16 reference layouts, 480 forward/reverse resize checks, 160 language/clock/metric combinations, preference/history/profile round trips, alert recovery, independent scheduling, rejected update payloads/redirects/timeouts, and 4× export dimensions. `-LiveAccounts` deliberately reads your accounts and can put private quota into local screenshots. CI tests Python 3.11 and 3.14, WPF, browser behavior and Git history. Physical mixed-DPI transitions and every provider plan are not covered.

See [architecture](docs/ARCHITECTURE.md), [releasing](docs/RELEASING.md), [change log](CHANGELOG.md), and [review notes](AUDIT.md). Packages use an explicit file list instead of copying a personal installation.

## License

[MIT](LICENSE). Contributions welcome. Provider names belong to their owners. The generated icon prompt is in `assets/icon-prompt.txt`. Dependencies retain their own licenses; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
