# SafeClean Security Policy and Threat Model

> Priority order: **protect user data → correctness → usability → performance.**
> A skipped file is always preferable to deleting an important one. Uncertainty means *skip*.

Status: **Policy defined (Phase 0); guardrails in place (Phase 1). Real deletion is NOT implemented and is disabled.**
This document is updated whenever behaviour changes. Values marked *(initial)* may be tuned later,
but only with a documented reason and matching tests.

---

## 1. Safe-by-default behaviour

| Rule | How it is enforced |
|---|---|
| App starts in read-only scanning mode | No deletion code path is reachable until the user switches to Real mode (Phase 7+). |
| Dry Run is the default mode | `CleanupMode.DryRun` is the default value (value 0); enforced by `SafetyDefaultsTests`. |
| Scanning never modifies files | Scanner opens files only with `FILE_READ_ATTRIBUTES` and full sharing; it never requests write/delete access. |
| No automatic cleanup on first launch or in the background | Cleanup runs only from an explicit user confirmation dialog. There are no timers, schedulers or startup tasks. |
| Never runs as administrator by default | App manifest requests `asInvoker`. If the app is started elevated anyway, **cleanup is disabled** (scanning still works). |
| Never weakens Windows security | No code touches Defender, UAC, services, registry, ACLs, ownership or security settings. |
| No network access | No networking APIs are used; `System.Net.Http` is not even imported implicitly. `ForbiddenApiTests` fails if any networking API is introduced. |

## 2. Approved cleanup root (version 1)

Version 1 supports **exactly one** location: the current user's own temporary directory.
It is resolved at runtime (never hard-coded) and accepted only if **all** of these hold:

| # | Root requirement |
|---|---|
| R1 | Obtained from `Path.GetTempPath()` (honours the user's `TMP`/`TEMP` settings). |
| R2 | Fully qualified local drive path (no UNC `\\server\share`, no device path `\\?\`/`\\.\`). |
| R3 | On a fixed, NTFS-formatted volume. |
| R4 | Exists, is a real directory, and **neither it nor any parent folder is a reparse point** (symlink, junction, mount point). |
| R5 | It is the **same directory** (same volume serial number and file ID) as `%LOCALAPPDATA%\Temp`. A `TEMP` variable redirected to `C:\`, `D:\Data`, `C:\Windows\Temp`, etc. is refused. |
| R6 | It is not a drive root, not the user profile root, does not contain any protected location, and is not inside Windows, Program Files or ProgramData. |
| R7 | The canonical root used for all comparisons is the path the **operating system reports** for an opened handle to the directory (`GetFinalPathNameByHandle`), not a string we built. |
| R8 | Cleanup (not scanning) is refused while the process is elevated. |

If any check fails, SafeClean shows why and offers **no cleanup at all**.

## 3. Protected locations

Even if a path appeared to be inside the approved root, these are never deletable. The approved root
must also never equal or contain them.

*Protected subtrees* (nothing inside may be touched): Windows directory, System32/SysWOW64,
Program Files, Program Files (x86), ProgramData, the Recycle Bin (`$Recycle.Bin`),
`System Volume Information`, and the user's Desktop, Documents, Downloads, Pictures, Music, Videos,
Favorites, OneDrive and other known personal folders.

*Protected exact paths* (may not be the root, but descendants may be legitimate): every drive root,
the `Users` directory, the user profile folder, `AppData`, `AppData\Local`, `AppData\Roaming`.

User exclusions (Phase 10) can only **add** restrictions. They can never remove a protected location
or widen the approved root.

## 4. Candidate policy (what may be offered for cleanup)

Candidates are discovered **only** by enumerating the canonical root. Paths typed by a user, read
from settings, or passed from the UI are never used as deletion targets on their own.

| # | Check | Failure result |
|---|---|---|
| C1 | Found by enumerating the canonical root; directories are traversed only if they are not reparse points, and only to a maximum depth *(initial: 16)*. | Not listed / Skipped |
| C2 | Is a file (directories are never deletion candidates). | Not listed |
| C3 | Has no reparse-point attribute (symlinks, junctions, mount points, cloud placeholders, dedup stubs). | Skipped |
| C4 | Attributes are within the allow-list: Normal, Archive, Temporary, NotContentIndexed, Compressed. Anything else (ReadOnly, Hidden, System, Offline, Encrypted, Sparse, Recall-on-open/access, Pinned, Unpinned, Device, IntegrityStream, NoScrubData…) | Skipped |
| C5 | Opening it read-only (with `FILE_FLAG_OPEN_REPARSE_POINT`) succeeds, and the OS-reported final path is strictly inside the canonical root. | Skipped |
| C6 | Hard-link count is exactly 1 (deleting one of several links frees nothing and may confuse other software). | Skipped |
| C7 | Neither created nor modified within the last *(initial)* **7 days**; timestamps in the future are suspicious. Matches the default age used by Windows Disk Cleanup for temporary files. | Skipped |
| C8 | Path passes string validation: no `..` segments, no alternate data stream (`name:stream`), no reserved device names (`CON`, `NUL`, `COM1`…), no trailing dots/spaces, no invalid characters. | Skipped |
| C9 | Not inside a protected location and not matched by a user exclusion. | Skipped |

A file's **extension or age alone never makes it safe**. Extensions are used only for grouping into
categories and to flag files that need review.

Outcome states shown in the UI:

- **Eligible** – passed every check; selectable.
- **Needs review** – passed every check but is a type that deserves a human look (executables,
  scripts, installers such as `.exe`, `.dll`, `.msi`, `.ps1`, `.bat`). Selectable, never auto-selected.
- **Skipped** – failed a check; shown with the reason; **cannot be selected**.

Estimated recoverable space is always labelled as an *estimate*. Only successfully deleted files count
as freed space.

## 5. Cleanup procedure (designed now, implemented in Phase 5, enabled in Phase 7)

Before any cleanup:

1. User has switched from Dry Run to Real mode **and** confirmed a summary dialog listing the count and
   total estimated size of the selected files.
2. Process is not elevated (R8).
3. The approved root is fully revalidated (R1–R7) and its canonical path and identity are unchanged
   since the scan.

For each selected candidate, in order:

| Step | Action |
|---|---|
| D1 | Candidate must come from the current scan session and be Eligible or Needs review. |
| D2 | Re-run string/protected-path checks (C8, C9) on the recorded path. |
| D3 | Open the file with `CreateFileW(DELETE \| FILE_READ_ATTRIBUTES \| SYNCHRONIZE, share mode = none, OPEN_EXISTING, FILE_FLAG_OPEN_REPARSE_POINT)`. If another program has it open, this fails → **skip** ("in use"). We never close other programs. |
| D4 | Through that same handle verify: still a file, no reparse attribute, attributes allowed, link count 1, **same volume serial + file ID**, **same size**, **same last-write time** as at scan time, and OS-reported final path identical to the scanned final path and inside the canonical root. Any mismatch → **skip**. |
| D5 | Mark that exact handle for deletion with `SetFileInformationByHandle(FileDispositionInfo)`. Failure → **skip**, never retried with force. |
| D6 | Close the handle; Windows removes the file. Only now is its size (read in D4) added to "space freed". |
| D7 | Log the outcome using the path **relative to the root** (no user name, no file contents). |

After cleanup the report shows deleted, failed and skipped files separately. Skipped files are never
reported as cleaned.

### Things SafeClean never does

Delete by path (`DeleteFile`, `File.Delete`, `Directory.Delete`), delete directories, use wildcards,
use `cmd.exe`/PowerShell/shell APIs for deletion, call broad cleanup APIs, change file attributes,
take ownership, change ACLs, bypass permission errors, close or kill processes, schedule
delete-on-reboot, or securely wipe data.

### Deletion is permanent

Like Windows Disk Cleanup, deleted temporary files are **not** moved to the Recycle Bin and recovery
is not guaranteed. Reasons: the Recycle Bin APIs work on paths (so they cannot give the handle-based
guarantees above), and recycling would not free space. A same-volume quarantine (handle-based rename)
may be considered in a later version.

## 6. Race conditions (time-of-check / time-of-use)

Checking a file and later deleting it leaves a gap where something could change. SafeClean's approach:

**What the design guarantees** (relying on documented Windows behaviour):

- The object deleted is **exactly the object that was validated**: validation and deletion use the same
  open handle, not a path looked up twice.
- While SafeClean holds the handle with share mode *none*, no other program can open, modify or
  replace that file.
- If a folder on the path was swapped for a junction between scan and cleanup, the OS-reported final
  path of the opened handle reveals the real location and the file is skipped.
- A file replaced or rewritten after the scan has a different file ID, size or timestamp and is skipped.

**What it cannot guarantee:**

- A program running as the same user could change a file and then restore its size and timestamps
  exactly; SafeClean would treat it as unchanged. Such a program could delete the file itself anyway.
- Kernel drivers, administrators or malware with higher privileges are outside the threat model.
- Hard links created after validation but before the handle closes would survive (no data is lost;
  only the Temp name is removed).

## 7. Threat model

**Assets:** the user's personal files, system files, other applications' working files, and privacy.
**Adversaries/causes considered:** bugs in SafeClean, unexpected file-system layouts, other software
creating links in Temp, a misconfigured `TEMP` variable, race conditions, accidental user clicks.

| ID | Threat | Mitigation |
|---|---|---|
| T1 | Bug in path logic deletes the wrong file | Layered checks: string validation, single approved root, protected list, OS-reported final path, file identity check; extensive tests in disposable sandboxes. |
| T2 | Symlink/junction inside Temp points to personal data | Reparse points are never followed during scanning and are never candidates; files opened with `FILE_FLAG_OPEN_REPARSE_POINT`. |
| T3 | A folder is swapped for a junction between scan and cleanup | Final path of the opened handle is verified before deletion; delete through that handle only. |
| T4 | File replaced or modified after scanning | File ID + size + last-write time compared under an exclusive handle. |
| T5 | File is in use by another program | Exclusive open fails → skip. Processes are never closed. |
| T6 | Hard link to a file stored elsewhere | Link count must be 1. |
| T7 | `TEMP` points somewhere dangerous | Root must be the same directory as `%LOCALAPPDATA%\Temp` and pass protected-path checks. |
| T8 | Malformed or malicious path strings | Strict validator (traversal, ADS, device names, UNC, trailing dots/spaces); deletion targets come only from enumeration. |
| T9 | SafeClean started as administrator | Manifest is `asInvoker`; cleanup disabled when elevated. |
| T10 | Cloud/offline placeholder files (e.g. OneDrive) | Offline/recall/pinned attributes and reparse points are skipped. |
| T11 | Read-only, hidden or system files | Skipped; attributes are never changed. |
| T12 | UI selects or sends wrong items | The cleanup service independently revalidates everything; the UI never decides what is deletable. |
| T13 | Accidental click | Dry Run default, explicit Real-mode switch, confirmation summary, visually distinct destructive button. |
| T14 | Privacy leak | No network code (enforced by test); logs store root-relative paths and no file contents. |
| T15 | Monitoring changes system state | Monitoring uses read-only APIs only; no kill, trim, standby-list or virtual-memory APIs (enforced by test). |
| T16 | Crash during cleanup | Each file is deleted independently; partial results are reported; errors are logged, not fatal. |
| T17 | Malicious or compromised dependency | Only Microsoft and xUnit packages; versions pinned centrally; lock files; NuGet vulnerability audit fails the build. |

## 8. Performance monitoring safety

Monitoring is **read-only**: total/available RAM (`GlobalMemoryStatusEx`), CPU usage (`GetSystemTimes`
deltas), disk capacity (`DriveInfo`), and process memory (`Process.GetProcesses`, tolerating access
errors). SafeClean will not empty the standby list, trim working sets, kill processes, change virtual
memory, or disable services/startup items, and offers **no "RAM cleaner" button**: Windows deliberately
uses spare RAM as a cache, so high usage is normal and "freeing" it usually makes the PC slower.

## 9. Privacy

- Everything runs locally. No telemetry, update checks or cloud services.
- Logs are stored in `%LOCALAPPDATA%\SafeClean\Logs` and contain events, counts, error codes and
  root-relative paths. They never contain file contents.

## 10. Known limitations

- Only the current user's Temp directory is supported.
- If the Temp folder or any parent is a junction (e.g. a relocated `C:\Users`), cleanup is refused.
- Case-sensitive directories (a WSL feature) are not specially handled; final-path comparisons use
  exact (ordinal) matching of OS-produced strings, which errs toward skipping.
- Freed space is the sum of logical file sizes; the actual change in free disk space can differ
  (cluster rounding, compression, other programs writing).
- No perfect race-freedom claim — see section 6.

## 11. Deliberately unsupported in version 1

Windows directory and system temp, Windows Update/installer caches, Prefetch, Recycle Bin, personal
folders, browser caches, other applications' caches, other users' or shared temp directories,
registry, startup entries, restore points, event logs, page/hibernation files, memory "cleaning",
directory deletion, secure wiping, scheduled/background cleaning, and any location whose safety has
not been independently assessed, documented and tested.
