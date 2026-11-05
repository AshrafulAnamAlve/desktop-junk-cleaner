# SafeClean

A safety-first Windows PC cleaner built with C#, WPF and .NET 10. Free, local-only, no cloud services.

> **Current status: version 1.0.0, all phases done** — the Junk Cleaner screen scans your Temp folder (read-only), shows
> every file with its status and reason, lets you tick files or categories, shows a **Dry Run preview**,
> and — only if you switch **Real cleanup mode** on and confirm — deletes the selected files through the
> safety-tested engine. The **Dashboard** and **Performance** screens show live, read-only memory,
> processor, drive and program-memory figures. **Settings** can make the rules stricter, and **History** lists past
> cleanups. 866 automated tests pass (7 skipped with documented reasons).

## What it does (version 1)

- Scan **only** the current user's Temp folder, read-only.
- Show each candidate file with size, date, status and the reason it was (or wasn't) offered.
- Let you tick files or categories and preview the result (Dry Run, the default).
- Delete only files you selected, after re-checking each one and after you confirm.
- Report space freed from files that were actually deleted — nothing else.
- Show RAM, CPU, disk and per-process memory usage (read-only; no "RAM cleaner").

What it deliberately will **not** touch is listed in [docs/SECURITY.md](docs/SECURITY.md) §11.

## Download and run

You don't need to install .NET, and you don't need administrator rights.

1. From the project's [Releases page](https://github.com/AshrafulAnamAlve/desktop-junk-cleaner/releases), download
   `SafeClean-<version>-win-x64.zip` and the `.sha256` file next to it.
2. Check the download. In PowerShell, in the folder holding the ZIP, run
   `Get-FileHash .\SafeClean-<version>-win-x64.zip -Algorithm SHA256`. The result must match the `.sha256` file exactly.
   If it doesn't, don't run the program.
3. Right-click the ZIP and choose **Extract All…**. This step matters: `SafeClean.exe` needs the files beside it, and
   without them it closes straight away with no message.
4. Run `SafeClean.exe` from the extracted folder.

**"Windows protected your PC":** SafeClean is free and has no paid code-signing certificate, so Windows SmartScreen doesn't
recognise it and may show this warning. The warning doesn't mean a virus was found, and it doesn't prove the file is safe, which is
why step 2 matters. If the checksum matches and you trust the source, choose **More info**, then **Run anyway**.

**Removing it:** close SafeClean and delete the extracted folder. Nothing was installed, so there's no uninstaller.
Settings, history and logs are in `%LOCALAPPDATA%\SafeClean`; delete that folder too if you want them gone. SafeClean adds no
startup entries, services or registry settings.

## Requirements

- **To run:** Windows 11 or Windows 10, 64-bit (x64), and about 130 MB of disk space.
- **To build:** [.NET 10 SDK](https://dotnet.microsoft.com/download) 10.0.403 or later 10.0.x. Older SDKs are refused on
  purpose, because a release carries the runtime of the SDK that built it.

Microsoft's [support list for .NET 10](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md) names
Windows 11 and the long-term (LTS) editions of Windows 10. Regular Windows 10 22H2 is no longer on it. SafeClean 1.0.0 was
tested on Windows 10 Home 22H2 (build 19045) and works there, but Microsoft no longer promises .NET fixes for that version. It
hasn't yet been tested on Windows 11 or on a second PC.

## Build, test, run

```powershell
dotnet build SafeClean.sln -c Debug
dotnet test --solution SafeClean.sln -c Debug
dotnet run --project src/SafeClean.App
```

The app runs with normal user rights (never as administrator) and always starts in **Dry Run** mode.

To make a release (a folder, a ZIP and a checksum, in `publish\`), run the command below. It checks each build before zipping
it, deletes nothing, and stops if that version's output already exists.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\publish-release.ps1
```

## Cleaning (Real mode)

1. **Junk Cleaner → Scan Temp folder.** Reading only; nothing changes.
2. Tick files or categories (a category tick selects only its *safe* files). Use **Preview cleanup (Dry Run)**
   to see what would happen.
3. Switch **Real cleanup mode** on (bottom bar turns red). It is off every time SafeClean starts, can't be
   switched on while SafeClean runs as administrator, and switches itself off after each cleanup.
4. Press **Delete N files…** and read the dialog. **Cancel is the default** — Enter or Esc cancels.
   Only the red button deletes.
5. Each file is checked again right before deletion; files that changed, are in use, or no longer pass
   the rules are skipped and kept. The report shows deleted, skipped and failed files, and space freed
   from deleted files only. Deleted files do **not** go to the Recycle Bin.

## Settings (stricter only)

**Settings** lets you make SafeClean *more* careful — never less: a longer minimum file age (7 days is the lowest),
and **exclusions** for things to always keep, written relative to your Temp folder (for example `Installers\Old`) or
as a file-name pattern (`*.log`). Changes apply from the next scan. If the saved settings file can't be read,
SafeClean uses safe defaults and **pauses cleanup**. Restarting may fix it; otherwise saving your settings replaces the
file, after asking you first, and keeps a copy of the old one as `settings.json.unreadable`.

**Logs and history:** you can choose how much SafeClean logs (each deleted file is always logged), copy the log
folder path, and — only when you press the button and answer *Yes* — delete SafeClean's **own** log files older than
the retention you chose. Nothing is deleted automatically. The **History** page lists past cleanups (counts and sizes
only, never file names). If the log file can't be written, SafeClean keeps working and shows a notice saying so.

**Trying it safely first (Debug builds only):** create a folder of synthetic files and point SafeClean at it.

```powershell
dotnet test --project tests/SafeClean.Infrastructure.Tests/SafeClean.Infrastructure.Tests.csproj -c Debug --output Detailed --explicit only --filter-class "SafeClean.Infrastructure.Tests.Manual.ManualSandboxSetup"
dotnet run --project src/SafeClean.App -- --sandbox-root "<the folder printed by the command above>"
```

An orange **SANDBOX MODE** banner then shows the folder being used. SafeClean refuses to start if the folder
has no `.safeclean-sandbox` marker file, and this option doesn't exist in Release builds. A sandbox run keeps its own
settings, history and logs in a folder beside the sandbox, so your real ones aren't touched.

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
