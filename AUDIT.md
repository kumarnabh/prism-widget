# Pre-publication review

Reviewed on 2026-10-02 before the initial GitHub push.

## Findings resolved

| Severity | Location | Finding and resolution |
| --- | --- | --- |
| P2 | auto_sources.py, cached account reads | A recent cache could retain an expired quota window or hide a failed refresh. Filter expired windows before every cached return and preserve stale/error status during backoff. Regression tests added. |
| P2 | claude_cli.py, login | API-key authentication could skip the subscription login required by the reader. Reuse the subscription-auth predicate in setup and collection; verify after login. Regression tests added. |
| Hardening | claude_feed.py | Rebuild only known numeric quota fields instead of persisting arbitrary nested input. Regression tests added. |

## Local validation

- 35 mocked unit/regression tests passed.
- Documented .NET 8 Release build completed with zero warnings or errors.
- Native checks passed for sensors, pinning, compact mode and eight layout sizes, including the 1200 × 120 strip.
- New multi-resolution application icon compiled and rendered.
- Staged-file publication checks found no private/runtime files, personal paths or credential patterns.
- Gitleaks 8.30.1 found no secrets after identifying the extension's public RSA key. Its exception requires the exact public value AND exact manifest path. RSA SubjectPublicKeyInfo validation confirmed the key is public. The generic-api-key rule still detected a different synthetic key placed at that same path.

Runtime account caches, preferences, logs, native-host registration, screenshots, dependencies and binaries are ignored and not committed. Live authentication and service availability depend on the account. Mocked tests and static review do not establish live provider acceptance, and no scanner guarantees detection of every possible bug or secret.
