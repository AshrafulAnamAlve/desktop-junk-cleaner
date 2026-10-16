# Phase 4 — Junk Cleaner Screen: Dry Run Preview

**Status:** ✅ Done (2026-10-16) · **Depends on:** Phase 3 · **Deletes anything?** No
(no deletion code exists in the project; a test enforces that the UI can't reach cleanup code)

## Goal

Show real scan results in a clear screen where the user can inspect files, tick individual files or
whole categories, and see a **preview** ("would delete N files, about X MB") — without deleting.

## Why (beginner explanation)

"Dry Run" is a rehearsal: it shows exactly what *would* happen. Users can see each file's reason and
status before any real cleanup exists. Estimates are labelled as estimates because files can change
between now and an actual cleanup.

## What was built

| Area | Files | Notes |
|---|---|---|
| Wiring | `App.xaml.cs` | Registers `TimeProvider.System`, `FileInspector`, `TempRootResolver`, `TempScanner` (read-only services only) |
| Commands | `Mvvm/RelayCommand.cs`, `Mvvm/AsyncRelayCommand.cs` | `Execute` re-checks `CanExecute`; async command is disabled while running (no double scans) |
| Display helpers | `Presentation/DisplayText.cs` | Explorer-style sizes, local dates ("Invalid date" for impossible ones), singular/plural, and **`Reveal`**: makes text-direction and invisible characters visible (`⟦U+202E⟧gpj.exe`) |
| Converter | `Converters/EnumToBooleanConverter.cs` | Filter radio buttons |
| View models | `ViewModels/JunkCleanerViewModel.cs`, `CandidateItemViewModel.cs`, `CategoryViewModel.cs`, `ScanState.cs` | Scan/cancel, states, totals, categories, filter, search, selection by **candidate ID**, Dry Run preview |
| Core | `Models/DryRunPreview.cs`, `Policies/DryRunPlanner.cs` | Preview from a selection; unknown or non-selectable IDs are **ignored and counted**, never trusted |
| View | `Views/JunkCleanerView.xaml(.cs)` | Toolbar, summary tiles, categories with tri-state checkboxes, filter + search, virtualised list, details panel, floating Dry Run preview (Esc closes), selection bar; Space ticks the highlighted row |
| Styles | `Resources/Controls.xaml`, `Theme.xaml` | Checkbox (tick / dash), segmented filter, search box, secondary button, progress bar, slim dark scrollbars, list rows, status badges (green / amber / grey — **no red**), overlay |
| Layout | `MainWindow.xaml`, other views | Pages now scroll themselves, so the long list virtualises inside a fixed-height area |
| Tests | `tests/SafeClean.App.Tests/` (new), `Core.Tests/Policies/DryRunPlannerTests.cs`, `Core.Tests/ArchitectureTests.cs` | 49 view-model/presentation tests; 4 planner tests; 2 new architecture guards |

## Safety rules on this screen (all tested)

- Nothing is selected after a scan. "Select all safe" never includes *Needs review*.
- *Skipped* entries can't be selected — not by clicking, not via a category, not via "select all".
- A category's checkbox selects only its **safe** files; a category with no safe files has its checkbox disabled.
- The selection is a set of candidate IDs from the current scan; a new scan clears it, and the preview is
  recomputed by Core from those IDs.
- Changing the selection discards an old preview, so a preview always describes the current selection.
- File names are shown with hidden direction/invisible characters revealed.
- View models only use Core interfaces (test); the App has no access to cleanup code until Phase 7 (test).

## Changes from the original plan (and why)

| Change | Reason |
|---|---|
| Dry Run preview floats over the results instead of sitting in the layout | First screenshots showed the list squeezed to ~2 rows at the default window size |
| Compact summary tiles and details panel | Same reason — more room for the list |
| Category list includes an "All files" entry with its own checkbox | Consistent behaviour; overall partial selection is visible |
| Dashboard "Junk files" caption changed to "Scan from the Junk Cleaner page" | The old text ("No scan has been run") became untrue once scanning existed |

## Verification results (actually run, 2026-10-16)

| Check | Result |
|---|---|
| `dotnet build SafeClean.sln -c Debug` | ✅ 0 warnings, 0 errors |
| `dotnet test --solution SafeClean.sln -c Debug` | ✅ 396 passed, 0 failed, 3 skipped (Core 287; Infrastructure 60 + 3 skipped as before; App 49) |
| First test runs | 3 compile errors (WPF test project doesn't import `System.IO` implicitly) fixed; 2 failures fixed: impossible dates displayed as 12/31/9999 (**code fix**: now "Invalid date"), and a test whose fake returned the same session twice (test fix) |
| App walkthrough via UI Automation (no keystrokes to other apps) | ✅ Intro → read-only scan of the real Temp folder (19,268–19,287 entries, 9.6–13.1 s) → results → category ticked → preview "would try to delete 1,925 files, about 2.3 GB (estimate)" → preview closed → details with path, size, dates, reason → Skipped filter. Screenshots reviewed; one layout problem found and fixed (above) |
| Disabled category checkboxes | ✅ "Temporary files" and "Logs" had 0 safe files on this PC, so their checkboxes were correctly disabled |
| Temp folder changed between Phase 3 and Phase 4 scans | Investigated: 24,896 → ~19,270 entries. SafeClean's `src/` contains no deletion calls (searched), and test deletions are confined to sandboxes. **Windows Storage Sense is enabled** on this PC ("run during low free disk space", "delete temporary files apps aren't using"), and C: free space rose from 16.7 GB to 18.4 GB — consistent with Windows cleaning Temp. An independent read-only count matched the app's numbers |

## Done when

- [x] Preview works with real data; nothing on disk is modified
- [x] Docs updated (`ARCHITECTURE.md` for the new test project)

## Follow-ups for Phase 9 (polish)

- At the default window size the list shows ~3 rows while the details panel is open → consider a resizable splitter or a side details pane on wide windows.
- Long category titles ("Logs and diagnostic files") are trimmed in the 240 px column.
- Keyboard-only check of Space/arrow behaviour and focus rings (manual; automation can't focus the window without stealing focus).
