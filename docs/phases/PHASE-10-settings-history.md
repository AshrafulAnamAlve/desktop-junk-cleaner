# Phase 10 — Settings, Exclusions, Logging Controls and Cleanup History

**Status:** Not started · **Depends on:** Phase 7 · **Deletes anything?** See "log retention" below

## Goal

Let users adjust SafeClean **only in safer directions**, see past cleanups, and control logging.

## Rule

**Settings can make SafeClean stricter, never looser.** No setting can add a cleanup location, remove a
protected location, lower the 7-day age limit, or make Real mode start enabled.

## Tasks

### Settings storage
- [ ] `%LOCALAPPDATA%\SafeClean\settings.json` using built-in `System.Text.Json` (no package)
- [ ] Validated on load; anything invalid or unreadable → safe defaults + warning in UI and log
- [ ] Written atomically (write temp file in the same folder, then replace) to avoid corruption

### Settings available
- [ ] Minimum file age: 7 days (default) up to 365 days — values below 7 rejected
- [ ] Exclusions: sub-folders or file-name patterns inside Temp to **always skip** (restrict-only)
- [ ] Log level (Information / Warning / Error)
- [ ] Log retention days (7–90)
- [ ] Real mode is **not** a setting

### Exclusions
- [ ] Must resolve to a location inside the approved root; anything else rejected with explanation
- [ ] Simple patterns only (`*.log`, `SomeFolder\`); no regular expressions
- [ ] Applied in `CandidatePolicy` (scan) **and** revalidated in cleanup (D2)

### Logging controls
- [ ] Show log folder path; "Open log folder" via Explorer (`explorer.exe` with the folder as argument only;
      allow-listed single call in `ForbiddenApiTests`)
- [ ] Log retention: on startup remove SafeClean's **own** log files older than N days — only files directly
      in the log folder whose names exactly match `safeclean-YYYYMMDD.log`, via the same handle-based
      deleter with the log folder as its approved root; no recursion

### Cleanup history
- [ ] Append-only `%LOCALAPPDATA%\SafeClean\history.jsonl`: date, mode, counts (deleted/skipped/failed),
      bytes freed, duration — **no file names**
- [ ] History screen lists entries (newest first); corrupt lines skipped with a warning
- [ ] "Clear history" removes only `history.jsonl` after confirmation

## Tests

| Case | Expected |
|---|---|
| Age setting 3 days | Rejected; stays 7 |
| Exclusion `C:\Windows` / `..\..` / Documents | Rejected |
| Exclusion inside Temp | Matching files become Skipped (`Excluded`) |
| Corrupt settings JSON | Defaults loaded; app runs |
| Settings cannot enable Real mode at startup | Always DryRun |
| Log retention | Deletes only matching old SafeClean logs in a sandbox log folder; other files survive |
| History | Entries round-trip; corrupt line ignored; no file names stored |

## Verification

- Build 0 warnings; tests pass; manual check of settings persistence across restarts.

## Done when

- [ ] Settings, exclusions, logging controls and history work and are documented in `SECURITY.md` and `README.md`

## Risks

- **Exclusion parsing bugs** → exclusions can only cause *more* skipping, so the worst case is cleaning less.
- **Log retention is a deletion path** → reuses the tested handle-based deleter with a narrow root and exact-name rule.
