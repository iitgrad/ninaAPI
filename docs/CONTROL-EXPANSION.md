# Starfront Control 0.4.10.0 — simulator verification

Plugin, Mac gateway and CLI 0.4.10.0 are installed and running in the isolated Parallels simulation. Kevin authorized this expansion and simulator testing. The observatory NUC, real equipment and production profiles were not changed. Work remains local and uncommitted.

The browser and CLI use the same typed command catalog and authenticated POST operations. Results and device states arrive over WebSocket; browser status polling is unnecessary. Commands are deduplicated by request ID and parameter identity, conflicting acquisition operations are rejected, and owned operations have explicit interrupt paths. Legacy GET control routes remain disabled.

| Capability | Actual simulator evidence |
| --- | --- |
| Camera | Capture/save, multiple light/dark frames, abort, binning/readout/dew/USB settings; OmniSim cooled to 5C and warmed to 20C with cooler off. Unsupported native cooling rejected. |
| Mount | Tracking, slew, coordinate sync, bounded axis movement/stop, home, park/unpark; final build verified actual East-to-West pier change and rejects a no-op flip. |
| Focuser/filter/rotator | Focuser position moves; filter selection; rotator sync and sky/mechanical moves. Autofocus produced two HFR points, cancellation completed and cleared the running flag. Optical convergence remains unverified with a static image fixture. |
| Guiding | Actual Windows PHD2 built-in simulator calibration and start; independent SettleDone status 0 and Guiding state, guide samples, stop and calibration clear. Positive settling was on 0.4.6; later builds retain that implementation. |
| Dome/panel/switch | Shutter open/close, azimuth/home/park, follow on/off; panel cover/light/brightness and switch values. Unsafe simulated safety input correctly blocked opening; safe input enabled the positive check. |
| Weather/safety | Both exact local simulator IDs connected and emitted live scalar readings. |
| Sequence | Load, start, stop, reset, numeric element editing, skip, skip-current, target creation and coordinate editing; actual capture plus wait completed with pushed tree states. |
| Images/calibration | Saved FITS streaming download; ASTAP+D05 solved public DSS M31 simulator image using an 1150mm scale hint. Incorrect scale failed explicitly. Automatic exposure flats saved a 640x480 FLAT at 1.05 seconds, measured mean 32767.66. |
| Administration | Bounded log tail and screenshot, refresh, list profiles and switch between two isolated simulation profiles, returning to the main simulation profile. |
| Mac browser/CLI | Catalog-driven typed controls, validation, submitted capture completed; live sequence/status/plots and image preview. Responsive 390px and desktop checks passed. CLI terminal results include original parameters. |

## Verification and evidence

49 core tests and 7 observation-analysis tests pass, along with strict TypeScript, CLI smoke checks and isolated gateway lifecycle tests. Gateway and CLI builds have zero warnings/errors. Windows plugin cross-build has 45 inherited dependency/legacy warnings and zero errors. Build logs, actual command histories, screenshots, states and package manifest are under `artifacts/2026-10-22-control-expansion/`.

`live-commands.json` intentionally retains earlier failures, fixture corrections, expected validation rejections and results across versions. Versioned final evidence includes `after-flats.json`, `after-dome-safe.json`, `after-autofocus.json`, `autofocus-result-final.json` and `browser-operation-final.json`. Do not describe every historical command as a successful final-build test.

Final resource observation: 19 WebSocket diagnostic samples over three minutes, one N.I.N.A. PID, version 0.4.10.0. Private bytes fell from 568655872 to 494882816; managed live estimate fell from 152973504 to 79344952. Pending telemetry remained zero, one transport/client remained retained and send/publish/diagnostic errors remained zero. N.I.N.A. log grew 6423 bytes and plugin diagnostic logs grew 15869 bytes. Plugin logs remain bounded at 20MiB. This short observation includes simulator commands and does not replace a long repeated-imaging soak or validate real drivers. See `resource-summary.json` and raw samples.

The ZIP is 420087 bytes, SHA256 `6e130bde05747ae7710f790b9bae4b18f1ef2c1f59d7ece33a891163d79780b0`. Every packaged file hash and size matches the installed guest copy. It contains plugin binaries/configuration only, no bearer credentials or production profiles. See `package-manifest.json`.

## Bounds and remaining scope

32 pending telemetry topics, eight plugin clients, one pending snapshot per client; 128 retained operations with 32 compact pushed summaries; 128 device choices/58KiB catalog with explicit truncation; 32 saved-image references and image-history entries; 64 autofocus and 120 guide points. Download streams are capped at 128MiB, log tails at 8KiB and screenshots at 16KiB. Sequence projection is limited to 256 nodes/depth16; target creation is capped at32. Consumers/subscriptions have paired disposal. Full operation parameters are fetched once after terminal socket notification.

This increment supplies common control access rather than complete legacy endpoint parity. Arbitrary script/plugin sequence editing, full framing/sky atlas, histogram/stretch tools, 3D telescope rendering and full external PHD2 UI remain outside this increment. Simulation sequence execution permits a bounded inspected set of standard containers, exposures, waits and loops. Sequence-root access remains version-sensitive in N.I.N.A.3.2 and reports unavailable on incompatible hosts. Real equipment, observatory installation and a long acquisition soak remain unverified.
