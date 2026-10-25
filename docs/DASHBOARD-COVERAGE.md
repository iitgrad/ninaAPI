# Mac dashboard capability coverage

Kevin's October 22 screenshot is a functional reference. Version 0.4.10.0 adds catalog-driven browser/CLI controls to visual telemetry and the sequence panel. See [simulator verification](CONTROL-EXPANSION.md) for the current control matrix. It is not a claim that every N.I.N.A. control is implemented.

| Panel/function | Current source and implementation | Verification boundary |
| --- | --- | --- |
| Full sequence | Bounded event-driven hierarchy of containers, instructions, conditions and triggers; stable identity per host object, original host status and display state | Actual advanced capture→wait→complete observed through plugin, gateway and browser. Source enum tests cover pending/running/completed/failed/skipped/disabled; waits preserve RUNNING as source status. |
| Image and statistics | Latest bounded JPEG, saved image metadata/statistics and last 32 image entries; fit/actual-size display | Actual simulator image decoded by Mac browser with live stats. Preview rendering is display data, not a scientific export. |
| Camera and mount | Existing cached state, simulator connect/disconnect, coordinates/tracking/parking fields | Actual simulator connections and acquisition verified. |
| Weather/safety/rotator/filter wheel/focuser | Copied scalar mediator updates; missing/disconnected values remain explicit | Actual local simulator connections and live values verified. |
| Autofocus | Start/end notifications and last 64 focus-position/HFR points | Actual two-point HFR run and cancellation verified; optical convergence remains unverified. |
| Guider | Connection/scalars and last 120 raw RA/Dec guide steps | Actual PHD2 simulator calibration, successful settling, guiding and stop verified; units are copied explicitly. |
| Diagnostics | Process/GC/queue/client/log budgets plus subscription count | Existing resource tests; latest functional checks record exact counters. |
| CLI | Existing commands plus `sequence --json`; `watch` carries the same dashboard topics | Installed CLI reads actual sequence tree and source timestamp. |

## Control access and remaining visual work

The shared typed catalog supplies capture/settings, all equipment groups, sequence actions and bounded editing, image solve/download, flat calibration and administration through validated POST operations and CLI. The browser retains these in its Controls & diagnostics dialog. The October 23 Observatory Console implements the selected first design concept with direct connection controls on the five equipment cards and a full-height right sequence panel. See the expansion report for per-capability simulation evidence and limits.

An explicitly labeled rendered-JPEG luminance histogram, Fit/1:1/2x zoom and fullscreen preview are implemented. Raw-image histogram/stretch tools, richer guide-star detail and a telescope-orientation model remain future visual work. Full arbitrary sequence/plugin editing is not exposed. No observatory actions are authorized by simulation testing.

## Resource and compatibility limits

Sequence projection: at most 256 nodes, depth 16 and 58 KiB of node JSON, with an explicit partial-tree indicator if limited. Topics retain at most 64 KiB each/256 KiB total; bridge frames remain limited to 512 KiB. Preview queue holds one immutable source bitmap, encoded preview <=40 KiB (800/480/256 maximum dimensions), and replaces the prior preview. History is 32 images, 64 autofocus points and 120 guide points. Adapter pending topic capacity is now 32, with one signal slot and 100ms coalescing. All additional consumers/event handlers are paired with disposal.

N.I.N.A.3.2's public sequence mediator exposes active items but not the full root. The adapter isolates one version-sensitive access to its navigation view model; the rest projects explicit properties and observes change notifications. Compatibility failures produce an unavailable sequence state, never an empty tree labeled successfully complete. A ten-second host discovery refresh catches initialization/root replacement; browser clients do not poll or walk host objects.

## October 23 browser acceptance

Actual Parallels tests exercised all five card connect/disconnect paths and their pushed status, auxiliary catalog connect/disconnect paths, load/start/wait/stop/reset/complete sequence transitions, image decoding and FITS download. Desktop/tablet/mobile layouts and modal/tree/zoom interactions passed with no browser errors. The test sequence is stopped/completed; no real-equipment command was issued. Optical autofocus/guider acceptance and a new long resource soak were not part of this web-only change. `?preview=1` is explicitly illustrative and disables all commands.
