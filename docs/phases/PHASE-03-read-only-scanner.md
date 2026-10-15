# Phase 3 — Read-Only Temp Scanner

**Status:** Not started · **Depends on:** Phase 2 · **Deletes anything?** No
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

## Tasks

### Native interop (`src/SafeClean.Infrastructure/Native/`)
- [ ] `NativeMethods` using `LibraryImport` + `DefaultDllImportSearchPaths(System32)`:
      `CreateFileW`, `GetFileInformationByHandleEx` (`FileBasicInfo`, `FileStandardInfo`, `FileIdInfo`,
      `FileAttributeTagInfo`), `GetFinalPathNameByHandleW`, `GetVolumeInformationByHandleW`
- [ ] All handles wrapped in `SafeFileHandle` (closed automatically, even on errors)
- [ ] Win32 error codes mapped to `SkipReason` (`Inaccessible`, missing, etc.); never thrown to the UI

### File inspector
- [ ] `FileInspector.TryInspect(path)` opens with `FILE_READ_ATTRIBUTES`, share **read+write+delete**,
      `OPEN_EXISTING`, `FILE_FLAG_OPEN_REPARSE_POINT` (+ `FILE_FLAG_BACKUP_SEMANTICS` for directories only)
- [ ] Never requests write or delete access (enforced by a test that inspects the flags used)

### Root resolver (rules R1–R7)
- [ ] `TempRootResolver` — `Path.GetTempPath()` → full path → checks fixed NTFS drive → walks every parent
      checking for reparse attributes → opens directory handle → compares identity with `%LOCALAPPDATA%\Temp`
      → canonical root = OS-reported final path → applies `RootPolicy`
- [ ] Returns either an `ApprovedRoot` or a clear, user-readable refusal reason

### Scanner
- [ ] `ITempScanner` (Core) / `TempScanner` (Infrastructure): `ScanAsync(IProgress<ScanProgress>, CancellationToken)`
- [ ] Iterative (stack-based) walk, one directory level at a time, using `EnumerationOptions`
      (`RecurseSubdirectories = false`, `AttributesToSkip = 0`, `IgnoreInaccessible = true`)
- [ ] Reparse-point directories are **listed as skipped and never entered**
- [ ] Depth limit 16; safety cap on entries (initial 250,000) with a visible "scan truncated" warning
- [ ] Every file → `FileInspector` → `CandidatePolicy` → `ScanCandidate`
- [ ] Runs on a background thread; checks cancellation between entries
- [ ] Returns a `ScanSession`: session ID, root + identity, start/end time, candidates, totals per status
      (estimated bytes, clearly named `EstimatedRecoverableBytes`)

### Test sandbox helpers (`tests/SafeClean.Infrastructure.Tests/Sandbox/`)
- [ ] `TestSandbox` — creates `bin/.../TestSandbox/<guid>/root` and `.../outside`; teardown removes links
      first (without following them), verifies every path is inside the sandbox before removing
- [ ] `Junction.Create(link, target)` via `DeviceIoControl(FSCTL_SET_REPARSE_POINT)` — no admin, no shell
- [ ] `HardLink.Create` via `CreateHardLinkW`
- [ ] File symlink via `File.CreateSymbolicLink`; if Windows refuses (no Developer Mode), test is **skipped** with reason
- [ ] Helpers for attributes, timestamps (make files "8 days old"), and locked files (`FileShare.None`)
- [ ] Test-only `ApprovedRoot` factory (internal, via `InternalsVisibleTo`) that applies every root rule **except** R5 (identity with real Temp)

## Tests

| Case | Expected |
|---|---|
| Plain old file in root | Eligible, correct size/time/ID |
| File in nested folder | Eligible, correct relative path |
| Junction in root → `outside` folder | Junction listed as skipped; files in `outside` never appear |
| File symlink → `outside` file | Skipped (reparse) or test skipped if privilege missing |
| Hard-linked file | Skipped (`MultipleHardLinks`) |
| Read-only / hidden / system file | Skipped; attributes unchanged after scan |
| Recent file | Skipped (`TooRecent`) |
| Locked file (other handle open, no sharing) | Still inspectable (read-attributes) or `Inaccessible`; never modified |
| Folder with access denied (ACL) | Skipped, scan continues |
| Unicode / emoji / RTL / leading-dot names | Correct |
| Path > 260 characters | Inspected correctly |
| Depth > 16 | Deeper entries reported as `TooDeep` |
| Empty root | Empty session, no errors |
| Cancellation mid-scan | Returns promptly with `OperationCanceledException`; nothing changed |
| Files added/removed during scan | No crash; disappeared files skipped |
| Snapshot check | Hash + timestamps + attributes of every sandbox file identical before and after scanning |
| Real Temp root resolution (read-only) | Resolver accepts this PC's real Temp; **no files created or changed** |
| `TEMP` redirected (resolver given a fake env path) | Refused with reason |

## Verification

- Build 0 warnings; all tests pass; skipped tests listed with reasons in `TESTING.md`.
- Manual: a read-only scan of the real Temp from a test console/command; compare file count and
  total size roughly against Explorer's folder properties. No changes made.

## Done when

- [ ] Scanner and resolver pass all tests above
- [ ] `ForbiddenApiTests` still pass (scanner contains no delete/write access)
- [ ] `SECURITY.md` updated with any discovered behaviour (e.g. real-world reparse points in Temp)

## Risks

- **Struct layout errors in native calls** → cross-check size/attributes/time against `FileInfo` in tests.
- **Short (8.3) names from `GetTempPath`** → comparisons use OS-reported final paths and file identity, not raw strings.
- **Huge folders** → cap, progress, cancellation.
