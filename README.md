# Prism

<img src="assets/prism-icon.png" width="112" alt="Prism glass icon">

**Your computer and AI capacity, at a glance.** A free, open-source Windows desktop widget with luminous glass, live system metrics, automatic subscription quota readings, world clocks and quiet reset reminders.

[Download](https://github.com/kumarnabh/prism-widget/releases/latest) · [Troubleshooting](TROUBLESHOOTING.md) · [Privacy](PRIVACY.md) · [Contribute](CONTRIBUTING.md)

![Checks](https://github.com/kumarnabh/prism-widget/actions/workflows/ci.yml/badge.svg)
![License: MIT](https://img.shields.io/badge/license-MIT-88dfd0)
![Platform: Windows x64](https://img.shields.io/badge/platform-Windows%20x64-a9d2ff)

## Features

- CPU/RAM usage, free system-drive space, download/upload rate and short history charts.
- Automatic Codex, Cursor Individual, OpenCode Go and Claude subscription quota adapters.
- Resizable dashboard, compact grid and slim ribbon. Labels disappear before metrics; the widget never scrolls.
- Two optional global timezone clocks with daylight saving, date rollover and UTC-offset tooltips. Defaults: US Eastern and US Pacific.
- Quiet reset reminders when **more than 10% remains**, with configurable lead time and persistent deduplication.
- Pinning, remembered position, size presets, keyboard controls and selected-app RAM trimming.
- Per-monitor DPI support, vector gauges/charts, and 4× exports with outlined text.
- Copyable dependency diagnostics without account details, credentials or machine paths.

Prism is an independent community project, not affiliated with OpenAI, Cursor, Anthropic or OpenCode. Percentages represent reported subscription allowance, **not exact spendable token counts**. Provider interface changes may require adapter updates. The interface is currently English; timezone selection is global.

## Install

1. Use **Windows 10/11 x64** and install [64-bit Python 3.11–3.14](https://www.python.org/downloads/windows/), including its launcher. Windows 11 is the locally tested platform.
2. Download `Prism-<version>-win-x64.zip` and `SHA256SUMS.txt` from [Releases](https://github.com/kumarnabh/prism-widget/releases/latest). Compare the ZIP hash using `Get-FileHash .\Prism-<version>-win-x64.zip -Algorithm SHA256`.
3. Extract into a writable folder, such as Documents. Keep the files together. Do not run inside the ZIP or install in Program Files.
4. Double-click **Setup.cmd**. It creates a local `.venv`, installs pinned terminal dependencies and launches Prism. Internet is needed for this first dependency installation.
5. Open **⋯ → Connections** for your accounts. System readings work independently.

The release includes .NET; no SDK or administrator access is needed. Executables are currently **unsigned**. Windows may show an unknown-publisher warning; verify the source and checksum before deciding whether to run it. Prism creates no startup task or automatic updater.

**Upgrade:** close Prism, extract a new release into a separate folder, and run Setup. Privately copy the old `data` folder before launching if you want to retain settings/caches. Never share it. Reconnect optional browser/status-line integration after changing the installation path. Keep the old installation until the upgrade works.

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

System readings update every two seconds. Provider collection runs each minute; adapters may reuse five-minute caches or back off after errors. Network is aggregate Ethernet/Wi-Fi throughput and may include virtual adapters. Charts retain up to 72 seconds. Disk bars show **free** space.

Drag the title to move and the lower-right grip to resize. The top toolbar's vertical-arrow icon fits height to content; double-clicking the grip does the same. The icon is available in full and compact layouts. The **⋯** menu remains accessible at every size. Short layouts combine metrics into two, four or eight columns. Hover for labels and details.

Free RAM, Refresh and Settings share one action row. Below 430 pixels wide or 760 pixels tall, labels collapse into icons with tooltips and accessible names. In compact mode, these actions share the header with Fit height and More. Use More or Ctrl+M to restore details; More or Esc closes Prism. Charts appear only when the entire grid fits without shrinking the dashboard.

| Shortcut | Action |
| --- | --- |
| F5 | Refresh |
| Ctrl+P | Pin / unpin |
| Ctrl+M | Compact / expanded |
| Ctrl+, | Connections |
| Esc | Close |

**Free RAM** asks Windows to trim eligible working-set pages from selected apps. It does not close apps, delete files or clear conversations. Memory may return immediately and apps may briefly slow down; this is not a memory-leak repair.

## Clocks and reminders

**⋯ → World clocks** enables two clocks with custom labels. US Eastern/Pacific follow daylight saving, rather than fixed EST/PST year-round. Hover for dates and UTC offsets. Compact clocks reuse the header without extra rows.

**⋯ → Reset reminders** offers 5/15/30/60/120 minutes, enable/disable and a preview. Default: enabled, 30 minutes. A quiet 12-second popup requires a current reading no older than ten minutes, a known future reset and **strictly more than 10% left**. Each provider/window/reset alerts once, even across restarts. Prism must be running and visible. No sound, model calls or automatic spending. Claude CLI currently has no parsed reset timestamp; browser/feed readings with timestamps can qualify.

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

Close Prism before rebuilding. `Build.ps1 -SkipDependencies` skips setup. Native checks use **offline account fixtures** by default, plus real system sensors. They cover controls, quota rules, reminders, clocks, 16 reference layouts, 480 forward/reverse resize checks and 4× export dimensions. `-LiveAccounts` deliberately reads your accounts and can put private quota into local screenshots. CI tests Python 3.11 and 3.14, WPF, browser behavior and Git history. Physical mixed-DPI transitions and every provider plan are not covered.

See [architecture](docs/ARCHITECTURE.md), [releasing](docs/RELEASING.md), [change log](CHANGELOG.md), and [review notes](AUDIT.md). Packages use an explicit file list instead of copying a personal installation.

## License

[MIT](LICENSE). Contributions welcome. Provider names belong to their owners. The generated icon prompt is in `assets/icon-prompt.txt`. Dependencies retain their own licenses; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
