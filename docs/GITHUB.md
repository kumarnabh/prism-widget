# GitHub monitoring (2.3)

GitHub is optional, off and invisible by default. Nothing is written to GitHub. No workflows, model calls, billing changes, artifact deletions or repository mutations are performed.

For monthly account totals, select **Account Actions minutes** and **Actions storage accrued** in Settings → Metrics → GitHub. These use the personal account's billing report (or the explicitly configured organization's report) across its billed repositories, independently of the repository selection. Storage is accrued **GB-hours**, not the amount currently stored or allowance remaining. Free/public runner usage absent from billing reports is not included. Missing storage rows or unsupported units remain unknown. The separate Actions artifacts/cache metrics describe current bytes in selected repositories.

Personal billing requires **Plan (read)** user permission. A normal GitHub CLI OAuth sign-in can still read repository statistics but may be denied access to billing. Prism keeps account billing values unavailable in that case; CLI sign-in alone does not grant billing access. Use a compatible fine-grained credential through Connections if billing permission is unavailable; enter it only in the local masked credential field, never in JSON or chat.

## Connect

Open **Connections → GitHub → Connect / disconnect**. Enable collection and explicitly choose a sign-in source. With GitHub CLI installed, run `gh auth login --hostname github.com` yourself, then choose **GitHub CLI**. Use `gh auth status --hostname github.com` to inspect your sign-in. Prism captures `gh auth token` privately in memory and verifies it through the bounded authenticated-user endpoint; command output and credentials are never forwarded to Prism stdout, diagnostics or logs. Environment token overrides are ignored so the selected CLI account is authoritative. Prism never changes the CLI's scopes or signs you out. After `gh auth switch --hostname github.com`, refresh Prism.

A normal CLI sign-in may not have billing permission. Personal billing needs **Plan: read** on a fine-grained token. Organization billing is a separate explicit selection and needs **Administration: read** plus appropriate access to the organization. Selected repository statistics need **Metadata: read** and **Actions: read**. Grant access only to the repositories you want to monitor; no write permissions are needed. If CLI reuse cannot access billing, choose **Windows Credential Manager**, enter an optional fine-grained token in the masked field and save. Tokens are saved only in Windows Credential Manager, under an installation-specific target. A blank field preserves the saved credential; the token is never displayed again. Disconnect removes Prism consent, its cached reading and the installation's saved token. It retains local history and the provider-owned CLI sign-in.

Leave Organization blank for personal billing. To inspect an organization, enter its name explicitly. The integration does not enumerate your organizations or repositories. Add up to 20 explicit `owner/repository` entries for workflow/artifact/cache statistics. Billing and repository permissions are independent: missing billing permission does not prevent accessible repository statistics.

GitHub.com is supported. Enterprise Server and custom API hosts are not accepted.

## Choose metrics

Open **Settings → Metrics → GitHub**. Each metric has its own checkbox, unit, source description and availability. Select a row and use Move up/down. Select none is supported. **Details only** keeps a selected metric in the scrollable details panel and out of the main dashboard, compact grid, ribbon and tray. Main layouts adapt without scrolling; showing many metrics increases the minimum height needed to fit them. Visibility/order/detail-only placement and theme are saved in layout profiles. Connections, targets, alerts and refresh settings remain global.

The general Metrics page can interleave GitHub metrics with other metrics. The dedicated GitHub page changes their relative order and groups that ordered selection at the end. Dashboard groups continue following Prism's existing paired-system layout. Hidden metrics are still collected and may alert. Disabling the GitHub provider stops collection. Select-none leaves no GitHub cards or reserved dashboard space.

## Meaning and limitations

- **Actions compute USD** sums net charges from reported Actions runner-minute rows after discounts. It excludes storage. **Minutes** sum those rows' reported quantity, not workflow wall time or all free/public/self-hosted executions. No plan/rate/allowance table is built into Prism.
- **Allowance remaining** stays unknown: the usage report does not expose an authoritative included allowance. Zero charges do not prove a full allowance. There is no guessed reset timestamp or remaining percentage.
- **Workflow executions/successes/failures** cover explicitly selected repositories and creation dates in the current UTC month. Rerun attempts are not counted separately. Only conclusion `failure` is classified as a failure; cancellations/timeouts are distinct from successes/failures. The documented filtered-runs API caps searches at 1,000: results at or above that ceiling, or incomplete results, become unavailable rather than a low partial total.
- **Artifacts/cache** are current byte snapshots in selected repositories. Artifact bytes sum non-expired entries; cache readings can lag by five minutes. Display uses MiB/GiB, independently of billing's metered storage. These snapshots are not invoice storage or GB-hours.
- **Packages, Codespaces and LFS** report quantities only when product/SKU/unit combinations match the reviewed unit. GB-hours are never converted into current stored GB; Codespaces machine-hours with unknown core counts remain unknown. Missing rows or unrecognized units remain unknown. An empty successful month report establishes zero total metered spend, but not zero for every product.
- **Month billable spend** sums net amounts in GitHub's USD metered-usage report for the requested UTC calendar month. It is not a final invoice and excludes subscriptions, taxes and adjustments outside the report. Reports can lag. Explicit incompatible currency fields or malformed/partial reports are rejected.
- **Repository billable spend** includes only repository-attributed billing rows. Its private drill-down shows validated repository names so you can identify each charge; histories retain opaque installation-local scopes. Names and billing details never enter shareable diagnostics. At most 1,000 repository summaries are accepted and the panel displays the first 100. Unattributed charges remain in overall spend.
- Missing permissions, unconfigured sources, unsupported units, unknown values, errors and stale readings are distinct. Unknown is a dash, never a fabricated zero. Hover for source, month and last successful fetch. Freshness means a successful fetch, not instantaneous billing settlement. GitHub readings become stale after one hour or a UTC month change.

Default collection is every 30 minutes, configurable in existing Refresh rates. Each provider has its own native process/deadline/cancellation. GitHub also bounds CLI output/time, API response bytes, total transfer, pagination and network deadlines. Redirects are refused. No automatic request retries occur; failures use bounded local backoff, including rate-limit headers. F5 still respects cache/backoff. Authentication/configuration changes split opaque scopes; tokens/accounts are checked again before a newly fetched reading is accepted. CLI reuse needs to retrieve the active credential to detect switching even when serving a cached reading.

## History, estimates and alerts

Typed history lives separately from percentage-only AI quota history. Existing history recording preferences apply; fresh captures are compacted into five-minute buckets, retained for 30 days and bounded at 40,000 samples. Details → History shows the current account and source selection, with units and explicit gaps. Clear GitHub history erases that file; it does not sign out.

Monthly-spend and local-target forecasts are **local estimates, low confidence**. They need at least six distinct, regular, fresh observations over 30 minutes, no gap above two hours, no downward correction, and a fitted trend with R² at least 0.9. Insufficient, irregular, flat, stale or corrected data suppresses forecasts. Estimates are not extrapolated from AI allowance percentages, are not GitHub predictions and cannot establish service exhaustion. A local-target time needs an explicit target above current total spending.

**Local targets & alerts** accepts per-metric targets in the displayed unit and separate opt-in alert checkboxes. Targets are local preferences, never GitHub-enforced spending limits or included allowances. Selectable notifications fire once at 75%, 90% and 100% of each target per account/source/month, respect the shared two-minute delivery cooldown and persist deduplication. Measured-usage alerts do not use forecasts. Hidden metrics may alert; unconfigured/missing/stale values cannot. No resource or billing action follows an alert.

## Reviewed official sources

Verified while implementing this integration:

- [Billing usage reports and permissions](https://docs.github.com/en/rest/billing/usage): personal `GET /users/{username}/settings/billing/usage`; organization `GET /organizations/{org}/settings/billing/usage`. Enhanced billing access is required. Preview summary endpoints and legacy billing quotas are not scraped or assumed.
- [Workflow runs](https://docs.github.com/en/rest/actions/workflow-runs#list-workflow-runs-for-a-repository), [artifacts](https://docs.github.com/en/rest/actions/artifacts#list-artifacts-for-a-repository), [cache usage](https://docs.github.com/en/rest/actions/cache#get-github-actions-cache-usage-for-a-repository), [repository metadata](https://docs.github.com/en/rest/repos/repos#get-a-repository), and [authenticated user](https://docs.github.com/en/rest/users/users#get-the-authenticated-user).
- [Actions billing and storage units](https://docs.github.com/en/billing/concepts/product-billing/github-actions) and [usage reporting guidance](https://docs.github.com/en/billing/tutorials/automate-usage-reporting).

API version is pinned to `2026-03-10`. Offline fixtures never need a live billing credential. Live account/organization acceptance and physical mixed-DPI transitions require manual verification; synthetic test renders do not prove those boundaries.
