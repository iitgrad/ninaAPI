# Stopped sequence editing

The plugin projects an editing contract alongside the existing sequence tree. The web editor is in `web/sequence-editor.ts`; the concurrent device-panel work owns its small mount/update integration in `web/monitor.ts`. This increment does not deploy the observatory plugin.

## Contract

The sequence topic adds `sequenceId`, `configurationRevision`, `editing` and `insertCatalog`. Each node adds `editableFields` and `insertKinds`. Field descriptions include actual values, types, units, ranges and configured choices. Unknown third-party types have no editable fields. Root/area iteration counters and TakeExposure.ExposureCount are execution counters, not settings. Repeat count belongs to LoopCondition; the Exposures field on TakeManyExposures/SmartExposure edits that loop condition. New compound exposures start at one second and one frame, preserving the original instruction name.

Submit through the existing POST `/api/v1/commands` operation lifecycle:

```json
{"requestId":"NEW-UUID","device":"sequence","action":"editField","parameters":{"sequenceId":"FROM-TELEMETRY","expectedRevision":1,"nodeId":"FROM-TELEMETRY","field":"ExposureTime","value":30}}
```

```json
{"requestId":"NEW-UUID","device":"sequence","action":"insert","parameters":{"sequenceId":"FROM-TELEMETRY","expectedRevision":1,"parentId":"FROM-TELEMETRY","index":0,"type":"TakeExposure"}}
```

The plugin refreshes and validates the configuration on NINA's UI thread at admission and again immediately before execution. A changed root or configuration rejects stale edits. Execution status and counters do not advance the configuration revision. Configuration identity covers the projected supported settings, selected profile/filter choices, node identity/order/hierarchy and names, rather than all private third-party settings. A truncated projection disables editing. Edits are blocked during acquisition/control operations, sequences and exposures. No auto-retry occurs. Existing legacy Starfront numeric edit/target/addTarget commands now also require the configuration tokens.

NINA remains authoritative. The successful operation and subsequent pushed values confirm the result. A timeout does not imply failure. The browser preserves drafts when another client changes the configuration and requires refresh before another submission. Old plugins without metadata hide the editor.

## Hierarchy and picker

The main sequence panel shows hover/focus insertion lines beneath containers and between steps; there is no Edit sequence launch button. Clicking a line opens a contextual flyout at that insertion point. Clicking an existing editable step opens a compact Step settings dialog. Equipment/category choices open an adjacent submenu by hover, click or keyboard; available entries are filtered by that parent’s insertKinds and the host catalog. Targets only offers the compatible deep-sky target container. Separate attachment lines offer conditions and triggers. Arrow keys move between categories/items; Escape closes the flyout and restores insertion-line focus. A changed configuration or unavailable connection closes the picker, and the existing revision checks still guard every submission. Start, Targets and End remain visible in the monitor when empty; missing advanced sections are display-only placeholders, never invented command targets.

- Camera: TakeExposure, TakeManyExposures, SmartExposure, TakeSubframeExposure, CoolCamera, WarmCamera, DewHeater, SetReadoutMode, SetUSBLimit.
- Filter Wheel: SwitchFilter, using active-profile filters.
- Timing: WaitForTimeSpan.
- Instruction sets: SequentialContainer, ParallelContainer, DeepSkyObjectContainer.
- Conditions: LoopCondition.
- Triggers: AutofocusAfterExposures, DitherAfterExposures.

Factory-created NINA instances are inserted with NINA's `InsertIntoSequenceBlocks`, which attaches the correct parent and collection. Parallel groups retain NINA's parallel execution strategy. Deep-sky objects expose target name, RA in hours, declination and position angle in degrees. Cooling ramp duration is in **minutes**, matching the NINA instruction (distinct from the direct camera command's rampSeconds).

Compound exposure instructions retain their built-in child structure. Their exposure/count/filter parameters are also exposed on the compound element; child loop and dither fields can be selected in the tree; their fixed internal structure is not an insertion target. New elements are selected after authoritative telemetry confirms insertion. Unsupported instruction settings remain read-only; this is not complete arbitrary NINA/third-party editor parity. Conditions/triggers other than the listed entries are visible but not offered for insertion in this increment.

Insertion is bounded below 240 projected elements, depth 15, 32 targets and the existing bounded telemetry byte budget. No sequence history is retained in the plugin. Changes affect the **loaded sequence only**; there is no automatic file save, implicit start, deletion or reordering of existing elements in this increment.

## Verification

Core tests cover typed values, ranges, configured choices, name bounds, unsupported payloads and configuration conflicts. `tests/integration/sequence-editing-parallels.ts` is explicitly pinned to the Parallels address, simulation environment, expected build and simulation profile; it never defaults to the observatory CLI configuration. Runtime evidence is saved under ignored `artifacts/2026-10-23-sequence-editor/`.

Runtime results are recorded in PROJECT-CONTEXT.md after the simulator run. Compilation does not establish simulator success or observatory validation.

## Current follow-up: 0.4.17.0

The catalog and structural restrictions described above for 0.4.16 are superseded by this increment. `sequenceCatalog` is a separate bounded push topic discovered from the active NINA sequencer factory: instructions, containers, conditions and triggers, including loaded third-party plugins. Entries include a localized category/label, assembly source, kind and actual node type; third-party insertion IDs are opaque hashes. The server resolves only its loaded templates, never a client-provided assembly/type name. The simulator with Sequencer Powerups reports all 156 entries in about 30 KiB. A catalog exceeding 60 KiB reports `complete:false` with an explicit error rather than silently truncating. Arbitrary third-party expression/settings editing remains read-only; discovering an instruction does not imply parameter editor parity or successful execution.

`duplicate`, `delete` and `move` require `nodeId`, `sequenceId` and `expectedRevision`; `move` additionally requires `parentId` and `index`. Indices identify insertion slots in the destination Items/Conditions/Triggers collection **before removal**. Same-parent forward moves adjust after removal. Duplicates deep-clone the host entity adjacent to the source; deletion removes a whole subtree. Root/Start/Targets/End and immutable compound children cannot be changed structurally. Placement, unique conditions/triggers, cycles, depth, target counts and subtree size are checked on the UI thread after current-root/configuration/stopped checks. Moves retain identity and attempt restoration to the original position if attachment fails. Mutations affect the loaded sequence only; file saving and execution remain separate.

Expanded presentation has compact numbered rows, loop rails, exit conditions and scoped trigger cards. Pending status has no icon; current is a spinner, success a green check, failure a red cross. Trigger cards say Configured while stopped and Watching only for an active scope with fresh telemetry. Inline slots support instructions, containers, conditions and triggers. Duplicate/trash icons and a grip handle appear in expanded rows; docked rows expose them on hover/focus. Pointer dragging and Alt+Up/Down reorder using guarded commands. A changed revision/session during dragging invalidates the drop. The long catalog has independently scrolling columns and category/submenu keyboard navigation.

Each expanded DSO starts with RA, declination and rotation controls (opening the existing guarded editor), a night selector, and a calculated target-altitude/twilight chart. `administration.site` publishes profile latitude/longitude/elevation/site name and the NINA host's IANA time zone. No location is inferred from the target or browser. Astronomy Engine 2.1.19 calculates J2000 target visibility, standard rise/set, transit and solar -12/-18 degree crossings. Altitude is geometric, with no terrain horizon mask. Date selection only changes the chart. No automatic observing-window instruction is created. The simulator profile uses an explicitly labeled approximate Texas test site at 31N/100W, 600m; those values are not production coordinates. The calculation cache and retained night selections each cap at 32 targets. Charts are constructed only in expanded mode. No new timer, polling loop or unbounded history was introduced.

Verification: full `scripts/build.sh` passed (79 core tests, astronomy/observation tests, plugin/service/CLI builds, CLI smoke and service lifecycle). Parallels scripts `sequence-catalog-parallels.ts` and `sequence-structure-parallels.ts` verified actual factory discovery/Powerups insertion, target settings, 16 structural checks, copied values/new identities, same/cross-parent moves, subtree deletion, and stale/cycle/structural/unique-condition rejection. Browser duplicate, Alt+Up, pointer drag and delete were each verified against native IDs. Chart date selection left the configuration revision unchanged; coordinate buttons opened the correct editor. Powerups Expressions submenu was visually verified. No sequence or exposure was executed in this increment. Unknown plugin execution is not verified.

Evidence and the hash-verified simulator-installed package are under ignored `artifacts/2026-10-23-sequence-catalog/`. Website JS/CSS are published separately. The real observatory plugin has not received 0.4.17; its upgrade/restart requires separate authorization.

## Explicit setup-first start: 0.4.18.0

`sequence/start` accepts optional boolean `skipValidation`, default false. The dashboard's Start confirmation exposes an unchecked “Start using sequence setup” option only when the pushed command catalog advertises this parameter. Selecting it calls NINA's native `StartAdvancedSequence(true)`, bypassing all startup validation warnings, not just disconnected-device warnings. Use it only when the loaded sequence connects required equipment before using it. Runtime instruction failures remain failures; no automatic retry or validation bypass is enabled. Existing authentication, fresh-state/session/sequence confirmation and acquisition/profile/simulation guards remain active. The option resets on every confirmation. Existing callers omitting the parameter keep native validation.

Parallels0.4.18 tested via the actual dashboard: camera/mount disconnected, ConnectEquipment(Camera), ConnectEquipment(Mount), TakeExposure(1s). All three instructions finished, one preview/FITS frame arrived, and the command succeeded without a blocking NINA popup. Camera/mount subsequently disconnected. Core tests include omitted/false/true and invalid boolean types. Simulator sequence inspection now permits only the additional built-in ConnectEquipment/DisconnectEquipment types; selected-device allowlist guards remain enforced.
