# Privacy

Prism runs locally. There is no Prism server, analytics, crash upload or automatic update check.

| Component | Reads | Destination |
| --- | --- | --- |
| System metrics | Windows CPU, memory, drive and adapter counters | Widget memory; local self-tests include sensor values |
| Codex | Current CLI quota; historical quota events as stale fallback | Provider through its own CLI; normalized readings in memory |
| Cursor / Go | Existing local sign-in after opt-in | Credential to its own fixed HTTPS usage endpoint; quota cache and credential fingerprint locally |
| Claude CLI | Authentication status and built-in usage panel | Claude manages its connection; normalized quota/account fingerprint cached locally |
| Optional browser bridge | Claude organization list and quota | Organization choice in extension storage; only quota values to local native host |
| Optional status-line feed | Recognized numeric quota fields | Local quota cache; unknown fields/conversation contents discarded |
| Diagnostics | Runtime versions, architecture, OS family, dependency/CLI availability | Screen; clipboard only when you choose Copy |

`data/` contains consent, caches, fingerprints, clocks, reminder history, window preferences and a dedicated Claude CLI workspace. Treat it as private even though Prism does not deliberately persist raw credentials. Claude may create files according to its own behavior. Self-tests create local screenshots and sensor reports; live-account tests may include quota in screenshots. Prism error logs contain exception class names rather than raw responses.

Prism submits no model prompts. Provider apps/CLIs retain responsibility for their own authentication/network policies. Cursor and Go reject redirects; the browser bridge is restricted to claude.ai. Setup contacts the configured Python package index; source builds contact NuGet.

## Control and removal

Disable Cursor/Go reuse in Connections to stop those adapters. Revoke sign-ins through the original provider app. Close Prism to stop its collection. Remove the optional extension to stop browser polling.

The browser installer registers `com.prism.widget` under these **current-user** keys:

```text
HKCU\Software\Google\Chrome\NativeMessagingHosts\com.prism.widget
HKCU\Software\Microsoft\Edge\NativeMessagingHosts\com.prism.widget
```

To uninstall the bridge, remove its extension and these two Prism-specific keys after checking they point to your installation's `native-host.json`. Do not remove other native hosts. Remove any Prism status-line command from Claude settings or restore your prior command. Removing the extracted installation, including `.venv` and `data`, erases Prism's local state. Prism installs no system service or startup task.

## Sharing

Diagnostics is designed to be shareable; review screenshots yourself. Never upload credential files, databases, environment files, browser cookies, CLI transcripts, `native-host.json` or `data/`. The manifest RSA key is public identity material. See SECURITY.md for private reporting.
