# Architecture

`App.cs` owns WPF, Windows sensors, adaptive layout and dialogs. System polling runs every two seconds. Provider collection uses a hidden Python subprocess each minute, with a 45-second deadline and cancellation on close. Single-instance identity is per installation.

`RuntimeSupport.cs` prefers `.venv/Scripts/python.exe`, with PATH fallback for older setups. Setup creates that isolated environment with pinned requirements. `doctor.py` checks dependencies without reading accounts or credentials.

`providers.py` runs adapters concurrently and normalizes source, status, capture time and quota windows. `quota_cache.py` validates caches; `QuotaSnapshot.cs` independently validates the UI boundary. Missing/malformed/expired values cannot become full balances. The limiting valid window determines displayed capacity.

`auto_sources.py` handles opt-in Cursor/Go access, fixed endpoints, account-scoped caches and backoff. `claude_cli.py` reads the built-in usage panel with hooks/MCP disabled. The optional browser bridge sends sanitized fields through `native_host.py`; the status-line feed accepts only known numeric quota. No model prompts are submitted.

`WorldClocks.cs` uses Windows timezone conversion. `ResetReminders.cs` checks >10%, freshness and known reset boundaries, with persistent bounded deduplication. WPF shows a short popup. Settings may scroll; the dashboard does not.

Glass, meters and charts use WPF vectors; `VectorSnapshot` outlines glyphs for HD export. A shared action-row component switches between labels and icons. Compact charts require a fresh measurement of the complete grid before becoming visible. Offline checks exercise eight sizes with clocks on/off, 480 forward/reverse breakpoint transitions, disconnected account fixtures and real system sensors.

Git ignores runtime state. A staged checker rejects private paths/patterns, and packaging copies only explicitly named files into a fresh self-contained publish directory. See privacy/release docs before adding adapters or changing packaging.
