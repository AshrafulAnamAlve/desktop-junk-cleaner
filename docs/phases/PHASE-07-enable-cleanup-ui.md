# Phase 7 — Enable Cleanup in the UI

**Status:** ✅ Done (2026-10-17) — **real deletion enabled in the app** (behind Real mode + confirmation)
**Depends on:** Phase 6 gate passed and reported ✅
**Deletes anything?** Only files the user explicitly selects, confirms, and the engine re-validates. During this
phase, deletion was exercised **only** on fake services (unit tests) and on synthetic files in a Debug sandbox.
The real Temp folder was not cleaned — that is the project owner's choice, whenever they want.

## Goal

Connect the tested cleanup engine to the Junk Cleaner screen behind several deliberate steps, then
show an honest result report.

## Why (beginner explanation)

Several separate "locks" must all be opened on purpose before anything is deleted: switch to Real mode,
select files, read the summary, and press a clearly labelled delete button. Any one of them stops the
process. Afterwards, the report separates files actually deleted from files skipped or failed.

## What was built

| Area | Files | Notes |
|---|---|---|
| Real mode | `JunkCleanerViewModel` (`IsRealMode`, `CanUseRealMode`, `RealModeHint`) | Off at every start (never saved); can't be switched on while elevated or while cleaning; switches itself off after every cleanup |
| Delete button | `JunkCleanerView.xaml`, `Button.Danger` style | Red, "Delete N files…", shown only in Real mode, no keyboard shortcut; enabled only with Real mode + selection + completed scan + not elevated |
| Confirmation | `Views/ConfirmCleanupWindow.xaml(.cs)`, `CleanupConfirmationViewModel`, `Services/ICleanupConfirmation`, `DialogCleanupConfirmation` | Count, estimated size, categories, "needs review" warning, "not moved to the Recycle Bin", re-check explanation; **Cancel is default and focused**; no "don't ask again" |
| Running | `JunkCleanerViewModel.DeleteSelectedAsync` | Re-checks every condition; refuses results older than 30 min; sends **candidate IDs** + `CleanupMode.Real`; progress; Cancel stops before the next file |
| Report | `CleanupReportViewModel`, report panel | Deleted, skipped (grouped by reason), failed (with Windows error); space freed from deletions only; refusals explained with `PolicyText`; "cancelled" / "stopped by error" notes; if the service itself fails, it says some files *may* have been deleted |
| After cleanup | `JunkCleanerViewModel` | Results closed (new scan required), Real mode off, sidebar back to DRY RUN |
| Mode badges | `MainWindow.xaml`, `MainViewModel` | Sidebar shows DRY RUN (blue) or REAL MODE (red); toolbar badge follows |
| Wiring | `App.xaml.cs` | `ICleanupService → CleanupService`, `ICleanupConfirmation → DialogCleanupConfirmation` |
| Sandbox mode (Debug only) | `Infrastructure/Scanning/DebugSandboxRootResolver.cs`, `App.xaml.cs`, `AppInfo` | `--sandbox-root <folder>`; marker file `.safeclean-sandbox` required or SafeClean won't start; no fallback to Temp; orange banner on every page; dialog names the folder |
| Manual sandbox helper | `tests/…/Manual/ManualSandboxSetup.cs` (Explicit) | Creates `<repo>\.sandbox\manual-<time>` with 3 safe, 1 review, 1 too-recent, 1 read-only file and a hidden read-only marker |
| Styles | `Theme.xaml`, `Controls.xaml`, `InverseBooleanToVisibilityConverter` | Danger colours, `Button.Danger`, `ToggleSwitch` (red when on) |
| Accessibility fix | `MainWindow.xaml`, `JunkCleanerView.xaml` | Accessible names had been put on `Border`s, which screen readers can't see (since Phase 1). Moved to the text elements; found because the sandbox safety check couldn't find the banner |
| Guardrails | `ArchitectureTests` | `App_HasNoAccessToCleanupCode_UntilPhase7` **replaced deliberately** by `App_ReachesCleanupOnlyThroughTheServiceInterface`; new `SandboxMode_ExistsOnlyInDebugBuilds` |

## Tests

| Case (from the plan) | Test | Result |
|---|---|---|
| Real mode off → clean command can't run | `RealModeIsOffAtStart_SoDeletingIsImpossible` | ✅ |
| Elevated → toggle disabled, command can't run | `WhenElevated_RealModeCannotBeSwitchedOn` | ✅ |
| Dialog cancelled → service never called | `CancellingTheDialog_NeverCallsTheCleanupService` | ✅ |
| Confirmed → called once with exactly the selected IDs and `Real` | `Confirming_CallsTheServiceOnce_WithExactlyTheSelectedIds_InRealMode` | ✅ |
| Report shown exactly as returned | `TheReport_ShowsExactlyWhatTheServiceReturned`, `SpaceFreed_ComesOnlyFromTheReport…` | ✅ |
| After cleanup: selection cleared, rescan required | `AfterCleanup_ResultsAreClosed_RealModeIsOff_AndANewScanIsNeeded` | ✅ |
| App restart → Real mode off | `ANewWindow_StartsInDryRunAgain` | ✅ |

Also: dialog contents and sandbox line; results older than 30 minutes (no dialog); refused cleanup; service
failure; locked controls and cancellation while cleaning; preview closed by switching Real mode on; sidebar
badge follows the switch. Sandbox resolver: marker required, marker must be a file, junction refused, marker
removed after the scan → cleanup refused and nothing deleted.

## Changes from the original plan (and why)

| Change | Reason |
|---|---|
| Real mode also switches **off after every cleanup** | Stricter: every cleanup needs a fresh, deliberate switch |
| The red warning is part of the bottom bar (red border, warning text next to the switch and button) instead of a separate banner | First screenshots showed the separate banner squeezing the file list to ~2 rows |
| "Drive free space change" not shown yet | Needs a disk-space service; moved to Phases 8/9 (monitoring and dashboard). The report shows only the engine's `BytesFreed`, as the plan requires |
| Sandbox mode refuses to **start** without the marker (instead of showing a refused root) | Simplest way to guarantee there's never a fallback to the real Temp folder |

## Verification results (actually run, 2026-10-17)

| Check | Result |
|---|---|
| `dotnet build SafeClean.sln -c Debug` | ✅ 0 warnings, 0 errors |
| `dotnet test --solution SafeClean.sln -c Debug` | ✅ 582 total: 576 passed, 0 failed, 6 skipped (the 5 known + the opt-in sandbox helper). First run: 1 failure in a new test (sorting IDs that aren't sortable) — test fixed |
| Release build | ✅ 0 warnings; `SafeClean.dll` / `SafeClean.Infrastructure.dll` contain **no** `DebugSandboxRootResolver` and **no** `--sandbox-root` (Debug build: present — proves the check works) |
| End-to-end in sandbox mode (UI Automation, Debug build) | ✅ Before anything destructive the script required 4 checks: command line names the sandbox; SANDBOX banner names it; scan found exactly 7 entries; dialog says only the sandbox can be affected. Then: Real mode on → sidebar REAL MODE → "Delete 3 files…" → dialog → **Enter = Cancel**, nothing deleted → red button → exactly the 3 safe files deleted, 62.0 KB freed; review/too-recent/read-only files, marker and folders kept → badge back to DRY RUN → rescan shows 4 entries. Run twice (second time after the layout change) |
| Safety checks working | ✅ The first two runs **stopped before deleting**: once because of a script bug, once because the SANDBOX banner wasn't visible to UI Automation (the accessibility bug above). In the third run a modal dialog the script couldn't find was closed with Cancel; all 7 files were confirmed intact each time |
| Audit log | ✅ One line per file with Temp-relative paths (`cache\data-old.dat`…), summary "63488 bytes freed"; no full paths or user name |
| Real Temp folder | Not cleaned. Only synthetic sandbox files were deleted |

## Done when

- [x] All checks above pass; `SECURITY.md`, `README.md`, `CHANGELOG.md` updated
- [x] Roadmap marks "real deletion enabled" with the date

## Risks (from the plan) → outcome

- **Accidental confirmation** → Cancel is default and focused; Enter was verified to cancel; the red button needs a deliberate click.
- **Sandbox mode misuse** → Debug-only (source test + Release binary check), marker required, never a fallback.

## Follow-ups

- Phase 9: rows in the file list are still limited at the default window size (especially with the sandbox banner).
- Phase 9: in sandbox mode, root-level files are labelled "Temp folder" in the list.
- Phases 8/9: drive free-space change shown separately in the report ("may include other programs' activity").
