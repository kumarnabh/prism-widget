# Provider development (2.1)

Prism ships reviewed Python adapters and a WPF dashboard. There is no remote plugin loader or Prism backend. Adding a source requires a source review and a normal signed-off repository release. Do not download executable adapters or evaluate manifest strings.

## Registration and lifecycle

`provider-manifest.json` declares ID, display name, icon/color, reviewed adapter ID, source, authentication description, default interval, default enablement and capabilities. Python validates this package file; the same file is embedded in the WPF assembly at build time. Dashboard cards, metric selections, schedules, Connections and history selectors use this registry. New providers do not require layout-specific edits.

`ProviderAdapter` in `provider_registry.py` exposes availability detection, collection, normalization, safe diagnostics and quota-cache removal where applicable. Its explicit dispatch allows only reviewed built-ins. Availability checks never retrieve credentials. Connection/setup guidance appears in the Providers page. Disabling a provider stops scheduling and cancels an in-flight native request; clearing adapter quota cache never signs out the provider or deletes quota history.

The existing Codex, Claude, Cursor Individual and OpenCode Go collectors remain source-specific. Shared normalization and account HTTP caching/backoff are reused. UI collection uses one Python process per due provider, one 45-second deadline per process tree, bounded stdout/stderr, and immediate application of each completed result. Shutdown cancellation kills active process trees synchronously; it does not depend on a WPF continuation. Python's single-provider worker is synchronous so CLI-owning finally blocks cannot be abandoned by daemon-thread exit. The multi-provider Python convenience API joins its workers; native callers must use independent single-provider processes for timeout isolation.

## Contract version 1

The normalized result uses existing wire names for compatibility:

```json
{
  "schema_version": 1,
  "provider_id": "example",
  "display_name": "Example",
  "account_label": "Default",
  "status": "Live",
  "source": "Reviewed source label",
  "capabilities": ["remaining", "history"],
  "at": 1800000000,
  "windows": [{"id": "included", "label": "Included", "remaining": 42, "reset": null, "rolling": false}],
  "detail": "Provider-reported allowance."
}
```

- `at` is the actual capture time in Unix seconds. Never refresh it merely because cached data was read. Future/invalid captures cannot carry quota. Current readings age to Stale after ten minutes.
- `remaining` is a finite percentage in [0,100], including legitimate zero/full. Never convert token counts, dollars spent or missing fields into an invented allowance.
- `reset` is a provider-exposed Unix timestamp or null. An exposed cadence alone is not an exact timestamp. Expired windows are omitted; duplicate IDs or malformed windows invalidate the whole set.
- `rolling` is a Boolean and defaults to false. Window IDs must remain stable across captures; labels are bounded, free of control characters and obvious identity/secret patterns.
- Optional `scope` and `account_id` contain the same installation-local HMAC identifier. Never emit email addresses, account handles, raw keys, key labels or a global account ID. The contract is ready for scoped account instances; 2.1 displays one active account per provider and does not claim multi-account UI support.
- Plan is omitted unless a future adapter provides a reviewed safe label. Arbitrary metadata is not supported. Raw provider payloads never enter the dashboard.
- Capabilities are from the reviewed manifest, not remote responses: remaining, reset, multiple_windows, history, multi_account, plan, api_credits, local_cli. Declare only supported capabilities.
- Connect/Waiting/Unavailable/Disabled results have empty windows and safe diagnostic guidance. An error must not inherit a current quota. Source/identity changes split local history conservatively.

WPF independently validates timestamps, numbers, window identifiers and freshness through `QuotaSnapshot`. It never assumes every source has resets or several windows. Forecasts remain optional local estimates; sources without a verified account scope do not generate account-scoped forecasts.

## Minimal adapter

An adapter supplies a raw, restricted reading; `ProviderAdapter.normalize` applies the contract. This example is illustrative and is not loaded:

```python
def collect_example():
    payload = approved_read_only_request()  # exact HTTPS host/path, no redirects
    return {
        'status': 'Live', 'at': capture_time,
        'windows': [{'id': 'included', 'label': 'Included',
                     'remaining': validated_remaining_percent, 'reset': None}],
        'scope': installation_local_scope,
    }
```

Add the reviewed function/import to the explicit registry dispatch, then one manifest row. Add any new adapter file to the package allowlist. New authentication must default off unless it can safely reuse a provider-owned CLI without reading/reusing credentials itself. Keep consent separate from dashboard visibility. Do not ask for pasted keys or create a credential manager.

## Credentials, network and cache rules

Reuse provider CLI/session authentication whenever possible. Opt-in HTTP reuse sends credentials only to exact approved HTTPS endpoints, refuses redirects, limits response bytes and uses finite deadlines. Never log exception bodies, command lines, account identity, authorization headers or arbitrary API labels. No model calls, account mutations, registration, analytics or key refresh.

Shared account caches hold only validated quota, capture time, fingerprint/scope and bounded retry state. Retry errors retain their original capture time and become stale. Unauthorized or changed credentials invalidate prior quota. Do not persist raw secrets. Cache JSON is untrusted: validate finite values, shapes, duplicate IDs, labels and timestamps. App folders are per-user writable installations; no new elevated service or global credential store is created.

## OpenRouter and Gemini decision

OpenRouter uses its documented [GET /api/v1/key](https://openrouter.ai/docs/api/api-reference/api-keys/get-current-api-key) interface, an existing `OPENROUTER_API_KEY` environment value, and a separate explicit consent. It is disabled by default. The percentage is `limit_remaining / limit`; it describes that key's credit allowance in USD, not total account credit or token availability. Unlimited, zero/invalid limits and unrecognized formats are unknown. Key labels, creator identity, BYOK usage and arbitrary fields are discarded. The response exposes reset cadence but no exact reset instant, so Prism shows unknown reset.

Gemini documents interactive [`/stats model`](https://geminicli.com/docs/resources/quota-and-pricing/) quota inspection. This release did not establish a supported stable headless quota interface with safe authentication isolation, so Gemini is deferred rather than scraping a private endpoint. Revisit with evidence and fixtures when such an interface is available.

## Fixtures and contribution checklist

`tests/fixtures/provider-responses.json` contains synthetic source examples. `test_provider_contract.py` exercises every registered adapter through normal, empty, malformed, unauthorized/connection failure, expired authentication, zero/full, stale, unknown reset, multiple windows, timeout and format-change boundaries. Existing provider-specific CLI/browser/cache fixtures remain mandatory. Native checks verify independent results, real child-process termination on deadline and shutdown, new-provider defaults, empty-error guidance and all-provider layouts in four languages. No CI test requires an account.

Before submission: document the legitimate source and quota meaning; minimize capabilities; demonstrate no-secret fixtures and scope separation; prove fixed-host/no-redirect behavior and cache freshness; run Python/browser/native tests and Release build; inspect expanded/compact/unknown states; scan exact source/package bytes; update privacy, support and this guide. Real-account qualification, offline tests and native UI evidence must be reported separately.
