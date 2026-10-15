# Phase 2 — Core Safety Policies (Pure Logic)

**Status:** Not started · **Depends on:** Phase 1 · **Deletes anything?** No · **Touches the disk?** No

## Goal

Write the rules that decide what is allowed — as plain C# that works on *data* only. Every rule gets
unit tests. Later phases (scanner, cleanup) must ask these rules; they may not invent their own.

## Why (beginner explanation)

If the safety logic lived inside the code that touches files, testing it would require real files and
mistakes would be dangerous. Keeping rules "pure" (input data → yes/no answer) means we can test
hundreds of tricky cases — fake paths, weird attributes, future dates — in milliseconds with zero risk.

## Scope

**In:** models, path validation, protected-location model, root containment, candidate policy, root
policy (the data-checking part of R1–R8).
**Out:** reading real files, native calls, UI.

## Tasks

### Models (`src/SafeClean.Core/Models/`)
- [ ] `FileFacts` — everything known about one file: path, OS-reported final path, attributes, size,
      creation/last-write time (UTC), volume serial, 128-bit file ID, hard-link count, is-directory
- [ ] `FileIdentity` — volume serial + file ID (used to detect replacement)
- [ ] `CandidateStatus` — `Eligible`, `NeedsReview`, `Skipped`
- [ ] `SkipReason` — `OutsideRoot`, `ReparsePoint`, `Directory`, `DisallowedAttributes`, `MultipleHardLinks`,
      `TooRecent`, `FutureTimestamp`, `InvalidPath`, `Protected`, `Excluded`, `Inaccessible`, `TooDeep`
- [ ] `CandidateCategory` — `TemporaryFiles`, `LogFiles`, `InstallerLeftovers`, `Other` (grouping only)
- [ ] `ScanCandidate` — ID, root-relative path, facts, status, reasons, category
- [ ] `ApprovedRoot` — canonical path + identity; **constructor internal** so only the resolver (and tests) can create one

### Policies (`src/SafeClean.Core/Policies/`)
- [ ] `PathValidator` — rejects: empty, relative, UNC, device paths (`\\.\`, `\\?\` input), `..`/`.` segments,
      ADS (`:` after the drive), reserved names (`CON`, `PRN`, `AUX`, `NUL`, `COM1-9`, `LPT1-9`, also with
      extensions like `NUL.txt`), trailing dot/space in any segment, invalid characters, control characters
- [ ] `RootContainment` — "strictly inside" check with trailing-separator handling
      (`C:\Temp2` is **not** inside `C:\Temp`); case-insensitive for user-facing strings; **ordinal** for OS-reported final paths
- [ ] `ProtectedLocations` — *subtrees* and *exact paths* (see `SECURITY.md` §3); built from an injected
      list of known folders so tests use synthetic paths
- [ ] `CandidatePolicy.Evaluate(facts, root, now, exclusions)` — applies C2–C9, collects **all** reasons,
      returns status; extension list for "needs review" (`.exe .dll .msi .msp .ps1 .bat .cmd .vbs .js .sys .com .scr`)
- [ ] `RootPolicy` — validates resolved-root facts (fixed NTFS volume, not reparse, identity match,
      not drive root, not protected, does not contain protected)

## Tests (`tests/SafeClean.Core.Tests/Policies/`)

| Area | Example cases |
|---|---|
| Path validation | `C:\T\a.tmp` ok; `C:\T\..\Users\x`; `C:\T\a.txt:secret`; `\\server\share\a`; `\\?\C:\T\a`; `C:\T\CON`; `C:\T\nul.txt`; `C:\T\a.tmp.`; `C:\T\a.tmp `; relative `a.tmp`; control chars; empty |
| Containment | sibling prefix `C:\Temp2`; root itself is not "inside"; mixed case; `/` vs `\`; trailing separators |
| Protected | file under Documents; root equal to profile; root containing Desktop; drive root as root |
| Candidate policy | eligible file; directory; reparse attribute; each disallowed attribute separately; link count 2; created 1 day ago but modified 30 days ago (**too recent**); future timestamp; `.exe` → NeedsReview; `.tmp` that's 1 hour old → Skipped (extension alone never decides) |
| Unicode / long | emoji, RTL, combining characters, 300+ character paths |
| Multiple reasons | a hidden, recent, hard-linked file reports all three reasons |

## Verification

- Build: 0 warnings. All tests pass.
- Architecture test still proves Core has no file-system or UI dependency.
- Manual review: each rule in `SECURITY.md` maps to at least one test (table added to `TESTING.md`).

## Done when

- [ ] Every rule R/C in `SECURITY.md` that can be expressed as pure logic has a passing test
- [ ] `SECURITY.md` updated if any rule wording changed

## Risks

- **Windows path quirks** (8.3 short names, trailing dots) → final decisions use OS-reported final paths (Phase 3), the validator is an extra layer, not the only one.
- **Case-insensitivity differences** → documented choice: ordinal for OS-produced strings, ignore-case for user-facing input.
