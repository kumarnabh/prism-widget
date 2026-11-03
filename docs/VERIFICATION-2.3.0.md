# Prism 2.3.0 verification

Objective: ship optional read-only typed GitHub monitoring with independent metric visibility, plus integrated Prism branding and selectable glass themes. Work was performed on `codex/github-capacity` from current `main` (`9c9034e`). UI/theme changes are isolated in commit `271f3af`; subsequent integration/review evidence belongs to the PR head.

## Local checks

- `dotnet build source/Prism.csproj -c Release --no-restore --nologo -p:UseSharedCompilation=false -o build/github-native` — passed, zero warnings/errors on the final build.
- `./.venv/Scripts/python.exe -m unittest discover -s tests -p 'test_*.py'` — 103 tests passed. Includes 18 GitHub tests with multiple subcases. The existing process-tree cleanup test requires the normal desktop session; it failed under the restricted process sandbox and passed unchanged outside it.
- `node --test tests/popup.test.cjs` — 3 tests passed.
- `./scripts/verify-native.ps1 -InstallationRoot ./build/github-native -TimeoutSeconds 180` — passed in 61.2 seconds on the final run. All 788 layout cases passed: 16 reference layouts, 480 resize transitions, 160 language/metric/clock cases, 60 hardware cases, 24 ecosystem cases and 48 GitHub/theme cases. HD export was 1770 × 3232. Expanded typed tiles are included in the bounds assertions.
- Native model checks cover units, zero/unknown values, freshness, UTC month changes, account isolation, cautious forecasts, irregular/gapped/corrected data, hidden-metric alert keys/deduplication, select-none thresholds, profile persistence, explicit consent migration, 30-day retention, 40,000-sample history restart and newer-schema preservation.
- `./.venv/Scripts/python.exe scripts/check_publish.py` — zero staged publication findings. The exact staged index was exported with `git checkout-index --all --prefix=...` and scanned with `gitleaks dir ... --config .gitleaks.toml --redact --no-banner` — no leaks found.
- `./scripts/package.ps1 -OutputDirectory ./build/github-release-2.3.0` and `./.venv/Scripts/python.exe scripts/verify-package.py build/github-release-2.3.0/Prism-2.3.0-win-x64.zip` — initial self-contained candidate packaged successfully; 515 entries, zero package-boundary findings. The published artifact is rebuilt at the final main commit and verified separately during release.

Early native assertion failures were diagnosed and fixed; no assertions or image baselines were weakened. Review additionally fixed unconfigured/migrated select-all visibility, typed tray selection, incomplete API totals, repository name casing, forecast unknown states, account-change cancellation and bounded-history reload size. No unresolved code defect was found in the final local review. Hosted CI, final published artifact hashes and installation acceptance are reported in the PR/release, separately from these local checks.

## Visual evidence

All images use synthetic account/quota/billing data and example hardware. The new header has no separate raster icon plate or glow. The Clear render uses neutral shades, including the resize grip.

- [Aurora glass](images/2.3.0/theme-aurora.png), [Ocean glass](images/2.3.0/theme-ocean.png), [Dusk glass](images/2.3.0/theme-dusk.png), [Clear glass](images/2.3.0/theme-clear.png)
- [Selected metrics in a ribbon](images/2.3.0/github-clear-1200.png)
- [Per-metric selection/order/detail-only controls](images/2.3.0/github-metrics-en.png)
- [Connection guidance](images/2.3.0/github-connection-en.png), [typed details/history](images/2.3.0/github-details-en.png)

## Manual acceptance checklist

- [ ] Connect a real personal account with the narrow billing permission and compare its current-month report with GitHub billing.
- [ ] Separately select an accessible organization and compare its report; test an inaccessible organization.
- [ ] Exercise a real optional fine-grained token through Windows Credential Manager, expiry/revocation and disconnect. Native interop is implemented; CI never needs credentials.
- [ ] Switch the active CLI account, refresh, and verify no prior-account values/history appear. Offline switch/in-flight/cache isolation cases already pass.
- [ ] Compare repository counts and storage against real selected repositories, including free/public usage; verify unknown units and API ceilings stay unavailable.
- [ ] Check physical monitor changes and mixed-DPI scaling at the user's display settings, including ribbon/tray and every glass theme.
- [ ] Exercise actual Windows notification delivery/quiet settings and restart persistence of opted-in targets. Scoped deduplication/cooldown logic has offline coverage.

No live GitHub billing credentials were used for qualification and no paid workflows were dispatched. Unsupported allowances remain unknown; forecasts are local low-confidence estimates and local targets are not GitHub-enforced limits. These manual items remain unverified rather than being implied by synthetic renders.
