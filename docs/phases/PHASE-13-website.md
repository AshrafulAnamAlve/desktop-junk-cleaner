# Phase 13 — Website and public download

**Status:** In progress · **Depends on:** Phase 12 · **Deletes anything?** No

## Goal

Give SafeClean a public page where people can read what it does and download it.

## Plan

- [ ] Static page in `website/`, with no scripts or fonts from other sites, no analytics and no cookies
- [ ] Say plainly what people need to know before running it: extract first, the unsigned-app warning, the checksum, permanent deletion, what was tested
- [ ] Don't call SafeClean "open source" until the owner has chosen a licence
- [ ] Screenshots show only the Dashboard and made-up test files, never real file names
- [ ] Serve the page from GitHub Pages (`gh-pages` branch, made from `website/`)
- [ ] Publish the ZIP and its checksum as GitHub release `v1.0.0`, built from a committed source tree
- [ ] Check it live: open the page, download the ZIP from the public link, compare its SHA-256

## Done when

- [ ] The public page opens and its button downloads a ZIP whose checksum matches
- [ ] Results recorded in `TESTING.md`; marked Done in this file, `phases/README.md`, `ROADMAP.md` and `CHANGELOG.md`

## Risks

- **Anyone can download it once it's live**, including with the known limits in `SECURITY.md` §10. The page states them.
- **A release can be replaced or removed, but copies people already downloaded can't be recalled.** That's why this ZIP was retested before upload.
