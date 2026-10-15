# Phase 6 — Cleanup Test Gate

**Status:** Not started · **Depends on:** Phase 5
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

## Gate tests (matches `TESTING.md`)

| # | Scenario | How the test creates it | Pass condition |
|---|---|---|---|
| 1 | Valid eligible file | Synthetic file, timestamps set 8 days back | Deleted; bytes counted |
| 2 | Path outside root | Candidate with path in `outside` (constructed) | Skipped; file survives |
| 3 | Traversal | `root\..\outside\f`, mixed `/`, `\\?\` prefix | Rejected |
| 4 | Protected folder nested by construction | Test protected list includes `root\Protected` | Skipped |
| 5 | Junction to outside | Junction `root\j` → `outside` | Never followed; outside files survive |
| 6 | File symlink | `root\s.tmp` → `outside\target` | Skipped; target survives (or test skipped: no privilege) |
| 7 | Hard link | `root\h.tmp` linked with `outside\h.tmp` | Skipped; both survive |
| 8 | Missing at cleanup | Scan, then test deletes the file | `SkippedMissing`; 0 bytes |
| 9 | Access denied | ACL deny `DELETE` on file and `FILE_DELETE_CHILD` on parent | `SkippedAccessDenied`; survives; ACL restored in teardown |
| 10 | Locked | Test holds `FileStream` with `FileShare.None` | `SkippedInUse`; survives |
| 11 | Modified after scan | Append bytes / change timestamp | `SkippedChanged`; survives |
| 12 | Replaced after scan | Delete + recreate same name, same size, same timestamps | `SkippedChanged` (new file ID); replacement survives |
| 13 | Parent swapped for junction | Scan `root\sub\f`; rename `sub` away; junction `root\sub` → `outside\sub` containing identical `f` | Skipped; `outside\sub\f` survives |
| 14 | Cancel during scan | Large synthetic tree, cancel after first progress | Returns promptly; nothing changed |
| 15 | Cancel before cleanup | Token cancelled before call | Nothing deleted |
| 16 | Unexpected attributes | Read-only, hidden, system, sparse, encrypted (EFS, if available) | Skipped; attributes unchanged |
| 17 | Partial failure | Mix of eligible, locked, changed, missing | Report lists each correctly |
| 18 | Byte accounting | Mixed batch | `BytesFreed` = sum of deleted sizes only |
| 19 | Empty result | Empty root / empty selection | Clean report, no errors |
| 20 | Large and zero-byte files | Sparse-free large file via `SetLength`, 0-byte file | Sizes exact |
| 21 | Unicode / unusual names | Emoji, RTL, combining marks, spaces, leading dots | Correct handling |
| 22 | Long paths | > 260 characters | Correct handling |
| 23 | Concurrent changes | Background task creating/deleting files during scan and cleanup | No crash; no out-of-scope deletion |
| 24 | Integrity after every test | SHA-256 + attributes + timestamps snapshot of `outside` and protected files | Unchanged |

## What race tests can and cannot prove

They prove that the **specific** interleavings above are detected. They cannot prove that *no* timing
window exists, because Windows decides exact thread timing. The real guarantee comes from the design
(validate and delete through one exclusive handle, `SECURITY.md` §6); the tests confirm the code follows it.
Test 23 is repeated many times (e.g. 50 iterations) to increase confidence, not to prove absence of races.

## Self-review checklist (recorded in `TESTING.md`)

- [ ] Every D1–D7 step exists in code, in order, with a test
- [ ] No path-based delete API anywhere (`ForbiddenApiTests`)
- [ ] Every Win32 failure maps to a skip/failed outcome, none to "deleted"
- [ ] Bytes counted only after successful disposition
- [ ] Logs contain no profile path or file contents
- [ ] Optional: run the `/security-review` or `/code-review` skill on the engine and record findings

## Done when

- [ ] All 24 gate tests pass (skips justified in writing)
- [ ] Results table with date and actual output recorded in `TESTING.md`
- [ ] Gate outcome reported to the project owner before Phase 7 begins

## Risks

- **ACL tests are tricky** (parent folder can grant delete) → deny on both file and parent; always restore in `finally`.
- **EFS may be unavailable on Windows Home** → encrypted-file test skipped with reason.
