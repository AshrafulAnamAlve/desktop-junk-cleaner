# Phase 8 — Read-Only RAM, CPU, Disk and Process Monitoring

**Status:** Not started · **Depends on:** Phase 1 (can run in parallel with 2–7 if needed)
**Deletes anything?** No · **Changes system state?** No

## Goal

Provide accurate, read-only measurements of memory, CPU, disks and per-process memory for the
Dashboard and Performance screens.

## Why (beginner explanation)

Windows deliberately keeps recently used data in spare RAM (a cache) so programs open faster. High RAM
usage is therefore usually **normal**. "RAM cleaner" buttons that force this cache out make the PC
*slower* afterwards, which is why SafeClean only shows information and never "cleans" memory.

## Tasks

### Contracts (Core)
- [ ] `ISystemMonitor` with `GetMemory()`, `GetCpuUsage()`, `GetDrives()`, `GetTopProcesses(int count)`
- [ ] Records: `MemorySnapshot(TotalBytes, AvailableBytes, UsedPercent)`, `CpuSnapshot(Percent?)`,
      `DriveSnapshot(Name, Label, Format, TotalBytes, FreeBytes)`, `ProcessMemoryInfo(Name, Id, WorkingSetBytes, PrivateBytes?)`
- [ ] Pure helper `CpuUsageCalculator` (from idle/kernel/user time deltas) — unit-testable

### Implementation (Infrastructure/Monitoring)
- [ ] Memory: `GlobalMemoryStatusEx`
- [ ] CPU: `GetSystemTimes`, two samples; first call returns "measuring" (null), never a made-up number
- [ ] Disks: `DriveInfo.GetDrives()` → ready, fixed drives only
- [ ] Processes: `Process.GetProcesses()`; each read in try/catch (access denied → value shown as "n/a");
      every `Process` object disposed; sorted by working set; top N
- [ ] Refresh: memory/CPU every 2 s while a monitoring screen is visible; process list on demand + optional 5 s

### Explicitly not used (added to `ForbiddenApiTests`)
`Process.Kill`, `CloseMainWindow`, `EmptyWorkingSet`, `SetProcessWorkingSetSize`, `NtSetSystemInformation`
(standby list), `SetPriorityClass`, service control APIs, page-file/virtual-memory settings.

## Tests

| Case | Expected |
|---|---|
| CPU calculator with synthetic deltas | Correct %, clamped 0–100, zero-delta safe |
| Memory % math | Correct rounding; total 0 handled |
| Real `GlobalMemoryStatusEx` | Total > 0; available ≤ total |
| Real drive list | System drive present; free ≤ total |
| Process list | Returns items; inaccessible process doesn't throw |
| Forbidden APIs | None present |

## Verification

- Build 0 warnings; tests pass.
- Compare readings with Task Manager (RAM total/in-use, CPU roughly, disk free) and record in `TESTING.md`.

## Done when

- [ ] Monitoring services implemented and tested
- [ ] Explanation text about Windows memory caching written for the UI

## Risks

- **CPU % differs slightly from Task Manager** (different sampling) → documented as approximate.
- **Process enumeration cost** → on-demand refresh, top N only.
