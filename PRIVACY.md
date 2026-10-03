# Privacy

Prism runs locally. There is no Prism server, analytics, crash upload or automatic update check.

| Component | Reads | Destination |
| --- | --- | --- |
| System metrics | Windows CPU, memory, drive and adapter counters | Widget memory; local self-tests include sensor values |
| Codex | Current CLI quota; local credential-file bytes only for an opaque history scope; historical quota events as stale fallback | Provider through its own CLI; no credential bytes persisted or sent by history code |
| Cursor / Go | Existing local sign-in after opt-in | Credential to its own fixed HTTPS usage endpoint; quota cache and credential fingerprint locally |
| Claude CLI | Authentication status and built-in usage panel | Claude manages its connection; normalized quota/account fingerprint cached locally |
| Optional browser bridge | Claude organization list and quota | Organization choice in extension storage; only quota values to local native host |
| Optional status-line feed | Recognized numeric quota fields | Local quota cache; unknown fields/conversation contents discarded |
| History / estimates | Provider, opaque scope, window ID, capture/reset timestamps and remaining percentage | Local `data/usage-history-v2.json`, at most 30 days / 40,000 window samples; forecasts stay in memory |
| Update checker | Public GitHub release metadata and ZIP/checksum, on request | GitHub receives ordinary network metadata; verified downloads stay in `data/downloads` |
| Startup | Explicit opt-in per Windows user | Quoted local executable path under HKCU Run; no service or scheduled task |
| Diagnostics | Runtime versions, architecture, OS family, dependency/CLI availability | Screen; clipboard only when you choose Copy |

`data/` contains consent, caches, fingerprints, clocks, reminder history, window/metric/language preferences, layout profiles, numeric usage history, downloaded updates and a dedicated Claude CLI workspace. Treat it as private even though Prism does not deliberately persist raw credentials. Claude may create files according to its own behavior. Self-tests create local screenshots and sensor reports; live-account tests may include quota in screenshots. Prism error logs contain exception class names rather than raw responses.

Prism submits no model prompts. Provider apps/CLIs retain responsibility for their own authentication/network policies. Cursor and Go reject redirects; the browser bridge is restricted to claude.ai. Setup contacts the configured Python package index; source builds contact NuGet.

## Control and removal

Disable Cursor/Go reuse in Connections to stop those adapters. Revoke sign-ins through the original provider app. Close/Exit Prism to stop its collection; hiding to the tray keeps it running. History recording can be disabled in Settings → Alerts; clear prior records in Usage history. History is pruned on load and while recording, so a closed or disabled installation can retain its existing file until reopened or cleared. Remove the optional extension to stop browser polling.

The browser installer registers `com.prism.widget` under these **current-user** keys:

```text
HKCU\Software\Google\Chrome\NativeMessagingHosts\com.prism.widget
HKCU\Software\Microsoft\Edge\NativeMessagingHosts\com.prism.widget
```

To uninstall the bridge, remove its extension and these two Prism-specific keys after checking they point to your installation's `native-host.json`. Do not remove other native hosts. Remove any Prism status-line command from Claude settings or restore your prior command. Removing the extracted installation, including `.venv` and `data`, erases Prism's local state. Prism installs no system service or scheduled task. Optional startup writes only the `PrismWidget` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. Disable Start with Windows before removing/moving the installation. Prism will not overwrite another installation's startup value.

## Sharing

Diagnostics is designed to be shareable; review screenshots yourself. Never upload credential files, databases, environment files, browser cookies, CLI transcripts, `native-host.json` or `data/`. The manifest RSA key is public identity material. See SECURITY.md for private reporting.

## Capacity history (1.9)

`history-scope.keydata` is a random installation-local HMAC salt, not a provider credential. History stores only its derived opaque scopes. Codex credential-file changes and Cursor/Go credential fingerprint changes split histories; Claude uses its existing authentication-status fingerprint and verifies it again after collection. No account emails or organization IDs are added to history. Sources without a verified scope (including browser/status-line quota feeds) cannot produce burn forecasts. Credential renewal may conservatively split one account into multiple series. These hashes are pseudonymous, not anonymous; keep all `data/` private.

The original `usage-history.json` is retained during migration for rollback and legacy viewing; it may contain mixed-account service-level percentages. Clear history clears both files. Files with a newer history schema are left untouched. Forecasts and notification delivery state remain local; no prompts, identities or history are uploaded. The new notification-state file stores timestamps and opaque provider/window/reset keys. Normal per-user folder permissions apply; Prism does not promise protection against other software running as your Windows account.
