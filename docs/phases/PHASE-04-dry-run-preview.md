# Phase 4 — Junk Cleaner Screen: Dry Run Preview

**Status:** Not started · **Depends on:** Phase 3 · **Deletes anything?** No
(no deletion code exists in the project yet)

## Goal

Show real scan results in a clear screen where the user can inspect files, tick individual files or
whole categories, and see a **preview** ("would delete N files, about X MB") — without deleting.

## Why (beginner explanation)

"Dry Run" is a rehearsal: it shows exactly what *would* happen. Users can see each file's reason and
status before any real cleanup exists. Estimates are labelled as estimates because files can change
between now and an actual cleanup.

## Tasks

### MVVM helpers (moved here from Phase 1)
- [ ] `Mvvm/RelayCommand.cs` and `Mvvm/AsyncRelayCommand.cs` — `Execute` re-checks `CanExecute`, so a disabled
      command can never run, even if invoked from code; async version blocks re-entry while running

### View model (`ViewModels/JunkCleanerViewModel.cs`)
- [ ] `ScanCommand` (async, disabled while running), `CancelScanCommand`
- [ ] Progress: files examined, current phase text; no fake percentages when total is unknown
- [ ] Root refused → show the refusal reason, hide selection, offer only "Scan again"
- [ ] Results grouped by `CandidateCategory`; per-category tri-state checkbox; per-file checkbox
- [ ] **Nothing is selected by default.** "Select all eligible" is an explicit button.
      "Needs review" items are never included by "select all eligible"
- [ ] Skipped items visible (with reasons) but **cannot be selected**
- [ ] Filters: All / Eligible / Needs review / Skipped; text search by name
- [ ] Details panel: name, full path (selectable text), size, modified time, created time, status, reasons
- [ ] Summary bar: "Selected: N files · Estimated size: X (estimate)"
- [ ] `PreviewCommand` → produces a Dry Run summary from the selection; touches nothing on disk
- [ ] Selection is held as **candidate IDs from the current scan session**, never as path strings

### View (`Views/JunkCleanerView.xaml`)
- [ ] Virtualised list (`VirtualizingStackPanel`, recycling) for tens of thousands of rows
- [ ] Empty state before first scan; "Nothing found" state; error state
- [ ] Keyboard: Space toggles checkbox, arrows move, Tab order logical; `AutomationProperties.Name` on controls
- [ ] Scan button = accent (blue). No red/destructive styling anywhere on this screen yet

### New test project
- [ ] `tests/SafeClean.App.Tests` for view-model logic (uses fake `ITempScanner`; no real disk, no UI thread)
      — deviation from the original layout, needed so selection logic is tested

## Tests

| Case | Expected |
|---|---|
| Category checkbox ticks only Eligible files in that category | Needs-review and skipped excluded |
| Ticking all files in a category | Category shows checked; some → indeterminate |
| Skipped file | Cannot become selected through any command |
| "Select all eligible" | Excludes Needs review |
| Estimated size | Sum of selected sizes only |
| New scan | Clears old selection (IDs from an old session are never reused) |
| Cancel during scan | UI returns to idle; partial results not shown as complete |
| Root refused | Selection and preview disabled, reason shown |
| Architecture | App project references no cleanup/deletion type |

## Verification

- Build 0 warnings; all tests pass.
- Launch app, run a real **read-only** scan of Temp, screenshot results, check numbers against a few
  files in Explorer, confirm no files changed (spot-check modified dates).

## Done when

- [ ] Preview works with real data; nothing on disk is modified
- [ ] Docs updated (`ARCHITECTURE.md` for the new test project)

## Risks

- **Slow UI with many rows** → virtualisation, grouping computed off the UI thread.
- **Confusing statuses** → plain-English reasons (e.g. "Modified 2 days ago — files newer than 7 days are kept").
