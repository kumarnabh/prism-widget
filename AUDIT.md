# Pre-publication review

## 1.8.0 desktop features — 2026-10-02

Implemented tray/startup, independent schedules, low-quota alerts, local history, metric customization, profiles, verified update downloads, monitor placement/snapping and four-language core UI with clock formats. Bug review found and resolved persisted alert recovery and malformed profile clock labels. Security review found no credential/privacy issue, but identified a body-transfer timeout gap; an operation deadline and stalled-stream regression now cover it. Failed tray delivery restores pending alert state.

Local Release build: zero warnings/errors. All 49 mocked Python tests and three browser tests passed in the isolated dependency environment. The global Python interpreter lacked the required terminal dependency; it was not used as a passing validation environment. Native checks cover 16 reference layouts, 480 resize transitions, 160 translated/12-hour/custom-metric variants, four rendered settings panels, and a 1768 × 3432 export. Feature checks exercise bounded preferences, history retention/deduplication, alert recovery, profile sanitization, schedules, synthetic negative/disconnected monitor geometry and mocked update integrity/redirect/body-timeout failures. Hindi widget and French settings renders were inspected.

A live desktop smoke check found that hiding during WPF Loaded was overridden by its initial Show. Tray creation/hiding is now queued after that transition. The follow-up check confirmed a hidden running process, duplicate-launch restoration and no duplicate instance. Startup registration was not enabled on the owner account during tests. Physical mixed-DPI transitions and delivery through Windows notification settings are not established by offline tests. Hosted CI and final source/package scans are recorded at publication.

## 1.7.2 adaptive controls — 2026-10-02

Free RAM, Refresh and Settings now share one responsive action row. Compact layouts use accessible vector icons in the header. A breakpoint check exposed chart activation shrinking narrow grids: WPF deferred a parent measurement after visibility changed. Chart visibility now invalidates its container immediately, and charts are suppressed when the complete grid exceeds available height.

Local Release build: zero warnings/errors. Native verification passed all 16 reference layouts plus 480 forward/reverse resize cases around width/height breakpoints, with clocks off/on and long labels. Checks cover visible actions, sibling overlap, label-mode restoration and full content scale. Full/compact renders were inspected. Hosted validation is recorded separately in GitHub Actions.

## 1.7 public-release review — 2026-10-02

Resolved active-account Codex telemetry precedence, first/removed Claude browser account selection, clipped reminder settings on short displays, and display-size assumptions in native checks. Added isolated dependency setup, privacy-safe diagnostics, offline native testing, public documentation, MIT licensing, CI and explicit-list self-contained packaging. Debug symbols are excluded from the public archive.

Local evidence: 47 mocked Python tests and three browser selector regressions passed; .NET Release build had zero warnings/errors. Native checks passed sensors, controls, clocks, reminder boundaries/deduplication, all 16 layouts at full content scale, and a 1768 × 3432 4x export. A freshly extracted self-contained package passed Setup under Windows PowerShell 5.1 and offline native checks. The packaged .NET runtime is 8.0.28 with matching upstream notices.

Gitleaks 8.30.1 found no leaks in the full seven-commit history before this release, the staged public source snapshot, or the first package's content. The publication boundary check found no prohibited files/patterns. pip-audit 2.10.1 reported no known vulnerabilities for the three pinned Python requirements. Final release scans are run again at publication; GitHub Actions records hosted checks separately.

Code/security review and offline tests do not establish live account acceptance, entitlement, every Windows configuration or physical mixed-DPI behavior. These scans cannot guarantee detection of every possible secret. No actual upcoming quota reset was used to test notification timing.

## Prior 1.6 review

Reviewed on 2026-10-02 for the reset-reminder update.

## Findings resolved

| Severity | Location | Finding and resolution |
| --- | --- | --- |
| P2 | auto_sources.py, cached account reads | A recent cache could retain an expired quota window or hide a failed refresh. Filter expired windows before every cached return and preserve stale/error status during backoff. Regression tests added. |
| P2 | claude_cli.py, login | API-key authentication could skip the subscription login required by the reader. Reuse the subscription-auth predicate in setup and collection; verify after login. Regression tests added. |
| Hardening | claude_feed.py | Rebuild only known numeric quota fields instead of persisting arbitrary nested input. Regression tests added. |
| P2 | quota_cache.py, account and CLI caches | Malformed cache envelopes could prevent refresh indefinitely. Validate timestamps, windows and numeric fields before reuse; corrupt caches become a cache miss. |
| P2 | providers.py, fallback selection | Failed Codex telemetry and Claude CLI reads suppressed healthy alternative sources. Isolate each reader and prefer a current source over stale results. |
| Hardening | source/App.cs | Cancel collection on close and tolerate collector exit races. Repeated launches activate the existing installation window. |
| Build | Build.ps1 | Force resource rebuilding so updated embedded branding cannot be omitted by stale intermediate output. Native verification rejects stale results and nonzero exits. |
| P2 | source/App.cs, system readings | The free-disk label was paired with a used-space bar. Both compact and detailed bars now represent free space. Initial unknown sensors have neutral status. |
| P2 | source/QuotaSnapshot.cs | Validate UI quota inputs independently: keep known zero, reject invalid percentages and timestamps, filter expired windows, and fail closed for malformed mixed windows. The footer counts current sources only. |
| P2 | source/App.cs, window state | Remember the expanded height across compact launches and restore that height instead of a fixed minimum of 700 pixels. |

| Rendering | source/App.cs, app.manifest | Isolate the shadow behind content, declare PerMonitorV2 awareness, keep text opaque, and tighten compact spacing to avoid fractional downscaling. Export a 4x preview with font outlines; screen-DPI glyph caches had remained visibly pixelated in earlier exports. |

| Reminders | source/ResetReminders.cs | Require remaining capacity strictly above 10%, a future reset inside the chosen interval, and current data no older than ten minutes. Deduplicate by provider/window/reset and persist locally. |

## Local validation

- 42 mocked unit/regression tests passed.
- Documented .NET 8 Release build completed with zero warnings or errors.
- Reset-reminder checks passed for threshold/time boundaries, stale/future/unknown readings, invalid percentages, disabled mode, restart deduplication, a new reset cycle, corrupted state, and popup opening/dismissal. The labeled sample popup was visually inspected. A real upcoming account reset was not used for validation.
- Clock checks passed for Eastern/Pacific winter and summer offsets, the spring DST transition, half-hour offsets, date rollover, invalid zones, preference round-trip and malformed preferences.
- Native checks passed for sensors, pinning, compact mode and eight layout sizes, including the 1200 × 120 strip. The verification script confirmed fresh results and a successful process exit, quota edge cases, and all three layout presets. A duplicate-launch smoke test confirmed only one widget instance remains.
- High-resolution header artwork and the multi-resolution application icon compiled and rendered. Ring gauges, rounded meters, reset countdowns, source states, and compact/expanded layouts were visually inspected.
- All eight reference layouts render at content scale 1 with clocks both enabled and disabled (16 cases). Native checks ran on the current 125% desktop. A 1770 x 3434 vector-text preview was generated and visually inspected; physical movement between monitors with different scaling was not tested.
- Staged-file publication checks found no private/runtime files, personal paths or credential patterns.
- Gitleaks 8.30.1 found no secrets after identifying the extension's public RSA key. Its exception requires the exact public value AND exact manifest path. RSA SubjectPublicKeyInfo validation confirmed the key is public. The generic-api-key rule still detected a different synthetic key placed at that same path.

Runtime account caches, preferences, logs, native-host registration, screenshots, dependencies and binaries are ignored and not committed. Live authentication and service availability depend on the account. Mocked tests and static review do not establish live provider acceptance, and no scanner guarantees detection of every possible bug or secret.
