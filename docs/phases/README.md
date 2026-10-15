# SafeClean — Phase Plans

One file per phase. Each file says **what** the phase builds, **why**, **which files** change,
**how it is tested**, and **when it counts as done**. Work happens strictly one phase at a time.

| # | Phase | File | Status |
|---|---|---|---|
| 0 | Environment, requirements, threat model, plan | [PHASE-00](PHASE-00-environment-and-plan.md) | ✅ Done |
| 1 | Solution skeleton, conventions, WPF shell | [PHASE-01](PHASE-01-solution-skeleton.md) | ✅ Done (2026-10-15) |
| 2 | Core safety policies (pure logic) | [PHASE-02](PHASE-02-core-safety-policies.md) | ✅ Done (2026-10-15) |
| 3 | Read-only Temp scanner | [PHASE-03](PHASE-03-read-only-scanner.md) | Not started |
| 4 | Dry Run preview screen | [PHASE-04](PHASE-04-dry-run-preview.md) | Not started |
| 5 | Cleanup engine (not connected to UI) | [PHASE-05](PHASE-05-cleanup-engine.md) | Not started |
| 6 | Cleanup test gate | [PHASE-06](PHASE-06-cleanup-test-gate.md) | Not started |
| 7 | Enable cleanup in the UI | [PHASE-07](PHASE-07-enable-cleanup-ui.md) | Not started |
| 8 | Read-only monitoring services | [PHASE-08](PHASE-08-monitoring-services.md) | Not started |
| 9 | Dashboard, Performance screen, UI polish | [PHASE-09](PHASE-09-dashboard-and-polish.md) | Not started |
| 10 | Settings, exclusions, logging, history | [PHASE-10](PHASE-10-settings-history.md) | Not started |
| 11 | Security review and verification | [PHASE-11](PHASE-11-security-review.md) | Not started |
| 12 | Publishing | [PHASE-12](PHASE-12-publishing.md) | Not started |

## Rules that apply to every phase

1. Before starting: re-read this phase file and `docs/SECURITY.md`; explain the goal and plan.
2. Keep changes inside this project folder. Never touch personal files, system settings or security settings.
3. Never run destructive tests against the real Temp folder, personal folders or Windows folders.
   Use disposable sandboxes under the test project's `bin` folder.
4. No new NuGet package without stating its purpose, publisher, version age and licence.
5. No commit, push or upload unless the project owner asks.
6. After finishing: build (0 warnings), run all tests, record **actual** results in `docs/TESTING.md`,
   update `docs/CHANGELOG.md`, `docs/ROADMAP.md` and this table.
7. Never claim something passed without running it.
8. Uncertainty means **skip**, never delete.

## Common definition of done

- `dotnet build SafeClean.sln -c Debug` → 0 warnings, 0 errors.
- `dotnet test --solution SafeClean.sln -c Debug` → all tests pass; any skipped test has a written reason.
- Phase marked ✅ Done (with date) in its own file, this table, `ROADMAP.md` and `CHANGELOG.md`.
- The phase's own exit criteria (in its file) are met and checked off.
- Documentation updated to match what the code really does.
