# Phase 2 — Core Safety Policies (Pure Logic)

**Status:** ✅ Done (2026-10-15) · **Depends on:** Phase 1 · **Deletes anything?** No · **Touches the disk?** No

## Goal

Write the rules that decide what is allowed — as plain C# that works on *data* only. Every rule gets
unit tests. Later phases (scanner, cleanup) must ask these rules; they may not invent their own.

## Why (beginner explanation)

If the safety logic lived inside the code that touches files, testing it would require real files and
mistakes would be dangerous. Keeping rules "pure" (input data → yes/no answer) means we can test
hundreds of tricky cases — fake paths, weird attributes, future dates — in milliseconds with zero risk.

## What was built

### Models (`src/SafeClean.Core/Models/`)

| Type | Purpose |
|---|---|
| `FileIdentity` | Volume serial + 128-bit file ID. `Matches()` never treats two *unknown* (all-zero) identities as equal |
| `FileFacts` | Everything known about one file: found path, OS-reported final path, attributes, size, created/modified (UTC), identity, hard-link count |
| `RootFacts` | Everything known about the Temp folder before approval (requested path, final path, attributes, ancestor reparse flag, fixed drive, file system, identity, expected identity) |
| `ApprovedRoot` | Canonical root path + identity. **Constructor is internal — only `RootPolicy` can create one** |
| `RootDecision` | Approved root *or* list of `RootProblem`s |
| `CandidateDecision` | Status, all skip reasons, category, root-relative path |
| `CandidateStatus` | `Skipped` (default value 0), `Eligible`, `NeedsReview` |
| `SkipReason`, `RootProblem`, `PathProblem`, `CandidateCategory` | Reason codes, each mapped to docs/SECURITY.md rules |

### Policies (`src/SafeClean.Core/Policies/`)

| Class | Rules | What it does |
|---|---|---|
| `PathValidator` | C8, R2 | Accepts only canonical `X:\...` paths. Rejects: empty, > 32,767 chars, names > 255 chars, device paths (`\\?\`, `\\.\`, `\??\`), network paths, relative/drive-relative/root-relative paths, forward slashes, empty segments (`\\`, trailing `\`), `.`/`..`, alternate data streams (`:`), `<>"|?*`, control characters, lone surrogates, trailing dot/space, reserved device names (CON, PRN, AUX, NUL, COM0–9, LPT0–9, COM¹²³, LPT¹²³, CONIN$, CONOUT$, CLOCK$ — also with extensions or spaces before the dot) |
| `WindowsPath` | C5 | Pure helpers: "strictly inside" (`C:\Temp2` is not inside `C:\Temp`), same-or-inside, relative path, folder depth, name, extension. Refuses culture-aware string comparisons |
| `ProtectedLocations` | C9, R6 | Subtrees + exact paths + every drive root; root conflict detection (is / inside / contains protected); ignores empty entries, rejects invalid ones |
| `RootPolicy` | R2–R7 | The only way to obtain an `ApprovedRoot`; reports **all** problems at once |
| `CandidatePolicyContext` | C7, C9 | Root, protected list, "now", minimum age (**can only be made stricter**), exclusions |
| `CandidatePolicy` | C1–C9 | Judges one file; reports every failed rule; decides Eligible / Needs review / Skipped; display category |
| `PolicyText` | — | Plain-English text for every status, reason, problem and category |

## Changes from the original plan (and why)

| Change | Reason |
|---|---|
| `RootContainment` became `WindowsPath` (more helpers in one place) | Simpler for a beginner to find |
| `ScanCandidate` moved to Phase 3 | The scanner creates candidates; `CandidateDecision` is the policy's output |
| **No test-only root factory needed** | `RootPolicy` compares the folder's identity with an *expected identity* supplied by the caller. Production passes `%LOCALAPPDATA%\Temp`'s identity; sandbox tests pass the sandbox's own identity. Same rules, no bypass |
| New reason `PathMismatch` (C5) | Found path and OS-reported path must be **exactly** equal; any difference means redirection or a race |
| `FutureTimestamp` became `UntrustworthyTimestamp` | Also catches implausibly old dates (before 1980, e.g. zeroed 1601 timestamps) that would otherwise make a file look "very old" |
| `MultipleHardLinks` became `UnexpectedLinkCount` | Also catches link count 0 (file already being deleted) |
| New reason `UnexpectedSize` | Defensive: negative size is impossible |
| Validator also rejects forward slashes, empty segments, lone surrogates, COM0/LPT0, superscript device names, CONIN$/CONOUT$/CLOCK$ | More conservative than planned; Windows-produced paths never contain these |
| `\\?\` prefix rejected by Core | Infrastructure (Phase 3) must strip it from `GetFinalPathNameByHandle` output first |

## Key decisions (plain language)

- **Exact vs. case-insensitive matching.** For "is this file inside Temp?" SafeClean compares *exactly*,
  because both paths come from Windows and should match character for character — any difference means
  skip. For "is this protected?" it ignores case, so more things count as protected. Both choices err
  toward keeping files.
- **Every failed rule is reported**, so the user sees all reasons at once (e.g. "hidden, recent and hard-linked").
- **Age uses the newer of created/modified**, because files extracted from archives keep old "modified" dates.

## Rule → test mapping

| Rule | Tests |
|---|---|
| R1 (Temp path from Windows) | Phase 3 (Infrastructure) |
| R2 plain local path | `RootPolicyTests.EachRuleRefusesOnItsOwn` (UNC, device prefix, empty, traversal) |
| R3 fixed NTFS drive | `EachRuleRefusesOnItsOwn` (removable, FAT32, exFAT) |
| R4 real folder, no redirection | `EachRuleRefusesOnItsOwn` (not a folder, root junction, parent junction) |
| R5 same folder as `%LOCALAPPDATA%\Temp` | `EachRuleRefusesOnItsOwn` (other folder, unknown identity), `FileIdentity_UnknownNeverMatches` |
| R6 not drive root / protected | `DangerousTempLocationsAreRefused`, `ProtectedRequestedPathIsRefusedEvenIfFinalPathLooksFine`, `ProtectedLocationsTests.*` |
| R7 OS-reported path is canonical | `HealthyTempFolderIsApproved_UsingTheOsReportedPath`, `ShortNameRequestedPath_CanonicalRootIsTheFinalLongPath` |
| R8 not elevated | Phase 5 (checked at cleanup time) |
| C1 depth | `DepthLimitIsEnforced` (enumeration part in Phase 3) |
| C2 files only | `FolderIsNeverACandidate` |
| C3 no reparse points | `ReparsePointIsNeverACandidate`, `JunctionFolderReportsBothReasons` |
| C4 attribute allow-list | `AllowedAttributesAreAccepted` (4), `EachDisallowedAttributeCausesSkip` (13, incl. unnamed cloud bits) |
| C5 inside root, exact location | `FileOutsideRootIsSkipped`, `SiblingFolderWithSamePrefixIsOutsideRoot`, `RedirectedFile_FinalPathElsewhere_IsSkipped`, `RedirectionWithinRootIsStillAMismatch`, `CaseOnlyDifferenceIsTreatedAsMismatch`, `DifferentCasingOfTheRootPrefixIsOutsideRoot`, `WindowsPathTests.*` |
| C6 one hard link | `HardLinkedOrDeletePendingFileIsSkipped` (0, 2, 5) |
| C7 age and timestamps | `RecentlyCreatedFileWithOldModifiedDateIsTooRecent`, `RecentlyModifiedFileIsTooRecent`, `TmpExtensionAloneNeverMakesAFileEligible`, `FileExactlyAtMinimumAgeIsEligible_OneSecondYoungerIsNot`, `FutureTimestampIsUntrustworthy`, `ZeroedTimestampIsUntrustworthy_NotOld`, `MinimumAgeCannotBeLoosenedBelowTheDefault`, `StricterMinimumAgeIsHonoured` |
| C8 path validation | `PathValidatorTests.*` (≈70 cases), `InvalidPathsAreSkipped` |
| C9 protected and excluded | `ProtectedLocationsTests.*`, `ProtectedLocationNestedInsideRootIsRespected`, `ExcludedFolderIsSkipped_CaseInsensitive`, `InvalidExclusionIsRejected` |
| Needs review | `ProgramsAndScriptsNeedReview`, `RecentExecutableIsSkipped_NotMerelyNeedsReview`, `RightToLeftOverrideTrickStillDetectsExecutable` |
| Unicode / long / sizes | `UnicodeAndUnusualNamesAreHandled`, `LongPathIsHandled`, `ZeroByteAndVeryLargeFilesAreAllowed`, `NegativeSizeIsSkipped` |
| Display | `CategoriesComeFromNameForDisplayOnly`, `PolicyTextTests.*` |

## Verification results (actually run, 2026-10-15)

| Check | Result |
|---|---|
| `dotnet build SafeClean.sln -c Debug` | ✅ 0 warnings, 0 errors |
| `dotnet test --solution SafeClean.sln -c Debug` | ✅ 281 passed, 0 failed, 0 skipped (Core 273, Infrastructure 8) — 239 new test cases this phase |
| First run | 2 failures, both test problems, fixed: (1) the forbidden-API "cmd" pattern also matched the `".cmd"` extension text → pattern tightened + 7 new pattern tests; (2) lone-surrogate test data was silently changed by C# attribute encoding → moved into a normal test |
| Core still has no file-system access | ✅ `ArchitectureTests.CoreSource_DoesNotAccessTheFileSystem` passes |
| Mutation check (deliberate bugs planted one at a time, then restored) | ✅ All 4 caught: containment without separator check (5 failing tests), hard links up to 2 allowed (3), `..` accepted (3), unknown identities matching (1). Source files verified identical afterwards |

## Done when

- [x] Every rule R/C in `SECURITY.md` that can be expressed as pure logic has a passing test
- [x] `SECURITY.md` updated where rule wording changed
