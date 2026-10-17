# Phase 8 — Read-Only RAM, CPU, Disk and Process Monitoring

**Status:** ✅ Done (2026-10-17) · **Depends on:** Phase 1
**Deletes anything?** No · **Changes system state?** No

## Goal

Provide accurate, read-only measurements of memory, CPU, disks and per-process memory for the
Dashboard and Performance screens (connected to the screens in Phase 9).

## Why (beginner explanation)

Windows deliberately keeps recently used data in spare RAM (a cache) so programs open faster. High RAM
usage is therefore usually **normal**. "RAM cleaner" buttons that force this cache out make the PC
*slower* afterwards, which is why SafeClean only shows information and never "cleans" memory.

## What was built

| Area | Files | Notes |
|---|---|---|
| Contract (Core) | `Interfaces/ISystemMonitor.cs` | `GetMemory()`, `GetCpuUsage()`, `GetDrives()`, `GetTopProcesses(count)` |
| Data (Core) | `Models/MonitoringSnapshots.cs` | `MemorySnapshot`, `CpuSnapshot` (null percent = "measuring"), `DriveSnapshot`, `ProcessMemoryInfo` (null values = "n/a"), `CpuTimes` |
| Maths (Core, pure) | `Policies/MonitoringMath.cs` | CPU % from idle/kernel/user time differences (kernel includes idle); used bytes and percentages; clamped 0–100; zero or backwards counters → unknown, never a wrong number |
| Windows calls | `Native/NativeMethods.cs` | `GlobalMemoryStatusEx`, `GetSystemTimes` — both only read |
| Monitor (Infrastructure) | `Monitoring/SystemMonitor.cs` | Memory; CPU from two readings (first call = "measuring"); fixed, ready drives only (unreadable ones left out); every process read in try/catch and disposed, sorted by memory, top N |
| Wiring | `App.xaml.cs` | `ISystemMonitor → SystemMonitor` registered (used by the screens in Phase 9) |
| Guardrails | `ForbiddenApiTests` | 6 more patterns: program memory limits, file-cache size, program priority, processor affinity, pausing programs, controlling services |
| Explanation text | `Views/PerformanceView.xaml` | "Why high memory usage is normal" (written in Phase 1, kept) |

## Tests

| Case (from the plan) | Test | Result |
|---|---|---|
| CPU calculator with synthetic deltas | `MonitoringMathTests`: 66.7% example, fully idle/busy, no time passed, 3 kinds of backwards counter, bogus idle > total, huge counters | ✅ |
| Memory % math | `MonitoringMathTests`: 75% example, total 0 → unknown, available > total → clamped; drive maths; percentage clamping | ✅ |
| Real `GlobalMemoryStatusEx` | `SystemMonitorTests.Memory_IsPlausible…` — also within 2% of the .NET runtime's own figure | ✅ |
| Real drive list | `Drives_IncludeTheSystemDrive_OnlyFixedOnes…` | ✅ |
| Process list | `TopProcesses_AreSortedLargestFirst…`, `AllProcesses_ListedWithoutErrors_IncludingThisOne`, zero/negative count | ✅ |
| CPU first reading | `Cpu_FirstReadingIsMeasuring_ThenAPercentage` | ✅ |
| Forbidden APIs | `ForbiddenApiTests` (now 38 patterns) | ✅ |

## Changes from the original plan (and why)

| Change | Reason |
|---|---|
| `CpuUsageCalculator` became `MonitoringMath.CpuPercent` (with the other monitoring maths) | One small pure class for all monitoring arithmetic |
| Percentages are `null` when the total is unknown (not 0) | "Unknown" must never look like a real measurement |
| Refresh timers (2 s / 5 s while a screen is visible) moved to Phase 9 | They belong to the screens; Phase 9 already tests that timers stop when a screen is hidden |
| Free disk space = space **this user** can use (`AvailableFreeSpace`) | The honest figure for the person using SafeClean (equal to total free space without disk quotas) |
| More forbidden patterns than listed | Also blocks memory-limit, priority, affinity, suspend and service-control APIs |

## Verification results (actually run, 2026-10-17)

| Check | Result |
|---|---|
| `dotnet build SafeClean.sln -c Debug` | ✅ 0 warnings, 0 errors |
| `dotnet test --solution SafeClean.sln -c Debug` | ✅ 615 total: 608 passed, 0 failed, 7 skipped (Core 382; Infrastructure 160 + 7; App 66) — all new tests passed on the first run |
| Memory vs Windows (CIM) | ✅ Total 11.90 GiB both; available 4.38 vs 4.43 GiB (read seconds apart) |
| Drives vs Windows (CIM) | ✅ C:, D:, E:, F: identical to 0.01 GiB |
| Top 5 programs vs `Get-Process` | ✅ Same five programs, values within ~1% |
| CPU vs Windows' processor counter | First comparison disagreed (86% vs 12%): the two readings were taken at different moments during a load burst. **Re-measured side by side**, second by second: within 2–3 points (e.g. 9.8 vs 10.4, 21.9 vs 21.7, 60.2 vs 57.9) ✅. Task Manager's "utility" figure can differ slightly more — shown as approximate |

## Done when

- [x] Monitoring services implemented and tested
- [x] Explanation text about Windows memory caching written for the UI

## Risks (from the plan) → outcome

- **CPU % differs slightly from Task Manager** → matches Windows' "% Processor Time" counter within a few points; documented as approximate.
- **Process enumeration cost** → top N only; refresh on demand (timers come with the screens in Phase 9).
