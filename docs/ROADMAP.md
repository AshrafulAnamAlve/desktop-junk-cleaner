# SafeClean Roadmap

Legend: `[x]` done and verified · `[~]` in progress · `[ ]` not started

**Real deletion was enabled in Phase 7 (2026-10-17), after the Phase 6 test gate passed — only behind
Real mode, a selection and a confirmation dialog.**

The original 12-phase plan was split further so that deletion code is written, tested and reviewed
*before* it is connected to any button.

Detailed plan for each phase: [`docs/phases/`](phases/README.md) (one file per phase).

| Phase | Goal | Status |
|---|---|---|
| 0 | Environment inspection, requirements, threat model, plan | Done |
| 1 | Solution, conventions, WPF shell, DI, logging, test projects | Done (2026-10-15) |
| 2 | Core safety policies (path validation, root rules, candidate rules) + unit tests | Done (2026-10-15) |
| 3 | Read-only Temp scanner + sandbox integration tests | Done (2026-10-16) |
| 4 | Junk Cleaner screen: Dry Run preview, categories, checkboxes (no deletion code) | Done (2026-10-16) |
| 5 | Handle-based cleanup engine + audit logging (not connected to UI) | Done (2026-10-16) |
| 6 | Cleanup test gate: races, replacement, locks, partial failures, byte accounting | Done (2026-10-17) — passed |
| 7 | Connect cleanup to UI behind Real mode + confirmation + result report | Done (2026-10-17) — **real deletion enabled** |
| 8 | Read-only RAM / CPU / disk / process monitoring services + tests | Done (2026-10-17) |
| 9 | Dashboard and Performance screens on real data; UI polish and accessibility | Not started |
| 10 | Settings, restrict-only exclusions, logging controls, cleanup history | Not started |
| 11 | Security review, regression, failure simulation, manual verification | Not started |
| 12 | Publish self-contained build with free tools; compatibility notes | Not started |

---

## Phase 0 – Environment, requirements, threat model ✅
- [x] Inspect workspace (empty folder, not a Git repository)
- [x] Inspect tools: .NET SDK 10.0.203, Windows Desktop runtime 10.0.7, Git 2.54, Windows 10 22H2 (19045), all drives NTFS
- [x] Choose target: **.NET 10 (LTS, supported until November 2028)**, `net10.0-windows`
- [x] Write threat model and security policy (`SECURITY.md`)
- [x] Write architecture, testing plan, roadmap

## Phase 1 – Solution skeleton ✅ (2026-10-15)
- [x] `global.json`, `Directory.Build.props`, `Directory.Packages.props` (central, pinned versions, lock files, NuGet audit, warnings as errors)
- [x] Projects: App (WPF), Core, Infrastructure, Core.Tests, Infrastructure.Tests in `SafeClean.sln`
- [x] App manifest: `asInvoker`, long-path aware, per-monitor DPI
- [x] Dark theme resources and main shell with keyboard-navigable sidebar
- [x] Honest placeholder pages (no fabricated numbers)
- [x] Dependency injection composition root
- [x] File logger (local only, profile path redacted) and global exception handlers
- [x] Guardrail tests: Dry Run default, layer boundaries, forbidden APIs (deletion, shell, process kill, memory trimming, network, registry)
- [x] Verified: build 0 warnings, 42/42 tests pass, app launches, screenshots and log checked

## Phase 2 – Core safety policies ✅ (2026-10-15)
- [x] Models: file facts, file identity, root facts, approved root, decisions, statuses, reasons, categories (`ScanCandidate` moved to Phase 3)
- [x] Path validator (traversal, ADS, device names, UNC, device prefixes, slashes, trailing dot/space, invalid chars, lone surrogates, length)
- [x] Protected-location model (subtrees + exact paths + drive roots)
- [x] Containment helpers (`WindowsPath`): exact for OS-reported paths, case-insensitive for protection
- [x] Root policy (R2–R7) — the only way to create an `ApprovedRoot`
- [x] Candidate policy (attributes allow-list, link count, age, timestamps, depth, exclusions, review types)
- [x] Plain-English text for every reason
- [x] Verified: 0 warnings; 281/281 tests pass (239 new); mutation check caught 4/4 planted bugs

## Phase 3 – Read-only scanner ✅ (2026-10-16)
- [x] Native interop layer (`CreateFileW`, `GetFileInformationByHandleEx`, `GetFinalPathNameByHandleW`, `GetVolumeInformationByHandleW`, `SHGetKnownFolderPath`) — read-attributes right only
- [x] Root resolver (R1–R7 via `RootPolicy`; R8 is checked at cleanup time in Phase 5)
- [x] Protected locations built from this PC's real folders
- [x] Scanner that never follows reparse points (two layers + policy final-path check), depth limit, entry cap, cancellation, progress
- [x] Sandbox test helpers (junctions, hard links, symlinks-or-skip, attributes, ACLs, snapshots) that never touch real user folders
- [x] Integration tests: junction/symlink/hard link/attributes/locked/no-permission/Unicode/long path/depth/cancellation/churn/empty/snapshot
- [x] Verified: 0 warnings; 341 passed, 3 skipped (documented); real Temp scanned read-only (24,896 entries, ≈4.6 GB estimated recoverable); mutation check passed

## Phase 4 – Dry Run preview ✅ (2026-10-16)
- [x] Junk Cleaner screen with categories (tri-state, safe files only), per-file checkboxes, filters and search
- [x] Details: name, full path, size, modified/created time, status, category, reasons
- [x] Estimated size clearly labelled; Dry Run preview computed by Core from candidate IDs
- [x] No deletion code referenced from the UI (enforced by test); disguised file names revealed
- [x] Verified: 0 warnings; 396 passed, 3 skipped; real read-only scan and preview driven through the app; layout issue found and fixed

## Phase 5 – Cleanup engine ✅ (2026-10-16)
- [x] Handle-based deletion (D1–D7) in a single, isolated class (`HandleBasedFileDeleter`); decisions in Core (`CleanupPolicy`)
- [x] Whole-request refusal: elevated, unknown mode, scan older than 30 minutes, Temp folder refused or no longer the same folder
- [x] Dry Run mode: read-only re-check, "would delete / would skip"
- [x] Audit log with root-relative paths; cancellation between files; report lists every selected ID once
- [x] Not connected to the UI (architecture test); delete APIs allowed in one file only (forbidden-API test)
- [x] Verified: 0 warnings; 508 passed, 3 skipped; tests on synthetic sandbox files only; mutation check caught 11/12 planted bugs (the 12th is backed up by a second layer)

## Phase 6 – Cleanup test gate (must pass before Phase 7) ✅ (2026-10-17) — PASSED
- [x] All 24 items in `TESTING.md` § "Gate before enabling real deletion" (3 sub-cases skipped with written reasons)
- [x] Self-review of the engine against `SECURITY.md`; one robustness fix (report kept even if progress/logging fails)
- [x] Verified: 0 warnings; 553 passed, 5 skipped, 0 failed; concurrency gate 50 rounds with ≈550 mid-cleanup look-alikes, all survived; mutation check 6/6 caught

## Phase 7 – Enable cleanup in the UI ✅ (2026-10-17) — real deletion enabled
- [x] Real-mode switch (off at every start, never saved, impossible while elevated, off again after each cleanup)
- [x] Red delete button (Real mode only); confirmation dialog with count, size, categories, review warning; Cancel is default
- [x] Results: deleted / failed / skipped, bytes freed from successes only; rescan required afterwards
- [x] Debug-only sandbox mode with marker file; never in Release (source test + binary check)
- [x] Verified: 0 warnings; 576 passed, 6 skipped; end-to-end in sandbox mode with 4 safety checks; real Temp not cleaned

## Phase 8 – Monitoring services ✅ (2026-10-17)
- [x] RAM, CPU, disk, process list (read-only), tolerant of access errors; unknown values never guessed
- [x] Pure maths tests with synthetic numbers + real read-only readings; explanation text about Windows memory caching
- [x] 6 more forbidden-API patterns (priority, affinity, memory limits, suspending programs, services, file cache)
- [x] Verified: 0 warnings; 608 passed, 7 skipped; readings match Windows' own figures (CPU within 2–3 points when sampled side by side)

## Phase 9 – Dashboard and polish
- [ ] Dashboard cards on live data; Performance screen
- [ ] Contrast check, keyboard navigation, resizing

## Phase 10 – Settings and history
- [ ] Settings (age threshold can only be made *stricter*), restrict-only exclusions
- [ ] Logging controls, cleanup history

## Phase 11 – Security review and verification
- [ ] Full regression, failure simulation, manual checklist, final security review

## Phase 12 – Publishing
- [ ] Self-contained `win-x64` publish with free tooling; compatibility and limitations documented
