# Sequence interface audit — October 24, 2026

Scope: the full sequence editor, docked right-hand panel, target visuals and
shared parameter/trigger dialogs. Preserve existing controls and stopped-only
editing. No NINA/plugin changes or observatory equipment actions are required.

## Findings and changes

| Finding in the previous presentation | Change |
| --- | --- |
| Docked edit menus were hidden; structural controls appeared only on hover. | Edit, move, duplicate and delete remain visible and keyboard reachable. |
| Docked loop badges and trigger status were hidden. | Show repeat/sequential/parallel badges, exit-condition sections and trigger state. |
| Many docked instruction parameters were hidden. | Show wrapped details below each instruction, including notification text and loop timing. |
| Deep indentation consumed the narrow panel. | Use nested group borders and modest local inset rather than accumulated row padding. |
| A target image stacked above a large graph consumed most of the panel. | Use a small camera preview beside a reduced graph; stack at very narrow target widths for readability. Coordinates, PA/FOV, twilight and site information remain. |
| Expanded names/details could be truncated; nested trigger columns could squeeze rows. | Wrap names and summaries, and stack triggers based on each set's own available width. |
| Compact status and exposure progress were less visible. | Preserve running/waiting/completed/error styling, current-step mark and exposure progress. |
| Compact triggers were far below long instruction lists. | Show each set's trigger section before its instructions in the docked view; expanded view retains the side column. |

| The narrowest docked toolbar could clip button labels. | Reflow it to two columns at panel widths under350px, retaining readable text and every control. |
| Trigger dialogs could remain outside the viewport after resizing. | Clamp open trigger dialogs back within the viewport on resize. |

Both sizes use the same rendered sequence tree, native node IDs, editing guards,
command handlers, insertion picker and modal editors. There is no duplicate
compact sequence model, new telemetry polling or additional retained history.
The native projection and existing editing limits remain unchanged.

## Acceptance coverage

Verification evidence is saved under Starfront Operations
`.codex-artifacts/2026-10-24-sequence-ui-audit/`.

- Compare compact/full node IDs, visible edit/structural controls, group badges,
  trigger states, target panels and exit sections on the same Parallels tree.
- Exercise notification, photo, fixed/astronomical time, rollover/countdown,
  DIY flip and unsafe/Once Safe editors through the docked view.
- Insert/edit/duplicate/reorder/remove disposable simulated steps; preserve
  stopped-only controls and stale/offline behavior.
- Inspect full and compact rendering at desktop/tablet/phone widths, checking
  overflow, clipping, target graph and camera image, and dialog bounds.
- Check collapse/expand persistence, sequence library and start confirmation;
  validate current/completed state with a separate wait-only simulated sequence.
- Production verification is viewing and dialog navigation only. No real
  equipment, sequence editing or start command is used for live acceptance.

Configured camera framing remains an estimate from saved rig optics; a survey
preview is not a captured exposure. Unsupported native parameter types stay
read-only. This audit does not establish new hardware capabilities or certify
all browsers or accessibility standards.

## Verified result

Deployed `app.js` and `styles.css` to the existing Starfront LXC gateway. No
plugin installation, NINA restart, gateway restart or website Worker deployment
was needed. The observatory acceptance run issued zero native command POSTs;
the current dated sequence was still stopped at readback.

| Check | Result |
| --- | --- |
| Page identity, meaningful content, no framework overlay | Passed in the simulation and public dashboard. |
| Application errors | No page errors in acceptance; existing public Cloudflare analytics CSP messages remain unrelated. |
| Compact/full parity on the 237-node Parallels projection | Same native row IDs,93 edit menus,312 structural controls,21 badges,20 trigger-state labels,3 target panels and17 exit sections. |
| Compact layout widths | No row/tree clipping at320,390,768,1024,1100,1440 and1920px. |
| Full layout widths | No row/tree clipping at320,390,768,850,1024,1100,1440 and1920px after responsive nesting fixes. |
| Header controls | All labels fit at320,390,768,1024,1100 and1440px. |
| Native edits from the compact panel | Notification text, time editor/countdown, DIY and unsafe/Once Safe actions, grouped insertion, wait parameters, duplicate, keyboard reorder, pointer drag and delete passed. Disposable changes removed. |
| Sequence state | A separate wait-only Parallels sequence verified Start confirmation, waiting/current-step mark, completion checkmark, reset/stop and editing guards while running. |
| Offline viewer | Start and structural edits disabled; cached sequence remained viewable. |
| Dialog resizing | Open DIY dialog stayed inside a390x844 viewport after desktop-to-phone resize. |
| Public dashboard | Compact/full control and visual parity, camera preview/graph, nested roof editor, mobile dialog bounds and zero native writes passed. |
| Static checks | Strict TypeScript and35 web tests/104 assertions passed. |

Environment: Windows11 Parallels/NINA plugin0.4.25.0 through an isolated gateway
at `http://127.0.0.1:1897`; production `https://starfront.thewcl.com/dashboard/`.
The Browser plugin was unavailable, so the existing regular Playwright Chromium
session was used. Screenshots and exact command/readback results are in the
artifact directory above. The simulator was restored blank/stopped/disconnected;
owned fixtures and helper services were removed/stopped. No new long memory
observation or hardware validation was performed for these presentation changes.

## Screenshots

The first image is the deployed compact panel with the camera preview, visibility
graph and trigger sections. The second is the native simulated waiting state,
including the current-step mark and blocked edit controls.

![Deployed compact sequence panel](/Users/kevinmcquown/Documents-Local/Personal/astrophotography/Starfront%20Operations/.codex-artifacts/2026-10-24-sequence-ui-audit/starfront-sequence-compact-live-panel.png)
![Simulated waiting sequence](/Users/kevinmcquown/Documents-Local/Personal/astrophotography/Starfront%20Operations/.codex-artifacts/2026-10-24-sequence-ui-audit/starfront-sequence-compact-running.png)
