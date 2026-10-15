# Phase 5 — Handle-Based Cleanup Engine (Not Connected to the UI)

**Status:** Not started · **Depends on:** Phase 4
**Deletes anything?** Only synthetic files inside test sandboxes. **No button in the app can call it yet.**

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

## Tasks

### Contracts (Core)
- [ ] `ICleanupService.CleanAsync(ScanSession session, IReadOnlyCollection<CandidateId> selected, CleanupMode mode, IProgress<CleanupProgress>, CancellationToken)`
- [ ] `CleanupReport`: deleted (path, bytes), failed (path, reason, Win32 code), skipped (path, reason),
      `BytesFreed` (= sum of deleted only), start/end time, mode, cancelled flag
- [ ] `CleanupOutcome` enum: `Deleted`, `SkippedChanged`, `SkippedInUse`, `SkippedMissing`, `SkippedAccessDenied`,
      `SkippedPolicy`, `SkippedProtected`, `SkippedCancelled`, `Failed`

### Engine (Infrastructure, single file `Cleanup/HandleBasedFileDeleter.cs` + `CleanupService.cs`)
- [ ] **Pre-flight (D0):** refuse whole request if elevated, if root revalidation fails, if root identity or
      canonical path changed, or if the scan session is older than *(initial)* 30 minutes
- [ ] **D1:** only IDs from this session with status Eligible/NeedsReview; unknown IDs → `SkippedPolicy`
- [ ] **D2:** re-run `PathValidator`, protected and exclusion checks
- [ ] **D3:** `CreateFileW(DELETE | FILE_READ_ATTRIBUTES | SYNCHRONIZE, share = 0, OPEN_EXISTING, FILE_FLAG_OPEN_REPARSE_POINT)`;
      sharing violation → `SkippedInUse`, access denied → `SkippedAccessDenied`, not found → `SkippedMissing`
- [ ] **D4:** via handle: not directory, no reparse tag, allowed attributes, link count 1, identity/size/last-write
      equal to scan, final path equal to scanned final path and inside canonical root → else `SkippedChanged`
- [ ] **D5:** `SetFileInformationByHandle(FileDispositionInfo { DeleteFile = true })`; failure → `Failed` (no retry, no force)
- [ ] **D6:** close handle; count bytes from D4 only after D5 succeeded
- [ ] **D7:** audit log per file: outcome + **root-relative** path + error code; summary line
- [ ] Files processed **one at a time**; cancellation checked *between* files, never mid-file
- [ ] `CleanupMode.DryRun` → re-inspects read-only (no exclusive open, no delete) and reports "would delete / would skip"
- [ ] `ForbiddenApiTests` updated: `SetFileInformationByHandle` and `DELETE` access allowed **only** in `HandleBasedFileDeleter.cs`

### Explicitly not used
`DeleteFileW`, `File.Delete`, `Directory.Delete`, `RemoveDirectoryW`, `MoveFileEx(..., DELAY_UNTIL_REBOOT)`,
shell APIs, attribute changes, ownership/ACL changes, process termination.

## Tests (first set; the full gate is Phase 6)

| Case | Expected |
|---|---|
| Eligible synthetic file | Deleted; bytes counted |
| DryRun mode | Nothing deleted; report says "would delete" |
| Elevated (fake probe returns true) | Whole request refused; nothing deleted |
| Root identity changed | Whole request refused |
| Stale session (fake clock + 31 min) | Refused |
| ID not in session / Skipped candidate | `SkippedPolicy`; file survives |
| Cancellation before start | Nothing deleted |
| Audit log | Contains root-relative paths only; no profile path |

## Verification

- Build 0 warnings; all tests pass; every destructive test asserts sandbox `outside` files are unchanged.
- Code read-through against `SECURITY.md` D1–D7 (checklist in `TESTING.md`).
- Confirm by architecture test that the App project still cannot reach `ICleanupService`.

## Done when

- [ ] Engine implemented and tested in sandboxes only
- [ ] `SECURITY.md` §5 updated to "implemented (not enabled)"

## Risks

- **`FileDispositionInfo` semantics** (file removed at handle close) → test that the file is gone after close and still present if D5 is skipped.
- **Antivirus holding files** → shows up as `SkippedInUse`, which is the safe outcome.
