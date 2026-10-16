# Phase 5 — Handle-Based Cleanup Engine (Not Connected to the UI)

**Status:** ✅ Done (2026-10-16) · **Depends on:** Phase 4
**Deletes anything?** Only synthetic files inside test sandboxes. **No button in the app can call it**
(an architecture test fails if the App project references any cleanup class).

## Goal

Write the one small, isolated piece of code that is allowed to delete files, following `SECURITY.md`
steps D1–D7 exactly, with audit logging and conservative failure handling.

## Why (beginner explanation)

Ordinary deletion says "delete whatever is at this path *now*". If something swapped the file since the
scan, the wrong thing goes. SafeClean instead:

1. **Opens** the file exclusively (nobody else can touch it while we hold it),
2. **checks** through that open handle that it's the very same file we scanned (same ID, size, date, location),
3. **marks that open file** for deletion, and
4. closes it — Windows removes exactly the file we checked.

If anything looks different or fails, the file is **skipped**. We never force anything.

## What was built

| Area | Files | Notes |
|---|---|---|
| Contracts (Core) | `Interfaces/ICleanupService.cs`; `Models/CleanupReport.cs`, `CleanupEntry.cs`, `CleanupOutcome.cs`, `CleanupRefusal.cs`, `CleanupProgress.cs` | The report lists every selected ID once; `BytesFreed` counts deleted files only; a Dry Run report can't contain "deleted" and a real one can't contain "would delete" (the constructor refuses); the default outcome (0) is a skip |
| Rules (Core) | `Policies/CleanupPolicy.cs` | Pure logic, unit-tested with plain data: `CheckRequest` (mode, elevation, scan age), `CheckRoot` (re-approved and same path + identity), `CheckRecorded` (D1 + D2), `CheckCurrent` (D4) |
| Settings and text | `SafetyDefaults.MaxScanSessionAge` (30 min), `PolicyText` | Plain-English text for every outcome and refusal |
| Engine | `Infrastructure/Cleanup/HandleBasedFileDeleter.cs` | **The only code that can delete.** D3 exclusive open (`DELETE \| FILE_READ_ATTRIBUTES \| SYNCHRONIZE`, share none, open reparse point, no backup semantics → folders can't be opened); D4 through the same handle; D5 `SetFileInformationByHandle(FileDispositionInfo)`; D6 bytes counted only after the handle is closed. Repeats D1/D2 itself, so it can't be pointed at anything the rules wouldn't select |
| Service | `Infrastructure/Cleanup/CleanupService.cs`, `AttemptResult.cs` | Refuses the whole request first if needed; one file at a time; cancellation between files (never throws; files not reached are listed as "not attempted"); an unexpected error stops the run; Dry Run re-inspects read-only; D7 audit line per file + summary |
| Shared reading | `Scanning/FileInspector.ReadFacts` | The same code reads facts for the scanner and, through the exclusive handle, for the engine |
| Native | `Native/NativeMethods.cs` | Error codes only (access denied, sharing/lock violation, delete pending); the delete access right lives in the engine file |
| Guardrails | `ForbiddenApiTests`, `ArchitectureTests` | Delete right / disposition / `SetFileInformationByHandle` allowed only in the engine file; delete-on-close, `NtSetInformationFile`, move/replace forbidden everywhere; only `CleanupService` calls the engine; the App can't reference any of it |
| Tests | `CleanupPolicyTests`, `CleanupReportTests` (Core); `HandleBasedFileDeleterTests`, `CleanupServiceTests`, `CleanupTestDoubles` (Infrastructure) | 112 new tests; every destructive test runs on synthetic sandbox files and checks the `outside` folder is unchanged |

## Planned tests → result

| Case (from the plan) | Test | Result |
|---|---|---|
| Eligible synthetic file → deleted, bytes counted | `SelectedEligibleFiles_AreDeleted_BytesCounted_FoldersKept` | ✅ |
| Dry Run → nothing deleted, "would delete" | `DryRun_ChangesNothing_AndReportsWhatWouldBeDeleted` (+ snapshot) | ✅ |
| Elevated (fake probe) → whole request refused | `Elevated_RefusesTheWholeRequest` (Dry Run and Real) | ✅ |
| Root identity changed → refused | `RootReplacedByANewFolderWithTheSameName_IsRefused`, `CleanupResolvingADifferentFolder_IsRefused`, `RootSwappedForAJunction_IsRefused_AndTheTargetIsUntouched` | ✅ |
| Stale session (+31 min) → refused | `OldOrFutureScans_AreRefused` (31 min and −1 min refused, 29 min accepted) | ✅ |
| ID not in session / skipped candidate → `SkippedPolicy`, file survives | `UnknownIds_AndSkippedEntries_AreNeverActedOn` | ✅ |
| Cancellation before start → nothing deleted | `CancelledBeforeStart_DeletesNothing` (+ `CancelledDuringCleanup_StopsBetweenFiles…`) | ✅ |
| Audit log: root-relative paths only | `AuditLog_UsesPathsRelativeToTheTempFolderOnly` | ✅ |
| Risk: disposition semantics | `MatchingFile_IsGoneAfterTheCall…`; `FileThatFailsTheHandleCheck_IsLeftInPlace_EvenThoughItWasOpenedExclusively` | ✅ |

Also done early (natural for D3/D4, and listed again in the Phase 6 gate): missing, in use, modified
(size only / date only), replaced with identical content and dates, parent folder swapped for a junction,
hard link added after the scan, made read-only after the scan, mixed batch with exact byte accounting.

## Changes from the original plan (and why)

| Change | Reason |
|---|---|
| The D1/D2/D4 decisions live in Core (`CleanupPolicy`), not in the engine | Pure logic can be unit-tested with plain data, like every other rule; the engine only gathers facts and acts |
| Re-checks are layered: the engine repeats D1/D2; D4 also requires "selectable now" | Found during self-review: D4 compared the status *then* with the status *now*, so a skipped file would have counted as "unchanged" if D1 were ever bypassed. Now each layer stands on its own (2 tests added) |
| D4 also compares creation time and attributes | Stricter: any difference means skip |
| Scan age measured from when the scan **started**; a scan "from the future" is refused | Stricter; a clock change makes the age untrustworthy |
| Elevation refuses Dry Run requests too | "Cleanup is disabled while elevated" is simpler and safer; the screen's own preview (`DryRunPlanner`) is unaffected |
| Unknown mode values are refused | An unexpected value must never be treated as Real |
| Cancellation returns a report instead of throwing | Files already deleted must always be reported |
| An unexpected error stops the run | Carrying on in an unknown state is riskier than stopping |
| New outcome `WouldDelete`; error mapping in `AttemptResult.cs` | Dry Run reports "would delete" explicitly; the mapping is shared by both modes and can't delete anything |

## Verification results (actually run, 2026-10-16)

| Check | Result |
|---|---|
| `dotnet build SafeClean.sln -c Debug` | ✅ 0 warnings, 0 errors |
| `dotnet test --solution SafeClean.sln -c Debug` | ✅ 511 total: 508 passed, 0 failed, 3 skipped (Core 354; Infrastructure 105 + the same 3 skips as before; App 49) |
| First test run | ✅ 509/509 non-skipped passed on the first run; 2 tests were then added with the layering fix above |
| Mutation check (12 planted bugs, one at a time) | ✅ 11/12 caught, see the table below; the 12th is redundancy, not a gap |
| Sandbox clean-up | ✅ no sandbox folders left behind after the runs |
| UI can't reach the engine | ✅ `App_HasNoAccessToCleanupCode_UntilPhase7`, `OnlyTheCleanupService_CallsTheDeletionEngine` |
| Real Temp folder | Not touched by any test. Nothing outside the test sandboxes was deleted or changed |
| App run by the project owner after the phase | ✅ Read-only scan of the real Temp folder in 8.7 s (1,998 safe, 959 need review, 16,558 skipped); the log shows no cleanup activity at all |

### Mutation check (how we know the tests have teeth)

A script planted one bug at a time, rebuilt **from scratch**, ran the Core and Infrastructure tests, then
restored the file and verified it byte-for-byte (SHA-256). Baseline before: 0 failing; final run after: 0 failing.

| # | Planted bug | Result |
|---|---|---|
| M1 | D4 ignores the file's identity | Caught (2): `AnyDifferenceFromTheScan…`, `FileReplacedAfterTheScan…` |
| M2 | Engine skips the D4 check | Caught (8): modified, replaced, hard link, junction swap, read-only, mixed batch… |
| M3 | Dry Run calls the engine | Caught (3): both Dry Run tests, selection-order test |
| M4 | Skipped files' bytes counted as freed | Caught (1): `BytesFreed_CountsOnlyDeletedFiles` |
| M5 | Elevation ignored | Caught (4): Core and sandbox elevation tests |
| M6 | Root identity not compared | Caught (2): same path but different folder (Core and sandbox) |
| M7 | Cancellation ignored | Caught (2): before start, between files |
| M8 | Scan age not limited | Caught (2) |
| M9 | Exclusive open shares everything | Caught (3): access pin, file in use, mixed batch |
| M10 | D1 "selectable" check removed | **Not caught — by design:** D2 independently refuses the same files ("selectable now and same status"). Like the scanner's link layers in Phase 3 |
| M11 | D1 **and** D2 "selectable" checks removed | Caught (2): skipped candidates acted on |
| M12 | Engine skips its own D1/D2 re-check | Caught (2): wrong root, forged candidate outside the root (D4 still kept the files safe; the outcome type revealed it) |

*Lesson re-learned:* the first attempt reused binaries containing earlier planted bugs (restored files keep
their old timestamps), so its results were discarded. The corrected script rebuilds with `--no-incremental`
for every mutation and gives restored files a fresh timestamp.

## Done when

- [x] Engine implemented and tested in sandboxes only
- [x] `SECURITY.md` §5 updated to "implemented (not enabled)"

## Risks (from the plan) → outcome

- **`FileDispositionInfo` semantics** (file removed when the handle closes) → tested: the file is gone when
  `TryDelete` returns, and a file that fails D4 after the exclusive open is still readable (not stuck "delete pending").
- **Antivirus holding files** → shows up as *skipped (in use)*, the safe outcome (tested with a test-held handle).

## Left for Phase 6 (the gate)

Sandbox tests still to add for the gate list in `TESTING.md`: traversal strings through the service (#3),
a protected folder nested in the root (#4), file symlink (#6, skipped without Developer Mode), ACL "access
denied" (#9), hidden/system/sparse/encrypted attributes (#16), large and zero-byte files (#20), Unicode
names (#21), long paths (#22), concurrent changes repeated many times (#23), plus the gate table with
actual results and a code review of the engine.
