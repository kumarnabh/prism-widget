# Prism

<img src="assets/prism-icon.png" width="128" alt="Prism glass icon">

A glass-style Windows desktop widget for system metrics and AI subscription availability.

- Live CPU, memory, free disk and network throughput.
- Automatic Codex, Cursor Individual, OpenCode Go and Claude quota adapters.
- Resizable layout: short windows show icons and metrics in 2, 4 or 8 columns, without scrolling. Charts appear when space permits; hover for details.
- Optional pinning and working-set trimming for selected apps.
- Smooth CPU and RAM history, crisp glass branding, and freshness indicators for every AI source.
- Single-instance startup and automatic recovery from malformed caches or unavailable quota sources.

## Build and run

Windows 10/11, .NET 8 SDK, and 64-bit Python 3.14 are required for this build. The bundled terminal dependency targets Python 3.14.

```powershell
./Build.ps1
./Prism.exe
```

Close a running Prism before rebuilding. Build outputs and installed Python dependencies are local and ignored by Git. `Build.ps1 -SkipDependencies` rebuilds without installing dependencies.

## Connections

Codex uses recent local quota telemetry with CLI fallback. Cursor and OpenCode Go require explicit opt-in in Settings to reuse their existing app sign-ins. Credentials go only to their own fixed HTTPS usage endpoints; redirects are refused.

For Claude, use **Settings → Sign in to Claude**. Finish Claude Code's first-run setup. Prism reads the interactive built-in `/usage` panel every five minutes without submitting a model prompt. The CLI manages authentication. CLI layout changes can require an adapter update. Browser sync and a status-line feed are optional alternatives; see [the connection guide](README.html).

Unknown quota displays a dash. Availability percentages are subscription allowance, not guaranteed token counts. Missing or expired windows are never treated as a full balance. Error backoff preserves stale timestamps.

## Controls

Drag the title to move and the lower-right grip to resize. Double-click the grip to fit height. The compact layout's □ control restores the detailed view. Hover over icons to identify metrics and inspect full readings. Free RAM asks Windows to trim eligible working-set pages from selected apps; it does not close apps, delete files or clear conversations. Memory may return immediately.

The **⋯** menu keeps pinning, fit height, Free RAM and Connections accessible at every size. Keyboard shortcuts: **F5** refresh, **Ctrl+P** pin, **Ctrl+M** toggle density, **Ctrl+,** connections, and **Esc** close. Green, amber, pink and gray dots indicate current, stale, error and unavailable readings; tooltips explain the status. Charts retain up to 72 seconds of actual samples.

## Privacy and publishing

`data/`, credentials, local connection preferences, quota caches, window state, logs, screenshots, dependencies, build output and native-host registration are excluded. Never force-add these paths. No account data is required to build or test.

The browser manifest's `key` is a public RSA key used solely for a stable extension ID; it is not a credential or private key.

Before committing:

```powershell
python -m unittest discover -s tests -p "test_*.py"
./scripts/verify-native.ps1
git add <intended-source-files>
python scripts/check_publish.py
```

The publication check examines the exact staged bytes and reports only paths/rule names. Use a dedicated scanner such as Gitleaks as a second check. Scanning reduces risk; it cannot guarantee the absence of every possible secret or bug.

## Implementation

- `source/`: .NET 8 WPF UI, sensors, adaptive layout and RAM controls.
- Python collectors: read-only provider usage adapters and local caches.
- `browser-extension/`: optional Claude quota-only bridge.
- `tests/`: mocked quota, authentication, privacy and regression tests.
- `assets/`: generated icon artwork and a multi-resolution Windows icon.

The icon was created with the built-in image-generation tool; its prompt is recorded in `assets/icon-prompt.txt`. No license grant is implied for the application source.
