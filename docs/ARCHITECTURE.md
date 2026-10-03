# Architecture

`App.cs` owns WPF, adaptive layout and dialogs. `SystemCapacity.cs` owns an independent background sensor worker and immutable normalized snapshots; the UI reads cached results every two seconds. Provider collection uses a hidden Python subprocess for independently due providers on a five-second scheduler, with a 45-second deadline and cancellation on close. Single-instance identity is per installation.

`RuntimeSupport.cs` prefers `.venv/Scripts/python.exe`, with PATH fallback for older setups. Setup creates that isolated environment with pinned requirements. `doctor.py` checks dependencies without reading accounts or credentials.

`providers.py` runs adapters concurrently and normalizes source, status, capture time and quota windows. `quota_cache.py` validates caches; `QuotaSnapshot.cs` independently validates the UI boundary. Missing/malformed/expired values cannot become full balances. The limiting valid window determines displayed capacity.

`auto_sources.py` handles opt-in Cursor/Go access, fixed endpoints, account-scoped caches and backoff. `claude_cli.py` reads the built-in usage panel with hooks/MCP disabled. The optional browser bridge sends sanitized fields through `native_host.py`; the status-line feed accepts only known numeric quota. No model prompts are submitted.

`WorldClocks.cs` uses Windows timezone conversion. `ResetReminders.cs` checks >10%, freshness and known reset boundaries, with persistent bounded deduplication. WPF shows a short popup. Settings may scroll; the dashboard does not.

Glass, meters and charts use WPF vectors; `VectorSnapshot` outlines glyphs for HD export. A shared action-row component switches between labels and icons. Compact charts require a fresh measurement of the complete grid before becoming visible. Offline checks exercise eight sizes with clocks on/off, 480 forward/reverse breakpoint transitions, disconnected account fixtures and real system sensors.

Git ignores runtime state. A staged checker rejects private paths/patterns, and packaging copies only explicitly named files into a fresh self-contained publish directory. See privacy/release docs before adding adapters or changing packaging.


## Desktop customization

`SystemViews.cs` composes advanced hardware/process details, active-provider display filtering, taskbar/ribbon actions and synthetic translated visual cases. `LiveTaskbarPreview.cs` renders cached values on native DWM callbacks with physical-client/requested-size bounds and deterministic GDI cleanup. Preview callbacks never collect sensors/providers. Sensor queries remain worker-owned through shutdown; cancellation never waits for an expensive native call on the UI thread. Repeated layout adaptation runs only on size/visibility changes, while cached values continue updating when hidden. `SystemCapacityChecks` covers freshness, missing hardware, engine aggregation, battery and process identity semantics. See SYSTEM-METRICS.md for source meanings, cadence and device qualification.

`WidgetFeatures.cs` supplies settings/history windows and composes the feature services. `Preferences.cs` validates configuration, holds schedules and bounded profile storage, and writes JSON by same-directory atomic replacement. `UsageHistory.cs` filters fresh samples, compacts five-minute buckets, keeps 30 days and persists alert deduplication. `HistoryChart.cs` renders time-based allowance lines with explicit gaps.

`DesktopIntegration.cs` wraps Shell_NotifyIcon, opt-in current-user startup and work-area geometry. A registered per-installation message restores an existing hidden window on repeated launch. Window state stores display identity plus work-area offsets, falling back to an available display. Physical mixed-DPI monitor transitions still require manual validation.

`Localization.cs` provides core resources for English/Hindi/Spanish/French and clock formats. Advanced provider messages remain their source language. `ReleaseUpdates.cs` accepts only the fixed repository's release metadata and bounded HTTPS downloads through allowlisted GitHub hosts, verifies exact size and SHA-256, and renames only validated temporary files. Body transfers have an operation deadline, not just a response-header timeout. No credentials, archive extraction or execution are involved.

`FeatureChecks.cs` uses mock HTTP and temporary files; no real account, startup writes, network or notification service is required. Native layout tests add 160 language/clock-format/metric selections to the existing 16 references and 480 breakpoint transitions.

## Capacity intelligence (1.9)

`history_scope.py` derives installation-local HMAC scopes at the adapter boundary. `UsageHistory` v2 adds validated window samples alongside legacy aggregates in a separate file, with finite percentage checks, 30-day / 40,000-sample bounds and five-minute compaction. Newer schemas are read-only.

`CapacityForecast.cs` is a pure local algorithm; see FORECASTING.md. The dashboard caches calculations for 30 seconds, refreshing on new readings. `CapacityViews.cs` composes secondary reset/tray views from normalized cached snapshots; opening them does not launch providers. Open views reevaluate freshness every 30 seconds. History charts break at gaps and replenishments and label projected lines as estimates.

Existing reset/low alerts and opt-in predicted exhaustion share `NotificationState` delivery cooldown, with bounded persisted deduplication. Account-scoped keys use stable window IDs; legacy notification keys are conservatively honored during migration. Failed tray delivery is not marked delivered. Rapid-burn alerts are not implemented in 1.9. ForecastChecks covers deterministic time/correction/account/reset scenarios; native checks render all new views in four languages using synthetic account data.
