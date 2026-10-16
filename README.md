# SafeClean

A safety-first Windows PC cleaner built with C#, WPF and .NET 10. Free, local-only, no cloud services.

> **Current status: Phase 5 done** — the Junk Cleaner screen scans your Temp folder (read-only), shows
> every file with its status and reason, lets you tick files or categories, and shows a **Dry Run
> preview** of what a cleanup would do. The safe deletion engine now exists and has been tested **only on
> synthetic files in test sandboxes**. It is **not connected to any button**, so the app still cannot
> delete anything. 508 automated tests pass (3 skipped with documented reasons).

## What it will do (version 1)

- Scan **only** the current user's Temp folder, read-only.
- Show each candidate file with size, date, status and the reason it was (or wasn't) offered.
- Let you tick files or categories and preview the result (Dry Run, the default).
- Delete only files you selected, after re-checking each one and after you confirm.
- Report space freed from files that were actually deleted — nothing else.
- Show RAM, CPU, disk and per-process memory usage (read-only; no "RAM cleaner").

What it deliberately will **not** touch is listed in [docs/SECURITY.md](docs/SECURITY.md) §11.

## Requirements

- Windows 10 or 11 (x64)
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.0.203 or later 10.0.x) to build

## Build, test, run

```powershell
dotnet build SafeClean.sln -c Debug
dotnet test --solution SafeClean.sln -c Debug
dotnet run --project src/SafeClean.App
```

The app runs with normal user rights (never as administrator) and always starts in **Dry Run** mode.

## Documentation

| Document | Contents |
|---|---|
| [docs/phases/](docs/phases/README.md) | One plan file per phase (goal, tasks, tests, exit criteria) |
| [docs/ROADMAP.md](docs/ROADMAP.md) | Phase status overview |
| [docs/SECURITY.md](docs/SECURITY.md) | Safety rules, threat model, limitations |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Project structure and technical decisions |
| [docs/TESTING.md](docs/TESTING.md) | Test commands, deletion test gate, manual checklist, results |
| [docs/CHANGELOG.md](docs/CHANGELOG.md) | Changes per phase |

## Privacy

Everything runs on your PC. SafeClean sends nothing anywhere. Logs are stored in
`%LOCALAPPDATA%\SafeClean\Logs` and never contain file contents.
