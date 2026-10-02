# Security policy

Security fixes target the latest release. Older versions are not maintained separately. Obtain packages from this repository and check SHA-256. Packages are currently unsigned: a checksum establishes integrity against the published checksum, not independent publisher identity.

## Private reports

Use [GitHub private vulnerability reporting](https://github.com/kumarnabh/prism-widget/security/advisories/new). Include version, synthetic reproduction and impact. Never send live credentials/account dumps. If the form is unavailable, open an issue requesting a private contact **without disclosing the vulnerability**. No response-time SLA is promised.

## Boundaries

Prism runs with your Windows privileges. Cursor/Go credential access is opt-in and read-only, with fixed usage endpoints and refused redirects. Native browser messages validate origin, size and numeric fields. Registration is current-user only. Do not share a writable installation with untrusted users: modifying its scripts permits code execution as you.

Contributors must preserve account scoping, freshness, payload sanitization, deadlines and no-model-prompt behavior. Tests must use fake data and mocked accounts. Before publication, check staged files, full Git history and release contents. Scanners reduce risk; they cannot prove absence of every secret or bug.

Update downloads use a fixed public repository, an HTTPS host allowlist, a bounded size/deadline, and a published SHA-256 digest before atomic finalization. They are never executed/extracted. This is corruption verification, not code signing or protection against compromise of the upstream release account. Startup is optional and limited to the current user; conflicting registrations are preserved.
