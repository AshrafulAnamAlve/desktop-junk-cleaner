# Phase 9 — Dashboard, Performance Screen and UI Polish

**Status:** ✅ Done (2026-10-27) · **Depends on:** Phases 4, 7, 8 · **Deletes anything?** No new deletion paths

## Goal

Turn placeholders into a polished interface showing **real** data, with good accessibility and resizing.

## What was built

| Area | Files | Notes |
|---|---|---|
| Dashboard | `ViewModels/DashboardViewModel.cs`, `Views/DashboardView.xaml` | Live Memory, Processor and System-drive cards with usage bars; Junk card shows the last real scan estimate, or the space freed **from a real cleanup report** (never anything else); Safety card follows the Real-mode switch; "Scan Temp folder" opens the Junk Cleaner and starts a read-only scan — no cleanup from the dashboard |
| Performance | `ViewModels/PerformanceViewModel.cs`, `MonitoringRows.cs`, `Views/PerformanceView.xaml` | Memory and processor with 2-minute line charts; drives with usage bars; top 25 programs by memory, sortable by every column, "n/a" for values Windows won't give; Refresh button and optional 5 s refresh; **read-only** (a test proves the page has no command other than refresh and sort) |
| Refresh only while visible | `PageViewModel.OnShown/OnHidden`, `MainViewModel`, `MainWindow.xaml.cs`, `Services/UiTimers.cs` | Pages start their timers when shown and stop them when another page is shown **or the window is minimised** |
| Charts | `Converters/SparklineConverter.cs` | A `Polyline` in a fixed box (no chart library); unknown readings leave a gap, never a 0 |
| Card layout | `Controls/AdaptiveColumnsPanel.cs` | Cards fill whole columns and share the right edge with full-width cards; 4 → 2 → 1 columns as the window narrows |
| Window fit | `Presentation/WindowFit.cs`, `MainWindow.xaml.cs` | **Found on this PC:** the 760 px window was taller than the 738 px usable area of a 1366×768 screen, so its bottom was hidden behind the taskbar. The window (and, on very small/scaled screens, its minimum size) now fits the usable area |
| Junk Cleaner | `JunkCleanerViewModel`, `JunkCleanerView.xaml`, `CandidateItemViewModel`, `CleanupReportViewModel` | Details panel more compact and closable (✕ button or Esc; Esc closes the preview first); summary tiles step aside on short windows (`AtLeastToVisibilityConverter`) — the same counts stay in the Categories list; one-line selection text; category titles wrap instead of being cut off; "Sandbox folder" label in sandbox mode; report shows the drive's free-space change **separately** from space freed, labelled "may include other programs' activity" |
| Accessibility | `Controls.xaml`, `CandidateItemViewModel.SpokenName` | Scrollbar page buttons named; file rows announce name, size **and status**; every interactive control on every page has a name (audit below) |
| Shared styles | `Controls.xaml`, `Theme.xaml` | `Notice.Warning/Danger/Success` (sandbox banner and dialog notices use them), `Button.Header` for sortable columns; page header spacing tightened |
| Guardrails | `ArchitectureTests.RedDangerButtons_AppearOnlyOnTheJunkCleanerScreen`, `ContrastTests` | Red buttons only on the Junk Cleaner and its dialog; WCAG contrast of 35 colour pairs computed from Theme.xaml |

## Tests

| Case (from the plan) | Test | Result |
|---|---|---|
| Dashboard shows "—"/"Measuring…" instead of zeros | `DashboardViewModelTests.BeforeAnyReading…`, `WhenWindowsGivesNoAnswer…` | ✅ |
| Dashboard never shows a cleanup number not from a real report | `JunkCard_NeverShowsAFreedNumber_WithoutARealReport`, `JunkCard_ShowsTheScanEstimate_ThenSpaceFreedFromTheRealReport` | ✅ |
| Monitoring timers stop when the screen is hidden | `RefreshesOnlyWhileVisible`, `OnlyTheVisiblePageRefreshes_AndNothingWhileMinimised`, `PerformanceViewModelTests.Timers_RunOnlyWhileVisible…` | ✅ |

Also: first processor reading "measuring"; history keeps 60 readings and skips unknown ones; sorting by every
column; read-only command list; dashboard scan button never cleans; percent/chart/column/window-fit/converter
maths; drive-change wording; Esc and close-details; folder labels; spoken row names; contrast; red buttons.

## Verification results (actually run, 2026-10-27)

| Check | Result |
|---|---|
| Builds | ✅ Debug and Release: 0 warnings, 0 errors |
| `dotnet test --solution SafeClean.sln -c Debug` | ✅ 694 total: 687 passed, 0 failed, 7 skipped (Core 383; Infrastructure 160 + 7 as before; App 144). First runs: 1 compile error (a test helper named `Main` looked like an entry point) and 1 failure that caught a real wording bug ("drive C::" with a double colon) — both fixed |
| Contrast (WCAG 2) | ✅ All 34 text/UI pairs pass; lowest: red on pale red 4.88:1, pressed red button 4.89:1 (need 4.5); disabled text 3.81:1 (exempt). Full table in `TESTING.md` |
| Screenshots, default (1200×738) and minimum (900×600) size | ✅ Dashboard, Performance and Junk Cleaner reviewed (Debug sandbox mode for the Junk Cleaner — synthetic files, nothing deleted) |
| File-list room (sandbox mode, banner included) | Default: 4 → **5** rows (details open: 2 → **3**). Minimum size: **0 → 2** rows. Without the sandbox banner there is one more row |
| Window fit on this 1366×768 screen | ✅ Window now 1200×738, bottom edge at the taskbar (before: 760 px, cut off) |
| Accessibility name audit (what a screen reader reads) | ✅ 0 unnamed interactive controls on every page (before: 2 unnamed scrollbar buttons) |
| Keyboard walkthrough (keys posted only to SafeClean's window) | ✅ Tab order: sidebar → Scan → Categories → All/Safe/Review/Skipped → Search → file list; ↓ moves rows and pages; Space ticks a file ("1 file selected · about 50.0 KB"); Esc closes details; Shift+Tab goes back |
| Focus ring visible | ✅ Screenshots show the blue focus ring on the "Safe" filter and the inset ring on a file row (open since Phase 1) |

## Changes from the original plan (and why)

| Change | Reason |
|---|---|
| Window fitting to the usable screen area | Found on this PC (1366×768): the window was cut off by the taskbar |
| Splitter for the details panel → closable, compact details + tiles that step aside on short windows | A splitter leaves an empty gap whenever the details close; this keeps the list's room without gaps |
| Free-space change shown in the report (moved here from Phase 7) | Needed the monitoring service from Phase 8 |
| Pages also stop refreshing while the window is **minimised** | "No background work" also when nobody can see the window |

## Not done here (and why)

- **Screenshots at 125 % / 150 % display scaling** — would mean changing a Windows display setting, which this project
  doesn't do. The window-fit logic is unit-tested for screens smaller than the minimum size (150 % on this screen leaves
  about 911×492 usable). Listed
  for the owner's manual check in Phase 11.
- **Listening with Windows Narrator** — replaced by the automated name audit above (the same names Narrator reads);
  a real listen is on the Phase 11 manual checklist.
- **Full spacing/type-scale audit** — spacing kept on the existing 4/8/12/16/24 steps; no separate audit document.
- **Animations** — none were added (only instant hover/selection changes), which satisfies "limited to short transitions".

## Done when

- [x] All screens use real services or honest empty states
- [x] Accessibility checks recorded

## Known limitations

- At the minimum window size, opening a file's details leaves little room: the list keeps priority and the details
  are cut off. Maximising the window or closing the details (Esc) fixes it.
- The processor figure is approximate and can differ slightly from Task Manager (different measure).
