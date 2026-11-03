# SafeClean Security Policy and Threat Model

> Priority order: **protect user data → correctness → usability → performance.**
> A skipped file is always preferable to deleting an important one. Uncertainty means *skip*.

Status: **Policy defined (Phase 0); guardrails in place (Phase 1); policy rules implemented and unit-tested
in `SafeClean.Core` (Phase 2); read-only scanner implemented (Phase 3); Dry Run preview screen (Phase 4);
handle-based cleanup engine implemented and tested on synthetic sandbox files only (Phase 5); cleanup
test gate passed (Phase 6, 2026-10-17); cleanup connected to the Junk Cleaner screen behind Real mode
and a confirmation dialog (Phase 7, 2026-10-17); stricter-only settings, exclusions, log controls and cleanup
history (Phase 10, 2026-10-28); security review and failure simulation (Phase 11, 2026-11-02; see `TESTING.md`).
Real deletion now happens only when the user switches Real mode on, selects files, presses the red
delete button and confirms — and the cleanup service re-checks every file first.**
This document is updated whenever behaviour changes. Values marked *(initial)* may be tuned later,
but only with a documented reason and matching tests.

---

## 1. Safe-by-default behaviour

| Rule | How it is enforced |
|---|---|
| App starts in read-only scanning mode | Real mode is off at every start and never saved; the delete button exists only in Real mode and only works with a selection, a scan under 30 minutes old and a confirmed dialog. Real mode switches itself off after every cleanup (tested in `JunkCleanerCleanupTests`). |
| Dry Run is the default mode | `CleanupMode.DryRun` is the default value (value 0); enforced by `SafetyDefaultsTests`. |
| Scanning never modifies files | Scanner opens files only with `FILE_READ_ATTRIBUTES` and full sharing; it never requests write/delete access. |
| No automatic cleanup on first launch or in the background | Cleanup runs only from an explicit user confirmation dialog. There are no timers, schedulers or startup tasks. |
| Never runs as administrator by default | App manifest requests `asInvoker`. If the app is started elevated anyway, **cleanup is disabled** (scanning still works). |
| Never weakens Windows security | No code touches Defender, UAC, services, registry, ACLs, ownership or security settings. |
| No network access | No networking APIs are used; `System.Net.Http` is not even imported implicitly (`ArchitectureTests.NoProject_ImportsNetworkingTypesImplicitly`). `ForbiddenApiTests` fails if a networking API appears in the source, and `CompiledAssemblyTests` fails if the compiled program references a networking library or makes a Windows call that hasn't been reviewed. |

## 2. Approved cleanup root (version 1)

Version 1 supports **exactly one** location: the current user's own temporary directory.
It is resolved at runtime (never hard-coded) and accepted only if **all** of these hold:

| # | Root requirement |
|---|---|
| R1 | Obtained from `Path.GetTempPath()` (honours the user's `TMP`/`TEMP` settings). |
| R2 | Fully qualified local drive path (no UNC `\\server\share`, no device path `\\?\`/`\\.\`). |
| R3 | On a fixed, NTFS-formatted volume. |
| R4 | Exists, is a real directory, and **neither it nor any parent folder is a reparse point** (symlink, junction, mount point). |
| R5 | It is the **same directory** (same volume serial number and file ID) as `%LOCALAPPDATA%\Temp`. A `TEMP` variable redirected to `C:\`, `D:\Data`, `C:\Windows\Temp`, etc. is refused. An identity that could not be read (all zeros) never matches. |
| R6 | It is not a drive root, not the user profile root, does not contain any protected location, and is not inside Windows, Program Files or ProgramData. |
| R7 | The canonical root used for all comparisons is the path the **operating system reports** for an opened handle to the directory (`GetFinalPathNameByHandle`), not a string we built. |
| R8 | Cleanup (not scanning) is refused while the process is elevated. |

If any check fails, SafeClean shows why and offers **no cleanup at all**.

In code, `RootPolicy` (Core) is the only place that can create an `ApprovedRoot`, so every scan and cleanup
is forced through these rules. There is no bypass, not even for tests: sandbox tests supply their sandbox
folder's identity as the expected identity and pass the same checks.

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
| C5 | Opening it read-only (with `FILE_FLAG_OPEN_REPARSE_POINT`) succeeds; the OS-reported final path is strictly inside the canonical root (**exact**, case-sensitive comparison of OS-produced strings) and is **exactly equal** to the path where the file was found. | Skipped (`OutsideRoot` / `PathMismatch`) |
| C6 | Hard-link count is exactly 1 (deleting one of several links frees nothing and may confuse other software; 0 means it is already being deleted). | Skipped |
| C7 | Neither created nor modified within the last *(initial)* **7 days** (uses the newer of the two dates). Timestamps in the future or before 1980 are untrustworthy. Matches the default age used by Windows Disk Cleanup for temporary files. Settings may raise but never lower the age. | Skipped (`TooRecent` / `UntrustworthyTimestamp`) |
| C8 | Path passes string validation: canonical `X:\...` form only; no `.`/`..` segments, no empty segments or trailing `\`, no forward slashes, no alternate data stream (`name:stream`), no device (`\\?\`, `\\.\`) or network prefixes, no reserved device names (`CON`, `PRN`, `AUX`, `NUL`, `COM0-9`, `LPT0-9`, superscript variants, `CONIN$`, `CONOUT$`, `CLOCK$`, also with extensions), no trailing dots/spaces, no `<>"\|?*`, control characters or lone surrogates, names ≤ 255 and paths ≤ 32,767 characters. | Skipped |
| C9 | Not inside a protected location and not matched by a user exclusion. | Skipped |

A file's **extension or age alone never makes it safe**. Extensions are used only for grouping into
categories and to flag files that need review. Every failed check is reported (not just the first), and
an uninitialised status value means *Skipped*, never *Eligible*.

Outcome states shown in the UI:

- **Eligible** – passed every check; selectable.
- **Needs review** – passed every check but is a type that deserves a human look (executables,
  scripts, installers such as `.exe`, `.dll`, `.msi`, `.ps1`, `.bat`). Selectable, never auto-selected.
- **Skipped** – failed a check; shown with the reason; **cannot be selected**.

Estimated recoverable space is always labelled as an *estimate*. Only successfully deleted files count
as freed space.

### How scanning stays read-only (implemented in Phase 3)

- Every file and folder is opened with **only** `FILE_READ_ATTRIBUTES` (no read, write or delete of
  contents), with full sharing so other programs are never blocked, and with `FILE_FLAG_OPEN_REPARSE_POINT`
  so links are opened as themselves. A test pins this access right, and a test proves a file locked by
  another program can still be inspected (which would fail if more access were requested).
- Paths are passed to Windows with the `\\?\` prefix only after validation; this also makes paths longer
  than 260 characters work while the Windows long-path setting is off.
- "Never enter a link" has three layers: (1) entries listed as links are never queued for entering;
  (2) each folder is re-inspected just before it is listed and skipped if it became a link or moved;
  (3) every file's OS-reported location must exactly equal where it was found (C5). A mutation test showed
  that removing layer 1 alone is still blocked by layer 2, and removing both is caught by tests.
- Listing a folder uses .NET's path-based enumeration. If a folder were swapped for a junction in the
  instant between the re-check and the listing, entries from the other location could appear — but only
  as **Skipped** (layer 3). Deletion never relies on scan-time checks alone (§5).
- Limits: depth 16, 250,000 entries; unreadable folders are listed as skipped; vanished entries are counted.

### How the Junk Cleaner screen handles selection (implemented in Phase 4)

- Nothing is selected after a scan. "Select all safe" and category checkboxes select only *Eligible*
  files; *Needs review* files must be ticked one by one; *Skipped* entries can't be selected at all.
- The selection is kept as **candidate IDs from the current scan session**, never as paths. The Dry Run
  preview is calculated in Core (`DryRunPlanner`), which ignores and counts any ID that is unknown or not
  selectable. A new scan discards the old selection.
- File names are displayed with Unicode direction-control and invisible characters made visible, so a
  name such as "‮gpj.exe" can't pose as "exe.jpg" (the real extension still marks it *Needs review*).
- Automated guards: view models only use Core interfaces, and the App project cannot reference cleanup
  code until Phase 7.

## 5. Cleanup procedure (implemented in Phase 5, connected to the screen in Phase 7)

Before any cleanup:

1. User has switched from Dry Run to Real mode **and** confirmed a summary dialog listing the count and
   total estimated size of the selected files. *(The screen's job — Phase 7.)*
2. Process is not elevated (R8).
3. The approved root is fully revalidated (R1–R7) and its canonical path and identity are unchanged
   since the scan.
4. The scan started no more than *(initial)* **30 minutes** ago, and not "in the future" (a clock change
   makes its age untrustworthy).
5. The requested mode is exactly Dry Run or Real; any other value is refused, never treated as Real.

If any of 2–5 fails, the cleanup service refuses the **whole request** and touches no file.

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

### How the engine is built (implemented in Phase 5)

- **One file can delete:** `Infrastructure/Cleanup/HandleBasedFileDeleter.cs`. A test fails if the
  delete access right, `SetFileInformationByHandle` or the disposition structure appears in any other
  source file. Another test allows only `CleanupService` to call the engine, and the App project can't
  reference either of them until Phase 7.
- **Decisions are pure logic in Core** (`CleanupPolicy`), unit-tested with plain data: the whole-request
  checks above, D1 + D2 (the recorded facts are judged again by today's rules, so a forged "eligible"
  verdict doesn't help), and D4.
- **Layered re-checks:** the engine repeats D1/D2 itself, so it can't be pointed at anything the rules
  wouldn't select (not even with a different root), and D4 independently requires the file to be
  selectable *now*. D4 then requires an exact match with the scan on location (path and OS-reported final
  path), identity, size, last-write time, creation time and attributes, and exactly one link.
- **The exclusive handle:** `DELETE | FILE_READ_ATTRIBUTES | SYNCHRONIZE` (no reading or writing of
  contents), share mode *none*, `FILE_FLAG_OPEN_REPARSE_POINT`, and no backup semantics, so a folder
  can't even be opened. All D4 facts are read through this same handle.
- **Windows errors never count as deleted:** missing → *skipped (missing)*, in use → *skipped (in use)*,
  access denied → *skipped (access denied)*, anything else → *failed*. Bytes are added only after the
  disposition succeeded **and** the handle is closed.
- **One file at a time.** Cancellation is checked between files and never throws; files not reached are
  listed as "not attempted". Progress is reported only every 50 files, because each report queues work ahead of
  mouse clicks on the UI thread: reporting per file delayed a Cancel click by about 4,000 files. An unexpected error stops the cleanup instead of carrying on. The report
  lists every selected ID exactly once, and a Dry Run report can never contain a deletion.
- **Dry Run** re-inspects each file through the scanner's read-only handle and applies the same D4
  comparison; nothing is opened exclusively or changed.
- **Audit log (D7):** one line per file with its outcome, its path relative to the Temp folder and the
  Windows error code, plus a summary line. Unknown items are logged as "(item N, path not shown)".

### Things SafeClean never does

Delete by path (`DeleteFile`, `File.Delete`, `Directory.Delete`, delete-on-close), move or overwrite
files, delete directories, use wildcards,
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
| T13 | Accidental click | Dry Run default; Real-mode switch off at every start and after every cleanup; red delete button shown only in Real mode, with no keyboard shortcut; confirmation dialog whose default button is Cancel (Enter, Esc and closing all cancel — checked end to end); no "don't ask again". |
| T14 | Privacy leak | No network code (enforced by test); logs store root-relative paths and no file contents. |
| T15 | Monitoring changes system state | Monitoring uses read-only APIs only; no kill, trim, standby-list or virtual-memory APIs (enforced by test). |
| T16 | Crash during cleanup | Each file is deleted independently; partial results are reported; errors are logged, not fatal. Closing the window stops the cleanup before the next file and shows its report first. |
| T17 | Malicious or compromised dependency | Only Microsoft and xUnit packages; versions pinned centrally; lock files; NuGet vulnerability audit fails the build. |
| T18 | File name disguised with hidden or right-to-left characters | Names shown with such characters revealed; the real extension still decides "Needs review". |
| T19 | Something else changes the Temp folder (Windows Storage Sense, installers) | Scans are snapshots; cleanup revalidates every file (§5); unexpected changes are investigated, not assumed. |
| T20 | The user's exclusions are lost or misread (corrupt or hand-edited settings) | Unreadable, invalid or unknown settings pause cleanup and old-log deletion; replacing them needs a Yes and keeps a copy; a valid value can't be lowered by saving (§13). |
| T21 | A test run reaches real data | Sandbox mode exists only in Debug builds and keeps its settings, history and logs beside the sandbox (§12). |

## 8. Performance monitoring safety

Monitoring is **read-only**: total/available RAM (`GlobalMemoryStatusEx`), CPU usage (`GetSystemTimes`
deltas), disk capacity (`DriveInfo`), and process memory (`Process.GetProcesses`, tolerating access
errors). SafeClean will not empty the standby list, trim working sets, kill processes, change virtual
memory, or disable services/startup items, and offers **no "RAM cleaner" button**: Windows deliberately
uses spare RAM as a cache, so high usage is normal and "freeing" it usually makes the PC slower.

*Implemented in Phase 8* (`Infrastructure/Monitoring/SystemMonitor`): every Windows call used only reads.
`ForbiddenApiTests` fails if code appears that kills, closes, pauses or re-prioritises programs, changes their
memory limits or processor affinity, trims memory, purges the standby list, changes the file cache, or
controls services. Values Windows won't give are shown as unknown ("n/a", "measuring"), never estimated.

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
- File symbolic links can't be created in automated tests on this PC without Developer Mode, so those two
  tests are skipped (reported, not hidden). Folder links (junctions) are fully tested, and file symlinks are
  rejected by the same reparse-point rule, which has its own unit tests.
- Protected folders redirected to network shares are left out of the protected list (they can't be inside
  the local Temp folder); the number left out is logged.
- No perfect race-freedom claim — see section 6.
- A Dry Run preview can't know whether a file will be in use or access-denied at cleanup time, so it
  reports what SafeClean would **try** to delete (pinned by a test).
- If another program holds a handle that only reads a file's attributes, Windows removes the file when that
  handle closes rather than at once. It is still certain to go: nobody can open a file pending deletion, and
  cancelling the deletion needs delete access, which SafeClean's exclusive open denied to everyone else.
- The 8.3 short-name alias test and the encrypted-file (EFS) test can't run on this PC (short names are off
  on the test drive; EFS would create an encryption certificate and isn't available on Windows Home). Both
  cases are refused by checks that are tested in other ways (OS-reported final path; attribute allow-list).
- Log redaction replaces the exact user-profile path. A path written another way (for example an 8.3 short form
  inside an error message) would not be replaced, and the audit log contains Temp-relative file names, which can
  themselves contain personal words. Logs stay on this PC, in the user's own folder.
- Deleting a file also removes any alternate data streams attached to it (such as the "downloaded from the
  internet" mark). "Space freed" counts the main contents only, so it can slightly under-report.
- Elevation is detected by membership of the active Administrators group. An elevated process whose token isn't in
  that group wouldn't be detected; its deletions are still limited by every other rule in this document.
- If two copies of SafeClean run at once, each keeps the settings it loaded at its start. Each file is still
  re-checked before deletion, so the second copy can't delete what the first already removed or changed.
- If SafeClean is killed in the middle of a cleanup (Task Manager, power cut), no report or history entry is written
  for that run. Files already deleted stay deleted, the rest are untouched, and the audit log can miss at most the one
  file being deleted at that moment.
- A scan stops at 250,000 entries and says so on screen; files beyond the limit aren't listed and can't be selected.
- **Open finding (Phase 11, low severity):** while SafeClean runs as administrator, it still writes its own log, settings and history in `%LOCALAPPDATA%\SafeClean` without checking for links. That folder can be changed without administrator rights, so malware already running as the user could aim those writes at a protected file if the user chooses to run SafeClean as administrator. It doesn't apply to a normal run, and SafeClean never asks for administrator rights. Planned fix: write nothing while elevated. Until then, don't run SafeClean as administrator.

## 11. Deliberately unsupported in version 1

Windows directory and system temp, Windows Update/installer caches, Prefetch, Recycle Bin, personal
folders, browser caches, other applications' caches, other users' or shared temp directories,
registry, startup entries, restore points, event logs, page/hibernation files, memory "cleaning",
directory deletion, secure wiping, scheduled/background cleaning, and any location whose safety has
not been independently assessed, documented and tested.

## 12. The cleanup screen and the Debug-only sandbox mode (Phase 7)

**Steps in front of every real cleanup** (all tested in `JunkCleanerCleanupTests`; the last five were also
checked end to end in sandbox mode):

1. A completed scan younger than 30 minutes (older results: no dialog, "scan again").
2. Real mode switched on — off at every start, impossible while elevated, off again after every cleanup.
3. At least one file selected (only *Eligible* in bulk; *Needs review* only one by one; *Skipped* never).
4. The red "Delete N files…" button, shown only in Real mode, with no keyboard shortcut.
5. A confirmation dialog: count, estimated size, categories, a "needs review" warning, "not moved to the
   Recycle Bin". **Cancel is the default button and has focus**; Enter, Esc and closing the window cancel.
6. The cleanup service then applies every check in §5 again, file by file.

The view model sends only **candidate IDs** of the current scan and `CleanupMode.Real`; it never decides
what may be deleted. Cleanup can start **only** from the Junk Cleaner screen: the Dashboard's quick action
starts a read-only scan, and red (destructive) buttons exist only on the Junk Cleaner and its dialog
(`ArchitectureTests.RedDangerButtons_AppearOnlyOnTheJunkCleanerScreen`, `DashboardViewModelTests`). After the cleanup the scan results are closed (a new scan is needed) and the report
shows exactly what the service returned; "space freed" comes only from files actually deleted.

**Debug-only sandbox mode** (for trying cleanup by hand on synthetic files):

- Started with `--sandbox-root <folder>` — **Debug builds only**. The code sits inside `#if DEBUG` (checked by
  `ArchitectureTests.SandboxMode_ExistsOnlyInDebugBuilds`), and a Release build was checked to contain neither
  the sandbox class nor the `--sandbox-root` switch.
- The folder must contain a plain file named `.safeclean-sandbox`; otherwise SafeClean **refuses to start**.
  If the marker disappears later, the folder is refused (no scan, no cleanup). There is **no fallback** to the
  real Temp folder.
- All root rules still apply (fixed NTFS drive, no links, not protected…) except R5: the folder is compared
  with itself instead of `%LOCALAPPDATA%\Temp`.
- An orange "SANDBOX MODE" banner names the folder on every page, and the confirmation dialog repeats it.
- A sandbox run keeps its settings, history and logs in a folder beside the sandbox (`<sandbox>.appdata\SafeClean`), so
  it can never read or change the real ones, and "Delete old logs" can reach only synthetic log files. (Found in the
  Phase 11 review: until then a sandbox run used the real files.)
- `ManualSandboxSetup` (an opt-in test) creates such a folder of synthetic files under `<repository>\.sandbox\`.

## 13. Settings, exclusions, SafeClean's own logs and history (Phase 10)

**Settings can make SafeClean stricter, never looser.** Stored in `%LOCALAPPDATA%\SafeClean\settings.json`:

| Setting | Allowed values | Effect |
|---|---|---|
| Minimum file age | 7 (default) to 365 days | Files changed more recently are kept. Below 7 is rejected. |
| Exclusions | Up to 100 entries | Matching files are listed as *Skipped (Excluded)* and never deleted. |
| Log level | Normal / warnings and errors / errors only | Less detail in the log; **the cleanup audit trail (one line per file, D7) is always written.** |
| Log retention | 7 to 90 days | How old SafeClean's own logs must be before "Delete old logs" may remove them. |

There is **no setting** for Real mode, cleanup locations or protected folders.

- **Exclusions** are written relative to the Temp folder (a folder or file, e.g. `Installers\Old`) or as file-name
  patterns (`*.log`, `~*.tmp`; only `*` and `?`, no regular expressions). Full paths, `..`, `.`, streams, device names,
  forward slashes and patterns containing folders are rejected with a reason. Exclusions only add skips, so a parsing
  mistake can only make SafeClean clean *less*.
- **Scanning and cleanup use the same settings** (`CandidatePolicyContext.ForSettings`). Cleanup reads them again at
  the start: an exclusion added after a scan is honoured; a removed one doesn't matter, because files excluded at scan
  time were never selectable.
- **Validation before saving:** invalid settings are never written. Saving is **atomic** (a complete temporary copy
  replaces `settings.json`); `UserSettingsStore` is the only source file allowed to move, copy or replace a file, and
  only its own settings file.
- **Saving can't lower a value by accident:** a valid saved value that isn't one of the preset choices (say 45 days) stays
  on offer, so changing something else and saving keeps it.
- **Unreadable or invalid saved settings** (corrupt, hand-edited below the minimum, a full path as exclusion, a name
  SafeClean doesn't know such as a mistyped `"exclusion"`): safe defaults are used **and cleanup is refused**
  (`CleanupRefusal.SettingsUnreadable`) until valid settings are saved, because exclusions the user relies on might be
  missing. Real mode can't be switched on, and old logs aren't offered or deleted, because the retention the user chose
  is unknown. A temporary problem (say a lock by antivirus) goes away by restarting SafeClean.
- **Replacing unreadable settings is a deliberate step:** Save asks first (the default answer is *No*, which keeps cleanup
  paused and the file as it was) and keeps a copy of the old file as `settings.json.unreadable`.

**SafeClean's own old logs** (`ILogMaintenance`) — the only deletion added in this phase:

- **Never automatic.** Only when the user presses "Delete old SafeClean logs…" in Settings and answers *Yes*
  (the default answer is *No*).
- **No new deletion code:** the log folder goes through the same root rules (R2–R7, compared with itself), the same
  read-only scan and candidate rules (minimum age = retention days), and the same `CleanupService` and handle-based
  engine as Temp cleanup — so elevation, re-checking and the exclusive handle all apply.
- Narrowed further: the folder must be SafeClean's own (path ends in `\SafeClean\Logs`); only files **directly** in it,
  named **exactly** `safeclean-YYYYMMDD.log` (lower case, real date), older than the retention by **both** their
  timestamps and the date in the name. The name rule is applied when finding **and again** just before deleting.

**Cleanup history** (`%LOCALAPPDATA%\SafeClean\history.jsonl`): one line per real cleanup with date, what was cleaned,
counts, bytes freed, duration and any refusal. The entry type has **no field for file names or paths**. Unreadable
lines are skipped and counted. "Clear history" (after a Yes/No question) **empties** the file; nothing is deleted.

The "Open log folder" button from the plan was replaced by **Copy path**, so SafeClean never launches another program.

**If the log file can't be written** (Phase 11), SafeClean keeps working and shows a notice on every page saying the
session isn't being recorded, so the on-screen report is the only record of a cleanup. It checks at startup, whatever the
log level, and again on every write.
