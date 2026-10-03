# Local capacity estimates

Prism distinguishes provider-reported percentages and reset times from calculated estimates. A percentage is allowance, not a token count or billing balance. Rates below are **percentage points per hour**. No model is called and no history leaves the device.

## Inputs and segmentation

Only current source states with captures no more than ten minutes old and no future timestamps qualify. Each series is isolated by provider, installation-local opaque account scope, stable window ID and exact reset timestamp. Legacy aggregates never qualify. Unknown account scopes and known rolling/replenishing allowances have no burn forecast.

Samples must have finite percentages in [0,100], valid capture/reset times, and remain within the 30-day retention bound. Forecasts use at most six hours of history, with the latest sample matching the current reading's capture and percentage. The latest uninterrupted segment begins after any increase above 0.2 percentage points, drop above 25 points, or gap above three times median collection cadence (clamped to 15–60 minutes). A new reset timestamp starts a new series. These conservative rules can withhold useful estimates; they do not guess whether a correction was real spending.

At least six distinct samples spanning 30 minutes are required. Recent pace uses up to 90 minutes, longer baseline up to six hours. Both divide net depletion by actual elapsed time, including quiet intervals. No-consumption intervals produce zero pace, not an exhaustion date. Uneven collection cadence is time-weighted.

## Qualification and projection

Interval-rate median absolute deviation above max(2, 65% of recent pace), disagreement between half-window rates above max(3, 75% of pace), or a peak interval above max(5, four times pace) produces **Highly variable usage**. Rates may remain informative but no projected trajectory/exhaustion is shown. This is a heuristic state, not a statistical confidence interval.

For stable usage with a known future reset:

- Projected remaining = captured remaining − recent pace × hours from **capture** to reset, clamped to 0–100.
- Estimated exhaustion = capture + captured remaining / pace, only when pace exceeds 0.05 and exhaustion is within the reset horizon (bounded to 30 days).
- Safe pace = last reported remaining / time from now to reset. It is a budget based on the last report, not an estimate of unreported consumption. Capture time remains available in provider details.

The measured card value is never replaced by a forecast. Within five minutes of reset, safe pace and projections are suppressed to avoid noisy values. Unknown resets have no safe pace or exhaustion date. Account renewal, plan changes, coarse rounded percentages and missing history can all reduce availability. A stable past pace does not guarantee future behavior.

## Notifications and controls

Predictive exhaustion alerts are disabled by default. When enabled, a stable estimate must place exhaustion at least 15 minutes before reset on two separate captures at least five minutes apart. Delivery is deduplicated for the provider/account/window/reset and shares a two-minute cooldown with existing low-quota and unused-reset reminders. Deduplication and cooldown survive restarts; stale/invalid readings do not qualify. Windows may suppress tray notifications. Rapid-burn alerts are deferred because a six-hour baseline is not yet a dependable personal norm.

Settings → Alerts can disable history, estimates or predictive notifications independently. More → Capacity & resets and Usage history contain details; the primary dashboard remains compact. Clearing history requires confirmation and clears both old and new files. Disabling history does not erase saved data. Forecast freshness requires a matching recent history sample, so disabling recording eventually removes estimates.
