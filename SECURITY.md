# Security policy

Security fixes target the latest release. Older versions are not maintained separately. Obtain packages from this repository and check SHA-256. Packages are currently unsigned: a checksum establishes integrity against the published checksum, not independent publisher identity.

## Private reports

Use [GitHub private vulnerability reporting](https://github.com/kumarnabh/prism-widget/security/advisories/new). Include version, synthetic reproduction and impact. Never send live credentials/account dumps. If the form is unavailable, open an issue requesting a private contact **without disclosing the vulnerability**. No response-time SLA is promised.

## Boundaries

Prism runs with your Windows privileges. Cursor/legacy Go credential access is opt-in and read-only, with fixed usage endpoints and refused redirects. Opted-in OpenCode Console reads use its native v2 CLI: only the Console integration is exported in memory and passed through stdin into a private temporary database. Normal completion removes that database; abrupt termination can leave private files under data/. No Console identity or raw credentials enter quota caches. No model prompt is submitted. Native browser messages validate origin, size and numeric fields. Registration is current-user only. Do not share a writable installation with untrusted users: modifying its scripts permits code execution as you.

Normal startup requires a verified local Python setup receipt tied to the current requirements checksum. Setup checks real terminal-library imports before writing it. The optional OpenCode installer verifies both the archive and extracted executable; an existing unknown binary is preserved and rejected. Re-running Setup does not grant account consent or modify global CLI installations.

Contributors must preserve account scoping, freshness, payload sanitization, deadlines and no-model-prompt behavior. Tests must use fake data and mocked accounts. Before publication, check staged files, full Git history and release contents. Scanners reduce risk; they cannot prove absence of every secret or bug.

GitHub is explicitly opt-in, GET-only and restricted to reviewed `https://api.github.com` paths. Redirects, arbitrary hosts, workflow dispatch and resource/billing mutations are rejected. Credentials are obtained privately from GitHub CLI or Windows Credential Manager; never place a token in preferences, command arguments, response output or diagnostics. Both credential and authenticated identity are rechecked before accepting new data. Cache/backoff/history scopes isolate account, organization and selected-repository changes. Raw API bodies, errors, identities and repository names cannot cross the typed display boundary. Personal billing needs Plan (read); organization billing and repository Actions permissions remain separate. Unsupported units, incomplete pagination and missing allowances must not become fabricated zeros or quotas.

Update downloads use a fixed public repository, an HTTPS host allowlist, a bounded size/deadline, and a published SHA-256 digest before atomic finalization. They are never executed/extracted. This is corruption verification, not code signing or protection against compromise of the upstream release account. Startup is optional and limited to the current user; conflicting registrations are preserved.
