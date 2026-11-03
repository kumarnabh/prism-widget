# Prism 2.3.1 verification

The follow-up adds monthly Actions storage accrual to the existing account billing report. It does not change authentication, request paths or repository discovery. The Actions minutes card now identifies its account scope explicitly. Selected repository filters do not restrict either billing metric. Current repository artifact/cache bytes remain separate metrics.

- 105 Python tests passed, including summed storage rows across repositories, compatible units, unrelated product isolation, missing data, true zero and typed normalization.
- Three browser tests passed.
- Build passed. Local NuGet vulnerability metadata was unavailable under restricted networking; hosted build checks remain required.
- Native offline verification passed in 71.1 seconds: 836 layout cases including all four themes, fallback fonts, all metric selections and the new account storage card. No assertions or baselines were relaxed.
- Full reachable history scan with the pinned Gitleaks 8.30.1 binary passed before commit, as did the publication checker. Exported committed bytes and the final package are checked separately at release.

An explicitly authorized check of the prior 2.3.0 Downloads installation returned Codex Live, Cursor Live, Claude CLI and OpenCode Live after installing the checksum-verified private runtime. GitHub CLI repository artifacts/cache returned Live; account billing returned Permission. The new metric uses that same protected billing report, so CLI access cannot be assumed to provide billing values. A compatible personal fine-grained credential with Plan (read) is required when the existing CLI sign-in is denied. No broader credentials were requested or generated automatically.

Exact-package live results, final asset hash and hosted checks are recorded in the release notes. Physical mixed-DPI/multi-monitor transitions and Windows notification delivery remain manual checks. The inherited visual/theme evidence is in VERIFICATION-2.3.0.md.
