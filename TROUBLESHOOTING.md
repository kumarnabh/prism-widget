# Troubleshooting

Use **⋯ → Diagnostics** first. For issues, include Prism/Windows versions, display scale, reproduction and the diagnostics report.

| Symptom | Check |
| --- | --- |
| Python not found | Install 64-bit Python 3.11–3.14 with launcher; check `py -3 --version` in a fresh terminal. A Store alias alone is not an interpreter. |
| Terminal dependency error | Run Setup again. Never copy `.venv` or `vendor` from another machine/Python version. |
| Access denied | Extract into a writable folder; close Prism before replacing binaries. Avoid Program Files/read-only shares. |
| Missing .NET | Release ZIP includes .NET. Source builds need the .NET 8 SDK. |
| Unknown publisher | Binaries are unsigned. Review source/checksum before running; do not disable Windows security globally. |
| Quota is a dash | No valid window returned. Check sign-in, tooltip and Connections. Unknown means neither zero nor full. |
| Codex stale | Active-account verification failed; historical data is unverified and cannot trigger alerts. Check CLI sign-in. |
| Cursor unavailable | Use an individual plan, sign in again, enable reuse and refresh. Prism does not renew credentials. |
| Go 403/unavailable | Confirm Go subscription entitlement. A generic Zen key is insufficient. |
| Claude needs setup | Finish first-run setup and subscription login through Connect Claude CLI.cmd. API-key auth is not a subscription. |
| Claude numbers, no reminder | CLI lacks reset timestamps. Browser/feed readings can qualify if they provide a current reset. |
| Browser account selection stuck | Select a current account explicitly in the popup, including after a saved account was removed. |
| Native host unavailable | Run Setup and Install Claude Browser Sync.cmd in the current folder, then reload the unpacked extension. |
| No reset alert | Needs running widget (visible or in tray), enabled reminders, fresh data, known reset in range, >10% left and a not-yet-notified reset. Preview only tests appearance. |
| No tray notification | Check Windows notifications/Do Not Disturb and Settings → General → Enable system tray. The widget must remain running. |
| Startup conflict | Disable Start with Windows in the old Prism installation first; registrations for another installation are preserved. |
| Empty history | Fresh provider readings are needed while recording is enabled. Data is not backfilled. |
| Update failed | Check connectivity to GitHub. Transfers time out after three minutes. A conflicting existing ZIP is preserved; move it before retrying. Never run a checksum-mismatched download. |
| Hidden metric still alerts | Visibility controls the layout, not collection or alerts. Configure alerts separately. |
| Clock one hour different | Regional Eastern/Pacific follow daylight saving, unlike fixed EST/PST. Verify selected zone and Windows timezone data. |
| RAM rises again | Normal when apps reload trimmed pages. This does not fix leaks. |
| Window off-screen | Close Prism and rename `data/window.json` to a backup, then reopen. |

Provider interface changes may require adapter updates. Describe failures with synthetic structures, never live tokens, databases, transcripts or data folders. Physical mixed-DPI transitions and Windows ARM64 are not validated; the release targets x64.
