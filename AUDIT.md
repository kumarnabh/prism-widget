# Pre-publication review

Reviewed on 2026-10-02 for the 1.3 gauges, presets and quota-validation update.

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

## Local validation

- 42 mocked unit/regression tests passed.
- Documented .NET 8 Release build completed with zero warnings or errors.
- Native checks passed for sensors, pinning, compact mode and eight layout sizes, including the 1200 × 120 strip. The verification script confirmed fresh results and a successful process exit, quota edge cases, and all three layout presets. A duplicate-launch smoke test confirmed only one widget instance remains.
- High-resolution header artwork and the multi-resolution application icon compiled and rendered. Ring gauges, rounded meters, reset countdowns, source states, and compact/expanded layouts were visually inspected.
- Staged-file publication checks found no private/runtime files, personal paths or credential patterns.
- Gitleaks 8.30.1 found no secrets after identifying the extension's public RSA key. Its exception requires the exact public value AND exact manifest path. RSA SubjectPublicKeyInfo validation confirmed the key is public. The generic-api-key rule still detected a different synthetic key placed at that same path.

Runtime account caches, preferences, logs, native-host registration, screenshots, dependencies and binaries are ignored and not committed. Live authentication and service availability depend on the account. Mocked tests and static review do not establish live provider acceptance, and no scanner guarantees detection of every possible bug or secret.
