# Phase 9 — Dashboard, Performance Screen and UI Polish

**Status:** Not started · **Depends on:** Phases 4, 7, 8 · **Deletes anything?** No new deletion paths

## Goal

Turn placeholders into a polished, premium-feeling interface showing **real** data, with good
accessibility and resizing.

## Tasks

### Dashboard
- [ ] Cards: Memory (used/total, %), CPU (%), System drive (used/free bar), Last scan (from the real
      last session or "No scan yet"), Safety status (mode, elevation, Temp root status)
- [ ] Quick actions: "Scan Temp" (navigates and starts a read-only scan). No cleanup from the dashboard.

### Performance screen
- [ ] Live RAM and CPU with a lightweight sparkline (`Polyline`, last 60 samples, no chart library)
- [ ] Drives list with usage bars
- [ ] Process table: name, PID, working set, private bytes; sortable; **read-only** (no end-task)
- [ ] Info panel: "Why high RAM usage is normal" (Windows caching explanation)

### Polish
- [ ] Custom dark scrollbars, tooltips, focus rings visible on every focusable control
- [ ] Contrast table computed and recorded (text ≥ 4.5:1, large text/UI parts ≥ 3:1)
- [ ] Responsive: cards wrap at narrow widths; minimum window size 900×600; test at 100/125/150% scaling
- [ ] Dashboard metric cards and full-width cards share the same right edge (adaptive grid instead of fixed-width `WrapPanel`) — noted in Phase 1
- [ ] Manually confirm the focus ring is visible on every control (couldn't be automated in Phase 1)
- [ ] Animations limited to short hover/selection transitions
- [ ] Consistent spacing scale (4/8/12/16/24/32) and type scale
- [ ] Clear success/warning/error components reused across screens
- [ ] Destructive actions remain red and only on the Junk Cleaner screen

## Tests

- [ ] View-model tests: dashboard shows "—"/"Measuring…" instead of zeros when data isn't available yet
- [ ] Dashboard never displays a cleanup number not produced by a real cleanup report
- [ ] Monitoring timers stop when the screen is hidden (no background work)

## Verification

- Screenshots of every screen at default size, minimum size and 150% scaling
- Keyboard-only walkthrough (Tab/Shift+Tab/arrows/Space/Enter/Esc) recorded in `TESTING.md`
- Windows Narrator spot-check of navigation and main buttons

## Done when

- [ ] All screens use real services or honest empty states
- [ ] Accessibility checks recorded

## Risks

- **Over-designing** → no heavy UI packages; plain WPF styles.
- **Timers wasting CPU** → active only while visible.
