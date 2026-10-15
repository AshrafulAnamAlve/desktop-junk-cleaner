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
| `ForbiddenApiTests` | 27 | 25 forbidden-API patterns (path-based deletion, shell/process launching, process killing, memory trimming, registry, networking) are absent from `src/`; the scanner really sees the source files; every pattern catches known-bad sample code. The deletion engine will be allow-listed in Phase 5 (one file only). |
| `FileLoggerTests` | 7 | Format, profile-path redaction (case-insensitive), exception details, daily files, unusable folder doesn't crash, logging after dispose ignored, default log folder is outside Temp. |

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
