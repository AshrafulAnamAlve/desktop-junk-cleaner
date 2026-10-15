# Phase 7 — Enable Cleanup in the UI

**Status:** Not started · **Depends on:** Phase 6 gate passed and reported
**Deletes anything?** Yes — only files the user explicitly selects, revalidates and confirms.

## Goal

Connect the tested cleanup engine to the Junk Cleaner screen behind several deliberate steps, then
show an honest result report.

## Why (beginner explanation)

Several separate "locks" must all be opened on purpose before anything is deleted: switch to Real mode,
select files, read the summary, and press a clearly labelled delete button. Any one of them stops the
process. Afterwards, the report separates files actually deleted from files skipped or failed.

## Tasks

### Real mode
- [ ] Toggle "Real cleanup mode" on the Junk Cleaner screen; **off at every launch** (not saved in v1)
- [ ] When on: red-bordered banner "Real mode — selected files will be permanently deleted. They are not
      moved to the Recycle Bin."
- [ ] Toggle disabled (with explanation) when elevated or root refused

### Clean button
- [ ] Danger style (red), label "Delete N files…", visually separate from the blue Scan button
- [ ] Enabled only if: Real mode on, ≥ 1 file selected, session fresh (< 30 min), not elevated, root valid

### Confirmation dialog
- [ ] Shows: number of files, estimated total size, breakdown by category, count of "needs review" files
- [ ] States clearly: permanent deletion, files in use or changed will be skipped, recovery not guaranteed
- [ ] Buttons: **Cancel** (default, focused, Enter/Esc) and "Delete N files" (must be clicked or reached with Tab)
- [ ] No "don't ask again" option

### Running and reporting
- [ ] Progress with current count; Cancel stops **before the next file** (never mid-file)
- [ ] Result screen: Deleted (count, bytes), Skipped (count, grouped by reason), Failed (count, reasons)
- [ ] "Space freed" = engine's `BytesFreed` only; disk free space before/after shown separately as
      "Drive free space change (may include other programs' activity)"
- [ ] After cleanup the session is closed; user must rescan to clean again
- [ ] Every run written to the audit log

### Debug-only sandbox mode (for safe manual testing)
- [ ] Debug builds only (`#if DEBUG`): `--sandbox-root <path>` accepted only if the folder contains a marker
      file `.safeclean-sandbox`; all root rules except R5 still apply; big orange "SANDBOX" banner
- [ ] Release builds contain no such code (source test checks the code is inside `#if DEBUG`; Phase 11
      confirms via the Release assembly)
- [ ] Script-free setup: a test helper creates a sandbox with aged synthetic files for manual UI testing
- [ ] `SECURITY.md` gains a section describing this mode and its limits

## Tests (`SafeClean.App.Tests`, fake services)

| Case | Expected |
|---|---|
| Real mode off | Clean command cannot execute |
| Elevated | Toggle disabled; command cannot execute |
| Dialog cancelled | `ICleanupService` never called |
| Confirmed | Called once with exactly the selected IDs and `CleanupMode.Real` |
| Report | Deleted/skipped/failed counts and bytes shown exactly as returned |
| After cleanup | Selection cleared; rescan required |
| App restart | Real mode off again |

## Verification

- Build 0 warnings; all tests pass.
- Manual end-to-end test **in sandbox mode** (Debug build): scan sandbox, select files, confirm, check report
  matches the files actually gone and that skipped files remain. Screenshots recorded.
- A cleanup of the **real** Temp folder is only done by the project owner, if and when they choose.

## Done when

- [ ] All checks above pass; `SECURITY.md`, `README.md`, `CHANGELOG.md` updated
- [ ] Roadmap marks "real deletion enabled" with the date

## Risks

- **Accidental confirmation** → Cancel is default; destructive button needs a deliberate click.
- **Sandbox mode misuse** → Debug-only, marker file required, never in Release.
