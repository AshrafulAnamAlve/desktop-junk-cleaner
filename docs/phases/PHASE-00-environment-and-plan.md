# Phase 0 — Environment, Requirements, Threat Model and Plan

**Status:** ✅ Done (2026-10-15) · **Depends on:** nothing · **Deletes anything?** No

## Goal

Find out what tools are on this PC, choose a supported .NET version, and write down the safety rules
and the plan *before* writing any application code.

## Environment findings (actually inspected)

| Item | Finding | Meaning |
|---|---|---|
| Workspace | `F:\API\Desktop-Clean-App`, empty, not a Git repository | New project; nothing of yours to overwrite. |
| Operating system | Windows 10 Home 22H2 (build 19045), x64 | Supported by .NET 10 and WPF. |
| .NET SDK | 10.0.203 (only SDK installed) | Can build .NET 10 apps. |
| .NET runtimes | .NET 9.0.0, .NET 10.0.7, Windows Desktop 10.0.7 | WPF apps for .NET 10 can run. |
| Templates | WPF, xUnit and MSTest templates present | No extra workloads needed. |
| Git | 2.54 | Available if you later want version control. |
| Elevation | Development shell is **not** running as administrator | Good: SafeClean never needs admin. |
| File systems | C:, D:, E:, F: are all NTFS | Junctions, hard links and file IDs (needed for safety checks and tests) are available. |
| Temp folder | `Path.GetTempPath()` → `%LOCALAPPDATA%\Temp` | The standard per-user location. |
| Long paths | Windows `LongPathsEnabled` = 0 | .NET still handles long paths internally; must be **tested** in Phase 3. |
| NuGet | nuget.org reachable | Packages can be restored. |

## Decisions

| Decision | Choice | Plain-language reason |
|---|---|---|
| .NET version | **.NET 10 (LTS)**, target `net10.0-windows` | "LTS" means Microsoft fixes security problems in it for 3 years (until Nov 2028). It's already installed. |
| UI | WPF + XAML, MVVM | Built into .NET for Windows; MVVM keeps screen code separate from logic so logic can be tested. |
| Logging | Microsoft.Extensions.Logging + small own file logger | Standard API; avoids a third-party logging library. |
| Tests | xUnit v3 | Free, widely used. |
| Deletion technique | Handle-based (open → verify → delete the *same* open file) | Prevents "checked one file, deleted another" mistakes. See `SECURITY.md` §5–6. |
| Packages | Only Microsoft + xUnit, versions 4–8 weeks old, pinned | Fewer and well-known dependencies = less supply-chain risk. |
| Folder layout | Solution at workspace root (not nested `SafeClean/`) | This folder already is the project. |

**Recommendation (not blocking):** the installed .NET runtime (10.0.7) is several security patches
behind the latest 10.0.x release. Before Phase 12 (publishing), update the .NET 10 SDK from
Microsoft's official download page so the published app bundles a patched runtime.

## Deliverables created

- [x] `docs/SECURITY.md` — approved root rules (R1–R8), protected locations, candidate rules (C1–C9),
      cleanup steps (D1–D7), race-condition analysis, threat model (T1–T17), limitations, unsupported list
- [x] `docs/ARCHITECTURE.md` — layers, dependency rules, technical decisions, packages
- [x] `docs/TESTING.md` — test commands, sandbox rules, the deletion test gate, manual checklist
- [x] `docs/ROADMAP.md` and these phase files

## Technical risk register

| # | Risk | How it is handled | Phase |
|---|---|---|---|
| 1 | File changes between scan and delete (race) | Validate and delete through one exclusive handle; compare file ID, size, timestamp, final path | 5, 6 |
| 2 | Junction/symlink in Temp leads to personal files | Never follow reparse points; open with `FILE_FLAG_OPEN_REPARSE_POINT`; skip them | 3 |
| 3 | `TEMP` variable points somewhere dangerous | Root must be the same folder (by file ID) as `%LOCALAPPDATA%\Temp` | 3 |
| 4 | Deleting files a program still needs | 7-day age rule, exclusive-open check, "needs review" for executables, user selects | 2, 5 |
| 5 | Mistakes in native Windows calls | Small interop layer, `SafeHandle`, results cross-checked against .NET values in tests | 3 |
| 6 | Long paths (> 260 chars) with OS setting off | Long-path-aware manifest + dedicated tests | 1, 3 |
| 7 | Very large Temp folders | Background scanning, cancellation, entry cap, virtualised lists | 3, 4 |
| 8 | Symlink tests need Developer Mode/admin | Junctions created without admin via `DeviceIoControl`; symlink tests skipped with reason | 3 |
| 9 | Misleading "space freed" numbers | Estimates labelled; freed bytes only from confirmed deletions | 4, 7 |
| 10 | Process list access errors / cost | Tolerate access-denied per process; refresh on demand | 8 |
| 11 | Compromised or vulnerable package | Minimal deps, lock files, NuGet audit as build error | 1 |
| 12 | Outdated runtime bundled at publish | Update SDK before publishing | 12 |
| 13 | A test accidentally deletes real data | Sandboxes under test `bin`; link targets inside sandbox; teardown never follows links | 3 |
| 14 | UI freezes | `async`/`await`, work off the UI thread | 3, 4, 8 |
| 15 | Project too complex for a beginner | Few abstractions, docs per phase | all |

## Required before real deletion is enabled

All 24 items in `docs/TESTING.md` → "Gate before enabling real deletion" must pass (Phase 6).

## Deliberately unsupported in version 1

See `docs/SECURITY.md` §11 (system folders, browser caches, Recycle Bin, registry, startup items,
memory "cleaning", directory deletion, secure wiping, background cleaning, etc.).
