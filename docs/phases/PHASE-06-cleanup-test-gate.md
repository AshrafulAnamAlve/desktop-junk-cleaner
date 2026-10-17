# Phase 6 — Cleanup Test Gate

**Status:** ✅ Done (2026-10-17) — **gate PASSED** (3 justified skips) · **Depends on:** Phase 5
**Deletes anything?** Only synthetic sandbox files. **Real deletion stays disabled.**

## Goal

Prove, with automated tests in disposable sandboxes, that the cleanup engine skips everything it should
and deletes only what it should. **Phase 7 may not start until every gate item passes** (or has a
written, justified skip).

## Why (beginner explanation)

This is the safety inspection before the car leaves the factory. Each test sets up a tricky situation —
a file that changed, a folder swapped for a shortcut to somewhere else, a file in use — and checks that
SafeClean leaves it alone. After **every** destructive test we also check that files outside the test
area are byte-for-byte unchanged.

## What was built

| Area | Files | Notes |
|---|---|---|
| Gate tests | `tests/SafeClean.Infrastructure.Tests/Cleanup/CleanupGateTests.cs` | Named `Gate01…Gate24` after the gate list; many feed the engine **forged** candidates whose facts lie (path, final path, attributes, link count) to prove the handle checks don't trust the caller |
| Test helpers | `Sandbox/SpecialFiles.cs`, `TestSandbox.Snapshot(folder)` | Make a file sparse, read an 8.3 short name, wait until antivirus has let go of a new file; snapshot one folder only (so a 4 GB test file isn't hashed) |
| Engine fix | `Infrastructure/Cleanup/CleanupService.cs` | **Found in self-review:** if a progress callback or logger threw, the whole cleanup failed and the record of already-deleted files was lost. Now reporting errors stop the run but the full report is still returned (`Gate17_…`) |
| Core tests | `CleanupPolicyTests` | D4 cases "now encrypted" and "now sparse" |

## Gate results (actually run, 2026-10-17)

Every destructive test ends with `AssertOutsideUnchanged()` (SHA-256, size, dates and attributes of the
`outside` folder), and the gate class checks it again when each test is disposed.

| # | Scenario | Test(s) | Result |
|---|---|---|---|
| 1 | Valid eligible file | `CleanupServiceTests.SelectedEligibleFiles_AreDeleted_BytesCounted_FoldersKept`; `HandleBasedFileDeleterTests.MatchingFile_IsGoneAfterTheCall…` | ✅ |
| 2 | Path outside root | `Gate02_CandidateOutsideTheRoot…`; `HandleBasedFileDeleterTests.ForgedEligibleCandidate…`, `CannotBePointedAtAnotherRoot` | ✅ |
| 3 | Traversal and special forms | `Gate03_TraversalAndSpecialPathForms…` × 9 (`..`, `/`, `.\`, `\\?\` ×2, `\\.\`, stream, trailing dot) | ✅ |
| 3+ | 8.3 short-name alias | `Gate03_ShortNameAlias_IsResolvedAndRefused` | ⏭ Skipped: 8.3 names are off on drive F: (no alias exists). Same final-path check as gate 5, which passes |
| 4 | Protected folder nested in the root | `Gate04_ProtectedFolderInsideTheRoot_RefusesTheWholeCleanup`, `Gate04_ProtectedFolder_IsAlsoSkippedByTheEngineItself` | ✅ |
| 5 | Junction to outside | `Gate05_JunctionToOutside_IsNeverFollowed` (incl. a forged candidate *through* the junction with genuine identity and size); `CleanupServiceTests.RootSwappedForAJunction…` | ✅ |
| 6 | File symlink | `Gate06_FileSymbolicLink_IsNeitherDeletedNorFollowed` | ⏭ Skipped: Windows refuses to create symlinks without Developer Mode (a system setting we don't change). Links are refused by the reparse-point rule (unit-tested) and opened as themselves |
| 7 | Hard link | `Gate07_HardLinkedFile…` (existing at scan + forged "1 link"); `CleanupServiceTests.HardLinkAddedAfterTheScan…` | ✅ |
| 8 | Missing at cleanup | `CleanupServiceTests.FileDeletedAfterTheScan_IsSkippedAsMissing` | ✅ |
| 9 | Access denied (ACL) | `Gate09_AccessDenied…` — deny on file **and** parent, restored afterwards; outcome *skipped (access denied)*, error 5 | ✅ |
| 10 | Locked by another program | `Gate10_FileOpenInAnotherProgram…` × 3 sharing modes; `Gate10_DryRunCannotPredictAFileInUse…` (limitation pinned) | ✅ |
| 11 | Modified after scan | `CleanupServiceTests.FileModifiedAfterTheScan…` × 2 (size only, date only) | ✅ |
| 12 | Replaced after scan | `CleanupServiceTests.FileReplacedAfterTheScan…`; gate 23 | ✅ |
| 13 | Parent swapped for a junction | `CleanupServiceTests.ParentFolderSwappedForAJunction…` | ✅ |
| 14 | Cancel during scan | `TempScannerTests.CancellationStopsTheScanPromptly` | ✅ |
| 15 | Cancel before cleanup | `CleanupServiceTests.CancelledBeforeStart…`, `CancelledDuringCleanup…` | ✅ |
| 16 | Unexpected attributes | `Gate16_…` × 10: read-only, hidden, system, offline, sparse — present at scan *and* added after; attributes left unchanged | ✅ |
| 16+ | Encrypted (EFS) | — | ⏭ Not run on purpose: encrypting creates an EFS certificate for the Windows account (a security change outside the sandbox), and Windows Home has no EFS. Covered by unit tests (`CandidatePolicyTests` attribute allow-list, `CleanupPolicyTests` "now encrypted") |
| 17 | Partial failure | `CleanupServiceTests.MixedBatch…`; `Gate17_ProgressCallbackThatThrows_StillReturnsTheFullReport` | ✅ |
| 18 | Byte accounting | `MixedBatch…`, `CleanupReportTests.BytesFreed_CountsOnlyDeletedFiles`, gates 20 and 23 | ✅ |
| 19 | Empty root / selection | `Gate19_EmptyRoot…`, `CleanupServiceTests.EmptySelection…` | ✅ |
| 20 | Large and zero-byte | `Gate20_…`: 4,294,967,297 bytes (just over 4 GiB) and 0 bytes, exact | ✅ |
| 21 | Unicode / unusual names | `Gate21_…` × 8 (emoji, Arabic, Japanese, combining accent, right-to-left override, leading dot, leading spaces, repeated spaces); `Gate21_NamesThatLookIdentical…` ("é" vs "e + ◌́": only the selected one goes) | ✅ |
| 22 | Long paths (> 260) | `Gate22_PathLongerThan260Characters…` | ✅ |
| 23 | Concurrent changes | `Gate23_…` 50 rounds × 20 files, while a background task swaps files for look-alikes (same name, size and dates) and adds/removes others. Three runs: ≈660 deleted, ≈330 skipped because of a race, **≈550 look-alikes created mid-cleanup — every one survived**. Plus `TempScannerTests.FilesChangingDuringTheScan…` | ✅ |
| 24 | Integrity after every test | `AssertOutsideUnchanged()` in each destructive test and in `Dispose`; snapshot tests | ✅ |

**Gate outcome: PASSED.** 21 items fully passed; items 3, 6 and 16 each have one sub-case skipped with the
written reason above. Totals: 558 tests — 553 passed, 0 failed, 5 skipped (the 3 file-symlink tests, the
8.3 alias test, and the opt-in real-Temp read-only scan).

## What race tests can and cannot prove

They prove that the **specific** interleavings above are detected. They cannot prove that *no* timing
window exists, because Windows decides exact thread timing. The real guarantee comes from the design
(validate and delete through one exclusive handle, `SECURITY.md` §6); the tests confirm the code follows it.
Gate 23 repeats 50 times to increase confidence, not to prove absence of races, and checks that the
background changes really overlapped with the cleanups (otherwise it fails).

## Self-review checklist

- [x] Every D1–D7 step exists in code, in order, with a test (table in `PHASE-05` and `TESTING.md`)
- [x] No path-based delete API anywhere (`ForbiddenApiTests`: 32 patterns; the engine is the only allow-listed file)
- [x] Every Win32 failure maps to a skip/failed outcome, none to "deleted" (`OpenErrors_…` theory; failures
      while reading facts or marking the file → *failed*)
- [x] Bytes counted only after successful disposition **and** closing the handle
- [x] Logs contain no profile path or file contents (`AuditLog_UsesPathsRelativeToTheTempFolderOnly`)
- [ ] Optional `/security-review` or `/code-review` — not run; offered to the project owner

**Findings from the review:**

1. *Fixed:* a throwing progress callback or logger could lose the report (see "What was built").
2. *Documented and pinned:* Dry Run can't know whether a file will be in use or access-denied at cleanup
   time, so the preview must keep saying "would **try** to delete".
3. *Documented:* if another program holds a handle that only reads attributes, Windows removes the file
   when that handle closes. It is still certain to go: nobody can open a file that is pending deletion,
   and cancelling it needs delete access, which SafeClean's exclusive open denied to everyone else.
4. *For Phase 10:* the cleanup must use the same exclusions and minimum age as the scan (today both use
   the defaults). Added to `PHASE-10`.
5. *For Phase 7:* show "stopped by error" and "cancelled" in the result screen; show refusals with `PolicyText`.

## Mutation check (does the gate have teeth?)

Same method as Phase 5 (corrected): one planted bug at a time, full rebuild, Core + Infrastructure tests,
file restored and verified byte-for-byte. Baseline 0 failing; final run 0 failing; no sandbox folders left.

| # | Planted bug | Result |
|---|---|---|
| G1 | D4 location checks removed (rule re-check *and* final-path comparison) | Caught (3): **`Gate05`** — the forged look-alike through a junction would otherwise have been deleted — plus Core D4 cases |
| G2 | Reporting safety net removed | Caught (1): **`Gate17`** |
| G3 | "Access denied" reported as *failed* | Caught (2): **`Gate09`**, error-mapping theory |
| G4 | Refused Temp folder ignored | Didn't compile (nullable warning = error), so it proved nothing; replaced by G4a/G4b |
| G4a | Refused Temp folder not detected (compiling version) | Caught (3): **`Gate04`** (protected folder in root), `RootSwappedForAJunction`, Core root test |
| G4b | A changed Temp folder ignored by the service | Caught (2): root replaced by a same-named folder; root resolved to another folder |
| G5 | File identity not compared | Caught (3): **`Gate23`** (look-alikes with the same name, size and dates were deleted), replacement test, Core D4 case |

## Done when

- [x] All 24 gate items pass (skips justified in writing)
- [x] Results table with date and actual output recorded here and in `TESTING.md`
- [x] Gate outcome reported to the project owner before Phase 7 begins

## Risks (from the plan) → outcome

- **ACL tests are tricky** → deny on both file and parent; both restored after the test (also on failure).
- **EFS may be unavailable on Windows Home** → not run, with reason (above).
- **Antivirus touching new test files** → the 4 GB test waits until no one else has the file open before scanning.
