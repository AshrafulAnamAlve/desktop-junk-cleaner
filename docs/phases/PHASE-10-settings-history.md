# Phase 10 — Settings, Exclusions, Logging Controls and Cleanup History

**Status:** ✅ Done (2026-10-31) · **Depends on:** Phase 7 · **Deletes anything?** Only SafeClean's own old log
files, only when the user asks and answers *Yes*, through the existing engine (no new deletion code)

## Goal

Let users adjust SafeClean **only in safer directions**, see past cleanups, and control logging.

## Rule

**Settings can make SafeClean stricter, never looser.** No setting can add a cleanup location, remove a
protected location, lower the 7-day age limit, or make Real mode start enabled.

## What was built

| Area | Files | Notes |
|---|---|---|
| Settings model and rules | Core `Models/UserSettings.cs`, `Policies/SettingsPolicy.cs`, `Policies/WildcardPattern.cs` | Minimum age 7–365 days, exclusions (up to 100), log level, log retention 7–90 days. **Real mode is not a setting.** Every value is validated in Core; invalid settings are never saved |
| Exclusions | `SettingsPolicy.ValidateExclusion`, `CandidatePolicyContext.ForSettings` | A folder or file written **relative to Temp** (`Installers\Old`, trailing `\` allowed) or a file-name pattern with only `*` and `?` (`*.log`). Full paths, `..`, `.`, network paths, `/`, streams, device names and patterns with folders are refused with a reason. Matching files become *Skipped (Excluded)* |
| Same rules for scan and cleanup | `TempScanner`, `CleanupService` (optional `IUserSettingsProvider`) | Both build their rules with `CandidatePolicyContext.ForSettings`. Cleanup reads the settings again when it starts, so an exclusion added after a scan is honoured (the gap noted by the Phase 6 review is closed) |
| Settings storage | Infrastructure `Settings/UserSettingsStore.cs` | `%LOCALAPPDATA%\SafeClean\settings.json` with built-in `System.Text.Json`; 256 KB limit; **atomic save** (complete temporary file, then replace). No file → defaults, nothing created |
| Unreadable settings pause cleanup | `CleanupRefusal.SettingsUnreadable`, `CleanupPolicy.CheckRequest` | Corrupt or hand-edited (e.g. age 3) settings → safe defaults **and the cleanup is refused** until valid settings are saved, because exclusions the user relies on might be missing. Real mode can't be switched on meanwhile; Settings and Junk Cleaner explain why |
| Log level | `FileLoggerProvider.MinimumLevel`, `FileLogger.IsEnabled`, `App.xaml.cs` | Normal / warnings and errors / errors only, applied at start and on save. **The cleanup audit trail (one line per file, D7) is always written** |
| Log folder | `Views/SettingsView.xaml(.cs)` | Path shown in a read-only box with a "Copy path" button |
| SafeClean's own old logs | Infrastructure `Logging/LogMaintenance.cs`, `Logging/LogFolderRootResolver.cs` | Manual only: "Delete old SafeClean logs…" → read-only search → *Yes/No* question (default *No*) → the **same** `CleanupService` and handle-based engine. Only files directly in `…\SafeClean\Logs` named exactly `safeclean-YYYYMMDD.log`, at least as old as the retention by name **and** by file age |
| Cleanup history | Core `Models/CleanupHistoryEntry.cs`, Infrastructure `Settings/JsonLinesCleanupHistory.cs` | `%LOCALAPPDATA%\SafeClean\history.jsonl`, one line per real cleanup (Temp folder or SafeClean logs): start time, deleted / skipped / failed, bytes freed, duration, refusal. **No field for file names or paths.** Unreadable lines skipped and counted |
| Screens | `ViewModels/SettingsViewModel.cs`, `HistoryViewModel.cs`, `Choice.cs`, `Views/SettingsView.xaml`, `HistoryView.xaml`, `Services/UserPrompt.cs` | Settings page (choices, exclusion list with reasons, log controls, Save / Undo changes); History page (newest first, "Clear history" after *Yes*) |
| Guardrails | `ForbiddenApiTests` | Replacing/moving files allowed **only** in `UserSettingsStore.cs`; delete APIs still only in `HandleBasedFileDeleter.cs`; test that each allow-listed file really uses what it is allowed |

## Tests

| Case (from the plan) | Test | Result |
|---|---|---|
| Age setting 3 days → rejected, stays 7 | `SettingsPolicyTests.AgeBelowSevenDays_IsRejected`, `SettingsAndHistoryStoreTests.InvalidSettings_AreNeverWritten`, `UnreadableOrInvalidFile_GivesDefaults_AndAProblemThatPausesCleanup` | ✅ |
| Exclusion `C:\Windows` / `..\..` / Documents → rejected | `ExclusionsOutsideTemp_OrMalformed_AreRejected` (14 inputs), `FullPaths_AreRefused_WithAHelpfulReason`, App `Exclusions_OutsideTemp_OrMalformed_AreRefusedWithAReason` | ✅ |
| Exclusion inside Temp → matching files Skipped (`Excluded`) | `ExcludedFolder_SkipsEverythingInside_AsExcluded`, `ExcludedPattern_SkipsMatchingNamesAnywhere`, `SettingsInScanAndCleanupTests.Scan_AppliesExcludedFolders_Patterns_AndALongerAge` | ✅ |
| Exclusion added after the scan is honoured by the cleanup (Phase 6 review) | `ExclusionAddedAfterTheScan_IsHonouredByTheCleanup` | ✅ |
| Corrupt settings JSON → defaults, app runs | `UnreadableOrInvalidFile_GivesDefaults_AndAProblemThatPausesCleanup` (6 kinds of bad file) | ✅ (and cleanup paused: `UnreadableSettings_RefuseTheWholeCleanup`, `JunkCleaner_UnreadableSettings_PauseRealMode`) |
| Settings cannot enable Real mode at startup | `Settings_HaveNoWayToStartInRealMode` | ✅ |
| Log retention deletes only matching old SafeClean logs in a sandbox log folder | `LogMaintenanceTests` (11 look-alikes; everything else byte-for-byte unchanged; other folder refused; elevated refused; stretched plan; retention range; exact name) | ✅ |
| History round-trips; corrupt line ignored; no file names | `History_RoundTrips_NewestFirst_AndStoresNoFileNames`, `History_CorruptLines_AreSkippedAndCounted_NeverFatal`, `JunkCleaner_RecordsEachRealCleanupInHistory_WithoutFileNames` | ✅ |

Also: log level never hides the audit trail; saving clears an earlier problem; duplicates removed; one bad exclusion
blocks the whole save; 100-entry limit; wildcard matching; no "prefix trap" (`Keep` doesn't exclude `KeepNot`); old-log
deletion asks nothing when nothing is old, deletes nothing on *No*, uses the **saved** retention and records the run;
history clear only after *Yes* and empties the file instead of deleting it.

## Verification results (actually run, 2026-10-31)

| Check | Result |
|---|---|
| Builds | ✅ Debug (from scratch) and Release: 0 warnings, 0 errors |
| `dotnet test` (all three test projects) | ✅ 803 total: 796 passed, 0 failed, 7 skipped (Core 441; Infrastructure 194 + 7 skipped as before; App 161) |
| Mutation check | ✅ 10/10 planted bugs caught (table below) |
| Settings page in the real app (UI Automation, nothing saved) | ✅ Age choices start at 7 days; log folder shown; `C:\Windows` refused with "Write it relative to your Temp folder…"; `..\Documents` refused; `*.bak` added on screen only ("Not saved yet.") and removed again by *Undo changes*; sidebar still DRY RUN |
| History page in the real app | ✅ "No cleanups yet" with an explanation (this PC has no history file) |
| Owner's files unchanged | ✅ `settings.json` and `history.jsonl` did not exist before and were not created; no Save, *Delete old logs*, *Copy path* or *Clear history* was pressed; the Junk Cleaner was not opened |
| Accessibility name audit (Settings, History) | ✅ 0 unnamed controls (1 found and fixed: the list of save problems had no name) |

## Mutation check (bugs planted on purpose, one at a time, then restored)

Each bug was planted in a fresh copy of the source, the projects were rebuilt from scratch, the Core and
Infrastructure tests were run, and the original file was put back (SHA-256 checked). Progress was logged before and
after every step, because the first attempt (2026-10-28) was cut off when the session ended and left bug S4 in
`SettingsPolicy.cs`; it was found, restored from its clean copy and the whole check was run again on 2026-10-31.

| # | Planted bug | Result | Caught by |
|---|---|---|---|
| L1 | Log-name rule removed when **finding** old logs (delete re-check still there) | ✅ caught | `OnlyOldSafeCleanLogs_DirectlyInTheFolder_AreFound`, `LongerRetention_FindsFewerFiles` |
| L2 | Log-name rule removed in **both** find and delete | ✅ caught | 4 tests, including `Deleting_RemovesExactlyThoseFiles_AndEverythingElseSurvives` |
| L3 | Any folder accepted as the log folder | ✅ caught | `AFolderThatIsntSafeCleansLogFolder_IsRefused` |
| L4 | Delete re-check removed (a plan could be stretched to other files) | ✅ caught | `APlanCannotBeStretched_ToIncludeOtherFiles` |
| L5 | Retention range 7–90 not enforced | ✅ caught | `RetentionOutsideTheAllowedRange_IsRefused` |
| S1 | Exclusions ignored when building the rules | ✅ caught | 4 tests in Core and Infrastructure, including `ExclusionAddedAfterTheScan_IsHonouredByTheCleanup` |
| S2 | Unreadable settings ignored by the cleanup | ✅ caught | `UnreadableSettings_RefuseTheWholeCleanup` |
| S3 | Minimum age below 7 days accepted | ✅ caught | `AgeBelowSevenDays_IsRejected`, `InvalidSettings_AreNeverWritten`, `UnreadableOrInvalidFile_…` |
| S4 | Full / rooted paths not refused up front | ⚠️ survived, then ✅ caught | The path validator behind it still refused every full path, so nothing unsafe got through — but the user saw the vaguer "This isn't a valid folder or file name". Added `FullPaths_AreRefused_WithAHelpfulReason`; re-planted S4: 5 failing; restored |
| S5 | Cleanup audit trail no longer exempt from the log level | ✅ caught | `HigherLogLevel_HidesInformation_ButNeverTheCleanupAuditTrail` |

**Result: 10/10 caught** (9 on the first full run, S4 after one added test). Final run on the restored sources:
0 failing; no planted text left in `src`; all target files identical to their clean copies.

## Changes from the original plan (and why)

| Plan | What was done | Reason |
|---|---|---|
| Delete old logs **automatically on startup** | A button, a read-only search first, then a *Yes/No* question (default *No*) | No file is deleted unless the user asks for it — the project's first rule |
| A second, equally strict entry point to the engine | None needed: the log folder goes through `RootPolicy` (`LogFolderRootResolver`), the existing `TempScanner` and the existing `CleanupService` | Reusing the tested pipeline adds no deletion code; the architecture tests stay as strict as before |
| "Open log folder" with `explorer.exe` | "Copy path" button | Starting another program would need an exception in the forbidden-API test; copying the path needs none |
| "Clear history" removes `history.jsonl` | Empties the file | Deleting a file would need a delete API outside the engine; emptying is ordinary writing |
| History records the mode | Records *what* was cleaned (Temp folder / SafeClean logs) and any refusal | Only real cleanups are recorded (Dry Run changes nothing), so the mode would always be "Real" |
| Invalid settings → defaults + warning | Defaults + warning **+ cleanup paused** until saved again | Defaults would silently drop the user's exclusions — the stricter choice is to not clean |
| Manual check of settings persistence across restarts | Automated (`Saved_Settings_AreReadBackExactly…` uses a fresh store, like a restart); the real app was only looked at, never saved | Saving would write to the owner's real `%LOCALAPPDATA%`; listed for the owner's manual check in Phase 11 |

## Done when

- [x] Settings, exclusions, logging controls and history work and are documented in `SECURITY.md` (§13) and `README.md`

## Known limitations

- Exclusions apply from the **next** scan; files already shown keep their status until you scan again (cleanup still
  honours a new exclusion).
- Patterns match file names only (`*.log`), not folders; to skip a folder, add it as a folder.
- Old SafeClean logs are only removed when you press the button; nothing is cleaned in the background.
- Lowering the log level hides details from new log lines only; existing log files are unchanged.
