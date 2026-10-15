# Changelog

## [Unreleased]

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
