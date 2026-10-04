# Releasing

Use a clean reviewed checkout; no provider sign-in is needed.

1. Update project version and CHANGELOG. Review dependency/runtime versions and corresponding license notices.
2. Run Build.ps1, Python tests, Node browser tests and offline `scripts/verify-native.ps1`. Inspect compact/full renders locally; never publish private screenshots.
3. Stage intended source only. Run `python scripts/check_publish.py` and inspect the staged diff. Export the index into a fresh scratch directory with `git checkout-index --all --prefix=<absolute-directory>/` and scan those bytes.
4. Run `gitleaks git . --config .gitleaks.toml --redact --no-banner` over full reachable history. The public extension-key exception requires exact value AND path; do not broaden it to hide findings.
5. Run `./scripts/package.ps1`. It publishes self-contained win-x64 into a new staging directory and copies an explicit list. Existing archives are preserved; use `-OutputDirectory` for another candidate.
6. Run `python scripts/verify-package.py build/packages/Prism-<version>-win-x64.zip`. Scan the printed staging folder using `gitleaks dir <stage> --config .gitleaks.toml --redact --no-banner`. Verify SHA256SUMS. Test Setup from a fresh extracted copy.
7. Commit/push the authorized branch and require hosted CI to pass. Create a version release at that exact commit with ZIP and SHA256SUMS. Never attach data, virtual environments, vendor folders, logs, runtime screenshots, native-host.json or self-test reports.
8. Verify visibility, download links and hashes. Distinguish local checks, hosted CI, visual inspection and live provider acceptance. Scanning is not a guarantee of absence of every secret/bug.

The allowlisted assets/prism-dashboard.png is an offline synthetic illustration with example hardware and quotas; inspect it before publication and never replace it with account/device screenshots.

The package includes .NET, not Python. Setup installs Python dependencies locally. No auto-updater/signing certificate is currently provided. CI cannot establish account entitlement or live provider availability.
