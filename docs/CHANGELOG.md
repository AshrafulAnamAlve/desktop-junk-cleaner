# Changelog

## [Unreleased]

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
