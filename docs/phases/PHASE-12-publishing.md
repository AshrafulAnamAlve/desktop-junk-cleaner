# Phase 12 — Publishing

**Status:** Not started · **Depends on:** Phase 11 sign-off · **Deletes anything?** No

## Goal

Produce a version of SafeClean that runs on a Windows PC **without** installing .NET, using only free tools,
and document what users should expect.

## Tasks

- [ ] Update the .NET 10 SDK first (official Microsoft installer) so the bundled runtime has the latest security patches
- [ ] Publish:
      `dotnet publish src/SafeClean.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`
- [ ] No trimming (not supported for WPF)
- [ ] App icon (`.ico`) created and referenced
- [ ] Portable ZIP as the main distribution format (no installer required, no admin)
- [ ] Optional installer with a free tool (e.g. Inno Setup) — per-user install, no admin, no startup entries,
      no services, uninstall removes the app but **asks** before removing settings/logs/history
- [ ] SHA-256 checksum file next to each download
- [ ] Release notes in `CHANGELOG.md`

## Documentation to write

- [ ] System requirements: Windows 10/11 x64 (confirm minimum build against Microsoft's .NET 10 supported-OS list at publish time)
- [ ] **Unsigned app warning:** without a paid code-signing certificate, Windows SmartScreen will show
      "Windows protected your PC" on first run. Explain how to verify the SHA-256 checksum instead.
- [ ] Where data is stored (`%LOCALAPPDATA%\SafeClean`) and how to remove it
- [ ] Limitations (from `SECURITY.md` §10–11)

## Verification

- [ ] Run the published `.exe` on this PC from a different folder; full smoke test (scan, preview, monitoring)
- [ ] Confirm it starts **not elevated** and in Dry Run mode
- [ ] Optional: test on a second PC or VM (Windows Sandbox is not available on Windows Home)
- [ ] Record output size and checksums

## Done when

- [ ] ZIP (and optional installer) built, checksummed, smoke-tested
- [ ] `README.md` has download/run instructions; roadmap complete

## Risks

- **Large file size** (self-contained WPF ≈ 70–160 MB) → acceptable for v1; framework-dependent build can be offered as a smaller alternative.
- **SmartScreen warnings** → documented; no paid signing (project must stay free).
