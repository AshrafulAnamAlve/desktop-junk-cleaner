# Phase 3 — Read-Only Temp Scanner

**Status:** ✅ Done (2026-10-16) · **Depends on:** Phase 2 · **Deletes anything?** No
**Touches the disk?** Reads only. The real Temp folder is only ever *read*.

## Goal

Find files in the current user's Temp folder and describe them accurately, without changing anything.
Each file is classified by the Phase 2 policy as Eligible, Needs review or Skipped (with reasons).

## Why (beginner explanation)

- **Reparse points** are special file-system entries that redirect somewhere else. A *junction* is a
  folder that is really a door to another folder (maybe your Documents). A cleaner that walks through
  such a door could delete the wrong things. The scanner **never walks through** them.
- **Handles**: opening a file gives a "handle" — a ticket to that exact file. Asking Windows questions
  through a handle (size, ID, real location) gives answers about *that* file, even if names change.
  The scanner opens files only with "read attributes" permission and lets everyone else keep using them.

## What was built

### Core additions (`src/SafeClean.Core/`)
| Type | Purpose |
|---|---|
| `CandidateId`, `ScanCandidate` | One listed entry (ID within its session, path, facts or none, decision) |
| `ScanSummary` | Counts and bytes per status; `EstimatedRecoverableBytes` (clearly an estimate) |
| `ScanSession`, `ScanResult`, `ScanProgress`, `RootResolution` | Scan output, refusal, progress, root decision + protected list |
| `ITempScanner`, `ITempRootResolver` | Interfaces the UI will use (Phase 4) |
| `CandidatePolicy.Skip(...)` | Verdict for entries that couldn't be inspected or weren't entered |
| `SafetyDefaults.MaxScanEntries` | 250,000-entry safety cap |

### Infrastructure (`src/SafeClean.Infrastructure/`)
| File | Purpose |
|---|---|
| `Native/NativeMethods.cs` | `CreateFileW`, `GetFileInformationByHandleEx`, `GetFinalPathNameByHandleW`, `GetVolumeInformationByHandleW`, `SHGetKnownFolderPath` — `LibraryImport`, System32 only, **read-attributes right only** |
| `Native/HandleQueries.cs` | Safe wrappers (buffers, final path, file system name, known folders, FILETIME conversion) |
| `Native/ExtendedPath.cs` | Adds/removes the `\\?\` prefix (long paths; network results left for the validator to reject) |
| `Scanning/FileInspector.cs` | Opens one file/folder read-only (`FILE_READ_ATTRIBUTES`, full sharing, `FILE_FLAG_OPEN_REPARSE_POINT`) and returns `FileFacts` |
| `Scanning/RootFactsReader.cs` | Gathers `RootFacts` (used for the real Temp folder *and* sandboxes) |
| `Scanning/KnownProtectedLocations.cs` | Protected list from this PC's real folders (special folders, Downloads & co. via known-folder IDs, OneDrive, Recycle Bin, System Volume Information, profile/AppData exact paths); non-local entries dropped and counted in the log |
| `Scanning/TempRootResolver.cs` | `Path.GetTempPath()` vs `%LOCALAPPDATA%\Temp` → `RootPolicy` |
| `Scanning/TempScanner.cs` | The walk (details below) |

### How the scanner walks (defence in depth)
1. Folders are listed **one level at a time**; entries marked as links are **never queued** for entering.
2. Each folder is **re-inspected just before it is listed**; if it became a link or its real location changed, it is reported as skipped instead.
3. Every file (and every link) is opened **as itself** and judged by `CandidatePolicy`, which requires the
   OS-reported location to equal the found location exactly — so even a folder swapped mid-scan can only
   produce *skipped* entries.
4. Depth limit 16, 250,000-entry cap, cancellation checked between entries, runs on a background thread.
5. Unreadable folders are listed as skipped (`Inaccessible`); entries that disappear mid-scan are counted, not errors.

### Test tools (`tests/SafeClean.Infrastructure.Tests/Sandbox/`)
`TestSandbox` (create aged files, snapshot with SHA-256), `FileSystemLinks` (junctions via
`DeviceIoControl`, hard links via `CreateHardLinkW`, symlinks or skip), `SandboxRootResolver`
(same `RootFactsReader` + `RootPolicy`, expected identity = the sandbox itself), `SyncProgress`.

## Changes from the original plan (and why)

| Change | Reason |
|---|---|
| No test-only root factory | Decided in Phase 2: sandbox passes the real rules with its own identity as "expected" |
| `\\?\` prefix always used when opening | Windows' long-path setting is off on this PC; the prefix makes > 260-character paths work (verified by tests) |
| `unsafe` allowed in Infrastructure (`AllowUnsafeBlocks`) | Required by generated native calls; a new test restricts `unsafe` to `Native/` |
| Real-Temp scan is an **Explicit** test | Runs only on request (`--explicit only`), prints totals only |
| DI registration of the scanner moved to Phase 4 | Nothing in the UI uses it yet |

## Verification results (actually run, 2026-10-16)

| Check | Result |
|---|---|
| `dotnet build SafeClean.sln -c Debug` | ✅ 0 warnings, 0 errors |
| `dotnet test --solution SafeClean.sln -c Debug` | ✅ 341 passed, 0 failed, 3 skipped (Core 281 passed; Infrastructure 60 passed + 3 skipped) — 60 new test cases this phase |
| Skipped tests | Real-Temp scan (Explicit, run separately below); 2 file-symlink tests — Windows refuses symlink creation without Developer Mode ("A required privilege is not held by the client"). Folder links (junctions) are fully tested and file symlinks are covered by the reparse-point rule tests |
| Snapshot test | ✅ SHA-256, sizes, dates and attributes of every sandbox file identical before and after scanning |
| Sandbox cleanup | ✅ 0 entries left in `TestSandbox` |
| Read-only scan of the **real** Temp folder (Explicit test) | ✅ 10.6 s; 7,135 folders; 24,896 entries; Eligible 5,127 files / 3,385.8 MB; Needs review 3,863 / 1,248.4 MB; Skipped 15,906 / 2,056.3 MB (TooRecent 15,893, DisallowedAttributes 421, Inaccessible 2); estimated recoverable 4,634.2 MB; 0 links, 0 vanished, not truncated |
| Mutation check | ✅ Inspector following links → 5 tests fail; both "don't enter links" layers removed → caught. Removing only layer 1 is (correctly) still blocked by layer 2. Sources verified restored, then `--no-incremental` rebuild |

## Done when

- [x] Scanner and resolver pass all tests above
- [x] `ForbiddenApiTests` still pass (scanner contains no delete/write access)
- [x] `SECURITY.md` updated with the implemented scanning behaviour
