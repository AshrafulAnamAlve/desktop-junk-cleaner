# SafeClean Architecture

## Layout (created in Phase 1; `SafeClean.App.Tests` added in Phase 4)

```
Desktop-Clean-App/              ← workspace root (the solution lives here, not in a nested SafeClean/ folder,
│                                  because this folder already is the project folder)
├─ SafeClean.sln
├─ global.json                  ← pins the .NET 10 SDK band
├─ Directory.Build.props        ← settings shared by every project
├─ Directory.Packages.props     ← the single list of NuGet packages and versions
├─ src/
│  ├─ SafeClean.App/            ← WPF user interface (MVVM)
│  │  ├─ Mvvm/                  ← tiny hand-written MVVM helpers (ObservableObject, RelayCommand, AsyncRelayCommand)
│  │  ├─ Presentation/          ← display formatting (sizes, dates, revealing disguised names)
│  │  ├─ Converters/            ← small XAML value converters
│  │  ├─ ViewModels/
│  │  ├─ Views/
│  │  ├─ Resources/             ← theme colours and control styles
│  │  ├─ Interop/               ← UI-only Windows calls (dark title bar)
│  │  ├─ App.xaml(.cs)          ← composition root: dependency injection, logging, error handlers
│  │  └─ MainWindow.xaml(.cs)
│  ├─ SafeClean.Core/           ← pure logic: models, interfaces, safety policies. No file access.
│  │  ├─ Models/                ← FileFacts, FileIdentity, ApprovedRoot, decisions, reason codes
│  │  ├─ Policies/              ← PathValidator, WindowsPath, ProtectedLocations, RootPolicy, CandidatePolicy,
│  │  │                            DryRunPlanner, CleanupPolicy, PolicyText
│  │  └─ Interfaces/            ← ITempRootResolver, ITempScanner, ICleanupService, IElevationProbe
│  └─ SafeClean.Infrastructure/ ← talks to Windows: scanning, cleanup, monitoring, logging
│     ├─ Native/                ← the only place with direct Windows calls and "unsafe" code
│     ├─ Scanning/              ← FileInspector, RootFactsReader, KnownProtectedLocations, TempRootResolver, TempScanner
│     ├─ Cleanup/               ← HandleBasedFileDeleter (the ONLY code that can delete), CleanupService, AttemptResult
│     │                            (Phase 5; not reachable from the App until Phase 7)
│     ├─ Logging/
│     └─ Environment/           ← administrator detection
├─ tests/
│  ├─ SafeClean.Core.Tests/
│  ├─ SafeClean.Infrastructure.Tests/
│  └─ SafeClean.App.Tests/      ← view-model tests with fake services (Phase 4)
└─ docs/
   └─ phases/                   ← one plan file per phase
```

## Who may depend on whom

```
SafeClean.App ───────────────► SafeClean.Core ◄──────── SafeClean.Infrastructure
      │                                                           ▲
      └──── (composition root only: registers services) ──────────┘
```

- **Core** depends on nothing but .NET itself. It decides *what is allowed*; it never touches the disk.
  This makes every safety rule testable with plain data.
- **Infrastructure** implements Core's interfaces using Windows APIs. It is the only layer that reads
  the file system or calls native Windows functions for files, memory or CPU.
- **App** shows data and forwards user intent. View models talk to Core interfaces; only `App.xaml.cs`
  knows the concrete Infrastructure classes. **The UI never decides which paths are deletable.**
- These rules are checked by automated tests (`ArchitectureTests`).

## Key technical decisions (plain-language)

| Decision | Why |
|---|---|
| **.NET 10 (LTS)**, `net10.0-windows` | Long-Term Support release, patched until Nov 2028. Already installed. |
| **WPF + XAML** | Mature Microsoft UI framework for Windows desktop apps, no extra packages needed. |
| **MVVM** with small hand-written helpers | Separates screens from logic so logic can be tested. Two short classes are easier to understand than a framework with code generators. |
| **Microsoft.Extensions.DependencyInjection** | Standard Microsoft way to wire classes together; makes swapping real services for test fakes easy. |
| **Microsoft.Extensions.Logging** + small built-in file logger | Standard logging API. Microsoft ships no file logger, and a 100-line one avoids a third-party dependency. |
| **Native Win32 calls (P/Invoke)** for file checks | .NET's normal file APIs work with *paths*. Safe deletion needs *handles* (open once, verify, delete the same object). See `SECURITY.md` §5–6. |
| **xUnit v3** on Microsoft.Testing.Platform | Free, widely used test framework; the newer Microsoft test platform needs no extra runner packages. |
| **Central Package Management + lock files** | Every dependency and version is listed in one file; lock files record exact package hashes so a tampered package fails restore. |
| **NuGet audit + warnings as errors** | Builds fail if a known-vulnerable package is used or if the compiler finds a likely bug. |

## Dependencies (all free)

| Package | Publisher | Used by | Purpose |
|---|---|---|---|
| Microsoft.Extensions.DependencyInjection 10.0.12 | Microsoft | App | Wiring services together |
| Microsoft.Extensions.Logging 10.0.12 | Microsoft | App | Logging setup |
| Microsoft.Extensions.Logging.Abstractions 10.0.12 | Microsoft | Infrastructure | `ILogger` interfaces |
| xunit.v3 4.0.0 | xUnit.net (.NET Foundation) | Tests only | Test framework (runs on Microsoft.Testing.Platform) |

Versions were chosen to be at least several weeks old at the time of selection (published Aug–Sep 2026).
Exact versions and hashes of all transitive packages are recorded in each project's `packages.lock.json`.

## Planned data flow

**Scan (read-only, implemented in Phases 3–4):** Junk Cleaner view model → `ITempScanner`
(`TempScanner`) → `ITempRootResolver` (`TempRootResolver` → `RootFactsReader` → `RootPolicy`) approves
the Temp root → folders listed one level at a time, links never entered, each folder re-checked before
listing → each entry inspected by `FileInspector` (read-attributes handle) → `CandidatePolicy` classifies
→ `ScanSession` with `ScanSummary` back to the UI. The user ticks rows; the view model keeps candidate IDs;
**Dry Run preview** = `DryRunPlanner.Create(session, ids)` in Core (unknown or non-selectable IDs ignored).

**Cleanup (implemented in Phase 5, connected to the UI in Phase 7):** the screen will send the scan
session and the IDs of the selected entries → `ICleanupService` (`CleanupService`) refuses the whole
request if elevated, the scan is older than 30 minutes, or `CleanupPolicy.CheckRoot` finds the re-resolved
Temp folder refused or different → for each ID, one at a time: `CleanupPolicy.CheckRecorded` (D1 + D2) →
Real mode: `HandleBasedFileDeleter` opens the file exclusively, reads its facts through that handle
(`FileInspector.ReadFacts`), `CleanupPolicy.CheckCurrent` (D4), marks that handle for deletion and closes
it; Dry Run: read-only re-inspection with the same D4 check → `CleanupReport` with one entry per ID and
bytes freed from deletions only; one audit log line per file with its Temp-relative path.

## Threading

Scanning, cleanup and monitoring run off the UI thread with `async`/`await` and `CancellationToken`.
View models update bound properties on the UI thread.
