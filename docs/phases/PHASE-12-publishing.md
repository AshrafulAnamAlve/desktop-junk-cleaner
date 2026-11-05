# Phase 12 — Publishing

**Status:** ✅ Done (2026-11-05) · **Depends on:** Phase 11 sign-off · **Deletes anything?** No

## Goal

Produce a version of SafeClean that runs on a Windows PC **without** installing .NET, using only free tools,
and document what users should expect.

## Change from the original plan

The plan's command used `-p:IncludeNativeLibrariesForSelfExtract=true`. With that option .NET unpacks DLLs into
`%TEMP%\.net` on every run, and a Temp cleaner must not put its own files in the folder it cleans. The release keeps the
option off, so 5 Microsoft DLLs ship beside `SafeClean.exe` in the ZIP. The cost: the ZIP must be extracted before running
(see "Limits found"). `PublishingTests` fails if the option is turned on.

## Tasks

- [x] Update the .NET 10 SDK first (official Microsoft installer; SHA-512 and signature checked): SDK 10.0.403, runtime 10.0.14. The older SDK is still installed
- [x] Publish with `tools\publish-release.ps1` (`-c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false`)
- [x] No trimming (not supported for WPF)
- [x] App icon (`.ico`) created (`tools\make-icon.ps1`) and referenced
- [x] Portable ZIP as the main distribution format (no installer required, no admin)
- [ ] Optional installer: not built. The portable ZIP is the only format for 1.0.0, and an installer tool would be new software to install on this PC
- [x] SHA-256 checksum file next to the download
- [x] Release notes in `CHANGELOG.md`

## Documentation written

- [x] System requirements, checked against Microsoft's .NET 10 supported-OS list on 2026-11-05 (regular Windows 10 22H2 is no longer listed)
- [x] **Unsigned app warning:** SmartScreen explained, with how to verify the SHA-256 checksum instead
- [x] Where data is stored (`%LOCALAPPDATA%\SafeClean`) and how to remove it
- [x] Limitations (`README.md`, `tools\README-portable.txt`, `SECURITY.md` §10–11)

## Verification

- [x] Run the published `.exe` on this PC from a different folder; full smoke test (scan, preview, monitoring): all checks passed. The preview used one ticked "review" file, because the real Temp folder had no "safe" files that day
- [x] Confirm it starts **not elevated** and in Dry Run mode (log: `Mode: DryRun. Elevated: False`)
- [x] Confirm it doesn't depend on the installed .NET: 0 of 71 loaded modules come from an installed `dotnet` folder
- [x] Confirm nothing is unpacked or loaded from Temp: no `%TEMP%\.net`, 0 modules from Temp
- [ ] Optional: test on a second PC or VM: not done, so it hasn't been run on a PC without .NET, or on Windows 11
- [x] Record output size and checksums (below)

## Result

| Item | Value |
|---|---|
| ZIP | `publish\SafeClean-1.0.0-win-x64.zip`, 54 MB (126 MB extracted) |
| SHA-256 | `bfcbf8c063e803a919e5d5468c46079bba6d2d4e134fcbd3ef6197c892820153` |
| Bundled runtime | .NET 10.0.14 |
| Built from | Uncommitted changes on top of commit 29a441c, so this ZIP can't be traced to a single commit. Commit first, then rebuild, before uploading a release |

## Limits found

- **Running `SafeClean.exe` without the files beside it** (for example from inside the ZIP): it closes straight away with no message. Nothing is harmed. The README's first step says to extract first. A friendly error would be a small change for a later version.
- **Not uploaded.** The project rule is no upload unless the owner asks, so the README's Releases link has nothing to download until a release is published.
- **No licence chosen for SafeClean itself.** The repository has no `LICENSE` file; that's the owner's decision. .NET's own licence notices are included in the ZIP.

## Done when

- [x] ZIP built, checksummed, smoke-tested
- [x] `README.md` has download/run instructions; roadmap complete

## Risks

- **Large file size** (126 MB extracted, 54 MB ZIP) → acceptable for v1.
- **SmartScreen warnings** → documented; no paid signing (project must stay free).
