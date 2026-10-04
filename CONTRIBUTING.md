# Contributing

Focused fixes, accessibility improvements, adapter updates and clear reports are welcome. Be respectful and constructive. Discuss substantial features in an issue before large implementation work.

Use Windows x64, Python 3.11–3.14, .NET 8 SDK and Node.js 22+. Run Build.ps1 and the README's tests. Keep a personal installation separate from test checkouts. Native checks default to offline account fixtures; Python tests mock provider access.

Use C# for WPF/sensors, Python for adapters and plain JavaScript for the extension. Preserve unknown/expired/stale semantics: malformed data must not invent capacity. Keep source/freshness visible. The dashboard must not scroll; settings can scroll on short displays. Avoid unnecessary dependencies.

Pull requests should explain the user problem, changed behavior, checks and remaining limitations. Add meaningful regressions for behavior fixes. Visual evidence must use synthetic accounts. Never weaken assertions or regenerate baselines to manufacture success.

Stage only intended files, run `python scripts/check_publish.py`, inspect `git diff --cached`, and use Gitleaks per the release guide. Never force-add runtime data. CI must pass before merge. Report security vulnerabilities privately under SECURITY.md.

Contributions are provided under this repository's MIT license. Third-party assets require provenance and compatible notices.

For new quota sources, start with the [provider SDK and fixture checklist](docs/PROVIDERS.md). Providers enter reviewed source/build releases; no arbitrary Internet plugin loading is supported.
