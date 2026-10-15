# SafeClean Architecture

## Layout (created in Phase 1; `SafeClean.App.Tests` will be added in Phase 4)

```
Desktop-Clean-App/              ← workspace root (the solution lives here, not in a nested SafeClean/ folder,
│                                  because this folder already is the project folder)
├─ SafeClean.sln
├─ global.json                  ← pins the .NET 10 SDK band
├─ Directory.Build.props        ← settings shared by every project
├─ Directory.Packages.props     ← the single list of NuGet packages and versions
├─ src/
│  ├─ SafeClean.App/            ← WPF user interface (MVVM)
│  │  ├─ Mvvm/                  ← tiny hand-written MVVM helpers (ObservableObject; RelayCommand from Phase 4)
│  │  ├─ ViewModels/
│  │  ├─ Views/
│  │  ├─ Resources/             ← theme colours and control styles
│  │  ├─ Interop/               ← UI-only Windows calls (dark title bar)
│  │  ├─ App.xaml(.cs)          ← composition root: dependency injection, logging, error handlers
│  │  └─ MainWindow.xaml(.cs)
│  ├─ SafeClean.Core/           ← pure logic: models, interfaces, safety policies. No file access.
│  └─ SafeClean.Infrastructure/ ← talks to Windows: scanning, cleanup, monitoring, logging
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

**Scan (read-only):** Junk Cleaner view model → `ITempScanner` (Infrastructure) → root resolver
validates the Temp root (R1–R8) → enumerator walks files without following reparse points → for each
file, read-only handle gathers facts → `CandidatePolicy` (Core) classifies → results to UI.

**Cleanup (Phase 5+, enabled Phase 7):** UI sends the IDs of selected scan results → `ICleanupService`
revalidates root and each file through an exclusive handle → deletes that same handle → returns
deleted / failed / skipped lists and bytes freed from successes only.

## Threading

Scanning, cleanup and monitoring run off the UI thread with `async`/`await` and `CancellationToken`.
View models update bound properties on the UI thread.
