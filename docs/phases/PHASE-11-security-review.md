# Phase 11 — Security Review, Regression and Manual Verification

**Status:** Not started · **Depends on:** Phases 1–10 · **Deletes anything?** Sandbox files only

## Goal

Check the finished application against every promise in `SECURITY.md`, simulate failures, and complete
the manual checklist before publishing.

## Tasks

### Automated
- [ ] Full test run (Debug and Release); results recorded with date and counts
- [ ] `dotnet list package --vulnerable --include-transitive` → none
- [ ] `ForbiddenApiTests` reviewed: allow-list contains only the deleter, Explorer launch and nothing else
- [ ] Release assembly check: no sandbox-mode code present
- [ ] Repeat race/concurrency tests (e.g. 50 iterations) and record any flakiness

### Threat model walkthrough
- [ ] For each threat T1–T17: name the code that mitigates it and the test that proves it (table in `TESTING.md`)
- [ ] Run the `/security-review` and `/code-review` skills; fix or document every finding

### Failure simulation
| Scenario | Expected |
|---|---|
| Log folder unwritable | App runs; logging disabled notice |
| Settings/history file corrupt | Defaults; warning |
| `TEMP` set to another folder (test session) | Cleanup refused with reason |
| Launched "Run as administrator" | Scan works; cleanup disabled with explanation |
| 100,000 synthetic files in sandbox | Scan completes or is capped with warning; UI responsive |
| Deep nesting / access-denied folders | Skipped, scan continues |
| Cancel mid-scan and mid-cleanup | Clean stop; report accurate |
| Disk nearly full (sandbox on small VHD, optional) | No crash |

### Manual checklist
- [ ] Complete every item in `TESTING.md` → "Manual test checklist"
- [ ] Verify no network activity (Resource Monitor → Network, filter by SafeClean)
- [ ] Read a log file: no file contents, no full profile paths

## Done when

- [ ] Every finding fixed or written down as a known limitation in `SECURITY.md`
- [ ] Sign-off summary in `TESTING.md`

## Risks

- **Late discovery of a design flaw** → fix before publishing; disable the affected feature if a safe fix isn't possible.
