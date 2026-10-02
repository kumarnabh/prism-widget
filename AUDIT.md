# Pre-publication review

Reviewed on 2026-10-02 for the 1.6 reset-reminder update.

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
