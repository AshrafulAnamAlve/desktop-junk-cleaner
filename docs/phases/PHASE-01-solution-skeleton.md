# Phase 1 — Solution Skeleton, Conventions and WPF Shell

**Status:** ✅ Done (2026-10-15) · **Depends on:** Phase 0 · **Deletes anything?** No

## Goal

Create the empty-but-working application: projects, shared build rules, a dark-themed main window with
navigation, logging, crash protection and the first "guardrail" tests. No scanning, no deletion.

## Why (beginner explanation)

A solution (`.sln`) groups several projects. Splitting the app into **App** (screens), **Core** (rules)
and **Infrastructure** (talks to Windows) means the safety rules can be tested without clicking
through the UI. Guardrail tests act like smoke alarms: if someone later adds a dangerous API, a test fails.

## What was built

| Area | Files |
|---|---|
| Build rules | `global.json` (SDK band + test runner), `Directory.Build.props` (net10.0-windows, nullable, warnings-as-errors, lock files, NuGet audit, no implicit `System.Net.Http`), `Directory.Packages.props`, `.editorconfig`, `.gitignore`, `SafeClean.sln` |
| Core | `Models/CleanupMode.cs` (`DryRun = 0`), `Policies/SafetyDefaults.cs`, `Interfaces/IElevationProbe.cs` |
| Infrastructure | `Environment/WindowsElevationProbe.cs`, `Logging/FileLoggerProvider.cs`, `Logging/FileLogger.cs` |
| App | `app.manifest` (`asInvoker`, long-path aware, per-monitor-v2 DPI), `App.xaml(.cs)` (DI composition root, global error handlers), `AppInfo.cs`, `Mvvm/ObservableObject.cs`, `Interop/DarkTitleBar.cs`, `MainWindow.xaml(.cs)`, `Resources/Theme.xaml`, `Resources/Controls.xaml`, `ViewModels/*` (Main, Dashboard, JunkCleaner, Performance, History, Settings, NavigationItem, PageViewModel, Glyphs), `Views/*` (5 pages) |
| Tests | `tests/SafeClean.Core.Tests` (`SafetyDefaultsTests`, `ArchitectureTests`, `ForbiddenApiTests`, `RepositoryFiles`), `tests/SafeClean.Infrastructure.Tests` (`ArchitectureTests`, `Logging/FileLoggerTests`, `Sandbox/TestSandbox`) |

## Tasks

- [x] `SafeClean.sln` (classic `.sln` format) with all five projects
- [x] `src/SafeClean.App` (WPF, `WinExe`, assembly name `SafeClean`)
- [x] `app.manifest`: `asInvoker`, `longPathAware`, per-monitor-v2 DPI, Windows 10/11 `supportedOS`
- [x] `Mvvm/ObservableObject.cs` — *`RelayCommand` moved to Phase 4, where the first command is needed (avoids unused code)*
- [x] `App.xaml.cs` composition root: DI container with `ValidateOnBuild`, logging, startup/exit log lines
- [x] Global error handlers: UI thread (log + friendly message, keep running), AppDomain (log), unobserved tasks (log + observe)
- [x] Theme tokens and control styles (buttons, cards, text, navigation, badges, focus ring, tooltips)
- [x] Main window: keyboard-navigable sidebar, page header, content area, "DRY RUN" badge, administrator warning badge (shown only when elevated)
- [x] Dark title bar (cosmetic, own window only)
- [x] Honest placeholder pages — no invented numbers
- [x] Test projects (xUnit v3) and guardrail tests
- [x] Restore with lock files; build; test; launch; screenshots; log check

## Changes from the original plan (and why)

| Change | Reason |
|---|---|
| Removed `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio`; added `"test": { "runner": "Microsoft.Testing.Platform" }` to `global.json` | xUnit v3 4.x runs on Microsoft's newer test platform; the .NET 10 SDK refuses the old runner for it. Result: **two fewer dependencies**. |
| Test command is now `dotnet test --solution SafeClean.sln` | Syntax of the new test platform. |
| `RelayCommand` deferred to Phase 4 | No screen has a command yet. |

## Theme tokens (contrast measured, WCAG AA)

| Token | Value | Lowest measured contrast |
|---|---|---|
| Text primary / secondary / muted | `#E6EAF0` / `#A8B2BF` / `#8B96A5` | 11.83 / 6.66 / 5.15 : 1 |
| Text on accent button | `#0B1524` on `#5AA2FF` (pressed `#4A8FE8`) | 5.57 : 1 |
| Badge text | `#8EC0FF` on `#1B2B40` | 7.59 : 1 |
| Warning | `#E3B341` on `#33291A` | 7.33 : 1 |
| Focus ring | `#8EC0FF` on window/card | 8.99 : 1 |
| Disabled text (exempt from WCAG) | `#7A8594` on `#252B33` | 3.81 : 1 |

## Verification results (actually run, 2026-10-15)

| Check | Result |
|---|---|
| `dotnet restore` | ✅ 5 `packages.lock.json` files created; NuGet audit reported nothing |
| `dotnet list package --vulnerable --include-transitive` | ✅ No vulnerable packages in any project |
| `dotnet build SafeClean.sln -c Debug` | ✅ 0 warnings, 0 errors |
| `dotnet test --solution SafeClean.sln -c Debug` | ✅ 42 passed, 0 failed, 0 skipped (Core 34, Infrastructure 8) |
| Test sandbox cleanup | ✅ `TestSandbox` folder empty after the run |
| App launch | ✅ Window "SafeClean" appears; closes with exit code 0 |
| All 5 pages reachable through UI Automation by accessible name | ✅ |
| Screenshots of all pages | ✅ Dark theme, dark title bar, icons render, Dry Run badge, no fake data. Two layout issues found and fixed (caption wrapping; History card stretching), then re-verified |
| Keyboard: Tab into sidebar, ↓↓ | ✅ Selection moves to Performance and the page changes |
| Focus ring visibility | ⚠ Not verifiable automatically (WPF hides focus rings in an inactive window) → manual check in Phase 9 |
| Log file `%LOCALAPPDATA%\SafeClean\Logs\safeclean-20261015.log` | ✅ Startup line "Mode: DryRun. Elevated: False." and exit line; real profile path **not** present |
| Process elevation | ✅ Not elevated; administrator badge hidden |

## Done when

- [x] All verification steps pass and results are recorded in `TESTING.md`
- [x] `README.md` explains how to build and run
- [x] `CHANGELOG.md` lists the files created

## Known follow-ups (tracked in later phases)

- Dashboard card row and the full-width Safety card don't share an exact right edge → Phase 9 responsive grid.
- Default (light) WPF scrollbars on dark background → Phase 9 custom scrollbars.
