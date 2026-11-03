# Phase 11 — Security Review, Regression and Manual Verification

**Status:** In progress (2026-11-02): everything automated is done and passing; 3 manual checks are waiting for the owner · **Depends on:** Phases 1–10 · **Deletes anything?** Sandbox files only

## Goal

Check the finished application against every promise in `SECURITY.md`, simulate failures, and complete
the manual checklist before publishing.

## Tasks

### Automated
- [x] Full test run (Debug and Release); results recorded with date and counts
- [x] `dotnet list package --vulnerable --include-transitive` → none
- [x] `ForbiddenApiTests` reviewed: the allow-list contains only the deleter and the settings store (Explorer launch was removed in Phase 10). The list was extended, and `CompiledAssemblyTests` now also checks the compiled program
- [x] Release assembly check: no sandbox-mode code present
- [x] Repeat race/concurrency tests (e.g. 50 iterations) and record any flakiness: 50/50, none

### Threat model walkthrough
- [x] For each threat (T1–T21 now): name the code that mitigates it and the test that proves it (table in `TESTING.md`)
- [x] Run the `/code-review` skill; every finding fixed or shown not to be a bug
- [x] Line-by-line manual security review of the deletion path; every finding fixed
- [x] `/security-review` skill, on commit 95803fd before pushing (the first attempt failed because the local `origin/HEAD` pointer wasn't set; an earlier note here wrongly said there was no remote). Result: no finding reached the reporting threshold. One was real but low severity (6/10 in the false-positive check): see `SECURITY.md` §10

### Failure simulation
Results in `TESTING.md` → "Failure simulation". Added during the run: closing the window and killing the process mid-cleanup.

| Scenario | Expected | Status |
|---|---|---|
| Log folder unwritable | App runs; logging disabled notice | ✅ |
| Settings/history file corrupt | Defaults; warning | ✅ |
| `TEMP` set to another folder (test session) | Cleanup refused with reason | ✅ |
| Launched "Run as administrator" | Scan works; cleanup disabled with explanation | ⏳ Owner |
| 100,000 synthetic files in sandbox | Scan completes or is capped with warning; UI responsive | ✅ |
| Deep nesting / access-denied folders | Skipped, scan continues | ✅ |
| Cancel mid-scan and mid-cleanup | Clean stop; report accurate | ✅ |
| Disk nearly full (sandbox on small VHD, optional) | No crash | ⏭️ Not run (needs administrator rights) |

### Manual checklist
- [ ] Complete every item in `TESTING.md` → "Manual test checklist": 10 of 13 done; 3 need the owner (run as administrator, display scaling, Narrator)
- [x] Verify no network activity: open sockets polled during a full run (not Resource Monitor; see `TESTING.md`)
- [x] Read a log file: no file contents, no full profile paths

## Done when

- [x] Every finding fixed or written down as a known limitation in `SECURITY.md`
- [ ] Sign-off summary in `TESTING.md`: written once the owner's 3 checks are in

## Risks

- **Late discovery of a design flaw** → fix before publishing; disable the affected feature if a safe fix isn't possible.
