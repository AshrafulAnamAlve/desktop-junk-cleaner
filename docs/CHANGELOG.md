# Changelog

## [1.0.0] — 2026-11-05 (built, not yet uploaded)

First release: a portable Windows x64 ZIP that needs no .NET install and no administrator rights.

**What's in it:** a read-only scan of your Temp folder, a Dry Run preview, optional real cleanup with re-checks and a
confirmation, live memory, processor and drive figures, settings that can only make it stricter, cleanup history and local logs.
Everything stays on your PC.

**Before you run it:** extract the whole ZIP first. Windows SmartScreen may warn about it because it's unsigned; compare the
SHA-256 checksum first. Regular Windows 10 22H2 is no longer on Microsoft's .NET 10 support list, though SafeClean works on
it. Limits are in `README.md` and `docs/SECURITY.md` §10–11.

### Phase 12 — publishing ✅ Done (2026-11-05)
Verified: builds 0 warnings on SDK 10.0.403; Debug 873 total: 866 passed, 0 failed, 7 skipped; Release 867 total: 861 passed, 0 failed,
6 skipped; no vulnerable packages; the extracted ZIP passed its read-only checks against the real Temp folder.
Optional and not done: installer, second-PC test.

Created:
- `tools/publish-release.ps1` (builds, checks, zips and checksums; deletes nothing), `tools/make-icon.ps1`, `tools/README-portable.txt`
- `src/SafeClean.App/Resources/SafeClean.ico`; `packages.win-x64.lock.json` in the three `src` projects (release builds keep their own package hashes)
- `tests/SafeClean.Core.Tests/PublishingTests.cs`

Changed:
- Version 0.1.0 → 1.0.0; `global.json` requires SDK 10.0.403 or later, so releases carry the patched runtime
- The plan's self-extract option was dropped: nothing is unpacked into `%TEMP%`, so 5 Microsoft DLLs ship beside the program
- Docs: `README.md` (download, run, verify, remove, requirements), `docs/SECURITY.md` (§10, §14), `docs/ARCHITECTURE.md`, `docs/TESTING.md`

### Phase 11 — security review and verification ✅ Done (2026-11-03)
Verified: builds 0 warnings; Debug 866 total: 859 passed, 0 failed, 7 skipped; Release 860 total: 854 passed, 0 failed,
6 skipped; mutation checks 32/32 planted bugs caught; race gate 50/50; no vulnerable packages. The owner checked display
scaling and Narrator; the real administrator launch passed. The optional disk-nearly-full case wasn't run. All deletion
was on synthetic sandbox files; the real Temp folder was only scanned read-only, and real settings and history weren't changed.

Fixed (full list with sources in `docs/TESTING.md` → "Review findings"):
- **Cancel was slow on big cleanups:** `CleanupService` now reports progress every 50 files instead of after each one
- **Settings can't quietly get looser:** saving keeps a valid value that isn't a preset; unknown names pause cleanup; replacing
  unreadable settings asks first and keeps `settings.json.unreadable`; "Delete old logs" respects the pause (in `LogMaintenance`)
- **The screens tell the truth:** a notice when logging is off (`ILogStatus`, checked at startup) and when a scan stops at its
  limit; the age rule shows the age in force; "Saved" clears after a change
- **Side jobs can't cost the real report:** free-space and history failures are contained; list and log-cleanup failures are
  said on the page; a bad history line no longer breaks the History page
- **Nothing is written while running as administrator** (no log, settings or history), so planted links can't aim an
  elevated write at a protected file; enforced in the three writing classes, with a test that fails if another starts writing
- **Closing the window mid-cleanup** stops it before the next file and shows the report
- **Debug sandbox runs** keep their settings, history and logs beside the sandbox, never the real ones
- **`System.Net.Http` implicit import really removed** (moved to the new `Directory.Build.targets`; the old setting did nothing)
- Settings Tab order; status line tooltip

Created:
- `src/SafeClean.Core/Interfaces/ILogStatus.cs`, `Directory.Build.targets`
- Tests: `CompiledAssemblyTests` (reads the compiled program: native calls, libraries, framework calls), `ReviewFindingsTests` (App);
  additions to `ForbiddenApiTests`, `ArchitectureTests`, `FileLoggerTests`, `LogMaintenanceTests`, `SettingsAndHistoryStoreTests`,
  `CleanupServiceTests`, `TempScannerTests`

Docs: `docs/SECURITY.md` (T20, T21, §12, §13 and new known limitations), `docs/TESTING.md` (threat table, findings, failure
simulation, checklist), `docs/ARCHITECTURE.md`, `README.md`.

### Phase 10 — settings, exclusions, logging controls and cleanup history ✅ Done (2026-10-31)
Verified: build 0 warnings; 803 total: 796 passed, 0 failed, 7 skipped; mutation check 10/10 planted bugs caught (one after an added test). The only new deletion
(SafeClean's own old logs) is manual, asks first and reuses the existing engine; tests used synthetic sandbox
folders only. The owner's real settings, history and logs were not changed.

Created:
- `src/SafeClean.Core/`: `Models/UserSettings`, `Models/CleanupHistoryEntry`, `Policies/SettingsPolicy`,
  `Policies/WildcardPattern`, `Interfaces/ISettingsService`, `Interfaces/ICleanupHistory`, `Interfaces/ILogMaintenance`
- `src/SafeClean.Infrastructure/`: `Settings/UserSettingsStore`, `Settings/JsonLinesCleanupHistory`,
  `Logging/LogMaintenance`, `Logging/LogFolderRootResolver`
- `src/SafeClean.App/`: `Services/UserPrompt`, `ViewModels/Choice`, `Converters/NullToCollapsedConverter`
- Tests: `SettingsPolicyTests` (Core); `Settings/SettingsAndHistoryStoreTests`, `Settings/SettingsInScanAndCleanupTests`,
  `Logging/LogMaintenanceTests` (Infrastructure); `SettingsAndHistoryViewModelTests` (App)

Changed:
- `CandidatePolicyContext.ForSettings` (excluded folders and name patterns, longer age); `TempScanner` and
  `CleanupService` use the saved settings; `CleanupRefusal.SettingsUnreadable` and `CleanupPolicy.CheckRequest`
- `FileLogger` / `FileLoggerProvider`: adjustable log level, cleanup audit trail always written
- `SettingsViewModel` / `SettingsView`, `HistoryViewModel` / `HistoryView`: real pages instead of placeholders
- `JunkCleanerViewModel`: Real mode paused while saved settings are unreadable; each real cleanup recorded in history
- `App.xaml.cs`: registers the settings store, history, log maintenance and prompt; applies the log level
- `ForbiddenApiTests`: file replace/move allowed only in `UserSettingsStore.cs`
- `docs/SECURITY.md` §13, `README.md` "Settings (stricter only)", `ARCHITECTURE.md`, `TESTING.md`

### Phase 9 — Dashboard, Performance screen and UI polish ✅ Done (2026-10-27)
Verified: builds 0 warnings (Debug and Release); 687 passed, 0 failed, 7 skipped; contrast of 34 colour pairs
meets WCAG; screenshots at default and minimum size; keyboard walkthrough; focus ring confirmed; 0 unnamed
controls. No new deletion paths; the Junk Cleaner screenshots used Debug sandbox mode (synthetic files).

Created:
- `src/SafeClean.App/`: `Services/UiTimers`, `Mvvm/RelayCommandOfT`, `Controls/AdaptiveColumnsPanel`,
  `Converters/SparklineConverter`, `Converters/AtLeastToVisibilityConverter`, `Presentation/WindowFit`,
  `ViewModels/MonitoringRows`
- `tests/SafeClean.App.Tests/`: `DashboardViewModelTests`, `PerformanceViewModelTests`, `LayoutAndFormatTests`,
  `JunkCleanerPolishTests`, `ContrastTests`

Changed:
- `DashboardViewModel` / `DashboardView`, `PerformanceViewModel` / `PerformanceView`: live, read-only data
- `PageViewModel` (`OnShown`/`OnHidden`), `MainViewModel`, `MainWindow`: refresh only the visible page, none while
  minimised; window fits the usable screen area (it was cut off on 1366×768)
- `JunkCleanerViewModel` / `JunkCleanerView`: closable compact details, Esc order, tiles step aside on short
  windows, one-line selection text, wrapping category titles, "Sandbox folder" label, drive free-space change in the report
- `CandidateItemViewModel` (spoken row names), `CategoryViewModel`, `CleanupReportViewModel`, `DisplayText.FormatPercent`
- `Controls.xaml`: `Notice.*`, `Button.Header`, named scrollbar buttons; page header spacing
- `ArchitectureTests`: red buttons only on the Junk Cleaner screen
- `docs/TESTING.md`: contrast table, keyboard walkthrough, two new manual checklist items

### Phase 8 — read-only monitoring services ✅ Done (2026-10-17)
Verified: build 0 warnings; 608 passed, 0 failed, 7 skipped; readings match Windows' own figures (RAM, drives,
top programs; CPU within 2–3 points when sampled side by side). Nothing is changed on the system.

Created:
- `src/SafeClean.Core/`: `Interfaces/ISystemMonitor`, `Models/MonitoringSnapshots`, `Policies/MonitoringMath`
- `src/SafeClean.Infrastructure/Monitoring/SystemMonitor.cs`
- `tests/SafeClean.Core.Tests/Policies/MonitoringMathTests.cs`
- `tests/SafeClean.Infrastructure.Tests/Monitoring/`: `SystemMonitorTests`, `RealReadingsForComparison` (Explicit)

Changed:
- `NativeMethods`: `GlobalMemoryStatusEx`, `GetSystemTimes` (read-only)
- `App.xaml.cs`: registers `ISystemMonitor`
- `ForbiddenApiTests`: 6 new patterns (memory limits, file cache, priority, affinity, pausing programs, services)
- `docs/SECURITY.md` §8: implemented and enforced

### Phase 7 — cleanup enabled in the UI ✅ Done (2026-10-17) — real deletion enabled behind Real mode + confirmation
Verified: build 0 warnings (Debug and Release); 576 passed, 0 failed, 6 skipped; Release binaries contain no
sandbox code; end-to-end run in Debug sandbox mode deleted exactly the 3 selected synthetic files after
4 safety checks, with Enter cancelling the dialog. The real Temp folder was not cleaned.

Created:
- `src/SafeClean.App/`: `Services/ICleanupConfirmation`, `Services/DialogCleanupConfirmation`,
  `Views/ConfirmCleanupWindow.xaml(.cs)`, `ViewModels/CleanupConfirmationViewModel`, `ViewModels/CleanupReportViewModel`,
  `Converters/InverseBooleanToVisibilityConverter`
- `src/SafeClean.Infrastructure/Scanning/DebugSandboxRootResolver.cs` (Debug builds only)
- `tests/SafeClean.App.Tests/`: `TestDoubles`, `JunkCleanerCleanupTests`
- `tests/SafeClean.Infrastructure.Tests/`: `Scanning/DebugSandboxRootResolverTests`, `Manual/ManualSandboxSetup` (Explicit)

Changed:
- `JunkCleanerViewModel` / `JunkCleanerView`: Real mode switch, red delete button, confirmation, cleaning
  progress with Cancel, result report; results closed and Real mode off after each cleanup
- `MainWindow` / `MainViewModel`: DRY RUN / REAL MODE sidebar badge, SANDBOX banner; accessibility names moved
  from `Border`s (invisible to screen readers) onto text
- `App.xaml.cs`: registers `CleanupService` and the dialog; `--sandbox-root` in Debug builds only; `AppInfo.SandboxRoot`
- `ScanState`: `Cleaning`, `CleanedUp`; `Theme.xaml` / `Controls.xaml`: danger colours, `Button.Danger`, `ToggleSwitch`
- `ArchitectureTests`: "no cleanup until Phase 7" replaced by "only through `ICleanupService`"; sandbox code Debug-only
- `.gitignore`: `.sandbox/`
- `docs/SECURITY.md` §12, `README.md` "Cleaning (Real mode)"

### Phase 6 — cleanup test gate ✅ Done (2026-10-17) — gate PASSED
Verified: build 0 warnings; 553 passed, 0 failed, 5 skipped (written reasons); all 24 gate items pass, with
3 sub-cases skipped for documented reasons (file symlink, 8.3 alias, EFS); mutation check caught 6/6 planted
bugs, each by the intended gate test. Synthetic sandbox
files only; real deletion still not connected to the UI.

Created:
- `tests/SafeClean.Infrastructure.Tests/Cleanup/CleanupGateTests.cs` (gate tests `Gate02`…`Gate23`, 45 cases)
- `tests/SafeClean.Infrastructure.Tests/Sandbox/SpecialFiles.cs` (sparse files, 8.3 short names, wait for antivirus)

Changed:
- `CleanupService`: a throwing progress callback or logger now stops the run but still returns the full
  report (previously the record of already-deleted files could be lost) — found in the gate self-review
- `TestSandbox.Snapshot(folder)`; `CleanupPolicyTests`: "now encrypted" and "now sparse" cases
- `docs/SECURITY.md`: gate passed; three new known limitations
- `docs/phases/PHASE-07` and `PHASE-10`: follow-ups from the gate review

### Phase 5 — handle-based cleanup engine (not connected to the UI) ✅ Done (2026-10-16)
Verified: build 0 warnings; 508 passed, 3 skipped; every deletion in tests was a synthetic file inside a
test sandbox; mutation check caught 11/12 planted bugs (the 12th removes a check that a second layer
repeats, and is caught when both are removed). No screen or button can reach the engine (architecture test).

Created:
- `src/SafeClean.Core/`: `Interfaces/ICleanupService`; `Models/CleanupReport`, `CleanupEntry`, `CleanupOutcome`,
  `CleanupRefusal`, `CleanupProgress`; `Policies/CleanupPolicy`
- `src/SafeClean.Infrastructure/Cleanup/`: `HandleBasedFileDeleter` (the only code that can delete),
  `CleanupService`, `AttemptResult`
- `tests/SafeClean.Core.Tests/Policies/`: `CleanupPolicyTests`, `CleanupReportTests`
- `tests/SafeClean.Infrastructure.Tests/Cleanup/`: `HandleBasedFileDeleterTests`, `CleanupServiceTests`, `CleanupTestDoubles`

Changed:
- `FileInspector`: facts can be read through an already-open handle (`ReadFacts`), shared with the engine
- `NativeMethods`: error codes for access denied, sharing/lock violation, delete pending
- `SafetyDefaults.MaxScanSessionAge` (30 minutes); `PolicyText` describes cleanup outcomes and refusals
- `ForbiddenApiTests`: 7 new patterns; delete access right, `SetFileInformationByHandle` and the disposition
  structure allowed only in `HandleBasedFileDeleter.cs`
- `ArchitectureTests`: only `CleanupService` calls the engine; the App can't reference any cleanup class
- `docs/SECURITY.md` §5: implemented (not enabled), whole-request checks, how the engine is built

### Phase 4 — Junk Cleaner screen with Dry Run preview ✅ Done (2026-10-16)
Verified: build 0 warnings; 396 passed, 3 skipped; real read-only scan and Dry Run preview driven through
the app with UI Automation; screenshots reviewed. No deletion code exists.

Created:
- `src/SafeClean.App/`: `Mvvm/RelayCommand`, `Mvvm/AsyncRelayCommand`, `Presentation/DisplayText`,
  `Converters/EnumToBooleanConverter`, `ViewModels/CandidateItemViewModel`, `CategoryViewModel`, `ScanState`
- `src/SafeClean.Core/`: `Models/DryRunPreview`, `Policies/DryRunPlanner`
- `tests/SafeClean.App.Tests/` (new project): `TestScans`, `JunkCleanerViewModelTests`, `PresentationTests`
- `tests/SafeClean.Core.Tests/Policies/DryRunPlannerTests.cs`

Changed:
- `JunkCleanerViewModel` and `JunkCleanerView` rewritten (scan, results, categories, filters, details, preview)
- `App.xaml.cs`: registers the read-only scanning services
- `Controls.xaml` / `Theme.xaml`: checkbox, segmented filter, search box, secondary button, progress bar,
  dark scrollbars, list rows, status badges, overlay
- `MainWindow.xaml` and other pages: each page scrolls itself (needed for list virtualisation)
- `DashboardView.xaml`: "Junk files" caption no longer claims no scan has run
- `ArchitectureTests`: view models use Core only; App can't reach cleanup code before Phase 7
- `docs/SECURITY.md`: selection rules, threats T18 (disguised names) and T19 (Temp changed by others)

### Phase 3 — read-only Temp scanner ✅ Done (2026-10-16)
Verified: build 0 warnings; 341 passed, 3 skipped (Explicit real-Temp scan, 2 symlink tests without
Developer Mode); real Temp folder scanned read-only (24,896 entries in 10.6 s); mutation check passed.

Created:
- `src/SafeClean.Core/`: `Models/CandidateId`, `ScanCandidate`, `ScanSummary`, `ScanSession`, `ScanResult`,
  `ScanProgress`, `RootResolution`; `Interfaces/ITempScanner`, `ITempRootResolver`
- `src/SafeClean.Infrastructure/Native/`: `NativeMethods`, `HandleQueries`, `ExtendedPath`
- `src/SafeClean.Infrastructure/Scanning/`: `FileInspector`, `InspectionResult`, `RootFactsReader`,
  `KnownProtectedLocations`, `TempRootResolver`, `TempScanner`
- `tests/SafeClean.Infrastructure.Tests/Sandbox/`: `FileSystemLinks`, `SandboxRootResolver` (+ `SyncProgress`)
- `tests/SafeClean.Infrastructure.Tests/Scanning/`: `FileInspectorTests`, `RootResolutionTests`,
  `TempScannerTests`, `ExtendedPathTests`, `RealTempReadOnlyScanTests`
- `tests/SafeClean.Core.Tests/Policies/ScanModelTests.cs`

Changed:
- `CandidatePolicy.Skip(...)` added; `SafetyDefaults.MaxScanEntries` (250,000) added
- `SafeClean.Infrastructure.csproj`: `AllowUnsafeBlocks` (generated native calls); new test keeps `unsafe` inside `Native/`
- `TestSandbox`: aged-file creation and SHA-256 snapshots
- `docs/SECURITY.md`: how scanning stays read-only; new known limitations

### Phase 2 — core safety policies ✅ Done (2026-10-15)
Verified: build 0 warnings; 281/281 tests pass (239 new); mutation check caught 4/4 planted bugs.
No disk access in this phase.

Created:
- `src/SafeClean.Core/Models/`: `FileIdentity`, `FileFacts`, `RootFacts`, `ApprovedRoot`, `RootDecision`,
  `CandidateDecision`, `CandidateStatus`, `SkipReason`, `RootProblem`, `PathProblem`, `CandidateCategory`
- `src/SafeClean.Core/Policies/`: `PathValidator`, `WindowsPath`, `ProtectedLocations`, `RootPolicy`,
  `CandidatePolicyContext`, `CandidatePolicy`, `PolicyText`
- `tests/SafeClean.Core.Tests/Policies/`: `TestData`, `PathValidatorTests`, `WindowsPathTests`,
  `ProtectedLocationsTests`, `RootPolicyTests`, `CandidatePolicyTests`, `PolicyTextTests`

Changed:
- `tests/SafeClean.Core.Tests/ForbiddenApiTests.cs`: "cmd" pattern no longer matches the `.cmd` extension; 7 new pattern tests
- `docs/SECURITY.md`: rules C5, C7, C8, R5 made more precise; `ApprovedRoot` only via `RootPolicy`
- `docs/phases/PHASE-03`: root resolver builds `RootFacts` for `RootPolicy`; no test-only bypass; strip `\\?\`

### Phase 1 — solution skeleton, conventions, WPF shell ✅ Done (2026-10-15)
Verified: build 0 warnings; 42/42 tests pass; no vulnerable packages; app launches, all pages render,
keyboard navigation works, log file correct.

Created:
- Build: `SafeClean.sln`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.gitignore`, `.editorconfig`,
  `packages.lock.json` per project
- `src/SafeClean.Core/`: `SafeClean.Core.csproj`, `Models/CleanupMode.cs`, `Policies/SafetyDefaults.cs`,
  `Interfaces/IElevationProbe.cs`
- `src/SafeClean.Infrastructure/`: `SafeClean.Infrastructure.csproj`, `Environment/WindowsElevationProbe.cs`,
  `Logging/FileLoggerProvider.cs`, `Logging/FileLogger.cs`
- `src/SafeClean.App/`: `SafeClean.App.csproj`, `app.manifest`, `App.xaml(.cs)`, `AppInfo.cs`, `MainWindow.xaml(.cs)`,
  `Mvvm/ObservableObject.cs`, `Interop/DarkTitleBar.cs`, `Resources/Theme.xaml`, `Resources/Controls.xaml`,
  `ViewModels/` (9 files), `Views/` (5 pages)
- `tests/SafeClean.Core.Tests/`: `SafetyDefaultsTests`, `ArchitectureTests`, `ForbiddenApiTests`, `RepositoryFiles`
- `tests/SafeClean.Infrastructure.Tests/`: `ArchitectureTests`, `Logging/FileLoggerTests`, `Sandbox/TestSandbox`

Changed from plan:
- Switched tests to Microsoft.Testing.Platform (required by xUnit v3 4.x on the .NET 10 SDK);
  removed `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio` (two fewer dependencies).
- `RelayCommand` moved to Phase 4.

### Phase 0 — environment, requirements, threat model, plan (2026-10-15)
Created:
- `README.md`
- `docs/SECURITY.md`, `docs/ARCHITECTURE.md`, `docs/TESTING.md`, `docs/ROADMAP.md`, `docs/CHANGELOG.md`
- `docs/phases/README.md` and `PHASE-00` … `PHASE-12` plan files (one per phase)
