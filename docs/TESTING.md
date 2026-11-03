# SafeClean Testing

## Commands

```powershell
dotnet build SafeClean.sln -c Debug               # build everything (warnings are errors)
dotnet test --solution SafeClean.sln -c Debug     # run all automated tests
dotnet run --project src/SafeClean.App            # start the app
dotnet list SafeClean.sln package --vulnerable --include-transitive   # dependency audit
```

Tests run on **Microsoft.Testing.Platform** (enabled in `global.json`), which xUnit v3 supports natively.
That is why the command uses `--solution` and why no `Microsoft.NET.Test.Sdk` package is needed.

## Ground rules

- **Destructive tests never touch the real Temp folder, personal files or Windows folders.**
  File-system tests create a disposable sandbox under the test project's own output folder
  (`bin/.../TestSandbox/<random id>`), create synthetic files there, and delete only that sandbox.
- Junction and link targets in tests always point to *other sandbox folders*, never to real data, so
  even a bug cannot reach personal files.
- Tests that need a Windows privilege the machine may not grant (e.g. creating file symlinks without
  Developer Mode) are reported as **skipped**, never silently passed.
- Tests that only *read* the real Temp location (root resolution) must not create, change or delete
  anything there.

## Guardrail tests (implemented in Phase 1)

| Test | Count | What it protects |
|---|---|---|
| `SafetyDefaultsTests` | 4 | Uninitialised mode = Dry Run; app starts in Dry Run; min age ≥ 7 days; depth limit bounded. |
| `ArchitectureTests` (Core) | 3 | Core references no UI/Infrastructure/App assembly, has no project/package references, and contains no file-system calls. |
| `ArchitectureTests` (Infrastructure) | 1 | Infrastructure references no UI or App assembly. |
| `ForbiddenApiTests` | 27 | 25 forbidden-API patterns (path-based deletion, shell/process launching, process killing, memory trimming, registry, networking) are absent from `src/`; the scanner really sees the source files; every pattern catches known-bad sample code. *Since Phase 5:* 7 more patterns, and the deletion engine is the only allow-listed file (see "Cleanup engine tests"). |
| `FileLoggerTests` | 7 | Format, profile-path redaction (case-insensitive), exception details, daily files, unusable folder doesn't crash, logging after dispose ignored, default log folder is outside Temp. |
| `ArchitectureTests` (added later) | 3 | `unsafe` only in `Infrastructure/Native` (Phase 3); view models use Core interfaces only; the App has no access to cleanup code until Phase 7 (Phase 4). |

## Policy tests (implemented in Phase 2)

All in `tests/SafeClean.Core.Tests/Policies/`, using synthetic paths only (no disk access):

| Test class | What it covers |
|---|---|
| `PathValidatorTests` | ≈70 accepted/rejected paths: traversal, streams, device/network prefixes, reserved names, trailing dot/space, invalid and control characters, lone surrogates, Unicode, long paths |
| `WindowsPathTests` | "Strictly inside" (sibling-prefix trap), case handling, culture comparisons refused, relative path, depth, extension |
| `ProtectedLocationsTests` | Subtrees, exact paths, drive roots, root conflicts, input normalisation |
| `RootPolicyTests` | Every root rule on its own, dangerous Temp locations, all problems reported together, unknown identity |
| `CandidatePolicyTests` | Every candidate rule on its own, boundaries (exactly 7 days, depth 16), redirection, exclusions, review types, Unicode, long paths, categories |
| `PolicyTextTests` | Every status/reason/problem/category has user-facing text; default status is Skipped |

The full rule → test mapping is in `docs/phases/PHASE-02-core-safety-policies.md`.

**Mutation check (how we know the tests have teeth):** a script plants one deliberate bug at a time
in the rules, runs the tests, and restores the file. Every planted bug must make at least one test fail.
*Lesson learned:* restored files keep their old timestamps, so afterwards run
`dotnet build SafeClean.sln --no-incremental` — otherwise the build may reuse the mutated binary.
This applies **between** mutations too (Phase 5): build with `--no-incremental` for every planted bug,
give restored files a fresh timestamp, and start with a clean baseline run (0 failing).

## Scanner tests (implemented in Phase 3)

All in `tests/SafeClean.Infrastructure.Tests/` and run against **sandbox folders only**, except the two
marked "real", which only *read*:

| Test class | What it covers |
|---|---|
| `FileInspectorTests` | Only the read-attributes right is requested; facts match .NET's; identities differ/change on replacement; folders report NTFS; missing files; invalid paths never reach Windows; locked files still inspectable and unchanged; > 260-character paths; junctions, hard links and symlinks inspected as themselves; impossible file times |
| `RootResolutionTests` | **Real** Temp folder approved (read-only); TEMP redirected to another folder refused; sandbox passes the same rules; junction root and junction parent refused; missing root refused; this PC's protected list covers Windows, Program Files, Documents, Desktop, Downloads, the profile |
| `TempScannerTests` | Empty root; eligible files with correct facts and totals; too recent; needs review; junction listed but never entered; symlink skipped; hard link skipped; read-only/hidden/system skipped and unchanged; locked file; folder without permission skipped while scan continues; Unicode names; long paths; depth limit; cancellation (and nothing changed); progress; files changing during the scan; **snapshot proves nothing on disk changed**; refused root |
| `ExtendedPathTests` | `\\?\` added and removed correctly; network results left for the validator to reject |
| `RealTempReadOnlyScanTests` (Explicit) | **Real**, read-only scan printing totals only |

Run the real, read-only scan on demand:

```powershell
dotnet test --project tests/SafeClean.Infrastructure.Tests/SafeClean.Infrastructure.Tests.csproj -c Debug --output Detailed --explicit only --filter-class "SafeClean.Infrastructure.Tests.Scanning.RealTempReadOnlyScanTests"
```

**Expected skips:** the two file-symlink tests are skipped on PCs without Developer Mode (Windows refuses to
create symlinks: "A required privilege is not held by the client"). The skip message says so explicitly.

## Screen-logic tests (implemented in Phase 4)

`tests/SafeClean.App.Tests/` uses a **fake scanner** and synthetic results built through the real Core
policies — no disk access, no windows shown:

| Test class | What it covers |
|---|---|
| `JunkCleanerViewModelTests` | Intro state; nothing selected after a scan; largest-first order; "select all safe" excludes review/skipped; skipped can never be selected (click, category, select-all); review files selectable by hand; category tick selects only safe files; tri-state dash; review-only category can't be bulk-selected; "All files" entry; Dry Run preview text and breakdown; old preview cleared on selection change; preview needs a selection; new scan clears selection; selected IDs belong to the session; status/category/search filters; details and reasons; refused root; cancellation without partial results; scan failure handled; no double scans; progress text; empty Temp; disguised names revealed |
| `PresentationTests` | Size formatting, revealing hidden/direction characters (and leaving emoji alone), invalid dates, singular/plural, commands refuse to run when disabled |

Core additions: `DryRunPlannerTests` (unknown/skipped IDs ignored, duplicates counted once) and two
architecture guards (view models don't use Infrastructure; the App can't reach cleanup code before Phase 7).

**Note about real-Temp numbers:** the Temp folder changes on its own. On this PC Windows Storage Sense is
enabled and removed several thousand old Temp files between the Phase 3 and Phase 4 scans. Always compare
scan numbers with an independent count taken at the same time, not with an older scan.

## Cleanup engine tests (implemented in Phase 5)

**Every file these tests delete is a synthetic file inside a sandbox under the test project's `bin` folder.**
Each sandbox has a `root` (scanned and cleaned) and an `outside` folder with stand-ins for personal files;
destructive tests assert that `outside` is byte-for-byte unchanged (SHA-256, size, dates, attributes).

| Test class | What it covers |
|---|---|
| `CleanupPolicyTests` (Core, plain data) | Whole request: Dry Run/Real accepted; elevated refused in both modes; unknown mode values refused; scan age at the limit accepted, past it or "from the future" refused. Root: same accepted; now failing → refused; other path or same path with a new identity → changed. D1/D2: eligible and needs-review pass; skipped, no facts, path disagreeing with facts refused; forged "eligible" verdicts for a personal file (→ protected) or a file outside the root refused; folder protected or excluded since the scan; different root; stricter age. D4: unchanged passes; 15 kinds of difference (identity, unknown identity, size, both dates, attributes added/removed, final path outside/elsewhere, path, hard link, delete pending, reparse point, directory, modified just now) all → changed; a skipped candidate never passes D4 even if nothing changed |
| `CleanupReportTests` (Core) | Bytes freed = deleted files only (even if other entries carry sizes); counts per outcome; a Dry Run report can't contain "deleted", a real one can't contain "would delete"; refusal has no entries |
| `HandleBasedFileDeleterTests` | Exact access right (delete + read attributes + synchronize), share mode none, no backup semantics; disposition structure is 1 byte; a matching file is gone after the call and its size returned; a file that fails D4 **after** being opened exclusively is left intact (not stuck "delete pending"); the engine can't be pointed at another root or at a forged candidate outside the root; every Windows open error maps to a skip/failure, never a deletion |
| `CleanupServiceTests` | Selected files deleted, bytes counted, folders kept; unselected files untouched (incl. a needs-review file deleted only when chosen); Dry Run changes nothing and reports what it would do, including changed and missing files; whole request refused when elevated, for an unknown mode, when the root was replaced by a same-named folder, resolved to a different folder, or swapped for a junction (target untouched); scans older than 30 minutes or from the future refused, 29 minutes accepted; unknown IDs, skipped entries and a junction never acted on; each ID reported once in selection order; empty selection; cancelled before start (nothing deleted) and between files (first file deleted, rest "not attempted"); missing after scan; in use (error 32); modified (size only / date only); replaced with identical content and dates (new identity); parent folder swapped for a junction to an identical look-alike (look-alike survives); hard link added after scan (both names survive); made read-only after scan (attribute left alone); mixed batch with exact outcomes and bytes; audit log has Temp-relative paths only |

Guardrails added: `ForbiddenApiTests` allows the delete access right (by name or number),
`SetFileInformationByHandle` and the disposition structure **only** in `HandleBasedFileDeleter.cs`
(and checks the allowance is really used there), and forbids delete-on-close, `NtSetInformationFile`
and moving/replacing files everywhere. `ArchitectureTests`: only `CleanupService` may call the engine;
the App can't reference `ICleanupService`, `CleanupService` or the engine until Phase 7.

### D1–D7 read-through checklist (Phase 5)

| Step | Where in the code | Done |
|---|---|---|
| Before any cleanup: mode, elevation, scan age | `CleanupPolicy.CheckRequest`, called first in `CleanupService.Clean` | ✅ |
| Before any cleanup: root re-approved, same path and identity | `ITempRootResolver.Resolve` + `CleanupPolicy.CheckRoot` | ✅ |
| D1 only IDs from the session, selectable | `CleanupService.Attempt` (unknown ID) + `CleanupPolicy.CheckRecorded` | ✅ |
| D2 path/protected/exclusion rules re-run | `CleanupPolicy.CheckRecorded` (full `CandidatePolicy` with current protected list); repeated inside the engine | ✅ |
| D3 exclusive open, share none, open reparse point | `HandleBasedFileDeleter.TryDelete` → `CreateFileW`; errors via `AttemptResult.FromOpenError` | ✅ |
| D4 checks through the same handle | `FileInspector.ReadFacts(handle)` + `CleanupPolicy.CheckCurrent` | ✅ |
| D5 disposition on that handle, no retry | `SetFileInformationByHandle(FileDispositionInfo)`; failure → `Failed` | ✅ |
| D6 close, then count bytes | size returned after the `using` block closes the handle | ✅ |
| D7 audit line per file, Temp-relative | `CleanupService.LogEntry` + summary line | ✅ |
| One file at a time; cancel between files | `foreach` in `CleanupService.Clean`; token checked before each file | ✅ |

## Cleanup screen tests (implemented in Phase 7)

| Test class | What it covers |
|---|---|
| `JunkCleanerCleanupTests` (App, fakes only) | Real mode off at start and after restart; elevated → can't switch on; delete needs Real mode + selection; dialog cancelled → service never called; confirmed → one call, exactly the selected IDs, `Real`; dialog contents (count, size, categories, review warning, sandbox line); results older than 30 minutes → no dialog; report exactly as returned; space freed only from deletions; refused cleanup; service failure; controls locked while cleaning; cancel reaches the service; results closed and Real mode off afterwards; sidebar badge follows the switch |
| `DebugSandboxRootResolverTests` (Debug only) | Marker required; marker must be a file; a junction as sandbox refused; marker removed after the scan → cleanup refused, nothing deleted |
| `ArchitectureTests` (Core) | `App_ReachesCleanupOnlyThroughTheServiceInterface` (replaces the "no cleanup until Phase 7" guard); `SandboxMode_ExistsOnlyInDebugBuilds` |
| `ManualSandboxSetup` (Explicit) | Creates a folder of synthetic files for manual testing (see `README.md`) |

**End-to-end check in sandbox mode** (UI Automation script, Debug build): the script refuses to press
anything destructive unless (1) the process command line names the sandbox, (2) the SANDBOX banner names
it, (3) the scan found exactly the sandbox's 7 entries and (4) the dialog says only the sandbox can be
affected. It then checks that Enter cancels the dialog, deletes via the red button, compares the report
with the files on disk, and rescans.

*Lessons learned:* (a) accessibility names on a WPF `Border` are invisible to screen readers and UI
Automation — put them on text or controls (found because safety check 2 failed); this also means the Phase 4
script's "preview closed" check could never fail. (b) UI Automation lists an owned modal dialog **under its
owner window**, not at the top level. (c) PowerShell 5.1 reads scripts without a BOM as ANSI, so non-ASCII
characters in search patterns don't match — keep scripts ASCII.

## Monitoring tests (implemented in Phase 8)

| Test class | What it covers |
|---|---|
| `MonitoringMathTests` (Core, synthetic numbers) | CPU % from time differences (example, fully idle/busy, no time passed, counters going backwards, impossible idle, huge counters); memory and drive used bytes/percent; total 0 → unknown; clamping |
| `SystemMonitorTests` (real, read-only) | Memory plausible and within 2% of the .NET runtime's figure; first CPU reading "measuring", then 0–100; system drive present, only fixed drives, free ≤ total; processes sorted, limited, include this test process, no errors for protected ones |
| `RealReadingsForComparison` (Explicit) | Prints memory, 8 timestamped CPU samples, drives and top 5 programs for comparison with Task Manager or Windows counters |

Compare CPU readings **side by side in time** (for example with `Get-Counter '\Processor(_Total)\% Processor Time'`
running at the same moment): a single sample taken a few seconds apart can differ hugely during a load burst.

## Screen tests and accessibility (implemented in Phase 9)

| Test class | What it covers |
|---|---|
| `DashboardViewModelTests` | "—"/"Measuring…" before readings and when Windows gives none; real figures; first processor reading "measuring"; refresh only while visible and not while minimised; junk card from real scans / real reports only; Safety card follows Real mode; "Scan" button opens the Junk Cleaner and scans, never cleans |
| `PerformanceViewModelTests` | Nothing runs before the page is shown; live figures; program list loaded once; history of 60 readings without invented values; timers (incl. optional 5 s) only while visible; sorting by every column; "n/a" values; **no command that acts on programs** |
| `LayoutAndFormatTests` | Percent rounding/clamping and "—"; chart points; card columns 4/2/1; window fit (incl. screens smaller than the minimum); optional rows on short windows; drive-change wording |
| `JunkCleanerPolishTests` | Close details, Esc order, "Temp folder"/"Sandbox folder" labels, spoken row names, drive change shown separately from space freed |
| `ContrastTests` | WCAG contrast of 34 colour pairs read from `Theme.xaml` (table below) |
| `ArchitectureTests` (Core) | Red danger buttons only on the Junk Cleaner and its dialog |

**Contrast (WCAG 2, computed from `Theme.xaml` on 2026-10-27).** Text needs 4.5:1; UI parts 3:1.

| Pair | Ratio | | Pair | Ratio |
|---|---|---|---|---|
| Text.Primary on Window / Card / Sidebar | 15.55 / 14.04 / 14.90 | | OnAccent on Accent / Hover / Pressed | 7.02 / 8.52 / 5.57 |
| Text.Primary on Hover / Selected | 12.78 / 11.83 | | OnDanger on Danger / Hover / Pressed | 6.21 / 7.53 / 4.89 |
| Text.Secondary on Window / Card / Sidebar | 8.75 / 7.90 / 8.39 | | Accent.SoftText on Accent.Soft | 7.59 |
| Text.Muted on Window / Card / Sidebar / Hover | 6.26 / 5.65 / 6.00 / 5.15 | | Danger on Danger.Soft / Card / Window | 4.88 / 5.45 / 6.03 |
| Warning on Warning.Soft / Card / Window | 7.33 / 8.71 / 9.65 | | Success on Success.Soft / Card | 6.16 / 7.65 |
| Accent on Card / Window (bars, charts) | 6.50 / 7.19 | | Focus ring on Window / Card / Sidebar | 9.96 / 8.99 / 9.54 |
| Success on Window (chart) | 8.48 | | Danger on Sidebar (badge border) | 5.78 |
| Disabled text on disabled background | 3.81 (exempt) | | | |

**Keyboard walkthrough (2026-10-27,** keys posted only to SafeClean's window): Tab order sidebar → Scan →
Categories → All/Safe/Review/Skipped → Search → file list; ↓ moves rows and switches pages in the sidebar;
Space ticks a file; Esc closes the details; Shift+Tab goes back. Screenshots confirmed the focus ring on a
filter button and on a file row.

## Settings, logs and history tests (implemented in Phase 10)

| Test class | What it covers |
|---|---|
| `SettingsPolicyTests` (Core) | Age below 7 / above 365 rejected; retention 7–90; unknown log level; 14 malformed or outside-Temp exclusions rejected, full paths with a helpful reason (full paths, `..`, rooted, network, `/`, streams, device names, trailing dot, patterns with folders, bad characters); valid folders and patterns; duplicates; one bad entry blocks the save; 100-entry limit; wildcard matching; settings → rules (excluded folders without prefix trap, patterns anywhere, longer age, defaults); unreadable settings refuse cleanup; history entry from a report |
| `SettingsAndHistoryStoreTests` | No file → defaults (nothing created); save/reload exactly, change event, no temporary file left; invalid settings never written; 6 kinds of unreadable or hand-edited files → defaults + "cleanup paused"; saving clears the problem; history round trip newest first with no paths or file names; corrupt lines skipped and counted; clear empties without deleting; log level hides details but never the cleanup audit trail |
| `LogMaintenanceTests` | Synthetic `SafeClean\Logs` folder with 11 look-alikes: only the two genuinely old `safeclean-YYYYMMDD.log` files are found and deleted; everything else byte-for-byte unchanged; longer retention finds fewer; another folder refused; elevated → refused; a plan stretched to include `notes.txt` can't delete it; retention outside 7–90 refused; exact-name rule |
| `SettingsInScanAndCleanupTests` | Scan applies excluded folders, patterns and a longer age; an exclusion added after the scan is honoured by the cleanup; unreadable settings refuse the cleanup |
| `SettingsAndHistoryViewModelTests` (App) | Saved values shown; no age option below 7; bad exclusions refused with a reason; add/remove/save; load problem shown until saved; **no setting can start Real mode**; old-log deletion: nothing old → no question, *No* → nothing deleted, *Yes* → deleted with the saved retention and recorded, folder problem reported; history newest first, refusals, unreadable lines, clear only after *Yes*; Junk Cleaner paused by unreadable settings and records each cleanup |

## Security review (Phase 11)

### Threat model walkthrough

Every threat in `SECURITY.md` §7: the code that stops it and the tests that prove it.

| Threat | Stopped by | Proved by |
|---|---|---|
| T1 Wrong file through a path bug | `PathValidator`, `WindowsPath.IsStrictlyInside`, `CandidatePolicy.Evaluate`, `CleanupPolicy.CheckCurrent` | `PathValidatorTests.RejectsUnsafePaths`, `CandidatePolicyTests.SiblingFolderWithSamePrefixIsOutsideRoot`, `CleanupPolicyTests.ForgedEligibleDecision_ForAPersonalFile_IsCaughtAsProtected`, `CleanupGateTests.Gate02_CandidateOutsideTheRoot_IsRefused_AndSurvives`, `Gate21_NamesThatLookIdentical_AreNeverConfused` |
| T2 Link inside Temp | `TempScanner.HandleEntry` (links never entered), `FileInspector` (opens links as themselves), rule C3 | `TempScannerTests.JunctionIsListedAsSkipped_AndItsTargetIsNeverScanned`, `Gate05_JunctionToOutside_IsNeverFollowed`, `CandidatePolicyTests.ReparsePointIsNeverACandidate` (file symlink tests skip here: no Developer Mode) |
| T3 Folder swapped for a junction | `HandleBasedFileDeleter.TryDelete` + `CheckCurrent` (final path through the open handle), `CleanupPolicy.CheckRoot` | `CleanupServiceTests.ParentFolderSwappedForAJunction_IsSkipped_AndTheLookAlikeOutsideSurvives`, `RootSwappedForAJunction_IsRefused_AndTheTargetIsUntouched`, `Gate23_ConcurrentChanges_NeverLeadToAWrongDeletion_RepeatedFiftyTimes` |
| T4 File replaced or changed | `CleanupPolicy.CheckCurrent` (identity, size, dates, attributes) under the exclusive handle | `FileModifiedAfterTheScan_IsSkippedAsChanged`, `FileReplacedAfterTheScan_IsSkipped_AndTheReplacementSurvives`, `CleanupPolicyTests.AnyDifferenceFromTheScan_IsSkippedAsChanged`, `Gate23…` |
| T5 File in use | `HandleBasedFileDeleter` (share mode none), `AttemptResult.FromOpenError` | `Gate10_FileOpenInAnotherProgram_IsSkipped_AndSurvives`, `FileInUse_IsSkipped_AndSurvives`; `ForbiddenApiTests` and `CompiledAssemblyTests` (no process killing) |
| T6 Hard link | Rule C6 in `CandidatePolicy`, `CheckCurrent` | `Gate07_HardLinkedFile_IsSkipped_AndBothNamesSurvive`, `HardLinkAddedAfterTheScan_IsSkipped_AndBothNamesSurvive`, `HardLinkedOrDeletePendingFileIsSkipped` |
| T7 `TEMP` points somewhere dangerous | `RootPolicy.Evaluate` (R5, R6), `TempRootResolver` | `RootResolutionTests.TempRedirectedToAnotherFolderIsRefused`, `RootPolicyTests.DangerousTempLocationsAreRefused`; failure simulation below |
| T8 Malformed paths | `PathValidator`; targets come only from enumeration | `RejectsUnsafePaths`, `Gate03_TraversalAndSpecialPathForms_AreRejected_BeforeAnythingIsOpened`, `UnknownIds_AndSkippedEntries_AreNeverActedOn` |
| T9 Run as administrator | `app.manifest` (`asInvoker`), `WindowsElevationProbe`, `CleanupPolicy.CheckRequest` | `Elevated_IsRefused_InEveryMode`, `Elevated_RefusesTheWholeRequest`, `WhenElevated_RealModeCannotBeSwitchedOn`, `WhenElevated_DeletionIsRefused_AndNothingChanges` (the real elevated launch is on the manual checklist) |
| T10 Cloud or offline placeholders | Attribute allow-list (C3, C4) | `EachDisallowedAttributeCausesSkip`, `Gate16_FileWithAProtectiveAttribute_IsNeverActedOn_AndKeepsIt` |
| T11 Read-only, hidden, system files | Attribute allow-list; attributes never changed | `FilesWithProtectiveAttributesAreSkipped_AndAttributesUnchanged`, `FileMadeReadOnlyAfterTheScan_IsSkipped_AndTheAttributeIsLeftAlone`, `Gate16_AttributeAddedAfterTheScan_IsCaught_AndKept` |
| T12 UI sends wrong items | `CleanupService.Attempt`, `CleanupPolicy.CheckRecorded`; the page sends IDs only | `UnknownIds_AndSkippedEntries_AreNeverActedOn`, `SkippedEntryCanNeverBeSelected`, `ForgedEligibleCandidate_ForAFileOutsideTheRoot_IsRefused`, `Confirming_CallsTheServiceOnce_WithExactlyTheSelectedIds_InRealMode`, `App_ReachesCleanupOnlyThroughTheServiceInterface` |
| T13 Accidental click | `JunkCleanerViewModel.CanDelete`, `ConfirmCleanupWindow` (Cancel is default and focused) | `RealModeIsOffAtStart_SoDeletingIsImpossible`, `CancellingTheDialog_NeverCallsTheCleanupService`, `AfterCleanup_ResultsAreClosed_RealModeIsOff_AndANewScanIsNeeded`, `RedDangerButtons_AppearOnlyOnTheJunkCleanerScreen`; Enter, Esc and closing checked end to end below |
| T14 Privacy | `FileLoggerProvider.Redact`, `CleanupService.LogEntry`, `CleanupHistoryEntry` (no file-name field); no network code | `Log_RedactsUserProfilePath_CaseInsensitively`, `AuditLog_UsesPathsRelativeToTheTempFolderOnly`, `History_RoundTrips_NewestFirst_AndStoresNoFileNames`, `NoForbiddenLibraryIsReferenced`, `NoProject_ImportsNetworkingTypesImplicitly` |
| T15 Monitoring changes the system | `SystemMonitor` (only reads) | `ForbiddenApiTests`, `ThePage_HasNoCommandThatActsOnPrograms`, `NoForbiddenCallIsCompiledIn`, `NativeCalls_AreExactlyTheReviewedOnes` |
| T16 Crash during cleanup | Files deleted independently; `CleanupService` stops and still reports; `MainViewModel.CanCloseNow` | `Gate17_ProgressCallbackThatThrows_StillReturnsTheFullReport`, `MixedBatch_ReportsEachFileCorrectly_AndCountsOnlyDeletedBytes`, `IfTheServiceFails_TheReportDoesNotClaimAnything`, `ClosingDuringACleanup_StopsItBeforeTheNextFile_AndKeepsTheWindowOpenForTheReport`; process kill below |
| T17 Compromised dependency | `Directory.Packages.props`, lock files, NuGet audit in the build | `dotnet list package --vulnerable` (results log), `CoreProjectFile_HasNoProjectOrPackageReferences`, `NoForbiddenLibraryIsReferenced` |
| T18 Disguised file names | `DisplayText.Reveal`; the real extension decides "Needs review" | `RevealMakesDisguisingCharactersVisible`, `DisguisedFileNameIsRevealed`, `RightToLeftOverrideTrickStillDetectsExecutable` |
| T19 Temp changes by itself | Every file re-checked at cleanup | `FilesChangingDuringTheScanCauseNoErrors`, `FileDeletedAfterTheScan_IsSkippedAsMissing`, `Gate23…` |
| T20 Exclusions lost or misread | `UserSettingsStore`, `CleanupPolicy.CheckRequest`, `LogMaintenance`, `SettingsViewModel` | `UnknownNames_PauseCleanup_InsteadOfSilentlyDroppingThem`, `UnreadableSettings_RefuseTheWholeCleanup`, `SavingOverAnUnusableFile_KeepsACopyOfIt`, `UnreadableSettings_SavingAsksFirst_AndAnsweringNoKeepsCleanupPaused`, `ASavedValueThatIsntAPreset_StaysOnOffer_AndSavingDoesNotLowerIt`, `SettingsBecomingUnreadable_AfterThePlan_RefuseTheDeletion` |
| T21 A test run reaches real data | `#if DEBUG` guard, `DebugSandboxRootResolver`, `App.SandboxDataFiles` | `SandboxMode_ExistsOnlyInDebugBuilds`, `FolderWithoutMarker_IsRefused_NeverReplacedByTheRealTempFolder`; Release binaries searched (results log); real settings and history hashes unchanged after every sandbox run |

### Review findings

The `/code-review` skill ran at level *high* on the working tree (10 findings). The first attempt at the `/security-review`
skill failed: it starts with `git diff origin/HEAD...`, and the local `origin/HEAD` pointer wasn't set. (An earlier version of
this note wrongly said the repository had no remote.) The whole deletion path was also read line by line by hand (5 more
findings below), which covers more than a review of recent changes would. Two more problems came from the failure simulation
itself.

| # | Source | Finding | Outcome |
|---|---|---|---|
| 1 | Code review | Saving lowered a valid age or retention that wasn't a preset (45 days became 7) | Fixed: the value stays on offer |
| 2 | Code review | Saving over unreadable settings erased the user's exclusions and lifted the pause without warning | Fixed: asks first (default No), keeps `settings.json.unreadable` |
| 3 | Code review | A failure reading free space could leave the page stuck on "Cleaning" or replace the real report with "unexpected error" | Fixed: never affects the cleanup; `SystemMonitor.GetDrives` also tolerates it |
| 4 | Code review | "Delete old logs" ignored the cleanup pause and used a fallback retention | Fixed in `LogMaintenance` itself, so the cleanup service refuses too |
| 5 | Code review | The Remove button "stays disabled" (`RelayCommand<T>`) | **Not a bug**: `IsEnabled` is true in the running app. No change |
| 6 | Code review | Sandbox mode used the real settings, history and logs, so "Delete old logs" could delete real logs | Fixed: Debug sandbox runs keep them beside the sandbox |
| 7 | Code review | "Saved" stayed on screen after changing a choice | Fixed: says "Not saved yet." |
| 8 | Code review | One history line with an unknown refusal value broke the whole History page | Fixed: skipped and counted like other bad lines |
| 9 | Code review | Failures reading the program list or old logs left the status stuck; auto-refresh would repeat an error box every 5 s | Fixed: said on the page and logged |
| 10 | Code review | The Junk Cleaner stated "7 days" even with a longer saved age | Fixed: shows the age in force |
| 11 | Manual review | `Using Remove="System.Net.Http"` did nothing, because `Directory.Build.props` is read before the SDK adds the using, so `SECURITY.md`'s claim was false | Fixed: moved to `Directory.Build.targets`, with a test reading what the build generated |
| 12 | Manual review | Nothing told the user when logging had turned off | Fixed: notice on every page, checked at startup |
| 13 | Manual review | A scan stopped at 250,000 entries never said so | Fixed: notice and status line |
| 14 | Manual review | A mistyped setting name silently dropped it (and the exclusions in it) while cleanup carried on | Fixed: an unknown name makes the file unusable, which pauses cleanup |
| 15 | Manual review | Closing the window mid-cleanup cut it off with no report or history; the banned-API list had gaps (attributes, permissions, `ProcessStartInfo`, `File.Copy`, more network APIs) | Fixed: closing stops the cleanup and shows the report; list extended, plus `CompiledAssemblyTests`, which reads the compiled program (native calls, libraries, framework calls) |
| 16 | Failure simulation | **Cancel was slow on big cleanups**: progress per file queued work ahead of mouse clicks, so about 4,000 more files were deleted after the click | Fixed: progress every 50 files; 324 more in the same test |
| 17 | Keyboard test | On Settings, Tab reached Add before its text box and Copy path before its field | Fixed |

`ForbiddenApiTests` review: the allow-list names only `HandleBasedFileDeleter.cs` (deletion) and `UserSettingsStore.cs` (its
own settings file). The plan's "Explorer launch" no longer exists: Phase 10 replaced it with **Copy path**.

### Failure simulation (2026-11-02)

Run through UI Automation on synthetic files. Sandbox runs used the Debug build's `--sandbox-root`; each destructive click
was preceded by checks of the command line, the SANDBOX banner, the exact entry count and the dialog's sandbox line, and a
mismatch aborted with Cancel (this stopped two runs safely, both because of mistakes in the test script).

| Scenario | Result | How it was run |
|---|---|---|
| Log folder unwritable | ✅ App runs; notice on every page; scanning works | Sandbox: a file placed where the log folder should be. The real log folder wasn't touched, so this used the sandbox's log folder, not `%LOCALAPPDATA%` |
| Settings and history corrupt | ✅ Defaults, Real mode paused with the reason; *No* leaves the file as it was; *Yes* keeps `settings.json.unreadable` and lifts the pause. History shows the good entry and counts 2 bad lines | Sandbox |
| `TEMP` set to another folder | ✅ Refused with the reason; no delete button; the file in that folder untouched | **Release build**, `TEMP` and `TMP` set for that process only |
| Run as administrator | ⏳ Tests cover the refusal; the real launch needs the owner to approve Windows' prompt | Manual checklist |
| 100,000 synthetic files | ✅ Exactly 100,003 entries counted; 41 s; 262 MB; slowest UI answer 1.9 s; Windows never reported "not responding" | Sandbox |
| Entry limit | ✅ Stops at the limit and says so | `ScanAtItsEntryLimit_StopsThere_AndSaysItWasCutShort` with a limit of 10 (not with 250,000 real files) |
| Deep nesting and access-denied folders | ✅ Each listed as one skipped entry; the scan continues | Sandbox: 20 levels, and a folder with a deny rule |
| Cancel mid-scan | ✅ Stopped within 0.8 s; no partial results; nothing changed on disk | Sandbox |
| Cancel mid-cleanup | ✅ Report matched the disk exactly (4,074 deleted, 4,074 gone); needs-review and recent files and the marker untouched; audit log had 4,074 lines; history one line with the same count and no names | Sandbox |
| Window closed mid-cleanup | ✅ Stayed open and stopped; report matched the disk (1,507); recorded in history | Sandbox |
| Process killed mid-cleanup | ✅ 1,294 files gone, 1,293 audit lines (only the file in flight is missing); other files untouched; a new scan sees exactly what's on disk; no history entry, as documented | Sandbox |
| Disk nearly full (optional) | ⏭️ Not run: a small test disk (VHD) needs administrator rights. A failed log write is covered by the unwritable-folder case | — |

The real Temp folder was only ever scanned read-only, by the Release build, with no code for the Real-mode switch or the
delete button in that script.

### Sign-off summary

**Not signed off yet.** Everything that can be checked automatically passed (results log below). Phase 11 stays open until the
owner finishes the unticked items in the manual checklist.

## Gate before enabling real deletion (Phase 6)

**Status: PASSED on 2026-10-17.** The item-by-item results table (test names, actual results, and the
written reasons for the 3 skipped sub-cases) is in [`docs/phases/PHASE-06-cleanup-test-gate.md`](phases/PHASE-06-cleanup-test-gate.md).
Gate tests live in `tests/SafeClean.Infrastructure.Tests/Cleanup/CleanupGateTests.cs` (named `Gate01…Gate24`);
several items are covered by the Phase 5 `CleanupServiceTests` and `TempScannerTests`.

Every item must have a passing automated test (or a documented, justified skip) before Phase 7:

1. Valid eligible file inside the sandbox root is deleted; bytes counted.
2. Path outside the approved root is rejected.
3. Path traversal attempts (`..`, mixed separators, `\\?\` forms) are rejected.
4. Protected directories are rejected even when nested under the root by construction.
5. Junction inside root pointing outside is not followed; target files survive.
6. File symlink inside root is not a candidate; target survives (skipped if privilege unavailable).
7. Hard-linked file is skipped; other link survives.
8. Missing file at cleanup time → skipped, not counted.
9. Inaccessible file (ACL denies delete) → skipped, not counted.
10. Locked file (opened by the test without delete sharing) → skipped, survives.
11. File modified after scan (size or timestamp) → skipped, survives.
12. File replaced after scan (same name, new file ID) → skipped, replacement survives.
13. Parent folder swapped for a junction after scan → skipped, target survives.
14. Cancellation during scanning returns promptly with no side effects.
15. Cancellation before cleanup deletes nothing.
16. Unexpected attributes (read-only, hidden, system, offline, sparse, encrypted where possible) → skipped, attributes unchanged.
17. Partial failure: mixed batch reports deleted/failed/skipped correctly.
18. Recovered bytes equal the sum of successfully deleted file sizes only.
19. Empty scan result handled.
20. Very large (sparse-free, multi-GB not required: size reported via length) and zero-byte files.
21. Unicode and unusual names (spaces, emoji, RTL characters, leading dots).
22. Long paths (> 260 characters).
23. Concurrent changes during scan (files created/deleted while enumerating) do not crash or mis-classify.
24. After every destructive test: assert that all out-of-scope and protected sandbox files still exist
    with unchanged content.

**What race tests can and cannot prove:** they show that the specific interleavings we simulate
(change → then cleanup) are detected. They cannot prove the absence of every possible timing window,
because the OS scheduler decides exact timing. The race-safety argument rests on the handle-based design
in `SECURITY.md` §6; tests confirm the implementation follows it.

## Results log

| Date | Phase | Command | Result |
|---|---|---|---|
| 2026-10-15 | 1 | `dotnet build SafeClean.sln -c Debug` | ✅ 0 warnings, 0 errors |
| 2026-10-15 | 1 | `dotnet test --solution SafeClean.sln -c Debug` | ✅ 42 passed, 0 failed, 0 skipped (Core 34, Infrastructure 8) |
| 2026-10-15 | 1 | `dotnet list SafeClean.sln package --vulnerable --include-transitive` | ✅ No vulnerable packages |
| 2026-10-15 | 1 | App launch + UI Automation walk of all 5 pages + screenshots | ✅ All pages reachable by accessible name; exit code 0; 2 layout issues found, fixed, re-verified |
| 2026-10-15 | 1 | Keyboard (Tab, ↓↓) posted to the app window only | ✅ Navigation works; focus-ring visibility needs a manual check (inactive window) |
| 2026-10-15 | 1 | Log file inspection | ✅ Startup/exit lines present; real profile path absent |
| 2026-10-15 | 2 | `dotnet build SafeClean.sln -c Debug` | ✅ 0 warnings, 0 errors |
| 2026-10-15 | 2 | `dotnet test --solution SafeClean.sln -c Debug` (first run) | ❌ 272/274 — two test-side problems (over-broad "cmd" pattern; lone-surrogate data altered by attribute encoding), both fixed |
| 2026-10-15 | 2 | `dotnet test --solution SafeClean.sln -c Debug` | ✅ 281 passed, 0 failed, 0 skipped (Core 273, Infrastructure 8) |
| 2026-10-15 | 2 | Mutation check: 4 deliberate bugs planted one at a time in Core rules | ✅ 4/4 caught (5, 3, 3 and 1 failing tests); sources verified restored |
| 2026-10-16 | 3 | `dotnet build SafeClean.sln -c Debug` | ✅ 0 warnings, 0 errors |
| 2026-10-16 | 3 | `dotnet test --solution SafeClean.sln -c Debug` | ✅ 341 passed, 0 failed, 3 skipped (Core 281; Infrastructure 60 + 3 skipped) |
| 2026-10-16 | 3 | Explicit read-only scan of the real Temp folder | ✅ 10.6 s, 24,896 entries, ≈4.6 GB estimated recoverable, 0 links, 0 vanished |
| 2026-10-16 | 3 | Mutation check on scanner/inspector | ✅ Inspector following links caught (5 tests); both link layers removed caught; layer 1 alone shown to be backed up by layer 2 |
| 2026-10-16 | 4 | `dotnet build SafeClean.sln -c Debug` | ✅ 0 warnings, 0 errors (after fixing 3 missing-`using` errors in the new test project) |
| 2026-10-16 | 4 | `dotnet test --solution SafeClean.sln -c Debug` (first run) | ❌ 397/399 — impossible dates shown as 12/31/9999 (code fixed) and one test reusing a session (test fixed) |
| 2026-10-16 | 4 | `dotnet test --solution SafeClean.sln -c Debug` | ✅ 396 passed, 0 failed, 3 skipped (Core 287; Infrastructure 60 + 3 skipped; App 49) |
| 2026-10-16 | 4 | App walkthrough via UI Automation + screenshots | ✅ Real read-only scan (≈19,280 entries, ~10–13 s), category tick, Dry Run preview, details, Skipped filter; one layout problem found and fixed |
| 2026-10-16 | 5 | `dotnet build SafeClean.sln -c Debug` | ✅ 0 warnings, 0 errors |
| 2026-10-16 | 5 | `dotnet test --solution SafeClean.sln -c Debug` (first run) | ✅ 506 passed, 0 failed, 3 skipped — all new tests passed first time |
| 2026-10-16 | 5 | `dotnet test --solution SafeClean.sln -c Debug` (after the layering fix + 2 tests) | ✅ 508 passed, 0 failed, 3 skipped (Core 354; Infrastructure 105 + 3 skipped as before; App 49); no sandbox folders left behind |
| 2026-10-16 | 5 | Mutation check, attempt 1 | ❌ Discarded: binaries from earlier planted bugs were reused (restored files keep old timestamps) |
| 2026-10-16 | 5 | Mutation check, attempt 2 (full rebuild per mutation, baseline 0 failing) | ✅ 11/12 caught; M10 survives by design (a second layer refuses the same files); final run 0 failing; files restored byte-for-byte |
| 2026-10-16 | 5 | App run by the project owner | ✅ Read-only scan in 8.7 s (1,998 safe / 959 review / 16,558 skipped); no cleanup lines in the log |
| 2026-10-17 | 6 | `dotnet build SafeClean.sln -c Debug` | ✅ 0 warnings, 0 errors |
| 2026-10-17 | 6 | Gate tests (`CleanupGateTests`, detailed output) | ✅ 43 passed, 0 failed, 2 skipped (file symlink: no Developer Mode; 8.3 alias: short names off on F:) — all passed on the first run |
| 2026-10-17 | 6 | Gate 23 run 3 times with statistics | ✅ Each run: 50 rounds × 20 files, ≈660 deleted, ≈330 skipped because of a race, ≈550 look-alikes created mid-cleanup — all survived |
| 2026-10-17 | 6 | `dotnet test --solution SafeClean.sln -c Debug` | ✅ 558 total: 553 passed, 0 failed, 5 skipped (Core 356; Infrastructure 148 + 5; App 49); no sandbox folders left behind |
| 2026-10-17 | 6 | Mutation check of the gate (planted bugs, full rebuild each, baseline 0 failing) | ✅ 6/6 compiling mutants caught (G1–G3, G4a, G4b, G5); the first G4 didn't compile (invalid mutant) and was replaced by G4a/G4b; final run 0 failing; files restored byte-for-byte |
| 2026-10-17 | 7 | `dotnet build SafeClean.sln -c Debug` and `-c Release` (App) | ✅ 0 warnings, 0 errors |
| 2026-10-17 | 7 | `dotnet test --solution SafeClean.sln -c Debug` (first run) | ❌ 581/582 — a new test sorted IDs that aren't sortable (test fixed) |
| 2026-10-17 | 7 | `dotnet test --solution SafeClean.sln -c Debug` | ✅ 582 total: 576 passed, 0 failed, 6 skipped (Core 357; Infrastructure 153 + 6; App 66) |
| 2026-10-17 | 7 | Release binaries searched for sandbox code | ✅ `DebugSandboxRootResolver` 0 hits, `--sandbox-root` 0 hits (Debug: present) |
| 2026-10-17 | 7 | End-to-end in sandbox mode (UI Automation) | ✅ 4 safety checks passed; Enter = Cancel; exactly the 3 safe files deleted (62.0 KB); others kept; DRY RUN restored; rescan 4 entries; audit log Temp-relative only. Two earlier runs stopped safely before deleting (script bug; banner invisible to UI Automation → accessibility fix) |
| 2026-10-17 | 8 | `dotnet build SafeClean.sln -c Debug` | ✅ 0 warnings, 0 errors |
| 2026-10-17 | 8 | `dotnet test --solution SafeClean.sln -c Debug` | ✅ 615 total: 608 passed, 0 failed, 7 skipped (Core 382; Infrastructure 160 + 7; App 66) — new tests passed first time |
| 2026-10-17 | 8 | Readings vs Windows (CIM, `Get-Process`) | ✅ RAM total 11.90 GiB both, available 4.38 vs 4.43 GiB; drives C–F identical; same top 5 programs within ~1% |
| 2026-10-17 | 8 | CPU vs `\Processor(_Total)\% Processor Time` | ❌ first single samples 86.4% vs 12% (taken at different moments in a load burst) → ✅ side by side for 7 s: within 2–3 points each second |
| 2026-10-27 | 9 | `dotnet build` Debug (no-incremental) and Release | ✅ 0 warnings, 0 errors |
| 2026-10-27 | 9 | `dotnet test --solution SafeClean.sln -c Debug` (first runs) | ❌ compile error (test helper named `Main` = entry point) → fixed; ❌ 1 failure caught a real wording bug ("drive C::") → fixed |
| 2026-10-27 | 9 | `dotnet test --solution SafeClean.sln -c Debug` | ✅ 694 total: 687 passed, 0 failed, 7 skipped (Core 383; Infrastructure 160 + 7; App 144) |
| 2026-10-27 | 9 | Screenshots at default and minimum size (UI Automation, sandbox mode for the Junk Cleaner) | ✅ Window now fits this 1366×768 screen (1200×738; before 760 px, cut off). File list rows: default 4 → 5, minimum 0 → 2 |
| 2026-10-27 | 9 | Accessibility name audit, keyboard walkthrough, focus-ring screenshots | ✅ 0 unnamed controls (2 scrollbar buttons fixed); logical Tab order; focus ring visible |
| 2026-10-28 | 10 | Mutation check, first attempt | ⚠️ cut off when the session ended; planted bug S4 was left in `SettingsPolicy.cs` → found on 2026-10-31, restored from its clean copy, clean rebuild 791 passed; results discarded |
| 2026-10-31 | 10 | Mutation check, full re-run (10 bugs, progress logged step by step) | ✅ 9/10 caught; S4 survived (second layer still refused full paths, but with a vaguer message) → added `FullPaths_AreRefused_WithAHelpfulReason`, re-planted: caught. Final run on restored sources 0 failing |
| 2026-10-31 | 10 | `dotnet build` Debug (no-incremental) and Release | ✅ 0 warnings, 0 errors |
| 2026-10-31 | 10 | `dotnet test` (Core, Infrastructure, App) | ✅ 803 total: 796 passed, 0 failed, 7 skipped (Core 441; Infrastructure 194 + 7; App 161) |
| 2026-10-31 | 10 | Settings and History pages in the app (UI Automation, nothing saved) | ✅ Bad exclusions refused with reasons; add + undo on screen only; `settings.json` / `history.jsonl` not created; 0 unnamed controls (1 fixed) |
| 2026-11-02 | 11 | Baseline before any change: builds (no-incremental) and tests | ✅ 0 warnings; Debug 803 total: 796 passed, 0 failed, 7 skipped; Release 797 total: 791 passed, 0 failed, 6 skipped |
| 2026-11-02 | 11 | `/security-review` skill, first attempt | ❌ Failed to start: the local `origin/HEAD` pointer wasn't set. A line-by-line manual review of the deletion path was done instead (5 findings) |
| 2026-11-02 | 11 | `/code-review` skill, level high | ✅ Ran: 10 findings, each checked; 9 real and fixed, 1 not a bug (the button was enabled in the running app) |
| 2026-11-02 | 11 | Race gate (`Gate23`) repeated 50 times, then the Infrastructure suite 5 times | ✅ 50/50 and 5/5 passed (each gate run: 50 rounds × 20 files); no flakiness; no sandbox folders left behind. Run before the progress change, which doesn't touch the checks the gate exercises |
| 2026-11-02 | 11 | 100,000-file sandbox, first runs | ❌ Two runs stopped safely before deleting (test script used the wrong button name, and hit a locked log file). One run found the real Cancel delay: about 4,000 more files deleted after the click → fixed; 324 in the same test |
| 2026-11-02 | 11 | Mutation check (21 planted bugs, restored and verified by SHA-256, progress logged step by step) | ✅ 21/21 caught, including a `File.Move` through an alias and a new native call, which the source-text checks can't see; final runs on restored sources 0 failing |
| 2026-11-02 | 11 | Failure simulation and dialog keys on the final build | ✅ 48 checks and the dialog checks passed (table above). Mid-cleanup cancel, close and kill, and the screen-vs-disk check, weren't repeated after the last small changes (Settings Tab order, a test-only entry limit), which don't touch cleanup; a read-only scan of the large sandbox was repeated (86,043 entries, exact) |
| 2026-11-02 | 11 | Screen vs disk: read-only scan of the real Temp folder (Release) | ✅ 5 of 5 files: exact size, modified and created dates matched; file names not printed |
| 2026-11-02 | 11 | Network | ✅ No sockets owned by SafeClean in 18 to 26 checks across a whole run including a real scan; `System.Net.Http` and `System.Net.Sockets` not loaded; compiled program references no network library. (`ws2_32.dll`, a Windows library, is loaded in the process, but no socket was opened.) This polls open sockets; it isn't a packet capture |
| 2026-11-02 | 11 | Real log files, read-only | ✅ About 100 lines in 6 files: 0 hold the user name, the profile path or a `C:\Users` path |
| 2026-11-02 | 11 | Dialog keys, minimum size, keyboard | ✅ Enter, Esc and closing all cancel; Cancel focused; all 5 pages fine at 900×600 (long pages scroll); Tab order on Settings fixed; focus ring visible |
| 2026-11-02 | 11 | `dotnet list package --vulnerable --include-transitive` and `--deprecated` | ✅ None in any project |
| 2026-11-02 | 11 | Release binaries searched for sandbox code | ✅ 0 hits for 6 markers in all 3 assemblies (Debug: present, so the search works) |
| 2026-11-02 | 11 | Final `dotnet build` Debug and Release (no-incremental) | ✅ 0 warnings, 0 errors |
| 2026-11-02 | 11 | Final `dotnet test` Debug | ✅ 857 total: 850 passed, 0 failed, 7 skipped (Core 455; Infrastructure 209 + 7; App 186). Skips: 3 opt-in manual tests, 3 file-symlink tests (no Developer Mode), 1 short-name test (off on this drive) |
| 2026-11-02 | 11 | Final `dotnet test` Release | ✅ 851 total: 845 passed, 0 failed, 6 skipped (the Debug-only sandbox tests don't exist in Release, so the total is 6 lower and there's one fewer opt-in test); no sandbox folders left behind |

## Manual test checklist (Phase 11)

Ticked items were checked by UI Automation on 2026-11-02 (details in the results log). Unticked items need the owner.

- [x] Fresh launch shows Dry Run mode and performs no scan or cleanup automatically. *(Release build)*
- [x] Keyboard only: Tab/arrow keys reach every navigation item and button, and the blue focus ring is visible on each. *(Tab order on Settings this phase, other pages in Phase 9; focus ring seen in screenshots of a sample of controls)*
- [x] Window resizes down to minimum size without clipped controls. *(All 5 pages at 900×600; long pages scroll. At that size, with the extra Debug banner, the file list shows 1 row)*
- [ ] **Owner:** Launch elevated ("Run as administrator"): cleanup controls disabled with explanation.
- [x] Set `TEMP` to another folder for a test session: app refuses cleanup and explains. *(Release build)*
- [x] Scan results match a manual look at a few files (size, date). *(5 real files, read-only)*
- [x] Cleanup report numbers match deleted files; skipped files still exist. *(Sandbox)*
- [x] Log file contains no file contents and no full user paths. *(Real logs, read-only)*
- [x] No network traffic. *(Open sockets polled, not Resource Monitor; see the results log. The owner can also look in Resource Monitor if they want a second opinion)*
- [ ] **Owner:** At 125 % and 150 % display scaling (the owner's own setting), every page fits and nothing is cut off (Phase 9).
- [ ] **Owner:** Windows Narrator reads the sidebar, the Scan / Preview / Real-mode / Delete controls and file rows sensibly (Phase 9).
- [x] Settings: change the minimum age and add an exclusion, save, close and reopen SafeClean — the values are still there; a scan lists matching files as *Skipped (Excluded)* (Phase 10). *(Sandbox, so the real settings file wasn't created)*
- [x] Settings → "Delete old SafeClean logs…": answering *No* deletes nothing; with no old logs it says so and asks nothing (Phase 10). *(Sandbox, with synthetic logs)*
