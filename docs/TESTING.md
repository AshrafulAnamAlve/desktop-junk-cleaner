# SafeClean Testing

## Commands

```powershell
dotnet build SafeClean.sln -c Debug               # build everything (warnings are errors)
dotnet test --solution SafeClean.sln -c Debug     # run all automated tests
dotnet run --project src/SafeClean.App            # start the app
dotnet list SafeClean.sln package --vulnerable --include-transitive   # dependency audit
```

Tests run on **Microsoft.Testing.Platform** (enabled in `global.json`), which xUnit v3 supports natively.
That is why the command uses `--solution` and why no `Microsoft.NET.Test.Sdk` package is needed.

## Ground rules

- **Destructive tests never touch the real Temp folder, personal files or Windows folders.**
  File-system tests create a disposable sandbox under the test project's own output folder
  (`bin/.../TestSandbox/<random id>`), create synthetic files there, and delete only that sandbox.
- Junction and link targets in tests always point to *other sandbox folders*, never to real data, so
  even a bug cannot reach personal files.
- Tests that need a Windows privilege the machine may not grant (e.g. creating file symlinks without
  Developer Mode) are reported as **skipped**, never silently passed.
- Tests that only *read* the real Temp location (root resolution) must not create, change or delete
  anything there.

## Guardrail tests (implemented in Phase 1)

| Test | Count | What it protects |
|---|---|---|
| `SafetyDefaultsTests` | 4 | Uninitialised mode = Dry Run; app starts in Dry Run; min age ≥ 7 days; depth limit bounded. |
| `ArchitectureTests` (Core) | 3 | Core references no UI/Infrastructure/App assembly, has no project/package references, and contains no file-system calls. |
| `ArchitectureTests` (Infrastructure) | 1 | Infrastructure references no UI or App assembly. |
| `ForbiddenApiTests` | 27 | 25 forbidden-API patterns (path-based deletion, shell/process launching, process killing, memory trimming, registry, networking) are absent from `src/`; the scanner really sees the source files; every pattern catches known-bad sample code. *Since Phase 5:* 7 more patterns, and the deletion engine is the only allow-listed file (see "Cleanup engine tests"). |
| `FileLoggerTests` | 7 | Format, profile-path redaction (case-insensitive), exception details, daily files, unusable folder doesn't crash, logging after dispose ignored, default log folder is outside Temp. |
| `ArchitectureTests` (added later) | 3 | `unsafe` only in `Infrastructure/Native` (Phase 3); view models use Core interfaces only; the App has no access to cleanup code until Phase 7 (Phase 4). |

## Policy tests (implemented in Phase 2)

All in `tests/SafeClean.Core.Tests/Policies/`, using synthetic paths only (no disk access):

| Test class | What it covers |
|---|---|
| `PathValidatorTests` | ≈70 accepted/rejected paths: traversal, streams, device/network prefixes, reserved names, trailing dot/space, invalid and control characters, lone surrogates, Unicode, long paths |
| `WindowsPathTests` | "Strictly inside" (sibling-prefix trap), case handling, culture comparisons refused, relative path, depth, extension |
| `ProtectedLocationsTests` | Subtrees, exact paths, drive roots, root conflicts, input normalisation |
| `RootPolicyTests` | Every root rule on its own, dangerous Temp locations, all problems reported together, unknown identity |
| `CandidatePolicyTests` | Every candidate rule on its own, boundaries (exactly 7 days, depth 16), redirection, exclusions, review types, Unicode, long paths, categories |
| `PolicyTextTests` | Every status/reason/problem/category has user-facing text; default status is Skipped |

The full rule → test mapping is in `docs/phases/PHASE-02-core-safety-policies.md`.

**Mutation check (how we know the tests have teeth):** a script plants one deliberate bug at a time
in the rules, runs the tests, and restores the file. Every planted bug must make at least one test fail.
*Lesson learned:* restored files keep their old timestamps, so afterwards run
`dotnet build SafeClean.sln --no-incremental` — otherwise the build may reuse the mutated binary.
This applies **between** mutations too (Phase 5): build with `--no-incremental` for every planted bug,
give restored files a fresh timestamp, and start with a clean baseline run (0 failing).

## Scanner tests (implemented in Phase 3)

All in `tests/SafeClean.Infrastructure.Tests/` and run against **sandbox folders only**, except the two
marked "real", which only *read*:

| Test class | What it covers |
|---|---|
| `FileInspectorTests` | Only the read-attributes right is requested; facts match .NET's; identities differ/change on replacement; folders report NTFS; missing files; invalid paths never reach Windows; locked files still inspectable and unchanged; > 260-character paths; junctions, hard links and symlinks inspected as themselves; impossible file times |
| `RootResolutionTests` | **Real** Temp folder approved (read-only); TEMP redirected to another folder refused; sandbox passes the same rules; junction root and junction parent refused; missing root refused; this PC's protected list covers Windows, Program Files, Documents, Desktop, Downloads, the profile |
| `TempScannerTests` | Empty root; eligible files with correct facts and totals; too recent; needs review; junction listed but never entered; symlink skipped; hard link skipped; read-only/hidden/system skipped and unchanged; locked file; folder without permission skipped while scan continues; Unicode names; long paths; depth limit; cancellation (and nothing changed); progress; files changing during the scan; **snapshot proves nothing on disk changed**; refused root |
| `ExtendedPathTests` | `\\?\` added and removed correctly; network results left for the validator to reject |
| `RealTempReadOnlyScanTests` (Explicit) | **Real**, read-only scan printing totals only |

Run the real, read-only scan on demand:

```powershell
dotnet test --project tests/SafeClean.Infrastructure.Tests/SafeClean.Infrastructure.Tests.csproj -c Debug --output Detailed --explicit only --filter-class "SafeClean.Infrastructure.Tests.Scanning.RealTempReadOnlyScanTests"
```

**Expected skips:** the two file-symlink tests are skipped on PCs without Developer Mode (Windows refuses to
create symlinks: "A required privilege is not held by the client"). The skip message says so explicitly.

## Screen-logic tests (implemented in Phase 4)

`tests/SafeClean.App.Tests/` uses a **fake scanner** and synthetic results built through the real Core
policies — no disk access, no windows shown:

| Test class | What it covers |
|---|---|
| `JunkCleanerViewModelTests` | Intro state; nothing selected after a scan; largest-first order; "select all safe" excludes review/skipped; skipped can never be selected (click, category, select-all); review files selectable by hand; category tick selects only safe files; tri-state dash; review-only category can't be bulk-selected; "All files" entry; Dry Run preview text and breakdown; old preview cleared on selection change; preview needs a selection; new scan clears selection; selected IDs belong to the session; status/category/search filters; details and reasons; refused root; cancellation without partial results; scan failure handled; no double scans; progress text; empty Temp; disguised names revealed |
| `PresentationTests` | Size formatting, revealing hidden/direction characters (and leaving emoji alone), invalid dates, singular/plural, commands refuse to run when disabled |

Core additions: `DryRunPlannerTests` (unknown/skipped IDs ignored, duplicates counted once) and two
architecture guards (view models don't use Infrastructure; the App can't reach cleanup code before Phase 7).

**Note about real-Temp numbers:** the Temp folder changes on its own. On this PC Windows Storage Sense is
enabled and removed several thousand old Temp files between the Phase 3 and Phase 4 scans. Always compare
scan numbers with an independent count taken at the same time, not with an older scan.

## Cleanup engine tests (implemented in Phase 5)

**Every file these tests delete is a synthetic file inside a sandbox under the test project's `bin` folder.**
Each sandbox has a `root` (scanned and cleaned) and an `outside` folder with stand-ins for personal files;
destructive tests assert that `outside` is byte-for-byte unchanged (SHA-256, size, dates, attributes).

| Test class | What it covers |
|---|---|
| `CleanupPolicyTests` (Core, plain data) | Whole request: Dry Run/Real accepted; elevated refused in both modes; unknown mode values refused; scan age at the limit accepted, past it or "from the future" refused. Root: same accepted; now failing → refused; other path or same path with a new identity → changed. D1/D2: eligible and needs-review pass; skipped, no facts, path disagreeing with facts refused; forged "eligible" verdicts for a personal file (→ protected) or a file outside the root refused; folder protected or excluded since the scan; different root; stricter age. D4: unchanged passes; 15 kinds of difference (identity, unknown identity, size, both dates, attributes added/removed, final path outside/elsewhere, path, hard link, delete pending, reparse point, directory, modified just now) all → changed; a skipped candidate never passes D4 even if nothing changed |
| `CleanupReportTests` (Core) | Bytes freed = deleted files only (even if other entries carry sizes); counts per outcome; a Dry Run report can't contain "deleted", a real one can't contain "would delete"; refusal has no entries |
| `HandleBasedFileDeleterTests` | Exact access right (delete + read attributes + synchronize), share mode none, no backup semantics; disposition structure is 1 byte; a matching file is gone after the call and its size returned; a file that fails D4 **after** being opened exclusively is left intact (not stuck "delete pending"); the engine can't be pointed at another root or at a forged candidate outside the root; every Windows open error maps to a skip/failure, never a deletion |
| `CleanupServiceTests` | Selected files deleted, bytes counted, folders kept; unselected files untouched (incl. a needs-review file deleted only when chosen); Dry Run changes nothing and reports what it would do, including changed and missing files; whole request refused when elevated, for an unknown mode, when the root was replaced by a same-named folder, resolved to a different folder, or swapped for a junction (target untouched); scans older than 30 minutes or from the future refused, 29 minutes accepted; unknown IDs, skipped entries and a junction never acted on; each ID reported once in selection order; empty selection; cancelled before start (nothing deleted) and between files (first file deleted, rest "not attempted"); missing after scan; in use (error 32); modified (size only / date only); replaced with identical content and dates (new identity); parent folder swapped for a junction to an identical look-alike (look-alike survives); hard link added after scan (both names survive); made read-only after scan (attribute left alone); mixed batch with exact outcomes and bytes; audit log has Temp-relative paths only |

Guardrails added: `ForbiddenApiTests` allows the delete access right (by name or number),
`SetFileInformationByHandle` and the disposition structure **only** in `HandleBasedFileDeleter.cs`
(and checks the allowance is really used there), and forbids delete-on-close, `NtSetInformationFile`
and moving/replacing files everywhere. `ArchitectureTests`: only `CleanupService` may call the engine;
the App can't reference `ICleanupService`, `CleanupService` or the engine until Phase 7.

### D1–D7 read-through checklist (Phase 5)

| Step | Where in the code | Done |
|---|---|---|
| Before any cleanup: mode, elevation, scan age | `CleanupPolicy.CheckRequest`, called first in `CleanupService.Clean` | ✅ |
| Before any cleanup: root re-approved, same path and identity | `ITempRootResolver.Resolve` + `CleanupPolicy.CheckRoot` | ✅ |
| D1 only IDs from the session, selectable | `CleanupService.Attempt` (unknown ID) + `CleanupPolicy.CheckRecorded` | ✅ |
| D2 path/protected/exclusion rules re-run | `CleanupPolicy.CheckRecorded` (full `CandidatePolicy` with current protected list); repeated inside the engine | ✅ |
| D3 exclusive open, share none, open reparse point | `HandleBasedFileDeleter.TryDelete` → `CreateFileW`; errors via `AttemptResult.FromOpenError` | ✅ |
| D4 checks through the same handle | `FileInspector.ReadFacts(handle)` + `CleanupPolicy.CheckCurrent` | ✅ |
| D5 disposition on that handle, no retry | `SetFileInformationByHandle(FileDispositionInfo)`; failure → `Failed` | ✅ |
| D6 close, then count bytes | size returned after the `using` block closes the handle | ✅ |
| D7 audit line per file, Temp-relative | `CleanupService.LogEntry` + summary line | ✅ |
| One file at a time; cancel between files | `foreach` in `CleanupService.Clean`; token checked before each file | ✅ |

## Gate before enabling real deletion (Phase 6)

Every item must have a passing automated test (or a documented, justified skip) before Phase 7:

1. Valid eligible file inside the sandbox root is deleted; bytes counted.
2. Path outside the approved root is rejected.
3. Path traversal attempts (`..`, mixed separators, `\\?\` forms) are rejected.
4. Protected directories are rejected even when nested under the root by construction.
5. Junction inside root pointing outside is not followed; target files survive.
6. File symlink inside root is not a candidate; target survives (skipped if privilege unavailable).
7. Hard-linked file is skipped; other link survives.
8. Missing file at cleanup time → skipped, not counted.
9. Inaccessible file (ACL denies delete) → skipped, not counted.
10. Locked file (opened by the test without delete sharing) → skipped, survives.
11. File modified after scan (size or timestamp) → skipped, survives.
12. File replaced after scan (same name, new file ID) → skipped, replacement survives.
13. Parent folder swapped for a junction after scan → skipped, target survives.
14. Cancellation during scanning returns promptly with no side effects.
15. Cancellation before cleanup deletes nothing.
16. Unexpected attributes (read-only, hidden, system, offline, sparse, encrypted where possible) → skipped, attributes unchanged.
17. Partial failure: mixed batch reports deleted/failed/skipped correctly.
18. Recovered bytes equal the sum of successfully deleted file sizes only.
19. Empty scan result handled.
20. Very large (sparse-free, multi-GB not required: size reported via length) and zero-byte files.
21. Unicode and unusual names (spaces, emoji, RTL characters, leading dots).
22. Long paths (> 260 characters).
23. Concurrent changes during scan (files created/deleted while enumerating) do not crash or mis-classify.
24. After every destructive test: assert that all out-of-scope and protected sandbox files still exist
    with unchanged content.

**What race tests can and cannot prove:** they show that the specific interleavings we simulate
(change → then cleanup) are detected. They cannot prove the absence of every possible timing window,
because the OS scheduler decides exact timing. The race-safety argument rests on the handle-based design
in `SECURITY.md` §6; tests confirm the implementation follows it.

## Results log

| Date | Phase | Command | Result |
|---|---|---|---|
| 2026-10-15 | 1 | `dotnet build SafeClean.sln -c Debug` | ✅ 0 warnings, 0 errors |
| 2026-10-15 | 1 | `dotnet test --solution SafeClean.sln -c Debug` | ✅ 42 passed, 0 failed, 0 skipped (Core 34, Infrastructure 8) |
| 2026-10-15 | 1 | `dotnet list SafeClean.sln package --vulnerable --include-transitive` | ✅ No vulnerable packages |
| 2026-10-15 | 1 | App launch + UI Automation walk of all 5 pages + screenshots | ✅ All pages reachable by accessible name; exit code 0; 2 layout issues found, fixed, re-verified |
| 2026-10-15 | 1 | Keyboard (Tab, ↓↓) posted to the app window only | ✅ Navigation works; focus-ring visibility needs a manual check (inactive window) |
| 2026-10-15 | 1 | Log file inspection | ✅ Startup/exit lines present; real profile path absent |
| 2026-10-15 | 2 | `dotnet build SafeClean.sln -c Debug` | ✅ 0 warnings, 0 errors |
| 2026-10-15 | 2 | `dotnet test --solution SafeClean.sln -c Debug` (first run) | ❌ 272/274 — two test-side problems (over-broad "cmd" pattern; lone-surrogate data altered by attribute encoding), both fixed |
| 2026-10-15 | 2 | `dotnet test --solution SafeClean.sln -c Debug` | ✅ 281 passed, 0 failed, 0 skipped (Core 273, Infrastructure 8) |
| 2026-10-15 | 2 | Mutation check: 4 deliberate bugs planted one at a time in Core rules | ✅ 4/4 caught (5, 3, 3 and 1 failing tests); sources verified restored |
| 2026-10-16 | 3 | `dotnet build SafeClean.sln -c Debug` | ✅ 0 warnings, 0 errors |
| 2026-10-16 | 3 | `dotnet test --solution SafeClean.sln -c Debug` | ✅ 341 passed, 0 failed, 3 skipped (Core 281; Infrastructure 60 + 3 skipped) |
| 2026-10-16 | 3 | Explicit read-only scan of the real Temp folder | ✅ 10.6 s, 24,896 entries, ≈4.6 GB estimated recoverable, 0 links, 0 vanished |
| 2026-10-16 | 3 | Mutation check on scanner/inspector | ✅ Inspector following links caught (5 tests); both link layers removed caught; layer 1 alone shown to be backed up by layer 2 |
| 2026-10-16 | 4 | `dotnet build SafeClean.sln -c Debug` | ✅ 0 warnings, 0 errors (after fixing 3 missing-`using` errors in the new test project) |
| 2026-10-16 | 4 | `dotnet test --solution SafeClean.sln -c Debug` (first run) | ❌ 397/399 — impossible dates shown as 12/31/9999 (code fixed) and one test reusing a session (test fixed) |
| 2026-10-16 | 4 | `dotnet test --solution SafeClean.sln -c Debug` | ✅ 396 passed, 0 failed, 3 skipped (Core 287; Infrastructure 60 + 3 skipped; App 49) |
| 2026-10-16 | 4 | App walkthrough via UI Automation + screenshots | ✅ Real read-only scan (≈19,280 entries, ~10–13 s), category tick, Dry Run preview, details, Skipped filter; one layout problem found and fixed |
| 2026-10-16 | 5 | `dotnet build SafeClean.sln -c Debug` | ✅ 0 warnings, 0 errors |
| 2026-10-16 | 5 | `dotnet test --solution SafeClean.sln -c Debug` (first run) | ✅ 506 passed, 0 failed, 3 skipped — all new tests passed first time |
| 2026-10-16 | 5 | `dotnet test --solution SafeClean.sln -c Debug` (after the layering fix + 2 tests) | ✅ 508 passed, 0 failed, 3 skipped (Core 354; Infrastructure 105 + 3 skipped as before; App 49); no sandbox folders left behind |
| 2026-10-16 | 5 | Mutation check, attempt 1 | ❌ Discarded: binaries from earlier planted bugs were reused (restored files keep old timestamps) |
| 2026-10-16 | 5 | Mutation check, attempt 2 (full rebuild per mutation, baseline 0 failing) | ✅ 11/12 caught; M10 survives by design (a second layer refuses the same files); final run 0 failing; files restored byte-for-byte |
| 2026-10-16 | 5 | App run by the project owner | ✅ Read-only scan in 8.7 s (1,998 safe / 959 review / 16,558 skipped); no cleanup lines in the log |

## Manual test checklist (to be completed in Phase 11)

- [ ] Fresh launch shows Dry Run mode and performs no scan or cleanup automatically.
- [ ] Keyboard only: Tab/arrow keys reach every navigation item and button, and the blue focus ring is visible on each.
- [ ] Window resizes down to minimum size without clipped controls.
- [ ] Launch elevated ("Run as administrator"): cleanup controls disabled with explanation.
- [ ] Set `TEMP` to another folder for a test session: app refuses cleanup and explains.
- [ ] Scan results match a manual look at a few files (size, date).
- [ ] Cleanup report numbers match deleted files; skipped files still exist.
- [ ] Log file contains no file contents and no full user paths.
- [ ] No network traffic (e.g. checked with Windows Resource Monitor).
