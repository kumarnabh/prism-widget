# System capacity (2.0)

Prism reads Windows sensors locally. The dashboard never substitutes zero for an unsupported, invalid or stale sensor. Add metrics in Settings → Metrics or load the System capacity profile. The original eight-metric layout and existing visibility choices are preserved. You can hide every metric; Settings remains reachable through the toolbar and Ctrl+,.

## Sources and meanings

| Metric | Source | Meaning |
| --- | --- | --- |
| CPU | GetSystemTimes | Usage over the sampling interval; first sample or a resumed interval is unknown |
| RAM | GlobalMemoryStatusEx | Physical memory in use, and used/total capacity |
| GPU | DXGI inventory + Windows PDH GPU Engine counters | Sum processes within each engine, then choose the busiest engine; multi-GPU headline is the busiest adapter only when every adapter has a valid reading |
| Dedicated VRAM | DXGI dedicated capacity + PDH GPU Adapter Memory | Used/total/free bytes; headline selects the adapter with the largest dedicated capacity. Shared memory is never presented as dedicated VRAM. Ambiguous multi-node counters stay unknown |
| CPU frequency | CallNtPowerInformation | Windows-reported average current clock across logical processors; not an instantaneous turbo measurement |
| Battery | GetSystemPowerStatus | Charge, plugged-in/charging/discharging state and Windows-supplied runtime only while discharging. Desktops omit battery in details |
| Disk | DriveInfo + PDH PhysicalDisk | System-drive free capacity, all local fixed-volume capacities in details, and aggregate read/write bytes per second. No SMART/health claim |
| Processes | System.Diagnostics.Process, on request | Top five CPU/RAM consumers over a one-second interval; PID plus start time prevents attributing reused PIDs. No command lines, history, network or termination action |

CPU temperature/package power, GPU temperature/power/clocks, battery health/design/full capacity and per-process GPU usage are deferred. Dependable support across vendors would require additional sensor/driver qualification; Prism does not imply those values are available.

## Sampling and freshness

One background worker owns native queries; the UI reads immutable cached snapshots. CPU/RAM/network/system-drive sampling is about two seconds. GPU and disk counters sample about six seconds (the next two-second tick after five seconds). Battery/frequency sample about ten seconds, inventory and other fixed-volume capacities about thirty. There is no elevated sensor service or new dependency. Rate counters warm up with two captures. Missing counters retry on a one-minute budget. A long gap resets CPU/network/rate-counter baselines. Failed readings replace values with unavailable; values older than twenty seconds are stale (sixty seconds for the secondary disk-capacity list).

## Taskbar and visibility

**⋯ → Minimize to taskbar** keeps Prism running and retains a taskbar button. Hover it for cached metric bars; Peek also uses cached metrics. Tiny previews show a bounded subset plus an omitted count instead of squeezing all labels. Restore with the taskbar button or its Show Prism action. Settings → General controls whether ordinary minimization goes to the tray (the compatible default) or taskbar. **Taskbar ribbon** places the normal compact widget immediately above the selected monitor's taskbar/work-area edge. It does not modify Explorer, install a deskband, reserve screen space or embed bars underneath other app icons.

General → Taskbar bar metric chooses one usage/remaining-capacity bar underneath Prism's taskbar button (RAM by default, or None). Windows supports one such progress bar per button; the hover preview supplies multiple bars. A hidden metric, unsupported sensor or stale quota suppresses this bar. CPU/RAM/GPU/battery show usage/charge; providers show remaining allowance.

Metric visibility/order and profiles control the ribbon and preview. **Only active AI subscriptions** filters on valid exposed quota windows, including explicitly stale readings; it cannot verify payment or plan entitlement. Collection schedules remain independent of display filtering. Source connections remain available in Settings. With many selected metrics, the minimum height increases so text stays readable. The main widget never scrolls.

## Qualification

Local native validation exercised AMD integrated + NVIDIA discrete GPUs together, dedicated VRAM, charging battery, one fixed disk, and bounded DWM thumbnail/Peek calls for compact/ribbon sizes. Fixtures cover no GPU, missing/partial GPU readings, unknown battery, desktop, stale/future/invalid metrics, PID reuse and multiple disk presentation. Actual Intel hardware, a battery discharging runtime, physical multi-monitor/DPI moves, unplug/replug and system suspend/resume still require broader device testing. Windows may suppress previews or notifications according to its settings.

Primary references: [GPU engine semantics](https://devblogs.microsoft.com/directx/gpus-in-the-task-manager/), [DXGI capacity](https://learn.microsoft.com/windows/win32/api/dxgi/ns-dxgi-dxgi_adapter_desc), [PDH formatted counters](https://learn.microsoft.com/windows/win32/api/pdh/nf-pdh-pdhgetformattedcounterarrayw), [CPU frequency semantics](https://learn.microsoft.com/windows/win32/power/processor-power-information-str), [battery status](https://learn.microsoft.com/windows/win32/api/winbase/ns-winbase-system_power_status), [DWM thumbnail](https://learn.microsoft.com/windows/win32/api/dwmapi/nf-dwmapi-dwmseticonicthumbnail).
