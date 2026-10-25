# Starfront Control: project context and handoff

Recorded October 22, 2026, America/Chicago. This document summarizes the discussion that led to this fork and the local foundation work below. Separate implemented components from proposed work and Windows/runtime validation. Dated operational observations must be refreshed before live work.

## Latest verified state

As of October23, Starfront Control0.4.16.0 is installed on observatory NUC SFO-Scope100.89.247.90 under Kevin's explicit restart/deployment authorization. NINA3.2.1.3002 restarted asPID6828 in interactive session2; fresh native session81a61fc7-b1a1-4cba-8bb3-0b18015575c5 and the live Starfront website are verified. Dashboard Edit sequence is enabled with stopped-only parameter editing and categorized insertion. All installed package hashes match the Parallels-tested build; plugin configuration is unchanged. NINA reopened with an empty sequence and did not restore the prior unsaved CoolCamera step; Kevin was informed. All equipment remains disconnected and no production sequence edit/start or equipment command was issued. Hosted editor/stream and new-command dry-run validation passed without changing the loaded sequence. Detailed latest evidence and limitations are in the final dated deployment section below. Older checkpoints are historical.

## What Kevin wants

Kevin operates a remote astrophotography rig at Starfront Observatory in Texas using N.I.N.A. on a Windows NUC. His Starfront website includes nightly plans/results, image processing progress, a gallery and a Live Observatory page.

He wants reliable live equipment status, coordinates and telescope orientation, image previews, guiding/autofocus information, and a scrollable view of the loaded sequence with the current step highlighted. Opening more browser tabs or reconnecting a dashboard should not add significant work inside the imaging application or interfere with acquisition.

We discussed writing our own N.I.N.A., potentially in Rust. Kevin is not committed to Rust and accepted the recommendation to stay with C#/.NET for the backend. The chosen starting point is a fork of Advanced API, rather than a wholesale rewrite of N.I.N.A. A TypeScript browser interface was proposed; it is not necessary to rebuild the existing website as the first step.

## October 22 follow-up: expanded product objectives

Kevin has expanded the objective beyond read-only telemetry: the fork should also support remote operation of N.I.N.A. through a local CLI, an eventual custom GUI, and commands Kevin gives Codex. N.I.N.A. should remain responsible for hardware integration and advanced sequence execution. Desired coverage includes connecting equipment, camera temperature control, and progressively the operations normally performed through N.I.N.A.'s GUI. Complete GUI-equivalent coverage is an objective, not an established capability of the current API.

Kevin wants resource-management fixes even for clients that continue polling, alongside a push-based website that receives initial/reconnect snapshots and subsequent state changes. Kevin selected the name **Starfront Control** and authorized local plugin implementation while Windows finishes installing.

A local CLI, authenticated HTTP command API and WebSocket state/operation updates are now implemented as an incremental simulation build (details below). Long-running commands need distinguishable acceptance, progress, completion, failure and cancellation, with protection against duplicate execution and conflicts with an active sequence.

Kevin installed fresh Windows through Parallels on his Mac after deleting the previous VM files to recover disk space. He explicitly authorized monitoring readiness and installing N.I.N.A. in this VM. Windows, N.I.N.A., the plugin, and basic simulator connections are now verified below. The intended test approach compares the unchanged plugin and the fork under matched simulation workloads, including polling, WebSocket clients, slow/disconnected clients and lifecycle cycles. Kevin explicitly deferred an overnight test; the telemetry observation was ended by Kevin after 93 minutes.

These objectives do not authorize deployment to the observatory NUC, uninstalling its existing Advanced API plugin, enabling production polling, connecting real equipment, or issuing live control commands. A possible later replacement of Advanced API was discussed; dependencies and rollback must be checked before that installation change.

## Current state: what exists and what does not

- GitHub fork: https://github.com/iitgrad/ninaAPI
- Original project: https://github.com/christian-photo/ninaAPI
- Local checkout: `/Users/kevinmcquown/Projects/Personal/nina-advanced-api`
- `origin` points to Kevin's fork; `upstream` points to the original project.
- At creation, the checkout was clean on `dev`, tracking `origin/dev`, at `353a853fb598cfa358900340396806c945a0de36`.
- The specifically inspected release is tag `2.2.15.2`, commit `9a814f03df397ae294c65200f37b9262e391cef6`. The tag is available locally.
- Upstream also has 3.0 beta tags pointing at the fork's initial dev commit, while its AssemblyInfo reports 2.2.15.2. Source comparison confirmed `353a853` differs from release `2.2.15.2` only in `Changelog.md`; development uses that dev commit, with the upstream .NET/N.I.N.A. dependency baseline unchanged.
- Local development branch: `codex/starfront-control-foundation`. The foundation and integration work are described below. The new plugin, gateway, CLI and browser monitor run in isolation against simulated camera/mount devices. The resource observation ended after 93 minutes at Kevin's request; no observatory deployment has occurred.
- Changes remain local and uncommitted, including the pre-existing README/handoff documentation. No deployment or push was performed; inspect current Git state on resumption.

## October 22 foundation implementation

- Plugin name `Starfront Control`, GUID `897ef051-204c-4dfb-9697-d3209a15adc0`, assembly `StarfrontControl.dll`, initial version `0.1.0.0`. Settings group, port-discovery broker topics, UI metadata, resource assembly URI and custom driver IDs are separate from Advanced API. Default port is 1889 and the server is disabled for a fresh settings group. Coexistence and isolation in the actual N.I.N.A. host are unverified.
- Removed automatic post-build installation and the historical batch script's upstream publishing/deployment actions. Builds only produce artifacts. The portable core is a required companion assembly; dependency staging is not yet validated.
- Event history now retains the newest 2,048 entries through the portable thread-safe `BoundedHistory<T>`. Stable snapshots avoid enumerating the mutable collection. `/event-history` no longer promises entire-process history. Entry count is bounded; individual event sizes are not yet limited.
- Removed the log processor's unused retained event list. Socket disposal now removes static rotator/filter-wheel event subscriptions and polar-alignment broker subscriptions. Screenshot/capture-result bitmaps and the raw-FITS memory stream have scoped disposal. These plugin changes compile but have not been exercised in N.I.N.A.
- Added the portable `TelemetryStateStore` with a pre-serialized immutable reconnect snapshot, per-instance session ID, revisions, per-topic observation timestamps, rejection of older observations and configurable topic/payload limits. Snapshot reads do not allocate or serialize. This component is **not yet wired to N.I.N.A. or any transport**.
- Added `StarfrontControl.sln`, a portable xUnit test project and [development/verification instructions](docs/DEVELOPMENT.md). Component tests exercise retention, collection of evicted/replaced objects, concurrent reads/writes, polling, byte limits, freshness and snapshot consistency. They do not establish overall N.I.N.A. memory stability.
- The Mac has .NET SDK 10.0.302/runtime 10.0.10. Portable projects target .NET 8; the tests allow major runtime roll-forward. Windows-targeted plugin cross-compilation on macOS passed. Native Windows build/loading, WPF runtime behavior, socket cleanup, simulator workflows and overnight resource validation remain pending. Build warnings include legacy references/packages and NuGet's high-severity advisory for the unchanged `Microsoft.Windows.Compatibility` 6.0.1 dependency; resolve/assess dependencies before deployment.
- Final foundation verification: 21 portable tests passed, zero failed; cross-build passed with zero errors and 66 warnings. Local ignored evidence is under `artifacts/2026-10-22-foundation/`: `verification.json`, `plugin-cross-build.log` and `core-tests.trx`. The manifest records output sizes and SHA-256 hashes. This is not an incident reproduction or a Windows runtime result.

Image/thumbnail retention, expensive sequence serialization, bounded socket delivery, acquisition isolation, authentication, the command lifecycle, new endpoints, CLI and website integration are still outstanding. The initial source review did not establish the cause of the historical exhaustion incident.

## October 22 Windows VM installation and startup

- Parallels VM `Windows 11`, UUID `8ce5a2a4-4ab0-4327-9057-b8764d964c4c`, is running Windows ARM64. Guest setup/OOBE flags were both zero and Explorer was running before application installation. Guest commands work through `prlctl exec`; use `--current-user` for the interactive Windows user `kevinmcquown` (session 2), otherwise commands run as SYSTEM. Use encoded PowerShell commands to preserve quoting.
- Before installing N.I.N.A., created Parallels snapshot `c2df2274-aaf5-4842-a946-1d5939325506`, named `Before NINA test installation 2026-10-22`.
- Downloaded N.I.N.A. 3.2 Hotfix 1 RC2, executable version `3.2.1.3002`, from the official download page's beta link: `https://f002.backblazeb2.com/file/ninasetup/Betas/3.2.1.3002/NINASetupBundle_3.2.1.3002.zip`. This matches the dated observatory version recorded in the handoff. The 167,029,312-byte archive's SHA-1 matched the download server's large-file metadata: `a8b0b0ac3565ea0b570119d33db998dc526b6371`. The contained installer reports `NotSigned`; installation used the official HTTPS source and matching archive hash.
- Installed with `/install /quiet /norestart`; exit code 0, no restart required. Guest setup evidence is under `C:\StarfrontControlTest\Setup\2026-10-22`. Application is `C:\Program Files\N.I.N.A. - Nighttime Imaging 'N' Astronomy\NINA.exe`.
- Opened N.I.N.A. as the interactive user and verified a responsive main window, file version `3.2.1.3002` / product version `3.2.1.3002-rc`, and the rendered equipment page. Left it open. This establishes application startup on this VM, not simulation or plugin compatibility.
- Startup logged no ASCOM Platform installed, as expected for the fresh VM. A background EarthRotationParameters update returned HTTP 400; N.I.N.A. still reached its main window. This nonfatal data-update issue remains unresolved.
- Starfront Control, Advanced API, ASCOM simulators and observatory profiles/credentials were not installed. No hardware connection, exposure or sequence was initiated; the observatory NUC was not accessed.
- Local ignored evidence: `artifacts/2026-10-22-windows-setup/`, including `snapshot.txt`, `guest-download.txt`, `guest-install.txt`, `nina-launch.json`, `nina-ready.json`, `nina-startup-check.txt` and `nina-ready.png`.

Kevin plans to add this folder as a separate Codex project. The original discussion and operational evidence live in the Starfront Operations workspace listed below.

## Why this approach

1. C# supports WebSockets; switching to Rust is not required for efficient telemetry. Architecture, allocation patterns, caching, rate limits and failure isolation matter more than language choice for this problem.
2. Advanced API already integrates with N.I.N.A.'s equipment, sequencer, images and events. Reusing those integrations avoids reimplementing hardware support, guiding coordination, autofocus, flats and sequence execution.
3. A native SDK or driver can block regardless of the calling language. A plugin still shares N.I.N.A.'s process; an embedded server thread is not a separate failure boundary.
4. Large image buffers can dominate memory use: a roughly 61-megapixel monochrome frame is about 122 MB at 16 bits or 245 MB as float32, before additional working copies. Avoid repeated full-resolution copies and encoding for a monitoring UI.
5. N.I.N.A. and Advanced API are MPL 2.0 projects. Private modifications are permitted. Distributing a modified build brings source-availability obligations for covered files and requires preserving applicable notices. Separate new files without MPL-covered code may use other licenses. See https://www.mozilla.org/en-US/MPL/2.0/FAQ/ for the official explanation; this paragraph is a summary.

## Operational history and evidence limits

An earlier dashboard collector repeatedly queried Advanced API. Kevin observed resource exhaustion/hangs and identified the new polling as a likely contributor. The collector was disabled. That association is worth investigating, but no controlled reproduction or memory profile established it as the cause of every incident.

Other failures were observed separately: Orion centering launched ASTAP without a recorded completion; later cancellation did not promptly stop it. A subsequent shutdown stalled at the filter-wheel disconnect. These observations do not prove a common cause, nor that a telemetry plugin change fixes them.

Local October 22 completion evidence records N.I.N.A. upgraded to 3.2 Hotfix 1 RC2, version `3.2.1.3002`, followed by an idle startup check. Advanced API `2.2.15.2` loaded, and dashboard polling remained disabled. Windows performance logging and morning log archiving were installed. That was startup verification, not a successful overnight imaging test. Treat these as dated observations, not current live state.

## How the existing plugin works

The findings below were inspected in the `2.2.15.2` release source unless otherwise stated. Recheck them against the baseline chosen for development.

### Hosting and interfaces

- `ninaAPI/WebService/API.cs` starts an EmbedIO HTTP server in N.I.N.A.'s process, with a server thread and event watchers.
- The usual port is 1888. HTTP routes live under `/v2/api`; the main event WebSocket is `/v2/socket`.
- REST handlers use N.I.N.A.'s internal mediators and other application interfaces. Some access concrete view models or use reflection. This is more tightly coupled than an independent hardware service.
- HTTP endpoints include both observation and equipment/sequence control. Explicitly allowlist telemetry endpoints; HTTP GET alone does not imply a harmless status read.

### Events and missing continuous state

- `ninaAPI/WebService/V2/WebSocketV2.cs` broadcasts events such as connection changes, image saves, filter changes, dithers and sequence start/finish.
- Image-saved events include useful metadata and statistics; clients need not repeatedly retrieve the full image history to count new frames.
- `SendConsumerEvent` immediately returns in the inspected release. Although there are enable/disable-consumer-events messages, the continuous equipment-info notifications are effectively disabled by this return.
- The built-in sequence watcher reports sequence start/finish; it does not by itself provide the complete incremental sequence-progress feed needed for the proposed right-hand panel.
- Event history is appended to a static list. No retention cap was found in the inspected implementation. This is a code-review finding, not a demonstrated explanation for multi-gigabyte memory growth.
- Broadcasts await sends to clients in a loop. Slow-client behavior and queue/resource limits should be measured rather than assumed.

### Request costs

- `ninaAPI/WebService/V2/Equipment/Connection.cs`: `/equipment/info` assembles information from multiple mediators. It is not equivalent to a fresh direct hardware query for every field, but particular getters and serialization still require inspection and measurement.
- `ninaAPI/WebService/V2/Application/Sequence.cs`: `/sequence/state` walks the sequence, conditions and triggers, uses reflection to read properties, and builds a response on every request.
- `ninaAPI/Utility/CoreUtility.cs`: response serialization is another relevant boundary. Removing fields after the client receives a response does not prevent the plugin from constructing/serializing them first.
- `ninaAPI/WebService/V2/Application/Image.cs`: image history, thumbnail generation, image preparation and transport require separate inspection. Do not confuse metadata history with retained full-resolution image buffers.

### What our old collector did

The local PowerShell implementation is `ops/windows/Watch-StarfrontLiveObservatory.ps1` in Starfront Operations. It requested equipment info and full sequence state approximately every three seconds. Approximately every eight to nine seconds, it refreshed guider graph, last autofocus, latest-image metadata and full image history. It fetched a thumbnail when the detected image changed. A mutex prevented duplicate collector instances in that implementation.

This is the local code that was inspected, not a fresh read of the installed Windows copy. It used polling rather than the existing event WebSocket. Its potentially expensive full-sequence and history calls are candidates for replacement, but request costs and causal effects have not been profiled.

## Proposed first implementation

Establish bounded monitoring first, then add the authorized control interface. N.I.N.A. remains responsible for acquisition, sequence execution and hardware control. The portable snapshot component exists; the adapter/service/transport below are still proposed.

```text
N.I.N.A. state/events
        |
small C# plugin telemetry adapter
        |  compact, rate-limited snapshots and events
separate C# telemetry/cache service
        |  WebSocket snapshots and updates
browser dashboard(s)
```

The separate service would isolate browser sessions, caches and image delivery from N.I.N.A. The adapter itself would still be in-process and must remain bounded and lightweight.

Desired behavior:

- Build compact snapshots from already available status; do not query equipment in response to individual browser requests.
- Coalesce frequent updates and publish at a controlled rate. Send an initial/reconnect snapshot, then updates with timestamps and sequence/version identifiers.
- Publish compact sequence identity/status/progress instead of reflecting and serializing the entire loaded tree repeatedly. Separate mostly static sequence structure from changing progress.
- Bound histories, pending messages, image caches and client queues. Define explicit overflow, disconnection, cancellation and shutdown behavior.
- A slow/disconnected browser must not block imaging or accumulate unlimited work. More browser viewers should be served by the external cache, not more N.I.N.A. collectors.
- Generate/cache small previews once per new image; keep full-resolution data out of routine telemetry.
- Surface stale or unavailable data honestly. A new heartbeat must not make old equipment values appear current.
- Instrument update rates, processing duration, queue lengths, dropped/coalesced messages, connected clients, memory and CPU.
- Preserve existing interfaces where practical. Decide whether a separate telemetry endpoint is preferable to altering upstream contracts.

These are design directions from the discussion, not a completed specification or authorization to deploy.

## Simulation and verification plan

N.I.N.A. can use simulated equipment. Use a separate Windows machine or VM, a dedicated N.I.N.A. simulation profile, and no production observatory connections or credentials.

- N.I.N.A. camera simulation/file-camera support can supply test image data.
- ASCOM Omni-Simulators provide virtual mounts, cameras, focusers, filter wheels, rotators, safety monitors and other device types through normal COM/Alpaca interfaces.
- PHD2 has its own simulation mode: its documentation describes a Simulator camera with an On-camera mount. Coordinate simulated guiding deliberately; do not assume independent simulators automatically describe the same virtual sky/mount.
- Generic noise images cannot validate plate solving. Use actual star-field fixtures and appropriate solver catalogs. A full centering-loop test needs images consistent with simulated pointing, not an unchanged image replayed after every slew.
- Autofocus realism, specific driver failures and permanently blocked operations may require custom test adapters/processes beyond stock simulators.

Suggested progression:

1. Establish an unmodified-plugin baseline with the same simulator setup and workload.
2. Run connect/slew/filter/expose/finish workflows and check snapshots and sequence-step transitions.
3. Test multiple clients, reconnect storms, slow readers, abrupt disconnects and service restarts. Acquisition must continue when monitoring disappears.
4. Use realistic image sizes and exposure cadence; tiny fixtures alone do not validate memory behavior for this rig.
5. Inject controlled timeouts and failures through test doubles or test processes. Verify cancellation and cleanup, not just successful requests.
6. Run an overnight soak test, recording resource trends and event delivery. Define pass criteria and acceptable resource budgets before declaring success; no numerical budgets were agreed yet.
7. Only later perform an explicitly authorized, controlled hardware validation. Simulation cannot establish ZWO USB/FPGA/SDK reliability or reproduce every real driver failure.

The native N.I.N.A. simulator camera and official ASCOM OmniSim v0.4.0 telescope now run in the isolated test environment. Basic connection and pushed telemetry work. Imaging/sequence behavior and real equipment remain unverified. The plugin uses Windows-targeted .NET/WPF dependencies.

## October 22 integration and instrumentation (current)

Kevin authorized continuing routine implementation and Parallels simulation tests without asking at each step, including a webpage, sockets/endpoints and CLI. He additionally requested memory/log instrumentation and observation over the next couple of hours. This does not authorize observatory access or real equipment actions.

Implemented plugin **0.2.0.0**, using the tested `TelemetryStateStore`: compact camera/mount/catalog/application/operation/diagnostic topics, one-slot client queues, authenticated `/api/v1/status`, `/diagnostics`, `/operations/{UUID}`, POST `/commands` and `/commands/validate`, and `/socket/v1`. Only camera/mount connect/disconnect are exposed as commands so far. Sequence/exposure conflicts are rejected. Request UUIDs deduplicate within 128 retained operations; acceptance and completion differ. Commands are never automatically retried.

An external C# gateway serves the TypeScript monitor on `http://127.0.0.1:1890`, caches one upstream socket, and separates browser sessions from N.I.N.A. The CLI is installed as `starfront-control` on PATH and works outside the repository. `starfront-control open` provides a one-use browser authentication ticket; `doctor --json`, `status --json`, `devices --json` and `watch` provide discovery/telemetry. Private credentials live only under `~/.config/starfront-control/` and the guest's local Starfront configuration, not in tracked files. New control endpoints reject direct browser Origins; browser access uses the authenticated local gateway. Legacy API is disabled in the test build.

VM: Windows 11 ARM in Parallels, UUID `8ce5a2a4-4ab0-4327-9057-b8764d964c4c`, currently `10.211.55.10`. N.I.N.A. 3.2.1.3002 uses the fresh **Starfront Control Simulation** profile; no observatory profile/credentials were copied. The plugin test listener binds the explicit guest address on port 1889, with Windows firewall access limited to the Mac VM-network address `10.211.55.2`. IPs must be rechecked after VM/network changes.

Exact simulation allowlist: native N.I.N.A. camera `4C0BBF74-0D95-41F6-AAD8-D6D58668CF2C`; discovered official local OmniSim telescope `5db8ce88-2b16-48b8-92bd-aa2ec5e59c43`. OmniSim v0.4.0 runs on the Mac VM-network address, TCP32323. Static-IP and all other catalog devices are disallowed. The profile's mount location synchronization is `NOSYNC` to avoid interactive prompts during isolated connection tests. This setting is not a production recommendation.

Verified: 31 portable tests; Windows cross-build 66 warnings/zero errors; gateway build zero warnings/errors; plugin loading/listener; CLI simulator connections; browser camera connect/disconnect/reconnect and mount connect; authentication/Origin/body limits; 100 socket reconnects returning retained clients to one. Source analysis found and corrected sequence-mediator startup ordering before collecting the observation. Detailed logs, checks and screenshots are in `artifacts/2026-10-22-integration/`. Existing dependency/security warnings remain and must be resolved/assessed before production packaging.

Memory/GC/CPU/handles/threads/queues/client counts and log sizes are sampled every ten seconds. Own plugin diagnostics have a 20 MiB rotating budget; gateway metrics and warnings have 20 MiB each. Host N.I.N.A. logs are measured and never deleted by the plugin. See [RESOURCE-OBSERVATION.md](docs/RESOURCE-OBSERVATION.md) for limits, evidence and interpretation.

The two-hour workload started **12:32:45 America/Chicago October 22**, planned finish **14:32:45**: five cached plugin GETs/second, two gateway GETs/second, four steady browser-equivalent sockets and a direct plugin reconnect every thirty seconds. Harness PID61412; `artifacts/2026-10-22-integration/observation-2h/report.json` is the current source of truth. Heartbeat automation `starfront-two-hour-resource-observation` checks every ten minutes, stays quiet on unchanged healthy state, and reports completion/failure/findings. The observation is not complete yet. Preserve running processes and evidence. No automatic restart, log deletion or equipment commands are part of the monitor.

### Source-only follow-up while observation runs

A subsequent source build normalizes UUID request IDs so casing/braces/compact forms cannot bypass duplicate protection. The CLI normalizes the ID before waiting and rejects invalid IDs locally; default `watch` output is consistently newline-delimited JSON. Core tests now pass **32/32**, with UUID-form deduplication coverage. `tests/integration/cli-smoke.ts` passes against a synthetic loopback peer (`cli-smoke.json`).

An isolated second-gateway test verifies ticket/cookie authentication, stale source timestamps, offline command rejection, upstream reconnect, client cleanup, graceful stop/restart and metric-log isolation (`service-lifecycle.json`). That exposed/fixed log paths ignoring `STARFRONT_SERVICE_CONFIG`; source now keeps custom configurations' logs alongside those configurations. These follow-ups are built and tested separately, **not installed** in the running main observation or PATH CLI. Do not mistake source timestamps for the observation's deployed build. `running-package.json` records the exact staged package hashes. See [ACCEPTANCE.md](docs/ACCEPTANCE.md) for the remaining goal checks.

The same source-only follow-up now excludes expected socket cancellation from the send-failure counter and recovers monitor controls if a new plugin session replaces a pending command. It explicitly shows that the previous result is unavailable; it does not retry or claim the command completed. TypeScript checking and the Windows cross-build passed. Playwright synthetic HTTP/WebSocket routes verified a pending button became usable after the session changed and the notice was visible (`browser-recovery.ts`, `monitor-recovery-synthetic.png` in integration artifacts). This test issued no real API command. The next web bundle is staged as `monitor-next.js`, leaving the running gateway's web assets unchanged.

An earlier follow-up was staged as **0.2.1.0** (plugin, staged gateway and CLI), separate from the running **0.2.0.0** observation. Plugin application telemetry and gateway doctor report their assembly version; CLI `--version --json` works without authentication. A build-only `scripts/package-plugin.sh` creates a five-file ZIP and refuses overwriting an existing package. The staged ZIP is `artifacts/2026-10-22-integration/StarfrontControl-0.2.1.0.zip` (379,138 bytes); exact SHA256/file hashes are in `next-package.json`. Packaging cross-build passed with 71 warnings/zero errors (including existing framework-reference and dependency warnings). Staged CLI smoke and gateway synthetic lifecycle tests both pass. The staged gateway folder includes the new browser bundle. None of these packages has been installed in the VM, running gateway, or PATH CLI yet.

A read-only saved-data analyzer is now available: `bun scripts/analyze-observation.ts OBSERVATION_DIRECTORY`. Six Bun tests pass for misleading allocation growth, process restarts, incomplete duration, sustained retention growth, stale/gapped samples and missing measurements. It reports process-specific ranges/deltas, sample/diagnostic timing and post-warm-up memory trends, and cannot label an ongoing run complete. Partial analysis is saved separately as `observation-partial-analysis.json`. This does not modify the harness or running services.

Additional bounded checks were performed on the running 0.2.0.0 build without restarting its processes: CLI mount disconnect/reconnect succeeded and both transitions arrived over a separate WebSocket (`mount-reconnect.json`). A12-attempt client-cap probe admitted7 additional viewers, rejected5, reached8 total retained clients, and returned to1 after cleanup (`client-cap.json`). The unread-client/small-TCP-window probe passed: after admitting its initial text frame, the plugin registry dropped from2 to1 by40seconds and stayed at1 at50seconds while the probe socket remained open (`slow-reader.json`), with zero publishing errors. These checks add brief operator activity to the observation; `operator-annotations.json` records them. The harness itself still issues no commands, and no exposures/slews/sequences were introduced. Do not describe the observation as an exclusively idle telemetry workload.

Latest staged bundle is now **0.2.2.0**, superseding the earlier0.2.1.0 staging. `next-package.json` identifies the current 379,126-byte plugin ZIP, hashes, CLI and gateway directories; nothing has been installed in the main environment. A separate two-minute same-workload GC comparison found240 successful requests per mode and identical 2 ms p95 latency; workstation GC collected once and ended at 4,714,424 estimated live bytes, while server GC had not collected and ended at 10,315,928 (`gc-comparison.json`). This supports selecting workstation GC for the modest local gateway; it is not proof of a leak or a production performance claim. The staged gateway uses that setting and records actual mode/gen counts/heap commitment/fragmentation. N.I.N.A.'s GC configuration is untouched. Main observation still uses0.2.0.0/server GC. See RESOURCE-OBSERVATION.md for interpretation and Microsoft's configuration reference.

Partial observation checkpoint at2,670seconds/44.5minutes: zero request/socket failures, same process identities. N.I.N.A. private-memory low-water decreased93.63MiB between post-warm-up windows; managed estimate low-water increased1.42MiB. Original gateway garbage collection occurred and its managed low-water decreased62.63MiB. No threshold triggered. Exact first-window snapshots are preserved in `observation-first-window-analysis.json` and `observation-first-window-report.json`. These are partial, not the completed two-hour result.

One-hour partial checkpoint now recorded:3,600seconds/121samples,25,092 GETs,120 reconnects, zero failures and unchanged process identities. N.I.N.A. private bytes376,180,736 (down91,676,672 from the first sample); managed estimate53,192,416 with+1.03MiB post-warm-up low-water difference. N.I.N.A. logs grew2,502bytes, plugin diagnostics296,783bytes, gateway metrics138,127bytes. Gateway managed low-water difference−7.18MiB after collections; no threshold triggered. Exact snapshots are `observation-one-hour-analysis.json` and `observation-one-hour-report.json`. Still partial: do not complete the goal before the second hour and remaining Windows listener lifecycle verification.

## Where to resume

1. Read current observation report, inspect exact process identities and artifact timestamps; do not restart a healthy run or repeat installation. Read docs/RESOURCE-OBSERVATION.md and docs/DEVELOPMENT.md.
2. On completion, evaluate post-warm-up low-water memory trends, GC, resource counts and log growth. Record measured results and limitations, then pause the completed heartbeat. A successful build or short smoke run is not a completed two-hour result.
3. Still pending: matched unchanged-plugin baseline, full acquisition/image/sequence tests, lifecycle toggles and blocked/slow-client failure injection in the actual host, further legacy image/history retention decisions, dependency warning assessment and production packaging. The existing overnight plan is deferred, not authorized to run now.
4. Extend control coverage incrementally (e.g. temperature) only after basic operation/lifecycle handling is verified. Do not imply complete GUI-equivalent coverage or remote observatory readiness.
5. Leave the observatory unchanged. Deployment, Advanced API removal, production polling, real hardware connection and live sequences require a separately authorized operational step.

## References and local evidence

- Upstream plugin: https://github.com/christian-photo/ninaAPI
- Inspected release: https://github.com/christian-photo/ninaAPI/tree/2.2.15.2
- WebSocket documentation: https://github.com/christian-photo/ninaAPI/wiki/Websocket-V2
- N.I.N.A. architecture: https://github.com/isbeorn/nina/blob/develop/ARCHITECTURE.md
- ASCOM simulators: https://ascom-standards.org/help/html/905bf41a-9a13-47c1-a9dd-4a354a8c4432.htm
- PHD2 simulation: https://openphdguiding.org/man/Basic_use.htm
- Starfront Operations workspace: `/Users/kevinmcquown/Documents-Local/Personal/astrophotography/Starfront Operations`
- Existing collector: `ops/windows/Watch-StarfrontLiveObservatory.ps1` under that workspace.
- October 22 upgrade/diagnostics evidence: `.codex-artifacts/2026-10-22-nina-hotfix/completion.json` and `README.md` under that workspace.
- October 21–22 actuals and incident evidence: `.codex-artifacts/2026-10-22-morning-results/` under that workspace.
- Source inspection checkout, pinned to the inspected release: `.codex-artifacts/2026-10-22-advanced-api-source/` under that workspace. The separate project checkout here intentionally remains on the fork's dev branch at initial handoff.
- Production website: https://starfront-observatory-night-ledger.iitgrad.chatgpt.site/live/

Keep this document current as work progresses, separating proposals, implementation, simulation results and actual deployment evidence.

## User-ended observation, October 22

Kevin ended the waiting phase after 5,581 seconds (93 minutes), before the planned two hours. The harness stopped gracefully with status `interrupted`; this is an intentional early stop, not a completed two-hour result. Across 187 samples: 38,767 requests, 23,939 socket frames and 186 reconnects; zero request/socket failures and no analysis investigation triggers. N.I.N.A. PID 5552 and gateway PID 60508 remained unchanged throughout.

N.I.N.A. private bytes fell from 467,857,408 to 386,207,744; post-warm-up managed low-water increased 1.60 MiB and private low-water fell 78.53 MiB. Gateway managed low-water fell 6.26 MiB, despite normal allocation/collection cycles. Plugin metric logs grew 460,460 bytes, gateway metric logs 215,201 bytes and N.I.N.A. logs 3,441 bytes. This supports proceeding with development; it does not establish acquisition/driver reliability or absence of every leak. The running observation package was 0.2.0.0.

Evidence: `artifacts/2026-10-22-integration/observation-user-ended-analysis.json`, `observation-user-ended.json`, original `observation-2h/report.json`, samples and operator annotations. The observation heartbeat is paused. Subsequent 0.2.2.0 simulation installation is separate from this resource result.

## Verified simulation follow-up: 0.2.2.0

After the user-ended observation, the staged 0.2.2.0 plugin was installed in the isolated Parallels VM. All five installed file sizes and SHA256 hashes matched the staged package. N.I.N.A. plugin UI showed 0.2.2.0; gateway doctor and installed CLI also report 0.2.2.0. Gateway now uses workstation GC; N.I.N.A.'s GC mode was not changed. The gateway runs from `artifacts/2026-10-22-integration/next-service-0.2.2`, with its matching browser assets. Private configuration and exact simulator allowlist were preserved.

Three runtime API Enabled off/on cycles passed with active clients and unchanged N.I.N.A. PID8848: each off persisted false, removed the port1889 listener and refused connections; each on persisted true, opened one listener, created a new telemetry session and automatically recovered pushed status. Evidence: `listener-lifecycle-0.2.2.json`. The UI driver needed foreground focus and DPI-aware coordinates; early driver attempts are not plugin failures.

Camera disconnect/reconnect succeeded; equivalent brace/uppercase request UUIDs deduplicated to the same operation (`commands-0.2.2.json`). Mount disconnect/reconnect succeeded with both transitions independently observed on a WebSocket (`mount-reconnect-0.2.2.json`). Authentication, Origin rejection, request-size limits and 100 reconnects passed again on the installed build, releasing both plugin client registries to one with zero publish errors (`live-checks-0.2.2.json`).

The actual updated browser recovered from offline state, displayed fresh connected simulator telemetry and operation results, and showed process diagnostics (`monitor-0.2.2.png`, final status/doctor JSON). Final source/component/synthetic evidence remains 32 core tests, six observation-analysis tests, CLI smoke and gateway lifecycle tests. No source changes were made in this follow-up beyond documentation; test helpers and evidence are local artifacts.

This completes the initial isolated plugin/gateway/WebSocket/monitor/CLI acceptance scope. The 93-minute resource measurement belongs to 0.2.0.0; the updated build has functional and lifecycle verification, not an equivalent long observation. Full exposures, advanced sequences, matched upstream performance and inherited dependency warnings remain deferred. No observatory work, real equipment, commit or push occurred.

Final diagnostics on 0.2.2 recorded `sendFailures=1`, `publishErrors=0`, `diagnosticErrors=0`, one hub/transport client and zero pending topics after lifecycle/reconnect tests. A send failure can occur when a peer closes during a send; its exact cause was not traced in this run. Do not report all transport counters as zero or claim this functional check proves leak freedom.

## October 22 image acquisition and simple sequence verification

Kevin explicitly authorized simulator image capture and a simple sequence without step-by-step confirmation. On plugin/gateway/CLI0.2.2.0 in the isolated Parallels profile, the N.I.N.A. native camera and exact local OmniSim telescope were reverified and connected. The test profile was backed up; image output was set to `C:\StarfrontControlTest\Imaging-2026-10-22`, with automatic mount/dome/cooling actions disabled. No production profile or observatory was accessed.

N.I.N.A.'s Legacy/Simple Sequencer ran `Starfront-Simulator-Smoke`: ten LIGHT exposures, two seconds each, bin1x1, with no slew/centering/guiding/dithering/autofocus. It completed10/10 at14:27:59 America/Chicago; the log independently records ten successful saves and `Simple Sequence finished`. Ten FITS files were copied into local ignored evidence and validated:640×480,16bit,EXPTIME2.0, correct native simulator identity, complete FITS headers/block alignment/pixel payload and nonconstant pixels. Each file is622,080bytes, total6,220,800bytes. SHA256 values are recorded per image.

Pushed telemetry showed sequenceRunning false→true→false and exposure transitions, with the same session/connected bridge throughout. A dry-run camera-disconnect validation during capture returned409 `A sequence or exposure is active.` No actual disconnect was submitted during acquisition. Final state is idle, simulators connected. Source capture/sequence commands have not been added; this acquisition was operated through the N.I.N.A. UI while monitoring Starfront Control.

Across the short observation, plugin queues stayed at0, retained hub/transport clients stayed at1, and send/publish/diagnostic errors stayed at0. N.I.N.A. private memory fluctuated during layout/acquisition and ended below the initial sample; full ranges and exact sample interval are in `artifacts/2026-10-22-imaging/result.json`. This small640×480 run is functional evidence, not a long imaging leak test or validation of real camera drivers. Simulator `cameraState` remained `NoState`; `isExposing` and sequence state were observed correctly. The pre-existing N.I.N.A. EarthRotationParameterUpdater HTTP400 occurred at startup before acquisition; no acquisition error appeared.

Evidence: `artifacts/2026-10-22-imaging/` includes configured/completed screenshots, captured telemetry/diagnostic samples, `fits-validation.json`, `result.json`, raw images and the N.I.N.A. log snapshot. Temporary transfer/observation processes were stopped; N.I.N.A., simulator and monitor remain available. No plugin source change, build, observatory deployment, commit or push was needed. Full advanced-sequence/acquisition-soak tests remain future work.

## October 22 dashboard and sequence expansion (current: 0.3.3.0)

Kevin asked to work toward the functional dashboard in his14:31:58 screenshot, with visual polish deferred, and specifically prioritized a full right-side sequence panel showing checked-off, executing and waiting elements. The current increment is visual telemetry and sequence visibility; full GUI-equivalent control remains incremental. See `docs/DASHBOARD-COVERAGE.md` for an explicit implemented/verified/pending matrix and next control increments.

Implemented and installed in the isolated Parallels environment: plugin, gateway and CLI0.3.3.0. The Mac monitor now has latest-image preview (fit/actual pixels), saved-image statistics/history, weather/safety/rotator/filter-wheel/focuser/mount/camera readings, autofocus and guide plots, and the right-side sequence hierarchy. Absent/disconnected devices remain explicit. New consumers and handlers have matching disposal. Image history retains32 entries, autofocus64 points and raw guider history120 points. Preview work has one pending immutable bitmap and <=40KiB encoded JPEG, replacing the previous preview. Pending telemetry topics are bounded at24; diagnostics now publish this capacity, and the saved-data analyzer respects it while retaining the original8-slot default for older evidence.

Sequence projection copies containers, instructions, conditions and triggers into flat scalar DTOs with parent IDs and weak per-host-object identities. N.I.N.A. status drives completed/running/pending/failed/skipped/disabled; a running known Wait instruction is displayed as waiting while its original RUNNING status remains available. Only FINISHED receives a completion check. Parent containers are distinguished from active instructions. Loop/exposure counters and configured scalar parameters are included when exposed. The adapter coalesces relevant property/collection notifications; one ten-second host discovery refresh catches startup/root replacement, independent of browser count. It projects at most256 nodes/depth16/58KiB of node JSON and explicitly flags partial trees. N.I.N.A.3.2's public mediator does not expose the root, so one isolated access to its navigation view model is version-sensitive; failures report unavailable rather than false completion.

Actual advanced-sequence verification on0.3.3.0: the saved isolated `C:\StarfrontControlTest\Dashboard-Test.json` ran a3-second native-simulator exposure followed by15-second Wait for Time Span. Pushed states showed the exposure running, exposure completed alongside wait waiting, then all nodes completed. Mac browser decoded the actual640×480 preview and showed saved FITS statistics. `starfront-control sequence --json` reads the same cached tree with source timestamp and bridge state. No new capture/sequence control commands were added; N.I.N.A. UI initiated this test.

Evidence: `artifacts/2026-10-22-dashboard/` includes release build/package logs and manifest, `run-evidence-0.3.3.json`, final doctor/status/sequence JSON and browser screenshots.41 core tests and7 analysis tests pass, plus strict TypeScript, CLI smoke and isolated service lifecycle tests. The plugin cross-build retains71 inherited warnings and zero errors; gateway/CLI builds have zero warnings/errors. Actual authentication/Origin/body-limit checks and100 reconnects passed on0.3.2.0; three listener off/on cycles on that build produced zero/one listeners and automatic session recovery without changing the N.I.N.A. process.0.3.3 adds queue-capacity reporting/constant consolidation; its advanced capture/wait run and browser checks passed again. Latest diagnostics showed0 pending topics,1 hub/transport client,34 sequence subscriptions and zero send/publish/diagnostic errors. This is functional evidence, not a new long stability run.

Browser QA used Playwright CLI because no in-app browser-control tool is available. Verified1584,1280 and390pixel widths without page overflow, fit/actual image sizing, waiting/completed states, full snapshot recovery on reload and clearing an old preview after a new plugin session. The supplied N.I.N.A. screenshot is a functional reference; a new visual concept/pixel-matched redesign was intentionally not pursued per Kevin's request. Screenshot evidence records the actual simulator data, including its nearly uniform noise image.

Next: explicit capture/settings and sequence start/stop commands using the same POST/operation/CLI interface, then other device controls and active autofocus/PHD2 fixtures. Live weather/safety/rotator/filter-wheel/focuser values and active autofocus/guiding have source hooks and panels but have not yet been validated with active simulators. Histogram, richer image tools, PHD2 guide-star detail and a3D telescope view remain pending. Observatory deployment, real equipment actions and production profile changes remain unauthorized. Work is local/uncommitted; no push occurred.

## October 22: direct local dashboard opening

Kevin asked to remove the Unlock monitor screen. Gateway0.3.4.0 now creates a browser session automatically using a same-origin POST from the loopback dashboard; the form/key field is removed. Exact Origin/Host and loopback peer checks protect this bootstrap; the session remains HttpOnly/SameSiteStrict and API/WebSocket authorization is retained. CLI ticket exchange still works, but ordinary page opening needs no CLI action or key. The N.I.N.A. plugin/CLI remain0.3.3.0; no plugin or equipment change was made.

Verified strict TypeScript, gateway publish and synthetic service lifecycle checks including successful automatic session, denied missing/foreign Origin and denied wrong Host. A fresh browser with cookies explicitly cleared opened the actual live dashboard, successfully read authenticated status and passed reload without any unlock controls. Evidence: `artifacts/2026-10-22-direct-open/`. Running gateway content root is `artifacts/2026-10-22-direct-open/service`; private configuration unchanged.

## October 22: equipment timestamp cadence and live clock

Kevin reported the Last observation display counting two seconds then pausing. A20-second paired socket probe observed24 frames at both plugin and gateway, maximum frame gaps1,480ms/1,476ms. Camera source interval median2.002s and mount1.995s, offset from one another. The page displayed the newest source observation timestamp, so the burst/pause pattern originated in host updates, not a delayed gateway clock. No device polling interval was increased.

The overview now separates Local time (a lightweight250ms browser timer updating clock/age text only) from Equipment update (actual source timestamp plus age). Ages over10s are marked stale; offline and device-clock mismatch are explicit. Source timestamps never advance without actual telemetry. Unchanged text writes are skipped and the time formatter is reused. Current source HTML/JS were checked with strict TypeScript, bundled and copied into the running gateway0.3.4.0 content root without restarting N.I.N.A. or the gateway.

A controlled12-second paused-feed browser test confirmed that the clock advances independently, observation time remains fixed, stale age appears and no extra network requests occur. Evidence: `artifacts/2026-10-22-observation-clock/summary.json`, raw timing records, browser test and screenshot. This is display clarity/freshness behavior; source telemetry retains its actual host cadence.

## Current request: complete control expansion

Kevin now authorizes adding the missing API capabilities discussed in the route comparison and testing them with simulated devices in Parallels first. Work is in progress; docs/CONTROL-EXPANSION.md tracks required groups and verification. No observatory access or real equipment actions are authorized.

## Control expansion in progress, October 22

Kevin explicitly authorized adding the previously discussed missing controls and testing in the Parallels simulated environment first. Broad implementation is in progress; do not deploy to the observatory. New command catalog has parameter validation, UUID/body identity, integer checks and bounded interrupt slots. Adapter owns one acquisition/control operation globally; matching abort/stop can cancel it. New controls are reachable through authenticated POSTs, a generic browser form and CLI DEVICE ACTION --params JSON. Legacy GET controls remain disabled.

0.4.1.0 has been installed/tested locally: native simulator captured/saved two1second images; abort cancelled a30second capture, conflicting disconnect/mount requests returned409. All six additional exact OmniSim endpoints connected: focuser, filter wheel, rotator, dome, cover/calibrator and switch. Focuser move, filter select, mechanical rotation, dome open/close/slew/home/park, panel brightness/light/cover and switch set succeeded. Sky rotation initially failed because N.I.N.A. requires an explicit rotator sync; source now has sync and preflight. Tracking-off returns the resulting state rather than a success flag; source corrected accordingly. A saved-image download returned622080bytes; screenshot/administration refresh/log tail requests succeeded. Some concurrent test harness requests were rejected by the global ownership check; do not treat those harness conflicts as device failures or claim these were complete passing sweeps.

0.4.3.0 staging now adds saved-event completion, rotator sync, explicit PHD2 simulator validation and other corrections. PHD2 2.6.14 was installed in the isolated VM from its official installer. A fresh Starfront Simulation profile ID2 uses PHD2's built-in Simulator camera and On Camera mount, no auxiliary mount/AO/rotator. Loopback RPC readbacks verified these exact selections before allowlisting PHD2. The new adapter checks the local server address/port, allowed profile ID, N.I.N.A. profile selection and actual PHD2 equipment before guiding commands and sequence start. PHD2 alone in a device-ID allowlist would be insufficient. Actual guiding tests are still pending; so are sequence control, autofocus, cooling-positive, plate-solving and flat calibration verification. See artifacts/2026-10-22-control-expansion for evolving evidence. Native simulator does not support cooling; rejection was verified.

Temporary guest helper server19000 is active for the authorized VM installation. Gateway was briefly0.4.0.0 then0.4.1.0; inspect its current process before any restart. Do not infer the source version is installed: staging/install/results must agree. Package0.4.2 was staged but not installed. Some Parallels guest tool invocations report invalid-argument errors; inspect resulting state before retrying any action. UI tests use DPI-aware helpers; PHD2's wizard buttons required native BM_CLICK/PostMessage (ordinary rapid mouse clicks did not advance pages reliably).

Later expansion checkpoint: installed0.4.6.0 corrected guider-start success by observing PHD2's actual `SettleDone` event and checking its app state; corrected already-stopped handling. The first0.4.5 run had actual guide samples but a settling-timeout event despite N.I.N.A. returning true; it must not be counted as successful settling. The dedicated simulation profile's guider settle time was subsequently corrected to1second (the initial broad XML selector had changed another section's similarly named field). On0.4.6, force-calibration/start completed after~109seconds with independently observed settling status0 and app state Guiding. Stop and calibration clear then succeeded. Exact PHD2 N.I.N.A. driver ID is `PHD2_Single`; the stale profile's `PHD2` entry was an OfflineDevice and is now rejected in catalogs/validation. PHD2's profile/equipment RPC proof remains required. An additional already-stopped request on the older code failed, motivating the state-based fix.

Native simulator Source Image was loaded through its setup dialog using a public STScI DSS M31 FITS fixture (3564x3571,~24MiB), not real observatory data. Plugin0.4.6 captured/saved it through the API; load/reset/start of Dashboard-Test.json then succeeded as an advanced3second capture plus15second wait. Capture-with-save now waits for the matching ImageSaved metadata event before terminal success. A short resource observation spans this increase in image size: managed estimate fell, while process private memory increased with the larger retained simulator/rendering buffers; do not interpret that as a matched-workload leak result. Counters remained bounded and error-free. The observer samples carry process IDs; split by PID across staging restarts.

ASTAP and its D05 database have been installed in the isolated VM from official upstream downloads. Plate-solving positive verification remains pending.0.4.7 is being installed with solve hints, target creation/edit, scalar RMS units, weather/safety connections, bounded prioritized device catalog and an operation feed that omits potentially large parameter bodies. Full command details remain available by requestId. Staging0.4.7 also has32 pending topic slots,8KiB log tails and16KiB screenshots; all remain bounded. Source tests:49 core tests,7 analyzer tests, strict TS, CLI smoke, gateway lifecycle and plugin compilation pass; inherited plugin warnings remain71. Actual testing is still in progress; do not claim the whole checklist complete.

Expanded verification checkpoint (0.4.7–0.4.10): native/OmniSim connection and common device sweeps, sequence editing/interruption and plate solving have run. Profile switching between the original simulation profile and a new isolated clone `dc33f3b5-e791-4e03-8052-09029ac0ee75` succeeded and returned to the original profile. The two profiles are the only profile-switch allowlist entries. Their startup chooser required selecting/loading the simulation profile and saving that choice; no production profile was present.

The local OmniSim0.4.0 setup initially served404 for `_framework/blazor.server.js` while rolled forward onto.NET10; apparent checkbox changes were browser DOM only and actual driver capability remained false. Installing supported ASP.NET Core/.NET8.0.32 and restarting OmniSim under8 restored the script (HTTP200) and actual Settings Saved round trips. All exact device GUIDs were reverified unchanged. The runtime installer replaced the Mac dotnet launcher; macOS initially SIGKILLed its cached executable until it was rewritten at a fresh inode with its valid upstream signature. Both8.0.32 and10.0.10 runtime listings were then verified. A previous gateway process ended during this staging change; its subsequent replacement runs.NET8. Treat this as a tooling/staging interruption, not a plugin leak result. No other product deployment occurred.

With actual OmniSim temperature capability enabled, cooling to5C succeeded. Its default10C ambient ceiling rejected N.I.N.A.'s fixed20C warm-up setpoint, leaving N.I.N.A.'s native warm routine waiting; the operation was explicitly aborted successfully. The dedicated simulator camera profile ambient was changed to25C, after disconnecting, with a backup and simulator restart. Warming then succeeded, including native cooler-off completion. No N.I.N.A. safety guard was disabled. Simulated safety input was set true in its own simulator profile; dome open/close then succeeded after the earlier unsafe-input rejection. The ASCOM XML simulator profiles are under `~/Library/Application Support/ascom/alpaca/ascom-alpaca-simulator/`; edits/backups were limited to the dedicated camera and safety fixtures.

Autofocus on0.4.9 generated two actual HFR measurements and was cancelled through focuser stop; its original operation became cancelled and the focuser returned idle. The public DSS image is a static star-field fixture with no focus-dependent blur, so this does not validate optical autofocus convergence. This run found that N.I.N.A. did not emit its successful-end callback on cancellation;0.4.10 now explicitly publishes autofocus outcome and running=false in the command finally block. Retest pending. The first mount flip request was a native no-op (already on the requested side); do not claim an actual flip from its succeeded return.0.4.10 rejects no-op flip requests and checks for a pier-side change. A real simulated side-change test remains pending. Dome following correctly failed with the original zero dome radius; a5000mm radius is now configured in the isolated profile for its positive follow test.

Latest staged0.4.10 retains the same memory bounds:32 pending topics,128 operation records/32 compact pushed summaries, bounded priority device catalog128 choices/58KiB, no parameter-body duplication in the operation feed,32 saved-image references, streamed download limit128MiB,8KiB log tail and16KiB screenshots. Target creation caps at32, sequence projection256/depth16. Full command result parameters are fetched once by CLI after terminal socket notification. New plugin-wide Windows platform metadata removes spurious platform warnings; current build warnings are45 inherited dependency/legacy-code warnings, zero errors. Source/component checks still49 core tests plus7 analysis tests, strict TS, CLI smoke and gateway lifecycle. Continue final actual simulation checks and package/hash documentation before calling the whole increment complete.


## October 22 completed control expansion — current 0.4.10.0

This checkpoint supersedes pending-test statements above. Plugin, gateway and CLI 0.4.10.0 are installed/running in the isolated Parallels simulation. See docs/CONTROL-EXPANSION.md for final capability and verification matrix; all historical failures remain in evidence. Actual final-build checks passed: mount East→West flip, safe dome opening/closing and follow on/off (simulation dome radius5000mm), autofocus two HFR points followed by cancellation with running=false, automatic flat exposure1.05seconds/mean32767.66, and browser-submitted two-image capture. Positive simulator cooling5C/warming20C with cooler off and isolated alternate-profile switch back to main also passed. Static DSS has no focus-dependent blur, so autofocus convergence remains unverified.

Final source checks:49 core tests,7 analysis tests, strict TypeScript, CLI smoke, gateway lifecycle; plugin45 inherited warnings/zero errors, service and CLI zero warnings/errors. All five packaged file hashes/sizes match the installed guest files. ZIP artifacts/2026-10-22-control-expansion/StarfrontControl-0.4.10.0.zip is420087bytes, SHA2566e130bde05747ae7710f790b9bae4b18f1ef2c1f59d7ece33a891163d79780b0. No tokens/profiles included.

Final19-sample three-minute WS diagnostic observation onNINA PID10212: private568655872→494882816bytes, managed live estimate152973504→79344952; pending topics0, one retained transport/client, send/publish/diagnostic errors0. NINA logs+6423bytes, own logs+15869bytes, own rotation budget20MiB. This is functional short-run evidence, not proof against long-duration leaks. Raw resource samples and summary are in the expansion evidence folder.

Gateway runs the published service-0.4.10 content root on127.0.0.1:1890. OmniSim runs supported.NET8.0.32 on10.211.55.2:32323. Main simulation profile GUIDcd432469-2b6e-442c-987e-78504975ff3e is active; only it and alternate GUIDdc33f3b5-e791-4e03-8052-09029ac0ee75 are profile-switch allowed. Explicit simulator IDs and PHD2 built-in simulator/profile checks remain enforced. Legacy GET controls disabled. No observatory/real-equipment changes, commit or push. Further real-equipment actions require separate authorization.

Final cleanup: restored the native simulator to the public DSS M31 IMAGE source, captured/saved, solved on0.4.10 using1150mm hint, then loaded and ran Dashboard-Test.json to successful completion. Final application sequenceRunning=false, camera isExposing=false and activeCommands=0. Final browser desktop check has no page overflow; screenshot and final-result.json are in the expansion evidence folder. Temporary helper HTTP server19000 stopped; Mac gateway, NINA and local device simulator remain running.

## October22 authorized NUC installation — in progress

Kevin explicitly requested installation on the observatory NUC, then chose to stop tonight's running production sequence and install now. He explicitly said no backup, just install and restart. NUC SFO-SCOPE/astroadmin100.89.247.90 has NINA3.2.1.3002, matching simulator host. OriginalPID3348 ran tonight's sequence at Wait until safe; legacy sequence/stop was source-reviewed and explicitly invoked after authorization. Follow-up showed no RUNNING nodes and camera disconnected/not exposing. NINA closed gracefully in interactive session2. No backup was made per Kevin. Package copied viaSSH and ZIP/SHA/files verified in AstroTools/StarfrontControl-Staged-0.4.10.0; five binaries installed under LocalAppData/NINA/Plugins/3.0.0/Starfront Control. New config enabled, bound only100.89.247.90:1889, legacyfalse, simulationOnlytrue and emptyallowlists deliberately block real equipment commands during first telemetry deployment. Scoped Windows firewall rule permits TCP1889 onthatlocaltailnetaddress from100.64.0.0/10. ExistingAdvancedAPI/plugin1888preservedbecause liveobservercompatibility exists. NativeNINA restarted interactivelyPID13284; plugin-load/listenerverificationstillinprogress.

Kevin also asked to configure the iOS app. Referencedchat01a16577-5e7b-79b1-9e63-0f7db48ad9a9 was read. Appis Starfront Operations/apps/StarfrontIOS; existingKeychainpairingusesMacloopbackgateway1890, gatewaymode. RelaunchoniPhone18ProsimulatorverifiedConnectedwithParallelsdata. Planis switchgatewayupstreamtoNUCwithoutchangingitslocalauthsoiOSsavedpairingandMacdashboardfollownewhost; actualNUCtelemetrystillpending. Physicalphonepairingnotyetverified.

## October22 NUC deployment verified; phone final pairing pending

Plugin0.4.10.0 successfully loaded on NUC13284 and listens only on100.89.247.90:1889; tailnet-scoped firewallrule installed, bearer retained privately. Source-compatible sameNINA3.2.1.3002. No backup per Kevin. ExistingAdvancedAPI was retained. Macgateway restarted with NUC upstream; CLI/macbrowser and iPhone-simulator existinggatewaypairing receive actualNUCtelemetry. Source consumers reportASI6200MMProandrealfiltervalues, with zero publish/send/diagnosticerrors. Diagnosticenvironmentstringstillreports simulation because SimulationOnly=true is the command guard; it does not mean attached real telemetry is simulated. No deviceconnect/actionorstartsequencecommandwasissued. NINAhost reports tonight's productionsequenceactiveagainafterrestart, waiting atWaitUntilSafe; independentNINAlogalsohadFindhomefailure. Do not infer who restarted it or silently remediate realhardware.

Startup slowdown coincidedwith severe memorypressure: olderPowerShell inspectionPIDs13968/15424 heldabout11GB, under400MBfree. CIMdecodedcommand classificationshowedread-onlyGetChildItem/GetContent/GetProcessinspections, notuploaderorhardwarecontrol. Theywereexplicitlycancelled; systemfreerecoveredabove11GBandNINAfinishedloading. ThisisdistinctfromStarfrontpluginmemory. No log files removed. Newtemporaryinteractiveinstallation/inspectiontasks were unregistered; user-facingNINA remainsrunning.

Physicalphonecommunicator00008130-001A4D622461401C,iOS27.0.1, ispaired/DeveloperModeenabled. Starfront Debugbuild signedwithWindyCityLabteam4K7853APASinstalled. iOSprojectnowsetsstandardDEBUGSwiftcompilationcondition; DEBUG-onlyphysicalprovisioningacceptsexplicitinheritedSTARFRONT_DEVICE_PAIRINGonlyfordirectplugintailnetIP100.64/10, savesthroughdevice-onlyKeychain. ItdoesnotbundlecredentialsorpermitReleasebootstrap. ThelatestprovisioninglaunchfailedwithiOSLocked; userwasasked tounlockandkeepawake, finalpairingverificationpending. EarlierstatementthatphonepairingwassavedwasprematurebecauseDEBUGconditionwasmissing; codecorrected/rebuilt/reinstalledbutmustperformunlockedlaunch. SimulatornormalrelaunchverifiedexistingKeychaingatewaypairingandnowshowsNUCdata. Evidenceartifacts/2026-10-22-nuc-install; physicalbuildlogs /tmp/starfront-ios-device-build.log.

Final current checkpoint: NINAisup; actualcameraZWOASI6200MMPro/filterLobservedviaNUCplugin,MacgatewayandnativeiPhoneSimulator. PhysicalStarfrontappisinstalledbutthelatestDebugbuild'spairinglaunchwasdeniedtwicebyiOSLocked; nofinalphoneconnectionclaimisvalid. Userhaspendingunlockquestion. Commandhelper /tmp/starfront-pair-phone.pyretrievesNUCtokenprivatelyoverSSH,passesDEVICECTL_CHILD_STARFRONT_DEVICE_PAIRINGinheritedenv,neverprintscredential; onceunlockedrunthenverify1889hasphysicalphoneclientandnormallaunchwithoutenvreconnects. Freephysicalmemoryrecoveredto11615972KiBaftercancelledolderread-onlyinspections. NoBackupscreated.NoStartSequenceorhardwarecommandsissued.

## October22 ~19:59CDT physical iPhone pairing complete

After Kevin replied unlocked, provisioninglaunchsucceeded. An initial launch transientlyreportedprocessIDunavailable; normalappstartthenprovisioningretryworked. ActualNUC1889socketfromphone100.124.150.76wasobservedalongsideMacgateway100.108.112.76. Normaldevicectllaunchwithterminate-existingandnoenvironmentprovisioningthenrestoredKeychainpairing; phoneconnectionreestablishedandpluginretainedclients=2. Endpointhttp://100.89.247.90:1889,directPlugin=true,autoConnect=true. Phonebuildsupportsmonitoringonly;realhardwarecommandguardunchanged. NoNINArestart,sequencecommandsorhardwareactionsinthisfollow-up. Evidencephone-provisioned-launch.json,phone-saved-pairing-relaunch.json,phone-connected-status.jsonanddeployment-result.jsonintheNUCinstallartifactfolder. Onecumulativesendfailureafterabrupttestrelaunch;publish/diagnosticerrors0. Earlierphone-pendingnotesarehistorical.

## October22 iOS controls increment — simulator verification complete

Kevin authorized adding camera/mount connect/disconnect and loaded-sequence start/stop to the iOS app, testing with simulated equipment first, then installing on his phone. Source lives in Starfront Operations/apps/StarfrontIOS; ControlCommands.swift/ControlViews.swift added, monitoring/rig/sequence integration updated. No plugin changes. NativeSimulatorUI tests passed allfourconnectionactions, confirmed sequenceStart→Wait→Stop (startCancelled/stopSucceeded), then Start→normalCompleted with3sexposure/8swait. Testsetupreset/editedonlyisolatedsimulation. Command POST operations use exactselectedIDs, confirmations, session/targetrecheck, boundedhistory/responses, eventdrivencompletion, uncertainresultsoneexplicitGETandnoautomaticretry. Automatedcontracts/lifecyclechecks pass. SourcebuildsforiOSsimulatorandphysicalphone.

Separate simulatorgateway runs on127.0.0.1:1891 using private ~/.config/starfront-control/ios-simulator-service.json, bridge10.211.55.10:1889/exactsimallowlist. iPhoneSimulatorpairedtogateway1891. MainMacgateway1890remainsNUC. NUCguardunchanged;actualdevicecommandsstillblocked;noNUCcommandorhostrestartinthisincrement. PhoneStopisforStarfront-ownedsequences;GUI-ownedsequencesareexplicitlyunavailable. Physicalphoneupdatedinstallationcurrentlyinprogress;inspect /tmp/starfront-ios-controls-install.txt andlaunchresultbeforeclaimingdeployed. Evidence artifacts/2026-10-22-ios-controls.

FinaliOScontrolsdelivery: updatedsignedDebugappsuccessfullyinstalledonphysicalcommunicator(bundlecom.kevinmcquown.starfront.ios,newcontainerD063F931-C6C5-4382-B57B-18B4C4C33C25). Devicewaslockedagain;automaticlaunchdeniedbyiOSLocked.Installationitselfcompleted;normaluseropenretainspriorNUCKeychainprofile.Noadditionalpairingrequired. SevenactualsimulatorUIoperationsrecorded:fourcamera/mountconnectdisconnectsuccesses,sequenceStartcancelledbystop,stopSucceeded,secondstartSucceeded/fulltreeCompleted. Unitcontracts/lifecyclechecksallpassed,simulatoranddevicebuildsucceeded.Sourceworkremainsuncommitted.NUCguardandNINAprocessunchanged. Separate1891simulatorgatewayleftavailableforexistingiPhoneSimulatorpairing. Evidenceandbinaryhashartifacts/2026-10-22-ios-controls/result.json.

## October23 ~06:44CDT physical phone update/connection reverified

Kevin requested loading the app and connecting to the Starfront plugin. Phonewasunlocked;latestpreviouslytestedcontrolsenabledsignedappwasreinstalledandforegroundlaunchedsuccessfully. DirectNUCpairinghttp://100.89.247.90:1889savedviaexistingprivateprovisioninghelper. EstablishedNUCsocketfromphone100.124.150.76verifiedalongsideMacgateway100.108.112.76;NINAPID13284,plugin0.4.10.0,two retainedclients. NoequipmentcommandsorNUCconfigchanges. Evidenceartifacts/2026-10-23-phone-connect.

## October 23 sequence selection labels (prepared, not deployed)

Kevin requested equipment/filter selections inline in companion sequence rows. Plugin 0.4.11.0 now projects bounded `details.equipment` from ConnectEquipment/DisconnectEquipment.SelectedDevice and `details.filter` from SwitchFilter.Filter.Name. It retains numeric details and observes selection changes plus the nested filter name. No host objects are serialized and the existing 58 KiB projection budget remains. The iOS formatter appends these selections inline while older snapshots retain the step name. Selection projection tests pass (54 portable tests total), Swift contract/lifecycle/control/title checks pass, and the plugin cross-build succeeds (40 warnings, zero errors). Prepared only: no NUC installation/restart, equipment action, physical-phone install or new Parallels runtime acceptance was performed. Production still needs both plugin and app updates before these labels appear.

## October23 ~07:21CDT authorized plugin0.4.11.0 deployment complete

Kevin explicitly requested installing the updated plugin and authorized restarting NINA. Preparedsequence-labelsZIP420798bytes/SHA256d97db742ada153a401ce87b5afd5ec19b50806190bb3410c742c1db469ed57b4wasverified/transferred. PreflightliveNUC0.4.10hadnosequencerunning,nocameraexposure. NINA13284closedgracefully;allfivepluginfileswerereplacedandinstalledSHA256/sizesmatchedZIP. Noconfigchanges:nativecontrol.jsonSHA256F18B05EFD7066561FA9E2A44ACF02ACC65D9A78BFC8E09E32D2E06A7B03813D7before/after. No backupperpersistinguserpreference. RestartedinteractiveNINA3.2.1.3002asPID1628,plugin0.4.11.0verifiedbyfreshWSsnapshot/newsession89340b68-969b-40a1-9b64-1d23b17d6823. Listeneronly100.89.247.90:1889.Macgatewayautomaticallyreconnected,publish/diagnosticerrors0.Sequenceprojectioncurrentlyfouremptyrootcontainers;noequipment/filterlabelvaluescanbeclaimedobserveduntilloadingappropriate sequence.Noequipment/sequenceactionsissued.NoiOSbuildorinstallinthisplugin-deploymentturn;matchingpreparediOSformatterupdateisseparate.Temporaryclose/launchtaskdefinitionsremoved.Evidenceartifacts/2026-10-23-nuc-update-0.4.11.

## October 23 active-profile device discovery (prepared only)

Kevin reported camera/mount Connect disabled by an instruction to select devices even though N.I.N.A. already has an active configured profile. Read-only NUC checks found profile b318e855-d2c5-49c8-ba16-079314e282ea retains camera ZWOptical_ZWO ASI6200MM Pro_ and mount ASCOM.ASIMount.Telescope, but the published devices catalog was empty. N.I.N.A. 3.2 initializes chooser Devices as a plain empty List and later replaces the property; the adapter observed only collection changes and missed these replacements.

Prepared plugin 0.4.12.0 observes chooser Devices/SelectedDevice property changes and ProfileChanged with a coalesced UI-thread refresh. Missing camera/mount chooser selections fall back to the current active profile IDs; available catalog names are retained. Explicit Connect performs discovery if needed, verifies the profile has not changed, checks acquisition/allowlist guards and resolves the requested driver before connecting. Telemetry never triggers hardware discovery. No real device operation or production restart was performed. Core tests: 60 passed; plugin cross-build: zero errors (40 existing warnings).

Companion Rig cards/detail sheets now display actual reported device names, falling back to the selected catalog name while disconnected. The misleading instruction to manually select a camera/mount is replaced by a missing-report message. Swift checks include disconnected camera/catalog-name selection and avoid choosing unrelated unselected devices. App and plugin deployment remain separate from these source checks.


## Address-only rigs deployed — October 23, 2026

Kevin explicitly requested removing the plugin/app password requirement, naming/selecting multiple rigs, updating both the NUC and Parallels, and restarting N.I.N.A. Plugin 0.4.13.0 removes bearer authentication for native HTTP/WebSocket clients; browser Origin rejection, configured listener addresses/firewalls, request limits, command/device/profile guards, and operation semantics remain. Legacy token configuration is tolerated but no longer required by plugin authentication. The local browser gateway retains its independent authentication. This supersedes older requirements for mandatory plugin bearer tokens.

0.4.13 includes the prepared 0.4.12 active-profile chooser/catalog fix and the sequence equipment/filter selections. Both hosts were idle at preflight. All five installed file hashes/sizes match the ZIP and each control.json hash was unchanged. Parallels restarted gracefully. Texas graceful closure timed out; the idle process was terminated before replacement, then NINA relaunched interactively as PID11704 (previous1628). Both report0.4.13 and support HTTP status and WebSocket snapshots without Authorization. Browser Origin remains403; invalid command validation remains400. Texas now exposes configured camera/mount names while disconnected. No real equipment commands, imaging, or sequence starts were issued.

The iOS app stores a Keychain rig library, migrates the old pairing, and provides name/address-only add/edit/delete/select. Seeded choices are Starfront Texas100.89.247.90:1889 and Parallels Simulator100.108.112.76:18891. Future southern rigs are user-added with their actual address. Switching clears telemetry and local control feedback, cancels retries, and invalidates old confirmations and late HTTP responses even after switching back to the same session. Defaults seed once, so deletion persists. The selected rig reconnects on launch/foreground with existing backoff.

The Parallels phone route uses Mac Tailscale Serve TCP18891 to a loopback-only relay, then guest10.211.55.10:1889. It preserves the guest firewall and rewrites Host for the explicit guest listener. No Funnel/public endpoint. A tested LaunchAgent attempt failed local-network access with errno65; its definition was removed. The relay now runs detached from the authorized local execution context and must be restarted after a Mac reboot; app README documents startup/removal. Texas is independent of the relay.

Core60 tests and Swift contracts passed. Live no-token HTTP/WS, Origin/validation rejection, actual simulator UI Texas/Parallels switching, and relaunch reconnection were verified. Evidence: artifacts/2026-10-23-address-only-rigs (package manifest, before/installed/verified records, network-checks.json). iOS device installation/launch receipts are kept separately and must be checked before reporting physical-phone success. Sources remain uncommitted with unrelated changes preserved.

Physical-phone completion: final signed Debug app installed and launched on communicator using hardware UDID00008130-001A4D622461401C. Normal relaunch without provisioning restored Texas and reconnected; NUC TCP independently verified phone100.124.150.76. Install/launch/normal-launch receipts and phone-tcp.json retained in the address-only-rigs evidence folder. Physical-phone Parallels selection itself was not observed; simulator switching plus end-to-end relay protocol checks passed.


## Texas equipment controls enabled — October 23, 2026

Kevin reported the host-control-guard message, explicitly authorized enabling real-equipment controls on Texas and restarting N.I.N.A., and confirmed it idle. Live preflight independently confirmed no sequence, exposure or active operation. Only control.json simulationOnly changed from true to false; all other configuration fields were compared and preserved, including the tailnet bind address and disabled legacy API. N.I.N.A. closed gracefully (PID11704) and restarted interactively as9364, plugin0.4.13.0, fresh session1ea3f225-ee12-466b-9244-b7d2c526f721. Initial startup sequenceRunning was null; verification waited for the explicit false state before proceeding.

Live catalog now reports the configured ZWO ASI6200MM Pro and ASI Mount selected/allowed=true, environment equipment. Both exact-device Connect requests passed POST /commands/validate with valid=true,dryRun=true. No commands were submitted to /commands, no devices were connected, and no imaging/sequence was started. Mac gateway reflects the changed catalog. Parallels remains in simulation mode with the existing session/configuration. Evidence: artifacts/2026-10-23-texas-equipment-mode (preflight, restart, verified, clients, Parallels unchanged). This supersedes earlier notes that real-device controls remain blocked by simulationOnly.


## Actual iOS simulator → Texas Connect acceptance — October23

Kevin explicitly requested selecting Starfront in the simulator and executing the failing workflow, testing and fixing it before returning. This authorized actual camera/mount Connect tests against Texas. Found/fixed two iOS defects without further plugin changes: ControlViews button label now fills its Form row (AX center previously fell outside the small button hit region); ATS exception added for tailnet100.64.0.0/10 because URLSession HTTP POST failed -1022 even while WebSocket monitoring worked.

Actual simulator UI camera Connect succeeded UUID6bf4eca9-1d0d-4fce-8a9d-18cdf951fdba, mount Connect succeeded447997f2-9e74-4900-b779-71bdff969cc9. NUC operation records and telemetry independently confirm both Connected; camera cooler off/not exposing, mount tracking off/not slewing, sequence stopped. Devices left connected. No capture/cooling/tracking/slew/sequence commands. Also verified UI Parallels selection and simulator-camera Connect over the Mac tailnet relay, succeeded2e80e649-c3c5-41e1-bedb-dde6b4a7291a; no cross-rig readings/feedback. Swift contracts, simulator/device builds passed. Fixed signed Debug app installed/launched on communicator, physical-phone Connect taps not separately tested. No plugin rebuild/restart/config change in this follow-up. Evidence artifacts/2026-10-23-ios-texas-connect contains before ATS log, screenshots, actual operation IDs, telemetry and phone receipts.

Final-state clarification: after the successful simulator Connect tests, a later camera Disconnect request f4a14740-d043-4c17-be61-a6503817fb89 succeeded at08:06:04CDT while this simulator was testing Parallels. It was not issued by this test. Later client actions changed the camera state again; check live telemetry for current connection state. This test issued no automatic reconnect or corrective action in response to that Disconnect. Both earlier Connect successes remain evidenced.


## iOS connection icon / status cleanup — October23

Kevin requested removing cached-reading orange clock badges app-wide and all command result/status history, replacing equipment detail status with a top-left Connected/Disconnected/Unknown icon. Implemented entirely in the iOS app; no plugin change or equipment action. Control feedback rows and routine disabled-reason subtitles removed; internal command records/guards remain and failures use alerts with optional explicit result check. Detail Updated section removed. Indicator and Rig-card state depend on live transport/bridge plus fresh diagnostics; disconnected bridge, expired heartbeat or missing bool connection value yields Unknown. Existing sequence execution-state icons remain.

Swift contracts passed, including new three-state/offline/expired-heartbeat checks. Simulator visually verified connected camera (mint check), disconnected filter wheel (gray cross), and unknown camera (orange question) after disconnecting only its monitoring socket. Device/simulator builds succeeded; signed app installed/launched on communicator. Evidence artifacts/2026-10-23-ios-connection-icons has all three screenshots, result and phone receipts. No physical-device control actions in this increment.


## October 23 Observatory Console web dashboard

Kevin selected ImageGen concept 1 and requested connect/disconnect controls plus complete browser verification, then explicitly required Parallels for operational testing. The existing vanilla TypeScript/CSS monitor now follows that concept: five equipment cards with direct selected-profile connect/disconnect, large image/thumbnail/statistics/imaging workspace, guider/autofocus charts, weather/safety/hardware, compact diagnostics, and an independently scrolling full-height sequence panel. Containers expand/collapse; completed, running, waiting, failed and pending states retain source identity. Follow-current is optional. Existing catalog controls and operation history remain in the toolbar dialog.

Browser startup retains the local HttpOnly session bootstrap and a single reconnecting WebSocket. The local 250 ms clock performs no requests. Diagnostic history retains at most 720 points and resets on NINA session replacement. Quick controls require fresh telemetry, the exact selected allowed device, acquisition guards, and confirmed disconnect/start/stop. Stop is limited to Starfront-owned sequence operations. Pending command outcomes are not retried after a lost response. Offline values are marked cached and safety becomes UNKNOWN.

The explicit `?preview=1` design view labels its telemetry illustrative, uses a recorded simulator M31 image, opens no telemetry socket and disables all commands. Histograms describe 8-bit rendered-preview luminance, not the raw FITS distribution. Sequence progress counts completed instructions rather than estimating remaining time; recent integration is the retained image-history sum rather than full-night totals. Native single-exposure counters are not presented as a planned frame total.

Verification: strict TypeScript and Bun bundle pass; synthetic gateway lifecycle pass. Actual Parallels plugin 0.4.13.0 (simulation profile, selected/allowed simulator IDs checked) completed 14 camera/mount/filter-wheel/focuser/rotator connection operations through the rendered dashboard. Weather, safety-monitor, dome, flat-device and switch also completed connect/disconnect/reconnect through its catalog form, with real status values shown. Dashboard-Test.json was loaded, started, observed capturing then waiting, stopped (start operation cancelled), reset and run fully to completion (2/2 instructions checked off). Browser decoded the actual 798x800 JPEG and downloaded a 25,473,600-byte valid FITS. Native simulator imagery retains its source FITS EXPTIME=60s despite the test instruction requesting 3s; the UI reports the received metadata. Guider/autofocus chart layout was checked with explicitly illustrative data; no new guider calibration or optical autofocus acceptance is claimed.

Playwright Chromium was used because Browser plugin is absent. Page identity/content, no error overlay, no console errors, zoom dimensions, modal/Escape behavior, tree expansion/follow, offline/session-replacement behavior, and desktop/tablet/mobile widths 1586/1920/1280/820/390 were checked. Connect/sequence request serialization and disconnected/stale guards also passed intercepted-request browser checks. This is functional/visual acceptance, not a new long memory soak.

Only Mac gateway static assets were published to the running shared 0.4.10 service content root; both existing browser gateways use that root. Main URL1890 remains connected to Texas; simulator URL1891 remains connected to Parallels. No NINA restart, plugin replacement, production equipment command or production sequence action was performed for this dashboard task. Simulator is left idle, completed test sequence loaded, simulator devices connected. Final screenshots are outside the repo at /Users/kevinmcquown/.codex/visualizations/2026/10/22/01a16464-0b7c-7851-a3ce-d74eb91905a6/observatory-console/. Operational receipts are in /tmp/starfront-dashboard-qa/. Source remains uncommitted; unrelated work preserved.


## iOS all-equipment Connect/Disconnect — October23

Kevin explicitly requested removing the camera/mount restriction and supporting all devices. iOS now offers Connect/Disconnect for every CommandCatalog equipment type: camera, mount, filterWheel, focuser, rotator, guider, weather, safetyMonitor, dome, flatDevice and switch. Removed UI and monitor's two-device restrictions, added missing auxiliary cards/readings, and mapped safety telemetry to safetyMonitor commands/catalog. All commands retain exact selected IDs, capability/allowlist/acquisition/offline checks and stale-confirmation validation.

Actual simulator UI tests completed22 successful connection/disconnection actions across all11 types, including PHD2; initial simulator connection states restored. UUIDs and matching telemetry retained under artifacts/2026-10-23-ios-all-equipment. Exhaustive Swift checks passed. Nine configured Texas devices passed dry-run Connect validation; no real-equipment actions or NUC changes in this increment. No plugin update required.

The UI cycle exposed a Mac relay bug: native HTTP clients reused EmbedIO's keep-alive connection without a second Host rewrite. Relay now rewrites HTTP response Connection to close and drops Keep-Alive, preserving WS upgrades/duplex traffic. Two Python regression tests and six consecutive live POST validation requests passed. Mac relay restarted locally with the corrected script; no NUC service changes. Both simulator/device app builds succeeded. Phone installation currently blocked by CoreDevice4016/communicator unavailable; user has been asked to unlock/reconnect. Do not claim installed until a new success receipt exists.


## October 23 actual M31 / PHD2 / autofocus dashboard demonstration

Kevin explicitly requested running Andromeda imaging, simulated PHD2 tracking and autofocus in Parallels. All control requests used the simulator gateway1891/private ios-simulator-service configuration, explicitly checked bridge10.211.55.10:1889 and environment simulation. No NUC requests, real-equipment commands, source edits, plugin replacement or NINA restart were performed.

Fresh native simulator M31 capture succeeded016e36a3-80a2-4be0-af9b-a99c2d3b8e8c. Initial PHD2 star selection failed and fixed-image autofocus correctly failed with identical HFR samples. PHD2 was stopped/disconnected and gracefully restarted via its verified local RPC. Only simulation profile2 (Starfront Simulation; Simulator camera/On Camera mount, localhost4400, no auxiliary mount/AO/rotator) was adjusted: SimCam40stars,0hotpixels,noise0.25,clouds0,dec_drift1,seeing1,PEscale2. Start c2ce0a92-7204-4c33-8dfe-f44081ac9dad completed successfully after calibration/settling. Guiding is left active; actual changing raw RA/Dec samples reach the bounded120-point dashboard history. NINA scalar RMS currently remains0 despite changing raw distances; do not infer zero physical guide error from it.

For optical simulation, the native NINA Simulator Camera now uses DIRECTORY input C:\StarfrontControlTest\OpticsSimulator. A local virtual-optics fixture takes the recorded M31 FITS and applies a focus-dependent Gaussian blur around position25000. It supplies16bit TIFFs; NINA performs the actual captures, star detection, HFR measurement, curve fitting and autofocus control. No measurements are injected into telemetry. Manual defocus at25200 measuredHFR4.822 versus2.722 at25000. After resolving the Windows fixture transport/file-replacement setup, autofocus b98e1489-8015-41db-a6b0-c58c887fd66d succeeded with9points from24800 to25200, a symmetric curve and minimum sampledHFR2.72184 at25000. The exposed result.position remains null; current focuser and sampled minimum establish25000, not a separately verified fitted-position field.

Final sharp3sM31 capture a00bb036-fb71-4311-9779-72534ff37515 succeeded, actual3564x3571 FITS,1152stars,HFR2.72184. Browser decoded798x800 JPEG, displayed two changing guide traces, Finished autofocus with9points, and no page errors. Sequence is idle/completed; guide operation initialization has completed but PHD2 remains guiding. Final screenshot outside repo: /Users/kevinmcquown/.codex/visualizations/2026/10/22/01a16464-0b7c-7851-a3ce-d74eb91905a6/observatory-console/live-m31-guiding-focus.png. Snapshots and operation receipts: /tmp/starfront-m31-guiding-focus/.

Runtime fixture files are /tmp/starfront-optics-http.py (bundled Python/Pillow/NumPy), /tmp/starfront-run-simulator.py, and Windows C:\StarfrontControlTest\optical-feed.ps1. The Mac image server binds only Parallels host-only10.211.55.2:1893, retains one rendered TIFF, and shuts down after125minutes. Windows on-demand task Starfront-Optics-Simulator runs for at most2hours, replacing one frame.tif atomically only when focus position changes. It has no boot/recurring trigger. It polls only this temporary virtual-optics fixture; dashboard telemetry remains WebSocket driven. Repeated autofocus needs this fixture running; after it expires, DIRECTORY contains the last fixed sharp image and a further autofocus will produce a flat curve until the fixture is restarted. No new long memory-soak acceptance is claimed. The native fixed-image source C:\StarfrontControlTest\dss-m31-fixture.fits remains available in the camera's IMAGE setup.

## iOS session home — October23

Kevin authorized replacing the first iOS page with a compact visual session overview: activity/safety, live guide traces and last measured autofocus curve near the top, latest image and age/quality, instruction progress and equipment/conditions. Implemented SessionDashboard/SessionReadings in Starfront Operations/apps/StarfrontIOS; existing control guards/screens preserved. Sequence heading navigates to full hierarchy; equipment icons open existing controls. No explanatory paragraphs, cached clock badges or command history were added. Unknown values remain unavailable; guiding Live requires fresh samples and live heartbeat; autofocus displays outcome/best sample, not an inferred good-quality result. Recent image/HFR history is32 entries, resets with session/rig change; preview decoding occurs once per revision. No polling added.

Swift contract/lifecycle/control checks and new projection checks passed. Simulator and signed device builds succeeded. Actual Parallels data visually verified Andromeda, changing120-sample RA/Dec traces, nine-point last autofocus, sequence2/2, equipment sheets and sequence navigation. A fresh3s simulator capture7b5c9997-205b-457d-b8c7-11a56f226639 succeeded and updated image/trend/recent counts2frames6s. Large accessibility text inspection prompted stacking charts into a single column. Updated app installed on communicator; see separate launch receipt in artifacts/2026-10-23-ios-session. No production equipment commands/plugin deployment were issued. Kevin separately requested temperature/cooling camera simulation after this increment; native NINA Simulator Camera reports null temperature and canSetTemperature=false, so the app did not fabricate a temperature.

Phone clarification: installation of the new session screen completed successfully (container FC869429-C389-4602-AE4A-CCB24B15BE35); automatic launch was denied by iOS Locked. User can open normally. No claim of new-screen observation on the physical phone is made.

## Virtual camera temperature/cooling — October23

After the iOS screen, Kevin authorized fixing camera simulation to report temperature/cooling. Added a separate test-only MEF fixture under tools/StarfrontSimulation, version0.1.0, GUID f1f5c5f3-fd51-4edc-bc94-490f9b72e879. It subclasses NINA SimulatorCamera and reimplements ICamera thermal properties; NINA's existing IMAGE/DIRECTORY optics/processing are retained. Virtual device d038dcf9-969f-4bd4-b215-2fd61c182242 reports its explicit Starfront Thermal Simulator Camera name. Monotonic first-order model starts15C, target−10C, coolingoff; no timer/log/history. Tests cover cooling/warming, frequency independence and invalid/unreachable targets.62 portable tests passed; fixture cross-build passed with4 existing transitive NuGet compatibility warnings.

Only the idle Parallels NINA was gracefully closed/restarted (2964→7268). Installed only StarfrontSimulation.dll into its separate plugin folder; active simulation profile camera ID and VM allowedDeviceIds gained the new exact ID, simulationOnly stays true. No production configuration/binary/equipment changes. Starfront Control remains0.4.13.0. Actual connect confirmed native temperature15C/canSetTemperature=true. Cool f2b2ca9d-b005-463e-b24b-87a488f5ed3f completed, temperature fell15→−9.52C and power100→63.95%. Warm6f3c7e9f-4efe-4f48-ae6b-23b14e660e85 completed: temperature~15C, coolerOff/power0. The initial client wait expired while the operation continued; inspected the same UUID to eventual success without resending. NINA warms toward20C and waits for its own no-progress detection at the model's15C ambient, then turns cooler off, completing after~193s. This is expected native regulation behavior.

Thermal-camera capture9286fd26-c59d-4a84-9ee4-15a2ecc81b24 succeeded; autofocus645990aa-d63b-41dd-aef6-7a75e89b4f86 succeeded with9 measuredpoints, bestsample25000/HFR2.72. Finalcool eb93b43f-d244-45b5-99ec-e3a5e3723ac2 and3sM31capture d1c689c1-84a8-416c-bcab-a5a0452f8bb3 succeeded. Left simulator camera connected/cooled toward−10C (~63%power), PHD2guiding, no sequence running. Existing temporary virtual-optics feed still supplies focus-dependent M31 images and retains its prior bounded lifetime. Native NINA scalar RMS still0 despite changing raw samples; UI labels NINA RMS separately. No replacement scalar RMS was fabricated.

Latest iOS main screen also shows pending capture/focus/cool/warm operations as current activity; masks disconnected-camera/weather defaults. Final signed app installed successfully on communicator (4BFEA358-E18D-42E5-8595-41454E6423ED); launch could not be verified because CoreDevice remoteService was unavailable (earlier launch was iOSLocked). Simulator UI observed actual Focusing and Cooling, completed curve, new captures, temperature−9.9C/target−10C/power63%, monitor disconnect→Offline/Unknown and reconnect restoring telemetry. Physical phone normal opening remains user-verifiable. Evidence in artifacts/2026-10-23-ios-session and artifacts/2026-10-23-thermal-camera. Temporary interactive UI task removed.

## October 23 LXC recorder application / CLI

Kevin chose the always-on Starfront LXC for telemetry recording, explicitly asked for a separate app tracked in the Starfront Operations monorepo and CLI start/finish control. Replay viewer/scrubber/event markers are a future direction and explicitly deferred. Implemented apps/StarfrontRecorder (TypeScript/Bun, compiled self-contained Linux x64) in /Users/kevinmcquown/Documents-Local/Personal/astrophotography/Starfront Operations. The independent native Mac CLI now exposes starfront recording status/list/show/start/finish; installed release.TFgkdF with the existing processing engine/catalogs preserved. Start accepts rig/name/UUID; finish requires exact recording ID. Requests use JSON over SSH stdin to a private Unix socket, with no automatic mutation retry or equipment commands.

Deployed version0.1.0 to CT105 starfront, klm@192.168.0.155. User systemd recorder/tunnel services enabled with linger, private0600 Unix control socket. CT105 cannot directly reach Tailscale, so a dedicated forwarding-only SSH key reaches Proxmox192.168.0.235 and permits only NUC100.89.247.90:1889 as its destination; pinned host identity verified through existing trusted Proxmox SSH. Loopback-only forward1899, native Host header preserved. A one-frame read-only production WebSocket handshake/schema check passed; no production recording or equipment command/NINA restart/plugin changes. Temporary Mac→LXC reverse forward1900 used for actual Parallels tests and removed afterward.

Recordings under /home/klm/.local/share/starfront-recorder/recordings on CT105 root disk, separate from disposable worker scratch. Timestamped changed topic envelopes plus minute/session checkpoints, explicit connection gaps, preserved source session/revisions/timestamps, independently compressed gzip NDJSON records in8MiB segments, content-addressed JPEG previews stored once; no FITS download/copy. Defaults512MiB/session,2GiB total,512MiB free floor,200 recordings; no automatic deletion. Process limits256MiB memory/25%oneCPU; no growing app stdout/stderr logs. State is retained incrementally, no unbounded in-memory history/write queue. Storage/write failures close recording explicitly. Crash recovery marks unfinished recordings interrupted and does not auto-resume; last gzip member can be partial and manifest counters may lag five seconds. This records received/coalesced snapshots, not every internal NINA event. No NAS archiving or automatic sequence-linked start implemented.

Verification:8 recorder tests (32assertions) and strictTS pass,21 Swift CLI tests pass, release build/install succeeds. Actual CT105 recording930384ad-217e-472c-9c81-002edc47a902 received208 Parallels snapshots/oneJPEG in211940data bytes over~81seconds; full journal reconstructed, indices/previewSHA256 verified. Native3s thermal-simulator capture7c885367-6970-46b9-bdde-bd695ff9eeb7 succeeded and its operation/state updates are present. Second recordingb3562e68-99f5-4278-ac52-745b6aa5148c has verified gap-start/gap-end after real tunnel disconnect/reconnect; SIGKILL/restart recovered idle and preserved87readable records, manifest interrupted. Active-recording deployment refusal verified. Short sample RSS~52–53MB, no overnight stability claim. Source/binary hashes and sanitized deployment state: monorepo ops/deployments/2026-10-23-recorder.json; detailed ignored receipts output/recorder/acceptance. Services left enabled/running and idle, no recording active; simulator capture finished. Unrelated source changes preserved; no commits/push.

## Website dashboard integration in progress — October23

Kevin requested integrating Observatory Console into public starfront.thewcl.com with a homepage shortcut. He specified that only the dashboard requires his chosen four-digit code. Implemented optional server-side code gate and strong origin credential on a separate Linux gateway, not in NINA. Code stored only as keyed digest in private config; not copied to tracked files/browser code. Website /dashboard/ route proxies strict allowlisted endpoints via authenticated Cloudflare Tunnel to CT105; homepage/shared navigation links added, existing public audience preserved. Backend currently uses Parallels via temporary1900 reverse tunnel while browser acceptance is pending. Native source lifecycle/Host rewrite/auth/cookie/forgery/logout/rate-limit tests pass; site75tests/build/link audit pass. Source-only saved version101 is deploying after191MiB archive upload timed out; check pending deployment rather than create another Site. User discussed OpenClaw overnight monitoring and eventual CLI-driven custom sequencing as future directions only; no agent/recovery/customsequencer implementation is authorized by those comments.


## iOS icon direction — October23

Kevin selected Observatory concept2 and requested a stylized approximation of his own rig. Built-in ImageGen produced a non-destructive variant using the RedCat91/AM5N/ASI6200/guide-scope equipment inventory, preserving the silver dome/navy sky/teal lighting. Saved output/imagegen/ios-icon-concepts/02-observatory-redcat91-v1.png and its prompt. Concept only; app icon assets/build/device installation were not changed.

Website deployment checkpoint: version101 source-only remote build failed because the hosted runner lacked Bun. Added pinned bun1.3.10 to the website devDependencies with its existing npm lockfile retained; local clean installer/build and75tests pass. Version102 (source6e2df41ff14da2228df0a0522007f084ed4eab1b) is pending/building under deploymentappgdep_6adb899813d48191a654c3e0ae4ff8f2. Do not retry101 or upload191MiB archive again without addressing the five-minute transport timeout. Actual Linux origin access now passes with the configured PIN after normalizing hexadecimal digest case; expired-cookie regression also passes. Gateway still points Parallels while browser acceptance is pending.


## iOS selected icon installed — October23

Kevin authorized using the RedCat91 Observatory icon and installing it on his phone. Added opaque1024px universal AppIcon asset in Starfront Operations/apps/StarfrontIOS/Sources/Assets.xcassets and selected AppIcon for Debug/Release. Simulator and signed physical builds passed; compiled app Info.plist references AppIcon and includes generated icons/Assets.car. Initial physical installation failed with CoreDevice network timeout. After Kevin unlocked/reconnected the phone, installation succeeded into9CE05A1C-6772-40E0-BC4F-4BF263B3C7E9, and devicectl launch succeeded. Receipts and asset hash: artifacts/2026-10-23-ios-icon. No equipment commands, profile changes, plugin deployment or simulation restart in this icon-only increment.


## iOS roof wording — October23

Kevin requested Safe→Roof open and Unsafe→Roof closed. Updated Status badge, Rig card summary and detail row; Unknown remains for missing boolean/offline/stale heartbeat. This is Kevin's roof-switch display convention, not a change to raw safety.isSafe, command guards or NINA sequencing. Existing projection checks now cover open/closed/unknown/offline; full Swift checks and simulator/device builds passed. Final app installed successfully on communicator, container3875EE94-F403-4431-B4CF-EBF649A7F3CB. Launch outcome saved separately in artifacts/2026-10-23-ios-roof-labels. No hardware commands or plugin deployment.


## iOS compact connection/roof header — October23

Kevin requested removing the entire idle/imaging-session/activity panel and placing roof status right-aligned on the rig connection row. Removed panel and unused view helpers; added roof label to shared header, retaining1s local freshness updates/no polling and Unknown when safety device disconnected or telemetry unavailable. Read-only native NUC status at16:30UTC showed live diagnostics but safety.connected=false/name=null/isSafe=false; disconnected default false is not roof-position evidence. No corrective connection, restart or hardware command was issued because Kevin reported concurrent NINA testing/restarts.

Swift contract checks and simulator/device builds passed. Actual simulator screenshot verified the compact row, right-aligned Unknown and absence of session panel. Updated signed app installed on communicator, containerD0A7F227-12E2-46AF-9D78-B285E1DD4710, and launch succeeded. Evidence: artifacts/2026-10-23-ios-compact-header. No backend/plugin changes.


## iOS system appearance and roof indicator — October23

Kevin requested Roof unknown instead of bare Unknown, green Roof open, and proper light-mode support. Updated roof labels in header/Rig summaries, retained Unknown detail under explicit Roof row. Open is explicit green, not mint/amber; closed/unknown retain warning color. Removed forced dark appearance from the live app. Added dynamic asset colors for canvas/cards/borders/accent/warning/roof-open and guide traces; chart grids use primary opacity. Native forms/sheets follow system appearance. Kept image pixels on black. Dark/light previews added.

Full Swift checks and simulator/signed-device builds passed. Visually inspected actual simulator light Status/Rig/Sequence and dark Status; current disconnected simulator route was left untouched. Light green/warning/accent/trace colors each exceed4.5:1 contrast on white. No new equipment or NINA operations. Updated app installed and launched on communicator, container27B980B2-E81E-412B-89E6-E168D21F971C. Evidence/screenshots/contrast values: artifacts/2026-10-23-ios-appearance.


## Website dashboard complete — October23

Published Sites version103, commit81a32078528b1730271eb88d4fcf136812bc58d6, deploymentappgdep_6adb8b85e9248191b6c2094e334f683e succeeded with environment revision2. Canonical https://starfront.thewcl.com/dashboard/ is linked from homepage CTA, quick link and shared Dashboard navigation. Public site audience unchanged. Dashboard-only server-side four-digit gate,12hourHttpOnlySecureSameSiteStrict cookie scoped/dashboard, signed sessions,20globalattempts/15minutes persisted in one bounded file; no actual code in source/browser. Separate strong server credential protects every tunnel-origin route, including assets and auth bootstrap. Local bootstrap/browser ticket/design preview routes excluded by Worker allowlist. Existing site images, plans, results and oldLive preserved.

Independent LXC gateway0.4.14.0 at loopback1894, Cloudflare Tunnelstarfront-control, native upstream through existing restricted loopback1899→Proxmox→NUC1889. User services starfront-web and starfront-web-tunnel enabled, resource caps384MiB/50%oneCPU and128MiB respectively. Gateway fans out one upstream socket to website viewers. Plugin/NUC binaries/settings unchanged, no NINA restart or real-equipment commands. Recorder app remains separate and idle. After Parallels verification, private gateway config restored to production; actual website endpoint and browser verified equipment environment/NUC0.4.13.0 and live real-device telemetry. Current NUC sequenceRunning=false; no image/sequence exists in this plugin session, correctly shown unavailable rather than preserving simulator image.

Verification:76site tests/9398assertions, production build and link audit pass; C# synthetic lifecycle and PIN/origin/cookie-forgery/expiry/nativeHost/WebSocket/logout/attempt-limit tests pass. Actual browser correct code opens, wrong code rejected, Lock returns to gate; public homepage/nav link verified. Parallels live JPEG, guide/autofocus data visible through hosted WS. Mount's initial Connect failed because Mac OmniSim helper was stopped; restored the existing simulator helper and same exact selectedID, then website API Connect9d04f0b4-eb50-4b0b-8661-1ba823b3a541 succeeded. Actual website UI Connecte566b2ed-6075-4944-801d-ee2caafae010 succeeded; confirmed UI Disconnect succeeded and restored initial disconnected state. Helper and temporary1900reverseSSH stopped afterward. Native compact32hex image IDs exposed a proxy allowlist mismatch; fixed strict32/36GUID support with regression, actual25459200byte valid FITS download passed through final site. Source HTML header preview anchor canonicalized for current proxy; proxy regex also accepts whitespace. Temporary API QA cookie removed. Browser left on live production dashboard with user's authorized code session.

Deployment difficulties were resolved:191MiB archive upload timed out, source-only fallback101 remote build lackedBun; added pinnedbun1.3.10 to existing npm devDependencies/lockfile. Version102 succeeded;103 adds compact-ID/header hardening. No remaining failed deployment blocks current103. Monorepo app ownership apps/StarfrontWebGateway, units/README and sanitized inventory ops/deployments/2026-10-23-web-dashboard.json. Browser proof screenshots under /Users/kevinmcquown/.codex/visualizations/2026/10/23/01a1693f-e653-73a3-9242-cfae9fbee278/. Evidence artifacts/2026-10-23-website. Plugin fork and monorepo edits remain uncommitted; website source was committed/pushed by Sites publishing helper. User's OpenClaw overnight supervision and eventual CLI-driven sequencer remain future discussion only; no agent, automatic recovery, custom sequencer or replay viewer added.


## iOS camera thermal controls/graphs — October23

Kevin authorized editable camera temperature controls, Cool/Warm taps, a dual-axis temperature/power time graph and a miniature on the Rig camera card. Added CameraDetailView and shared CameraThermalHistory, a−30…+10C slider/1C buttons and60s Cool/Warm requests. Slider edits never send commands; Cool captures the chosen draft. Gain/offset remain readouts. Exact selected camera ID/session, host catalog/capability/allowlist, connected/live/acquisition guards and uncertain-result handling are preserved. ControlIntent now encodes numeric parameters, locally rejects out-of-range/nonfinite targets/unexpected parameters and revalidates before sending. Other controls retain their previous behavior. Pending thermal/acquisition operations must finish before new commands.

History is shared in ObservatoryMonitor, push-only, at least5seconds between samples, max720 samples/onehour; resets on rig/session/camera changes. Missing/disconnected readings create gaps, long gaps are not joined. Full chart has leftC/right0–100% axes, horizontal time, solid blue temperature and dashed teal power. Miniature replaces numeric thermal summaries, with current-value accessibility support. Both appearances verified with actual simulator readings. No new timers/queues/logs or equipment polling.

The Mac Python relay18891 was listening but resetting requests; restarting only that local process restored the existing Parallels route. Native plugin remained0.4.13.0; no VM/NINA restart, production query/control or plugin deployment in this increment. Actual iOS simulator Cool42fe79b1-34b0-40c7-a7ae-2678da2f269a to−15C succeeded; Warm771ff2c3-5e79-40c9-9010-d72accd3c961 succeeded after NINA settling (~223s); restoreCool a1a80042-997b-4c08-9cf2-3881948beb2f to−10C succeeded. Settled Warm telemetry independently verified coolerOff/power0/temp~15C after an initial immediate post-completion sample still contained the preceding camera flags. No resend occurred. Simulator left in its original camera state: connected/cooled toward−10C (~63%power), idle, with other device states unchanged.

Swift checks cover720/1h bounds, missing values, resets, encoded thermal parameters, unsupported/disconnected/offline capability, invalid targets and changed-camera rejection. Existing lifecycle/all-equipment/command checks pass; simulator/device builds passed. Closing/reopening detail preserved actual graph history. Updated signed app installed on communicator, containerC3AC728B-6762-4321-89B6-FFBED75E17FD; launch receipt and detailed results under artifacts/2026-10-23-ios-camera-thermal.

Thermal controls phone-launch clarification: the final signed installation succeeded, but automatic launch was denied by iOSLocked. No physical-phone control operation was tested; user can open the installed app normally.

### Website navigation cleanup — 2026-10-23

At Kevin's explicit request, deleted the old public Live Observatory page and its dedicated browser script/styles/source. Moved Dashboard from the primary menu into the far-right shared header position, linking to `/dashboard/`; updated the remaining archive link. Preserved live API/backend and current audience/configuration. Opened the latest Sites source first (version104, commit fbc7f8a307f9d49233901bd7c1b44b698d9be34d), preserving the concurrently published nightly plan.

Site checkout: `/Users/kevinmcquown/Documents-Local/Personal/astrophotography/Starfront Operations/sites/starfront-observatory`. Published version105, source commit42c03c4c7006ba9ab0726b9caa02eae55caa3446, deploymentappgdep_6adbb252030c8191a9500a2e7e2ce6a3 succeeded, environment revision2. Full build and76tests/9407assertions passed;87pages reachable with3766valid references and no link audit issues. Browser verified published far-right Dashboard link opens the Observatory Console, and `/live/` shows404. Dashboard reported Offline with cached telemetry during this check; no backend/equipment actions taken. Screenshot `/tmp/starfront-navigation-published.png`; test/build logs `/tmp/starfront-navigation-tests.log`, `/tmp/starfront-navigation-full-build.log`.

## Graphical website equipment controls — October23

Kevin requested web counterparts to the iOS graphical equipment panels. Added web/device-panels.ts and camera-thermal.ts, wired in web/monitor.ts, and extended wwwroot/styles.css. Main cards open dialogs; six compact auxiliary buttons cover all11 device types. Camera has −30 to+10°C local target slider, explicit Cool/Warm with60-second ramp, dual-axis measured temperature/power graph and bounded720sample/1hour history, miniature graph and single-capture controls. Added filter choices from active profile, focus move/autofocus curve, rotation/mount/dome orientation controls, guider chart/start/stop, weather/safety readings, flat-panel and power sliders. Capability/host guard, freshness, busy, unknown outcome and changed-camera confirmation checks remain; no new polling. Source readback/operation completion drives status, not optimistic hardware success.

Verification: strict TypeScript,62 preexisting core tests,3 thermal-history tests, isolated gateway lifecycle and dashboard access tests passed. Browser synthetic checks: all11 panels, no POST on slider/cancel, exact confirmed thermal/filter parameters, offline disabled controls, changed-camera confirmation blocked without POST,390px/no overflow and desktop. Actual Parallels0.4.15 UI Connect→Cool10C→Warm→Disconnect all succeeded; Cool reached10.4C. Warm reached15C ambient with coolerOff/power0, despite its interim20C setpoint; no claim of20C reached. All11 connection flags matched initial snapshot and sequence stayed stopped. Evidence under /tmp/starfront-web-controls; screenshots in ~/.codex/visualizations/2026/10/23/starfront-device-controls.

Coordinated simulator ownership with Starfront Control Plugin (2), thread01a1693f-e653-73a3-9242-cfae9fbee278, per Kevin’s explicit coordination request there. No plugin/core files changed here. Its separate sequence-editor.ts mount is wired; old production0.4.13 lacks editing.supported, so editor launch stays hidden. Its owner owns sequence editor backend/runtime acceptance and future observatory deployment.

Published only app.js and styles.css atomically to existing klm@192.168.0.155 LXC ~/.local/share/starfront-web/service/wwwroot. Matching local/remote hashes: app60b57a56abd2e97c6f882a1b447bce166a1bfd93a5a34fa5d2e6f427c343ebec, styles034f3c43123ee51fb423016ba778503fd7d40868b78a945dcfbe0522e4a834cb. Rollback copies in ~/.local/share/starfront-web/ui-backups/2026-10-23-device-panels. Hosted browser camera modal/auxiliary buttons/editor hidden and console verified. No gateway restart, service/config/PIN/tunnel/permissions changes, NUC/plugin changes or real equipment commands. Sites Worker/source needed no change because it already proxies these protected origin assets. No repository commit/push made; preserve all existing unrelated edits.

### Dashboard equipment scope and compact roof status — October23

Kevin clarified: no dome, flat panel, power controls or weather on this dashboard; Guider already has its PHD2 panel. Removed the entire auxiliary shortcut row and obsolete Weather/Safety & hardware sections at UI mount. Kept the primary equipment cards and existing PHD2 chart. Added compact noninteractive roof status immediately after the Live/Offline status, using the safety monitor: fresh connected boolean true=Roof open (green), false=Roof closed (amber), missing/disconnected/stale transport=Roof unknown (amber). Reuses existing update clock and push telemetry, no new polling or commands.

Strict TypeScript/build and3thermal-history tests passed. Synthetic browser fixture (no equipment connection) verified closed→open→monitor disconnected→transport offline, with unknown on both disconnected paths, and camera dialog still opens with offline controls disabled. Published only app.js/styles.css to existing LXC gateway wwwroot, without restart/config/backend changes. Matching local/remote SHA256 app6f085ffc9d026f073a9fdcda0e3b4fc5b1b8a1ea2adb8171b05b674f2b8a2e10 styles1de7714178fdd26894602f14539ad83a73ff2829faf08b20d1bd2f50d09c151e. Hosted browser showed Offline/Roof unknown, waiting for telemetry. Screenshot /tmp/starfront-roof-status-published.png. No real equipment commands. Source remains uncommitted with prior edits preserved.

## Stopped sequence editor and structured insertion — October 23, 2026

Kevin authorized the first editing increment, Parallels verification and coordination with “Tonights Planning (2)”; he then added insertion between instructions/end of container, categorized pickers, and explicit support for sequential/parallel/deep-sky instruction sets plus conditions/triggers. Development remains on the existing local branch; unrelated edits preserved. No observatory plugin deployment or real equipment commands are authorized by this increment.

Implemented plugin0.4.16.0: editable scalar metadata, loaded-root identity/configuration revisions, synchronous UI-thread revalidation before mutation, stale/reloaded-root rejection, stopped/acquisition guards and authoritative WebSocket readback. Existing numeric edit/target/addTarget also enforce tokens in the new adapter; their shared catalog tokens remain optional for compatibility with older gateway/upstream hosts. Configuration revisions exclude execution counters (container Iterations, TakeExposure.ExposureCount, completed/progress values). Compound exposure counts edit the actual loop condition, not the acquisition counter.

Insertion uses NINA's factory and InsertIntoSequenceBlocks with proper Items/Conditions/Triggers attachment. Catalog preserves Camera, Filter Wheel, Timing, Instruction sets, Conditions and Triggers. Camera includes the five NINA Camera namespace instructions (CoolCamera, WarmCamera, DewHeater, SetReadoutMode, SetUSBLimit) and four imaging types (TakeExposure, TakeManyExposures, SmartExposure, TakeSubframeExposure), yielding nine camera choices total. Containers include SequentialContainer, ParallelContainer and DeepSkyObjectContainer, with target name/RA-hours/Dec/rotation settings. Initial conditions/triggers are LoopCondition, AutofocusAfterExposures and DitherAfterExposures. Unknown third-party settings stay read-only. New compound exposures preserve NINA's instruction name and default to one second/one frame. Changes affect the loaded sequence; no automatic save/start/delete/reorder was introduced.

New web/sequence-editor.ts owns its own stopped editor, insertion lines, equipment/category then instruction dropdowns, parameter controls and preserved-draft conflict handling. Tonights Planning (2) owns monitor.ts integration/device panels; file boundaries were agreed through explicit coordination. The editor is hidden when production0.4.13 lacks metadata. That thread deployed device UI assets without plugin/server/config changes. A final module correction preserves the Refresh button DOM node across pushed frames; strict TS and actual browser conflict/Refresh checks passed after it. Include the latest module when next bundling UI assets before production editor activation.

Verification:76 core tests passed; Windows cross-build passed with0errors/47existing warnings; CLI build passed. Hash-verified plugin0.4.16 loaded in Parallels NINA3.2.1.3002 under simulation profile cd432469-2b6e-442c-987e-78504975ff3e. Final explicit simulator test passed62 command checks: all catalog entries inserted, typed fields/readbacks, target/filter changes, sequential/parallel hierarchy, condition/trigger attachment and settings, invalid/stale/legacy-token requests, two-client edit race (exactly one winner), duplicate UUID, sequence reload, blocked edits/insertion while running and two WebSocket viewers. Two waits inside a ParallelContainer were observed RUNNING simultaneously during a normal simulator sequence; run finished successfully and configuration revision stayed unchanged by execution. Trigger firing itself and other condition/trigger types were not executed in this increment.

Runtime prerequisites: existing local PHD2 simulator was initially stopped; the start guard rejected the run until PHD2 was launched and its built-in Simulator camera/On Camera mount/profile were verified by the existing guard. No guider operation was issued. Browser testing in the isolated localhost1897 harness inserted a DSO, renamed it, inserted Smart Exposure through Camera grouping and applied count3; native NINA readback confirmed the underlying LoopCondition.Iterations=3. Independent-client changes preserved an unsent draft, disabled Apply, and successful Refresh restored authoritative values. No file save occurred. Final simulation remains stopped with camera disconnected and the browser verification tree loaded.

Evidence: ignored artifacts/2026-10-23-sequence-editor/ contains package+SHA256manifest, guest installation hash verification, final runtime log/commands, edited-tree and final snapshots, two-viewer summary, browser readback/conflict operation and screenshot. Tests: tests/integration/sequence-editing-parallels.ts and isolated sequence-editor-preview.ts. Detailed contract/limitations: docs/SEQUENCE-EDITING.md. The real NUC plugin remains unchanged.

### Guider connection card — October23

Kevin requested Guider beside the primary equipment cards, retaining the lower PHD2 status/chart. Added sixth Guider card, live readout using native nested RMS units, same selected-device connect/disconnect guards/pending/confirmation lifecycle, and existing guider detail dialog. Six desktop columns and balanced two-column mobile pairs. No new endpoint or polling.

Strict TypeScript and browser build pass. Actual Parallels profile cd432469-2b6e-442c-987e-78504975ff3e/plugin0.4.16 browser acceptance: initial attempts were blocked by local PHD2 verification while PHD2 was stopped; launched installed PHD2 in the interactive Windows session using temporary Starfront-PHD2-UI-Test task. Connect0d702ccc-0cf9-4c60-9eac-c238c07c2c5f succeeded; native state and both top card/lower chart showed connected. Confirmed Disconnect through top card returned both panels to disconnected. No sequence run or other equipment command. Temporary task and helper processes/files cleaned up; guider restored initial disconnected. Published app.js/styles.css only to LXC origin; no service or NINA restart/observatory commands. Matching SHA256 app5a6ecb65fe22ed711501ca6ad7bda7462fbb635f52c2e1fe42344aecd9ad3802 styles47046adfb9d9a50add7beb9d3818e4cbbebf011916082c61200ffbd7dc1a7418. Hosted browser verifies Guider/PHD2 card and disabled Connect while production telemetry is offline/stale. Changes remain uncommitted.

## Observatory deployment and live sequence editor — October 23, 2026

Kevin explicitly authorized installing the updated plugin on the NUC and enabling sequence editing on the Starfront dashboard, including restarting NINA. Preflight confirmed NUC SFO-Scope/astroadmin100.89.247.90 idle, no active operations/exposure/sequence, all equipment disconnected, active production profile b318e855-d2c5-49c8-ba16-079314e282ea. The loaded, unsaved tree contained one CoolCamera instruction under Targets.

Installed the exact Parallels-tested0.4.16.0 ZIP430368bytes; all five deployed file sizes/SHA256 match its manifest. Plugin DLL SHA256285224193AEDF52FC94553DD855090CEDAFD9F13E8CF8B0AEDB91E3737AFA800. NINA11256 closed gracefully and restarted interactively asPID6828/session2. No backup made per Kevin's standing preference. Control configuration SHA256C09441A13DED60947CE1F637C4B123805D292BE26F8BEB7C72F04307F55434E5 unchanged; listener remains100.89.247.90:1889. Temporary close/start scheduled tasks removed. No firewall/auth/settings changes, equipment commands or sequence start were issued.

NINA reopened with four empty root/area containers; the previous unsaved CoolCamera instruction was not restored by the host. This was explicitly reported to Kevin; no replacement instruction/parameters or production sequence were silently created or loaded. Fresh native session81a61fc7-b1a1-4cba-8bb3-0b18015575c5 confirms plugin0.4.16, environmentequipment, editing.supported/canEdit=true, sequence stopped, camera/mount/filterwheel/focuser/rotator disconnected. Publish/diagnosticerrors0, pendingTopics0, plugin logs~6.7MB at acceptance. These are short deployment checks, not an acquisition/resource soak.

Rebuilt latest monitor bundle (including stable stale-draft Refresh control and all concurrent device/roof/guider work), strict TypeScript passed. Atomically deployed only wwwroot/app.js on existing klm@192.168.0.155 Starfront web gateway. Local/remote SHA2565a6ecb65fe22ed711501ca6ad7bda7462fbb635f52c2e1fe42344aecd9ad3802 match. Styles, gateway binaries/configuration, tunnel, PIN and Sites source/deployment unchanged. Existing gateway already proxies new command actions, so no gateway restart required.

Actual hosted browser https://starfront.thewcl.com/dashboard/ eventually reported Live after reconnect; Edit sequence is enabled and opens the editor. Camera/category instruction dropdown and insertion positions verified without submitting Insert/Apply. Separate authenticated public website WebSocket received two fresh connected frames for the new NUC session and closed normally1000. Public /dashboard/api/v1/commands/validate accepted a new sequence/insert WaitForTimeSpan command as dryRun=true; before/after configuration revision/tree identical and no operation created. No actual production sequence edit was used as a deployment test. Anonymous dashboard API remains401 when using the expected Origin/client headers; default urllib UA is blocked earlier403. Four-digit dashboard gate preserved. Temporary QA cookie removed after checks.

Evidence under ignored artifacts/2026-10-23-nuc-sequence-editing/: preflight/before/after snapshots, installer/installed manifest, public website snapshot and dry-run validation, WebSocket events, anonymous access check, final verification and live editor screenshot. Browser left on the production editor picker with no command pending. Simulator evidence remains in the separate sequence-editor artifact folder.

### Diagnostics-only cog — October23

Kevin authorized removing legacy generic system controls from the cog. Removed mountControls import/mount/render from monitor bundle. Renamed cog/dialog accessible labels to Diagnostics; removed tools-controls mount, command dropdowns/raw status/screenshot/download section. Dialog now shows visible process-memory history, current N.I.N.A. private bytes and managed-live heap estimate, plus the existing last12 command results. Existing equipment/sequence controls remain. No new polling, commands or backend changes.

Browser bundle and strict TypeScript passed. Local synthetic telemetry browser verified memory numbers/chart and empty command-results state, open/close dialog; hosted browser verified same Diagnostics-only content and native numeric values, no old form. Published matching app.js/index.html to existing LXC wwwroot without restart/config/auth change. SHA256 appa4de7dcf3d7ce14def5184a103361e78d70465aa3f5b28bb5ef9ea6c3459dc3c indexaa798d79c69d5eb5bb7062e37b745f53184eba3dafaf580b23a2e796fe44f2fd. Screenshot /tmp/starfront-diagnostics-published.png. No equipment commands issued; real-equipment validation not performed. Source remains uncommitted.

### Persistent sequence areas and contextual flyout — October23

Kevin authorized always-visible empty Start/Targets/End sections and a contextual equipment submenu picker. Removed the monitor renderer's no-instructions early return; advanced area rows render even when empty. Missing advanced area telemetry produces display-only placeholder rows (not editing IDs). Replaced editor category/instruction dropdowns with an anchored two-column flyout: hover/click categories, arrow keys, Escape/focus return, parent/position label, filtered host insertKinds/catalog. Targets allows target-compatible DSO only; conditions/triggers use their own attachment context. Clamped menu to viewport, scrollable lists, closes on changed revision, lost readiness, outline scrolling and outside click. Existing selected-item parameters and revision/pending guards retained.

Strict TypeScript/browser bundle passed. Actual Parallels0.4.16 editor: Targets flyout showed only DSO; Start offered Camera/Filter Wheel/Timing/groups; Escape returned focus. Camera→Warm camera inserted under Start at index1, operation1a1db74d-4303-4ec8-a717-edba7d969ccd succeeded and authoritative native tree confirmed WarmCamera/Start; editor selected its ramp field afterward. Sequence remained stopped and no camera warming/execution occurred. Read-only projection of actual Parallels root/three areas verified all three headings with zero instructions. No production insertion or equipment command. Published only app.js to existing LXC origin, matching SHA25696da0104093a4ec5a57a438fcd53edaa148d908824b6303fcf8eca86e3504131. Hosted dashboard verified actual empty Start/Targets/End; production editing remained disabled during offline telemetry. Screenshot /tmp/starfront-sequence-camera-flyout.png shows simulated camera submenu. Temporary acceptance servers stopped. Source remains uncommitted.

### Header label and roof diagnosis — October23

Kevin requested removal of the generic Observatory session header text. Removed initial markup text and fallback; real target names still appear when available. Strict TS/bundle passed, published app.js/index.html to current gateway and hosted browser verified generic label absent. Screenshot /tmp/starfront-header-cleaned.png.

Read-only installed CLI doctor/status verified live native bridge/plugin0.4.16 equipment environment; selected safety driver ASCOM.GenericFile.SafetyMonitor (Generic File SafetyMonitor), connected=false, name=null, default isSafe=false. This disconnected value cannot establish roof closed. Website transport separately reports offline/stale. No safety connect command or other real-equipment action issued. Reported root cause and retained Roof unknown rather than fabricating closed status.

### Sequence state icons — October23

Kevin requested no Pending labels and visual execution status. Pending/unknown/skipped/disabled now have blank status marks; running/waiting use an animated ring, successful completion green check, failed red ×. Removed duplicate per-row state text; empty containers show Empty. Accessible icon labels preserve Running/Waiting/Successfully completed/Error; source status remains in row tooltip. Reduced-motion disables animation and offline transport pauses it.

Strict TypeScript/browser build passed. Read-only test projection derived from Parallels telemetry covered all five execution states and empty areas; browser visually verified blank pending, both spinners, green check, red × and Empty labels. No acquisition/sequence/equipment command was issued. Published app.js/styles.css only to LXC; matching SHA256 apped11abfb3a3d8f98a979415a4597f2420a86425f29dfaf2d7eb775375534122b styles08d193d89ae0e4345bf942ea3b7d977f5251a5f8b0e6882ab244f4c434cdba51. Hosted browser verified all three actual empty areas show Empty without Pending. Visual test screenshot /tmp/starfront-sequence-state-icons.png. Temporary test server stopped; source uncommitted.

### Inline sequence insertion — October23

Kevin requested insertion directly in the main panel without Edit sequence. Removed that launch button. Added hover/focus highlight lines below supported containers and before/between/after direct children; each line captures parent ID/index and opens the contextual category/submenu picker in a top-layer popover. Targets lines only offer target-compatible entries. Conditions/triggers retain attachment-specific contexts. Existing supported step labels now open compact Step settings, retaining typed edits/stale-draft protections. Main insertion lines are keyboard accessible and disabled when disconnected/busy. DOM decoration is limited to changed tree instances; guard disabled states change only when necessary, without new timers/polling.

Strict TypeScript/bundle pass. Actual Parallels browser main End line→Timing→Wait operation47e0d692-e4c0-4aeb-9107-fbef0b9741bd succeeded. Native End children exactly WaitForTimeSpan and sequenceRunning=false. No exposure or other equipment action. Direct Targets line displayed only DSO; clicking inserted Wait opened its settings without an editor tree. Published only app.js; matching local/remote SHA2563d4e9f12af4ad9b8569f66f4615d498b3733a531aff40c2388477f07a9690ee7. Hosted browser reconnected Live; actual empty Start/Targets/End show inline controls, no Edit sequence button. Opened real Start picker for verification only, dismissed with Escape; no observatory insert/edit/sequence command. Screenshot /tmp/starfront-inline-sequence-published.png. Acceptance server stopped; source remains uncommitted.

### Expandable sequence workspace and compact area titles — October23

Kevin requested an upper-right expand/restore control on Sequence, room for right-side flyouts, and left-aligned Start/Targets/End with a small expansion gutter. Added dynamic sequence-expand button (outward/inward arrows, accessible label/state), viewport overlay preserving the same tree, scroll and collapse state, background inert while expanded, Escape restore when no menu/dialog owns Escape. Expanded desktop tree uses a left lane, with contextual picker placed beside its insertion line when room permits; narrow screens retain viewport-clamped placement. Layout toggle closes existing picker. All three area headers now use one14px chevron gutter, no reserved status-mark column, truncated titles, and expandable/collapsible behavior even when empty. No new timer/polling/backend changes.

Strict TypeScript/bundle passed. Parallels browser verified expand, side-by-side Camera submenu, section collapse and restore with equipment dashboard available afterward, no commands sent. Published only app.js/styles.css, matching hashes app6ac6377ed53ae2053c093c5c0ab8c32ed663a69ed7ad748a45adcd4e979d11d9 styles3f2495264ab9b94560fdcfb835251532825d6fded7f4d83ad26938e6a2de0586. Actual hosted browser expanded empty sequence, opened Start picker with Camera submenu and compact titles, dismissed and restored docked panel; no production edits/commands. Screenshot /tmp/starfront-expanded-sequence-published.png. Test server stopped. Source remains uncommitted.

### Graphical sequencer, DSO planning header, complete host catalog and structural editing — October23, 0.4.17.0

Kevin approved the ImageGen compact loop/condition/trigger design, then the DSO header concept, then explicitly requested duplicate/delete icons and drag-and-drop reorder. Implemented these in the existing C#/TypeScript stack. Fullscreen uses compact sequence rows, loop rails, exit conditions, scoped watcher cards and DSO coordinate/rotation controls plus calculated altitude/twilight/rise/transit/set chart. Docked view retains names/status. No pending circles. Coordinates open the existing guarded editor; night selection is presentation-only. Site telemetry uses the active profile and NINA host time zone. New local web/package.json/bun.lock pins Astronomy Engine2.1.19; chart and night-selection caches cap32 entries and charts are only built expanded. No new polling/timers/history. The initial dependency install accidentally resolved the parent manifest; restored its original dependency versions from its untouched package-lock and isolated the dependency under web/.

Host factory discovery replaces the17-entry list; separate sequenceCatalog topic, opaque third-party IDs, bounded60KiB with explicit incomplete error. Parallels with actual Sequencer Powerups loaded reports all156 entries (~30KiB), including Expressions/variables and plugin conditions/triggers. Plugin parameter editing remains explicitly unsupported/read-only. Duplicate/delete/move use root/revision/stopped guards, native clone/attachment operations, immutable structural protections, cycle/depth/target/subtree bounds and restoration on failed move attachment. Pointer drag uses stationary slots; Alt+Up/Down also works. Conditions/triggers have individual insertion/drop slots. Long picker categories scroll independently, fixing clipping discovered with the expanded catalog.

Full scripts/build.sh passed:79 core tests, astronomy and observation tests, all builds, CLI smoke and service lifecycle. Final plugin0.4.17 loaded in the pinned Parallels profile cd432469-2b6e-442c-987e-78504975ff3e. Simulator profile's initially unset site (0,0) was explicitly configured to an approximate Texas TEST site31N/100W/600m, labeled accordingly. Added Sequencer Powerups only to this VM for catalog verification. No production device settings changed. Catalog integration verified built-in/Powerups insertion and DSO settings. Structural integration passed16 checks including deep clone values/new IDs, same/cross-parent moves, subtree deletion and stale/cycle/structural/unique-condition rejection. Browser duplicate, keyboard move, pointer drag and delete each confirmed native ID/order readback. Date change recalculated chart without changing revision; coordinates opened typed editor. No acquisition/sequence run/equipment connection in this increment. Final VM stopped, camera/guider disconnected, test tree loaded.

Package artifacts/2026-10-23-sequence-catalog/StarfrontControl-0.4.17.0.zip is437461bytes, SHA25655b960abc75278e68f1e2f01c39040672cef9eee05ea92ca26ee044545187c0d. Installed plugin DLL SHA25669cf2522c2ae2e931433c9803c32d7530657bc1703a600d04b8a9b54b4d2c9af; core5f230f37cb75396bd77dc20afe7bcbc9b569943e5f645d6d5eafe793772cb871. Evidence: catalog-with-powerups.json, verified-tree.json, catalog-command-results.json, structure-command-results.json, browser-* readbacks, expanded-target-panel.png and powerups-picker.png. Temporary1948 install server stopped. Local simulator acceptance server1937 retained for Kevin's open preview; it pins environment/profile and only relays explicit sequence editing actions, never equipment or start actions.

Published only wwwroot/app.js/styles.css to existing LXC gateway, no restart/config/auth changes. Matching local/origin hashes app0bdf8d4fdbd783ad7adffd69c9b329563772d0ca1bc190d57c34f4802250fe09 and stylesafea50019a6732a88d507423416e10c15863f2684c1185b33713298997a6d46b. Observatory plugin remains0.4.16; new catalog/site/structural controls become available there only after separately authorized0.4.17 installation/restart. Frontend retains legacy insertion catalog fallback and hides structural icons without new metadata. No real observatory commands issued. Source remains uncommitted; preserve unrelated edits.
Public unauthenticated fetch still reaches the existing PIN gate and returns401 for protected app.js, as expected. Publication is hash-verified at the authenticated LXC origin; this follow-up did not authenticate a new public browser session or claim a fresh hosted-render check. The user-facing localhost1937 simulator view is visually verified and retained.

### Explicit sequence-managed setup start — October23, 0.4.18.0

Kevin authorized adding the start option, testing disconnected devices with connection instructions first in Parallels, and installation/restart on NUC plus dashboard publication. Implemented optional boolean sequence/start skipValidation (default false), forwarded to NINA StartAdvancedSequence. Dashboard confirmation shows unchecked Start using sequence setup only when host catalog supports it, explains that all startup validation warnings are bypassed and equipment must be connected by the sequence. Checkbox resets each opening; normal Start/callers unchanged. Existing stale/session/sequence/active-operation/auth/profile/selected-device guards retained. Added only built-in ConnectEquipment/DisconnectEquipment to simulator execution type allowlist. No automatic connections or retries outside the sequence.

Full scripts/build.sh passed:84 core tests,11 astronomy/observation tests, all builds, CLI smoke and service lifecycle. Actual dashboard against Parallels0.4.18 started with camera/mount disconnected; ConnectEquipment Camera then Mount finished before1s TakeExposure. Start operation303f69f4-3f1b-4ba3-a67c-c3edaa2ca6fa succeeded in about7s, exposureCount1 and actual image/preview metadata received. No blocking startup popup. Reopened confirmation verified checkbox reset unchecked; cancel issued no command; browser error log empty. Final simulator stopped/camera+mount disconnected. Owned installer/gateway/Alpaca helpers stopped after verification. PHD2 was launched in isolated simulator only.

Installed exact hash-verified five-file0.4.18 package on NUC via graceful interactive close/start, oldPID6828→11444, control configuration unchanged. Production was stopped before update. Coordinated with Tonights Planning (2): preserved October23 file SHA25620c805495f2f27658e6ddf4e4df22a75200102f2016e2659014944bc00a83395 and reloaded2026-10-23.json with operation133789a7-789f-4ae1-aaff-f7d2ed3dfee6 succeeded. Final NUC equipment environment0.4.18, sequence stopped, camera/mount/focuser/filterWheel/rotator/guider disconnected. No real sequence start, acquisition or equipment connection. Existing production tree exceeds58KiB projection bound (140 projected nodes with richer0.4.17 metadata versus149 before); matching projected prefix verified, full loaded file unchanged; dashboard explicitly labels partial sequence and disables editing. This is a telemetry limit, not file truncation. No claim of live-equipment execution validation.

Published only current app.js to existing LXC gateway atomically, matching SHA25646f9246f8c1a79051f55a39a4924ea0f4a668ea07e351a0bd258243655b561cc. Public authenticated assets/status verified and live browser Start confirmation shows new option unchecked; canceled without submitting. No gateway/auth/config changes. Retained current graphical sequencer/catalog/structural features from verified0.4.17. Evidence/package/manifest/before-after runtime snapshots/screenshots under ignored artifacts/2026-10-23-sequence-setup-start. Source remains uncommitted.

### Deployment preflight reconciliation — October23, 16:04 CDT

Kevin requested deployment and then asked whether the plugin was updated. Read-only preflight found a newer0.4.18 already running on the NUC (PID11444/session2), with matching DLL hashes from artifacts/2026-10-23-sequence-setup-start/package.json. Do not downgrade to0.4.17 or overwrite that concurrent work. New full catalog/site/canStructure telemetry is live;185 catalog entries, complete=true. The loaded production sequence is stopped, but its projection contains140nodes with truncated=true and editing.supported/canEdit=false, consistent with the58KiB serialized node budget. This remains a known limitation for the full production sequence; no remediation/deployment, sequence edits, equipment commands or restart performed in this preflight. Evidence artifacts/2026-10-23-nuc-0417/before.json and current-installed-0418.json. User informed that update is already installed and editing is blocked for the oversized tree.

### Detailed instruction rows — October23, 0.4.19.0

Kevin requested equipment-specific labels and visible instruction options, then explicitly authorized deployment. Added pure shared TypeScript sequence-presentation formatter for primary names, labelled settings, full expanded details, compact docked summaries and matching upcoming/current summaries. Connections show the selected equipment. Exposure shows fractional seconds, gain, offset, image type and binning; compound sets retain settings alongside repeat badges. Cooling/ramp, Powerups cooling expressions, tracking mode, fixed/twilight waits, target-centered coordinates, calibration flags, filter names and notification text are shown from actual values. Unknown values are not invented. Read-only bounded SequenceStepDetails additions never evaluate expressions or expose webhook URLs. Optional null edit-field metadata is omitted to reduce payload overhead; existing58KiB cap retained.

Full build passed87 core tests,20 TypeScript astronomy/presentation/observation tests, all three builds, CLI/service lifecycle checks. Pinned Parallels0.4.19 native telemetry test verified actual target-10C/ramp5min, exposure0.025s/gain100/offset50/bin1x1, tracking/calibration/time metadata and omission of unused nulls. No simulator sequence/exposure/equipment operation was executed. Regression found boxed short binning values; added scalar support and repeated simulator installation/readback successfully. Local preview1937 retained.

Deployment authorized by Kevin. NUC live preflight stopped/no exposure/operations; devices had become connected from a separate sequence start/stop while development continued, but cooler off and tracking off. Announced restart effects. First install preflight conservatively rejected because PowerShell Invoke-RestMethod returned text; fixed explicit JSON parsing and repeated all guards without weakening them. Graceful restart/install verified all five files against exact simulator-tested0.4.19 manifest and unchanged control.json. New NINAPID14792; reloaded2026-10-23.json via successful recorded operation, never started it. Saved file SHA20c805495f2f27658e6ddf4e4df22a75200102f2016e2659014944bc00a83395 unchanged. Real native readback and authenticated hosted browser confirm detailed connection/cooling-expression/tracking/time/center/exposure/filter/message rows. Existing production projection still140nodes/truncated=true with editing disabled; no claim this independent limit is fixed.

Published JS/CSS only to existing LXC, hashes app2bd8e23236bbc75d1542e269c151f1152625ae763d719041e3f327a2c551be55, styles2abb09f25ed6f780f3f9413d53c77027e6db8976353b7c42b06d22f893168470. No gateway/auth/config changes or equipment commands. Temporary1948 installer server stopped. Evidence/package/scripts/readbacks in ignored artifacts/2026-10-23-sequence-row-details. User then requested ImageGen redesign and consistent theming for parameter dialogs; that UI work is next, with existing deployment authorization retained.

### ImageGen parameter inspectors and simplified labels — October23

Kevin requested a better ImageGen-designed parameter dialog and a shared theme for other additional settings. ImageGen produced a compact navy/teal inspector with icon/title, aligned rows, inset numeric steppers, exposure presets, row apply checks and Done. Implemented reusable web/parameter-control.ts for numeric/string/choice/boolean fields; applied shared parameter-dialog styling to all sequence editors, equipment dialogs and command confirmations. Retained per-field guarded submissions; presets/steppers only change drafts. Apply checks disable for unchanged fields. No backend change/restart, new timer or polling. Existing API/root/revision/busy guards retained. Partial-sequence editor now states Read only · partial sequence instead of incorrectly asking to stop an already stopped sequence.

Kevin then explicitly removed the redundant Sequence step eyebrow and diamond/Loaded sequence footer text. Both are absent; only title, controls and Done remain. ImageGen reference: /Users/kevinmcquown/.codex/generated_images/01a16464-0b7c-7851-a3ce-d74eb91905a6/exec-6776323a-ae9d-44a2-adce-8c44f49d8b7c.png. These two removals intentionally supersede that concept.

Strict TypeScript/browser build and15 current presentation/astronomy tests passed. Parallels browser verified preset draft changes do not alter native revision; row Apply changed exposure0.025→30s with gain100/offset50 unchanged, then restored0.025. Unsaved gain101 draft survived another-client Wait30→31 edit, with all Apply controls disabled and a stable Refresh button; Refresh restored authoritative gain100. Restored wait30. Filter selection Green draft closed via Done without modifying native Red. Cooling, filter and camera equipment layouts visually verified. Simulator remains stopped and no equipment/capture/sequence-run commands were issued. Real hosted browser verified new dialog/labels with production read-only partial-sequence guard; no production setting edit.

Published only JS/CSS to existing gateway, no restart/auth/config changes. Origin hashes match app098833b1186f282eccf4540e6f8d564a553cdea6d71c7d29a99b8212cfd24c76, stylese8b54711e417b982fc2ae346e132920c85090414ea43de5bc81000168206d1cf. Evidence screenshots/before/after native snapshots in ignored artifacts/2026-10-23-parameter-inspectors/. Local1937 preview retained open at final exposure inspector. Existing large production sequence truncation remains unchanged. Source uncommitted; preserve concurrent presentation/cooling changes.

### iOS Rig equipment icon connection colors — October23

Kevin requested the existing Rig card equipment symbols gray when disconnected and green when connected, then phone installation. Scoped change in Starfront Operations/apps/StarfrontIOS/Sources/RigView.swift: original symbols/layout retained; fresh .connected uses adaptive RoofOpen green, all other states Color.gray. Existing textual connection state/freshness guards unchanged. Build number2026102305. Swift contracts, XcodeBuildMCP simulator build/run and signed-device build passed. Actual iOS simulator Rig visually verified gray disconnected cards and green camera after connecting only Parallels thermal camera; camera then disconnected to restore initial state. No real-equipment command/plugin deployment. Signed build installed successfully on communicator, containerB9E86BA8-F59B-4B5C-9411-D86742D3A7FF. Evidence/launch receipt in artifacts/2026-10-23-ios-rig-colors.
Phone automatic launch was denied because communicator was locked. Installation succeeded; user can open the updated app after unlocking. Physical-phone rendered colors were not independently observed.

### Sequence Reset button — October23

Kevin requested Reset beside Start/Stop and deployment. Added dashboard button, existing sequence/reset command wiring, stopped/fresh/pending/acquisition guards, reset-specific confirmation text and sequence-change check between confirmation and submission. Uses installed plugin0.4.19 ResetAll endpoint; no backend update or NINA restart. Strict TypeScript/browser bundle passed. Actual Parallels dashboard Reset→Confirm succeeded as operation436b061a-174d-4673-8ad8-4ea5631b766f; native sequence remained stopped and all nodes/settings unchanged (the test tree was already pending, so this is idempotent reset verification, not a completed-run reset test). No capture/equipment command issued.

Published app.js/index.html to existing gateway; hashes31cafd3814b13baa6f3a345642919c41b015edf3ae29c5dd93fcf26fb2cc32bf and0c2aa6dc8fbc512a05bfd4f2c14939e43a9762e02dc7211b8ceaf177319d26c6 match origin. Hosted authenticated dashboard visibly shows Reset alongside Start/Stop, disabled correctly while telemetry is offline/stale; browser errors/warnings empty. No production reset/start issued. Existing projection limit unchanged. Screenshot /tmp/starfront-reset-deployed.png; simulation snapshots /tmp/starfront-reset-before.json and /tmp/starfront-reset-after.json. Browser validation used available CUA in-app controls; no separate Browser plugin listed. Source remains uncommitted.

### Responsive target planning in expanded and docked sequence — October23

Kevin reported stretched full-screen target charts and requested the same readable target panel in the docked right column. Removed expanded-only creation/hiding. Target coordinates/date now use container-responsive layout. Altitude SVG redraws at actual observed content width with fixed-size labels, reduced time tick density and separate rise/meridian/set labels in compact mode. Shared ResizeObserver releases old target elements on every sequence-tree replacement and ignores unchanged widths; astronomy calculations retain bounded cache. No polling or plugin change.

Strict TypeScript/build and15 astronomy/presentation tests pass. Actual Parallels stopped profile received one layout-only M31 DSO target with RA0.7123h, Dec41.269deg, rotation22.5deg under Targets; no equipment or execution commands. Initial Name edit was rejected because DSO schema uses TargetName; corrected using native schema, no duplicate target. Browser verified docked chart viewBox370x180 at369.86px and expanded1105x230 at1105px, normal text proportions, readable coordinate/twilight labels, expand/restore, no browser warnings/errors. Observer cleanup inspected in source; no long memory observation claimed. Evidence /tmp/starfront-target-docked.png and /tmp/starfront-target-expanded.png. Local1937 preview left showing target in right panel, simulator stopped. This increment is local only; no production deployment or NUC change performed.

### Responsive target panel deployment — October23

Kevin explicitly requested deployment. Published only the verified app.js/styles.css to existing LXC gateway using staged .new files and rename. Matching local/origin hashes appce15e3b4a3eacd704c4bd5ce86468369f14d1bc81bfdcc64a02c8d546fe563d7, stylesa575e55460fb04807f05da61c86dbbc0c2946c72613c8611474c8c92a00bf9f3. Hosted authenticated dashboard renders all three production DSO headers/charts in docked mode; each SVG viewBox404x180 matches404.20px rendered width. Browser warning/error log empty. Only presentation collapse used to expose first target; no production commands, edits, plugin update, NINA restart or gateway config/auth changes. Website transport showed Offline/cached during verification; existing controls correctly disabled. Evidence /tmp/starfront-target-deployed.png. Prior simulator full-screen/docked interaction and15 passing checks remain applicable.

### Remove acquisition/command banner — October23

Kevin requested removal of the visible “Acquisition or another command is active.” message and redeployment. Suppressed only that sequence-action-hint message; commandReason guards and disabled controls/tooltips remain intact. Other connection/startup feedback preserved. Strict TypeScript and browser bundle passed. Published only app.js to existing gateway; local/origin SHA256f3b6805a3e9312da5274b95cf94eb54ec1013259ab51ef8046a8b8f0cb0b0bcd match. No equipment/sequence commands or service/NINA restarts.

### Refresh-persistent cooling graph samples — October23

Kevin requested preserving enough recent cooling graph data across website refresh. ThermalHistory now optionally persists one bounded context in localStorage (same origin): maximum720 samples, one-hour wall-clock retention, original sensor timestamps and gaps. Context includes plugin session/profile/selected-camera ID; samples never transfer across those contexts. Restore validates bounded JSON, field types/ranges, monotonic timestamps and expiry/future bounds. Unavailable/quota/corrupt storage falls back to in-memory telemetry; no new polling/timers/server data retention. Preview and hosted origins naturally remain separate. A refresh restores only samples already observed in that browser; it does not backfill while browser is closed or survive a new NINA session.

Strict TypeScript/browser build and21 tests pass, including6 dedicated persistence tests for refresh/deduplication, stale telemetry, wall-clock expiry, context separation,720 cap/gaps, corrupt/blocked storage and invalid cached data. Actual Parallels pinned thermal simulator connected only for test, no cooler/capture/sequence command: collected3 live15C/0% readings; browser temperature SVG path before and immediately after refresh was identical (' M110.3,29.1 L180.0,29.1 L250.1,29.1'), no browser warnings/errors. Simulator camera disconnected afterward to restore initial state. Screenshot /tmp/starfront-cooling-refresh.png. Published only app.js; matching local/origin SHA256b75061db5a637072f52baef1c9a2621936853d579045ad39009b4fd07eb994a1. Public urllib asset probe returned403; public browser verification pending. No NUC/plugin restart, real-equipment command, gateway auth/config change. Production history begins accumulating after loading this update; discarded pre-update samples cannot be recovered.

### Telescope pointing graphic and mount control panel — October23

Kevin requested a scope graphic on the connected ASI mount card, larger popup with RA/Dec slew and Home, visibly following reported motion. Added shared red refractor/equatorial-mount SVG schematic driven by live altitude/azimuth using an oblique horizon projection; mini and panel consume identical telemetry. Offline/disconnected values are suppressed/desaturated; reported Slewing/Tracking/Home/Parked/Stopped status displayed. Existing mount popup now shows graphic plus precise current RA/Dec/Alt/Az, prefilled RA(hours)/Dec(degrees) inputs, existing confirmed Slew/Home/Stop controls. Kept profile/session/command/acquisition guards and existing plugin endpoint contracts. This is a schematic pointing view, not a calibrated physical pier-side/collision model. No plugin change or new polling/timers.

Actual isolated Parallels profile cd432469-2b6e-442c-987e-78504975ff3e, exact official local OmniSim mount5db8ce88-2b16-48b8-92bd-aa2ec5e59c43: restored supported.NET8 helper on10.211.55.2:32323. Native explicit Connect lazily rediscovered offline cached device; succeeded200cfa11-80e0-4da9-bb93-19e768901518. Browser RA1h/Dec35 slew8b881c6d-fe60-4fcb-ac93-0b872e0ac03e succeeded; native intermediate slewing positions recorded, finished apparent RA~1.025h/Dec35.148deg (NINA transforms entered coordinates). Both DOM graphics changed from38.921/180 to70.280/137.657. Browser Home959bd263-ea3e-43a3-ab17-30c381339fb1 succeeded, reported altitude51.07861/az0, atHome=true, slewing=false. Graphics match and show Home. Sequence remained stopped, camera disconnected. Mount disconnected after test. No production commands or deployment of this increment.

Strict TypeScript/build and23 tests pass, including2 mount projection/state tests and6 cooling-persistence tests. Final browser errors/warnings empty. Evidence /tmp/starfront-mount-controls-final.png, /tmp/starfront-mount-home-motion.png, /tmp/starfront-mount-slew-samples.json, /tmp/starfront-mount-home-samples.json and /tmp/starfront-mount-verified.json. Local1937 preview contains updated mount UI. Source remains uncommitted. Cooling persistence was previously published separately; current local bundle also contains the not-yet-deployed mount changes.

### iOS reversible warming/cooling — October23, 0.4.20.0 / iOS2026102306

Kevin authorized safe mode switching, actual Parallels tests, NUC plugin installation/restart and phone installation, with sequence left stopped. Added parameterless temperature-only cancelTemperature interrupt. UI-thread ownedActions tracks only currently executing operations; cancellation rejects no owner/nonthermal owner/acquisition, cancels existing thermal token and waits for owner release. Exposure abort is explicitly excluded for this action. Existing per-device interrupt reservation, selected-camera/profile/allowlist and initialized-sequence guards remain. Does not alter NINA's native thermal algorithm.

App Camera view now keeps the actual active mode outlined, with Cooling/Warming and Cooler off after actual coolerOn=false. Opposite button is capability-gated and stays available during thermal ramps. Monitor performs explicit cancellation, waits for its succeeded result and old ramp terminal/fresh owner-free state, then issues ordinary replacement ramp after identity/acquisition revalidation. Reject/unknown/timeout/connection or device change sends no replacement. Transition disables additional controls; bounded35second wait observes push state only, no new HTTP polling. Standard60second ramp parameters unchanged.

88 core tests pass, updated Swift contracts cover both directions/unsupported host/capture/sequence guards and rejected/lost cancellation responses with no replacement. Simulator and signed-device builds pass (existing UIRequiresFullScreen warning only for iOS). Actual iOS simulator on Parallels thermal camera: Cool8d3b6384-8464-40ff-9b4d-2d58381cb0f4→cancel8a5e6ba1-dc06-49dd-bab7-479e725c39d1→Warm0aef863a-0a47-4a95-ae63-bde866be83bd→cancel4f961980-056d-48f8-a360-76d8f0e9fe5d→Cool9f3f265c-ce35-48a9-a84e-95f3fa8c5c44. Both old ramps cancelled, both cancellations succeeded, replacement accepted strictly after old completion. Native temperatures/power and actual highlighted app controls independently verified. Screenshots and runtime snapshots in ignored artifacts/2026-10-23-thermal-switch. Simulation-only extra cleanup/negative checks recorded separately there.

Installed exact five simulator-tested package files on NUC0.4.20 by graceful interactive restart, PID3076, hashes verified and control.json unchanged. Preflight sequence/exposure/operations idle. Reload2026-10-23.json operation61b96695-c6a4-498c-b384-179c3350acd1 succeeded; all140 projected types/names/details match pre-restart and saved file SHA20c805495f2f27658e6ddf4e4df22a75200102f2016e2659014944bc00a83395 unchanged. Final no operations/exposure, sequence stopped. Restart leaves equipment disconnected; no production thermal/control/exposure command issued. Existing partial-sequence projection limit persists. No web assets published, preserving concurrent unshipped mount-dashboard work.

Signed app2026102306 installed on communicator (container245CDABF-EB6C-4638-B126-3A42FA362439) and devicectl launch succeeded PID67728. Saved pairing/settings untouched. Physical-phone thermal commands not tested against real equipment. Evidence package/manifest/install/launch/native before-after snapshots and Swift logs under artifacts/2026-10-23-thermal-switch. Source remains uncommitted; preserve existing unrelated edits.

### Mount Connect affordance and background focus cleanup — October23

Kevin reported faint/unavailable Connect in larger mount dialog and lingering card highlight. Moved connection action beside status at top of all equipment dialogs; enabled Connect is high-contrast teal with explicit hover/disabled styling and reason tooltip. Pointer opens blur the background opener before showModal, preventing restored mouse focus outlines; keyboard opens preserve normal focus restoration. Equipment title text no longer accidentally selects. No command guards removed.

Actual reproduction initially showed enabled but low-contrast Connect, with previously stopped OmniSim helper unreachable. Restored helper. Another thread independently restarted simulation plugin to0.4.20 and ran camera Warm/capture; temporary preview's old upstream socket was stale. Restarted preview helper and added local-harness-only socket reconnect after close, preserving bounded single socket. Waited for other commands to finish without interrupting. Native simulator catalog still had offline cached mount after host restart; explicit exact-ID Connect lazily rediscovered official allowlisted mount, then Disconnect restored pretest state. Actual new dialog Connect4c45fc54-17c5-4fb1-adba-8c1ce80bd628 succeeded, visible Connected/red telescope graphic and matching live pointing; no browser warning/errors. Pointer close gives BODY focus and zero background focus-visible elements; keyboard Enter/close restores Mount controls with visible focus. Strict TypeScript/build and23 tests pass.

Evidence /tmp/starfront-mount-connect-prominent.png and /tmp/starfront-mount-connect-fixed.png. LOCAL ONLY, not deployed. For user interaction leave OmniSim running on10.211.55.2:32323 (exec session9602), preview1937 running with reconnect (session95587), simulated mount connected/tracking off/sequence stopped, camera state owned by separate concurrent test. Do not stop these helpers merely as routine cleanup while user is trying mount UI. No production commands/changes. New NINA sessions can still initially project configured mount as offline until discovery; backend exact-ID connection already supports lazy discovery, but dashboard allowed=false blocks that first connection. This remains a separate catalog/UI eligibility limitation rather than bypassing host guards.

### Deploy telescope graphic and improved Connect dialog — October23

Kevin reported deployed site still showed compass instead of scope. Published the prior simulator-tested mount UI/Connect/focus changes, only app.js/styles.css, using staged rename on existing gateway. Local/origin hashes match appaaf89f5c2eb6a69d8392d714ba2ff75f8e044b76879fa47fc1575800d1685068 and styles729e07166eb6f0f8a70e1b70d7243d1866870112e74094ba6daa36e35898e14a. Existing concurrent0.4.20 plugin/thermal work preserved, no backend install/restart/config change.

Actual hosted browser reports Live and ASI Mount already Connected/Home from independently existing telemetry. Verified scope graphic on card and larger Mount dialog, top connection control, RA/Dec fields and Home/Slew controls. Only opened dialog; no equipment connection/disconnection, movement, tracking or sequence command issued. Browser error/warning log empty. Screenshot /tmp/starfront-telescope-deployed.png. Initial page-navigation tool timeout recovered by acquiring created tab6; website itself rendered normally. New live tab left as deliverable for user.

### iOS mount pointing popup and controls — October23, build2026102307

Kevin requested the website-like ASI mount popup in iOS. Dedicated MountDetailView now opens from existing Rig Mount card. Native Canvas reproduces web horizon projection with red scope/mount/compass and reported altitude/azimuth, fresh coordinates and Slewing/Home/Parked/Tracking/Stopped state; unavailable position is suppressed. Compact scrollable cards include RA(hours)/Dec(degrees) target and confirmed Slew/Home/Park/Unpark/tracking on/off; immediate Stop allowed only for single Starfront-owned motion and never sequence/exposure. Existing Connect/Disconnect preserved. ControlIntent encodes tracking.enabled as Boolean, numeric slew payload strictly bounded; monitor retains fresh connection/session/exact device/profile/allowlist/capability/global operation guards. Added mount disconnected motion guard. No new plugin endpoints, network polling or retained history; no NINA restart/deployment or real hardware commands. Graphic is a horizon schematic, not a physical pier/collision model.

Swift contracts pass including projection/cardinals/zenith, coordinate bounds/NaN/extra fields, stale/missing position, Boolean tracking encoding, no missing Boolean, Stop ownership/acquisition guard, disconnected and changed device rejection, plus all prior thermal/lifecycle/control checks. Initial new test incorrectly supplied a native frame to a gateway-mode fake; fixed the envelope and reran successfully. Simulator and signed-device builds pass. Initial Form layout was too tall during visual verification; final uses compact ScrollView/cards, matching camera detail structure, with primary actions visible. No claimed performance history beyond existing bounded telemetry.

Actual iOS simulator on existing isolated Parallels profile: selected official OmniSim mount5db8ce88-2b16-48b8-92bd-aa2ec5e59c43, confirmed simulation/stopped. UI RA1h/Dec0 slew79314234-688c-45d7-a08b-072be26aac8c succeeded, native intermediate coordinates + Slewing and graphic orientation observed; final apparent RA1.02335h/Dec0.14742 matches NINA coordinate transformation. UI Home4575946d-764c-4346-982a-d38664d97fed succeeded atalt51.07861/az0/atHome=true, graphic matched. UI tracking on/off both Boolean commands succeeded, Park/Unpark succeeded with actual flags, UI Stop succeeded and cancelled subsequent owned slew. Native operation readbacks and screenshots in ignored artifacts/2026-10-23-ios-mount. Simulator cleanup restores original connected/unparked/tracking-off38.92139alt/180az reference; no sequence run/exposure.

App2026102307 installed on communicator (container4BC89D43-6302-4F39-936F-7767A711177E), devicectl launch succeeded. Saved pairing preserved. Physical-phone mount controls not exercised against production. No website assets touched; source remains uncommitted, preserving concurrent work.

### Dynamic SE/SW presets and orbitable 3D views on web/iOS — October23

Kevin requested SE/SW halfway-up test points, then questioned polar-home coordinates against a fisheye webcam image, then explicitly requested drag-to-orbit on both website and phone. Added website presets at az135/225 and apparent altitude45. Astronomy Engine horizontal→J2000 transform uses active profile site/current click time; existing mount/slew expects Epoch.J2000. Missing site disables presets. Existing command confirmation and host/sequence/device guards remain. No new endpoint. Home remains separate from Reset View. Presets currently WEBSITE ONLY; the explicit phone scope of the follow-up was orbitable graphic, with its existing manual RA/Dec controls preserved.

Read-only production diagnosis: ASI Mount Home=true, Dec90, altitude31.1938889, az360, latitude31.194, RA~11.601h. Values consistently describe north celestial pole; RA at exact pole is degenerate. The old view projected all due-north tube directions vertically onscreen; this was a display ambiguity, not evidence the driver numbers were wrong. NASA reference confirms pole altitude equals site latitude. Fisheye camera extrinsics/physical pier pose remain uncalibrated.

Web and native SwiftUI now use bounded polygon/cylinder 3D schematic meshes with the same orthographic camera basis, depth sorting and red refractor/pier model. Larger panels drag horizontally360deg and vertically5–80deg to orbit, preserving camera viewpoint across live telemetry. Reset View restores135deg/20deg. Web arrow keys and iOS accessibility adjustment rotate viewpoint. Telescope optical direction still uses live alt/az; no mount commands originate from orbit/reset handlers. The mechanical body is schematic, not a calibrated pier-side/collision model. No new polling, retained history or animation loop.

Parallels verification: official OmniSim site's prior51.07861/-0.29444/613 location mismatched test profile31/-100/600. Aligned only simulated driver site to profile, retaining old values in /tmp/starfront-mount-preset-original-site.json. Browser SE preset operationdfb1713a-7fa2-4b3b-9bac-239e9415de45 succeeded, settled altitude45.0373/az135.0966; SW c3747f0a-258b-4628-8abf-8a4abaa6d37d succeeded, observed44.9169/225.1263 shortly after completion. Later tracked coordinates changed with time as expected; an erroneous delayed assertion against the initial horizontal point failed and was corrected in interpretation, not masked. Switched simulated tracking off afterward; mount remains connected/stopped at44.0379/226.6977, no active operations/sequence. No real-equipment movement issued.

Web drag changed view135/20→185/32 while pointing continued from telemetry; Reset returned135/20. Actual iOS27 simulator Parallels pairing, Rig→Mount: drag changed accessible view135→215 with unchanged altitude/az, persisted across telemetry, Reset restored135. Default tool taps did not activate iOS27 tab;150ms touch down/up did, then runtime drag worked. Browser simulator mirror was used for inspection and scoped helper cleanup; no other simulator touched.26 web tests, strict TypeScript and full Swift contract suite pass; simulator and signed-device builds pass. Geometry bounds/finite mesh, camera clamps and no-invalid-site slews covered. Evidence /tmp/starfront-web-orbit.png, /tmp/starfront-web-orbit-deployed.png, /tmp/starfront-ios-orbit.jpg.

Deployed app.js/styles.css only to existing website gateway; local/origin SHA256 app1a1806caab5646873b37f06bf266e03ba476c6a0501c366aff626b086e1171df, styles9144c0d1f9a48231ba3352e61581328f9df53f5c7f0cd423f784eaa563f8f6bf match. Actual hosted ASI Mount dialog verified Connected/Home31.194/360, orbit changed camera only, browser error/warning log empty. No plugin restart/config/auth change or real mount command. Prior0.4.20 concurrent work preserved.

Native iOS build2026102308 installed successfully on communicator00008130-001A4D622461401C, containerB93311A0-4C30-4EC1-8E09-E7179B9C8B1B. Automatic launch denied because phone locked; user can open after unlocking. Existing pairing unchanged. Signed build /tmp/starfront-ios-orbit-phone, build log /tmp/starfront-ios-orbit-phone-build.log. Physical-phone gesture rendering not observed; simulator gesture evidence above is separate. Source remains uncommitted; keep preview1937/OmniSim available for user.

### Cross-client sequence Stop and jump to current — October24, 0.4.21.0 / iOS2026102401

Kevin reported Stop disabled for a NINA-started sequence and requested jump to the active/waiting step on web and iOS. Diagnosed production0.4.20: running sequence without a Starfront-owned Start, and a byte-truncated projection omitting the actual instruction. Production was read only throughout; safety reported unsafe/roof closed, but the exact roof-wait/dawn condition was not established from that partial snapshot. By final inspection NUC sequence had stopped independently, still0.4.20, no active operations. No production commands, installation, restart or web deployment performed for this increment.

Plugin0.4.21 allows sequence/stop independent of Start ownership. Existing command authentication, initialized-host check, interrupt reservation and timeout remain. Shared SequenceStop execution cancels any tracked Start, requests native cancellation if running, and waits for BOTH actual NINA runner and any tracked owner before success; idle Stop is harmless. New commands capability externalSequenceStop gates updated clients against old hosts. This change specifically covers sequence Stop; other device interrupt/thermal ownership guards remain unchanged.

Sequence telemetry now obtains exact current IDs from public GetAdvancedSequencerCurrentRunningItems. Scanning/subscriptions are bounded at1024 nodes/depth16; transmitted projection remains256 nodes/58KiB node-array budget. Single serialization per row replaces repeated whole-array serialization. Pruning retains current/running branches, all ancestors and Start/Targets/End; retained children keep valid parents and original ordering. Truncated projections still explicitly disable editing. activeComplete reports whether all reported current IDs fit. An oversized protected branch fails projection explicitly rather than exceed byte limits. No per-viewer polling or unbounded retention added.

Web Current step button expands collapsed ancestors and centers active instruction/trigger/container; Follow remains separate. iOS Sequence has matching toolbar jump and persistent bottom Start/Stop/Reset controls, so Stop remains visible after scrolling. Old hosts show upgrade-required eligibility instead of promising unsupported native-run Stop. iOS pure contracts cover native-run capability without tracked Start, unsupported old host, duplicate Stop and disconnected guards; builds preserve saved pairing.

Validation:96 core tests,29 web tests, strict TypeScript, full build/service+CLI smoke/lifecycle and full Swift contracts pass. Signed iOS2026102401 build succeeds at /tmp/starfront-sequence-control-phone (not installed on phone). Actual final five-file0.4.21 package installed/hash verified in isolated Parallels profile cd432469-2b6e-442c-987e-78504975ff3e. Disposable sequence of180 zero-duration waits plus a600second final wait exercised byte truncation. Exact current late WaitForTimeSpan retained, activeComplete=true,157 transmitted nodes,59653 compact sequence-topic bytes, all three areas retained. Dashboard jump from collapsed Targets found the wait; web Stop f2189daf-1bd9-4fa8-a519-5f00719eec15 succeeded and cancelled native runner/tracked start. A second API-started run was stopped from iOS377d1b16-f229-4621-84f2-0263eead257a, verified native stopped. Final helper build was reinstalled and retested with actual start→wait→Stop plus idle no-owner Stop; restored saved Dashboard-Test.json stopped, equipment disconnected after simulator restart. Parallels native-window input did not activate navigation, so a GUI-started/no-owner RUNNING sequence was not exercised end-to-end; that path has automated native-runner/no-owner tests and actual idle/no-owner acceptance evidence, distinct from cross-client runtime tests.

Evidence/package/manifest/logs/screenshots: ignored artifacts/2026-10-24-sequence-control. Package StarfrontControl-0.4.21.0.zip. Source remains uncommitted across both repos. NUC/plugin and phone deployment remain pending explicit request; NUC was idle at latest read, but recheck before any installation. Local1937 preview remains pointed only at Parallels. No earlier production sequence file changed.

### Production deployment and historical NINA log preservation — October24

Kevin explicitly authorized NUC plugin installation/NINA restart, website deployment and phone update, requesting full sequence-operation evidence before update. Archived all21 retained NINA log files (1,325,013,366 raw bytes), saved2026-10-23.json and pre-update native telemetry BEFORE restarting. ZIP64,655,825bytes SHA256D72B7DE2AA572F3A46B55AA5401C1175B49E0CFB31006E7CE16DB8EB45226788 transferred and every archived file hash verified. After graceful close, also captured final complete old-process3076 log31,953,187bytes SHA256c8596cf51381ffbe7fc92f237d42b7cacef51cee025fd35d1dab963eb7367d8e. Original NUC logs retained. Chronological CSV/JSONL index covers latest full session (Oct2316:54–Oct2406:35 NUC Central local timestamps), includes item/trigger lifecycle and all session warnings/errors with multiline exceptions/source filename/line. Older retained sessions remain raw in ZIP. Index distinguishes Finishing from inferred success, preserves failed-child/ContinueOnError evidence and concurrent ordering ambiguity. No claim that deleted logs were recovered. Initial attempt to index every historical context was unnecessarily large; stopped it, discarded only our partial generated indexes, and produced bounded latest-session index instead. Raw archives unaffected.

Installed exact simulator-tested0.4.21 five-file ZIP (441,742bytes SHA25612dce1afebb492c70916bbb6a6db621ea14ba24819843f6ff0ec29d9fe82d224); every installed size/hash matches manifest, control.json hash unchanged. Graceful interactive scheduled-task close/start; new NINA PID2564. Initial script-file invocation hit Windows execution policy before doing anything; executed identical reviewed contents inline without changing system policy. Subsequent initialization-only preflight briefly saw unknown acquisition state; waited and reverified sequenceRunning=false. Current profile b318e855-d2c5-49c8-ba16-079314e282ea, plugin0.4.21. Reload ce054efe-a6f2-43f3-a4c5-6ea30f47b515 succeeded for2026-10-23.json; saved file SHA20C805495F2F27658E6DDF4E4DF22A75200102F2016E2659014944BC00A83395 unchanged. Left stopped; equipment disconnected by restart, no connect/move/exposure/run command. ExternalSequenceStop capability=true observed natively and through production gateway. No actual production Stop test issued.

Published only app.js with staged rename on existing gateway; live origin authenticated read hash e75c211ebd22e47f9b0b40c8e9ea2248392d48cbac07a4649ec7e7d739bc0888 matches local tested bundle. styles unchanged9144c0d1f9a48231ba3352e61581328f9df53f5c7f0cd423f784eaa563f8f6bf. Gateway connected to0.4.21 stopped host. Public browser still serves preserved four-digit-code gate after old session expired; authenticated public browser controls not reverified without code. No auth/tunnel/config changes.

Signed iOS2026102401 installed on communicator00008130-001A4D622461401C, containerB75D3FB9-3C13-4ECC-AA7E-A0973E5D5989. Launch refused solely because phone locked. Pairing/settings untouched. Physical-phone rendering/control test not claimed; prior simulator tests remain separate. Evidence, archive notes, index, hashes, install/launch reports and native snapshots in ignored artifacts/2026-10-24-production-update. Source remains uncommitted.

## October24 automatic LXC sequence recording

Kevin authorized automatic recording on sequence start/end, with Parallels validation before enabling production. Updated independent Starfront Operations apps/StarfrontRecorder to0.2.0 (no plugin/dashboard change). Private config autoRig=starfront arms one persistent read-only native WebSocket, including idle; application.sequenceRunning=true starts a journal immediately with that full snapshot, and confirmed false saves the terminal snapshot and finishes it. Normal completion and user stop close the recording; manual recordings retain explicit CLI ownership. Manual finish and storage errors suppress restart until a stopped sequence is observed. Network loss preserves the recording with gap markers. NINA session replacement interrupts the old segment; recorder restart recovers interrupted metadata and starts a new segment if execution is already active, with no backfill. Memory/storage limits and no automatic deletion remain unchanged.

Strict TypeScript and14 tests/64 assertions passed, covering repeats, gaps, session replacement, service restart during execution, manual override, storage failure and environment mismatch. Actual compiled Linux recorder ran separately on CT105 against Parallels: natural capture+15s-wait completed and automatically closed recording9299a970-9bf1-42d3-8c5c-bdbd68b3442c (82frames,85records,126307bytes,1JPEG), explicit sequence stop closedf67b4180-676c-4dd3-8304-b14b4b1e1198 (4frames,7records,39411bytes,1JPEG). Both gzip logs reconstructed with contiguous indices, running initial snapshot, stopped terminal snapshot, and preview SHA verified. Simulation returned stopped/reset with camera disconnected as initially observed; separate acceptance recorder stopped and temporary reverse tunnel removed.

Production LXC binary SHA2561c4bec4625f26d95a25e1de6a7ad6fc2cdbb9f7aacfae62f2a4dcf9e4d251c74 deployed through guarded script, enabled services preserved, private config armed for starfront. Verified live connected=true, automation enabled, sequenceRunning=false, active=null, error=null; awaiting next real sequence. No production equipment commands, sequence changes or NINA restart. Recorder remains256MiB process-capped/25%oneCPU; disk~5.9GiB free at deployment. This is short functional validation, not an overnight resource-stability claim. Sanitized receipt: Starfront Operations ops/deployments/2026-10-24-recorder-automatic.json; ignored detailed evidence output/recorder/automatic-acceptance/. Unrelated edits preserved; no commit/push.

## October 24: additive native iOS notifications

Kevin authorized native notifications on Starfront LXC and the phone, then explicitly
required preserving existing functionality. Added NotificationProjection to Core,
PushNotifications to the gateway, scoped /mobile/notifications enrollment/device
routes and an additive observer on the existing shared BridgeConnection. No new
NINA connection/polling or equipment commands. Added Alerts, device-only notification
Keychain storage, native APNs delegate and rich-preview service extension in
Starfront Operations/apps/StarfrontIOS, build2026102402. Rig Keychain identity,
bundle ID and signing team are unchanged.

Created a dedicated topic-specific sandbox APNs key for Starfront under Windy City
Lab using Kevin's authenticated Safari session. No other keys were revoked or
modified. Signing key is0600 outside repositories; LXC configuration is beneath
~/.config/starfront-web/Notifications. Existing service.json and all six deployed
wwwroot assets have byte-identical verification. Previous service retained in
~/.local/share/starfront-web/rollback/native-push-*; only starfront-web.service
was restarted. No Ground Station uninstall, Discord changes or sequence edits.

101 Core tests,2 service tests, existing iOS Swift contracts and isolated HTTP
authentication tests pass. Simulator and signed-device builds pass. Original API
requires strong credentials; notification device credentials cannot reach it.
Live LXC remains connected to pushed telemetry, sequence not running.

Phone installation completed after Kevin unlocked communicator. devicectl
confirmed installed build 2026102402 and successfully launched the app; no
uninstall was issued. Physical APNs delivery remains unverified until
notification permission is granted and enrollment completed with the existing
dashboard code. Simulator screenshots
show the added tab, but automated taps were ineffective even for existing and
system controls, so interaction acceptance is incomplete.

Dated evidence/builds: Starfront Operations/.codex-artifacts/2026-10-24-native-notifications
and /tmp/starfront-ios-notifications. See docs/NATIVE-NOTIFICATIONS.md.


## October 24: Discord migration and Send Notification instruction

Kevin confirmed the test worked; LXC history independently records received-in-app
for the enrolled phone. He then explicitly authorized removing Discord messages
and disabling Ground Station, and asked for an optional custom notification
instruction in our plugin. Current loaded library sequence is 2026-10-23.json.
Removed 24 messages + one Ground Station failure trigger from both root and Targets
copies, cleaned 18 legacy Discord items from six reusable templates (including
Unsafe Management). Exact backups on NUC at
C:\Users\astroadmin\AstroTools\native-notification-migration-20261024\backup.
Ground Station folder moved there intact; active profile automatic image sender
false. Historical other-night exports and encrypted webhook settings retained.
No equipment connected, cooled, moved or exposed; NINA restarted gracefully while
idle. Cleaned sequence reloaded and left stopped; original Texas profile and
seven filter selections verified, no unknown/Discord projected instructions.

New plugin0.4.22.0 exports Send Notification under Starfront Control, literal
1–512 character message and optional latest-preview checkbox. Additive bounded
16-item local outbox feeds the existing pushed snapshot, gateway projection sends
as sequence notices; no extra NINA polling/network wait inside sequence. Existing
Send WebSocket Event unchanged. 103 Core and2 sender tests pass. Parallels host
catalog/clone/run of two identical messages emitted distinct numbered events;
original simulator tree restored, camera remained disconnected. Production five
installed file hashes match tested package4f3d56c169e5ee0c29e5ee2c5a46a241077678e96685e7ca14a3aad437f471a1.
Production native catalog exposes the instruction; no production execution test.

LXC custom-event deployment initially included the wrong Mac apphost and failed
203/EXEC. Restored prior service; framework-dependent Linux package also lacked a global
runtime and was rolled back. Final package is self-contained linux-x64.
Deployment now checks ELF/runtime and authenticated HTTP bridge health before acceptance,
with rollback; private configuration and all six live static assets preserved.
Detailed status/receipts in Starfront Operations .codex-artifacts/2026-10-24-native-notifications/migration
and this repo artifacts/2026-10-24-native-notifications. Changes remain uncommitted.

Final authenticated live LXC check: active, bridge connected, plugin0.4.22.0, sequenceRunning=false, one enrolled phone retained. Final service DLL SHA256 c044e5eb0cf2abd30305d4680082c138307c5f163caf0df778a1e99edf4e4d95; Core DLL matches plugin package0f40361f0daafc8384d68063a0057976c5453a527b19475aae4555646cf5b1df.


## October 24: missing Attach photo label / text-only message diagnosis

Kevin reported the new instruction's switch lacked a label; turning it on sent
text but no photo. Read-only live LXC inspection confirmed custom outbox number2
AttachPreview=true and history event4069014c779341fbb2d6a84b1c2fd018 opened-on-phone.
Camera disconnected; neither image nor preview topic exists in this NINA session.
Thus the requested attachment had no image source; no exposure was taken to test.
The option attaches an existing preview less than five minutes old; it never
captures an exposure. No claim that rich-photo delivery itself has been verified.

Fixed XAML to put a separate visible TextBlock `Attach photo` beside the checkbox
rather than depend on themed CheckBox.Content. Added automation name and tooltip
explaining preview availability/five-minute limit/no new exposure. Compiled package
0.4.23.0 is staged at artifacts/2026-10-24-native-notifications/StarfrontControl-0.4.23.0.zip.
Production remains0.4.22.0: do not restart/discard Kevin's currently edited loaded
sequence. Wait for him to save/confirm readiness before applying the label update.
No iOS/service/equipment settings changed for this label fix. See photo-label-status.json.


## October 24: label fix installed with explicit restart authorization

Kevin said the sequence was saved and authorized restart/install. Verified live
sequence stopped, all six primary devices disconnected, cooler off, no exposure.
Installed exact0.4.23.0 ZIP e883aecc2edfb31743ecb1b7639b9e96504c1350d0aaae53e8990c9eba832650
using graceful interactive scheduled close/start. NINA14956 replaced9804. All
five installed file hashes match package; control.json unchanged. Previous plugin
retained at C:\Users\astroadmin\AppData\Local\Temp\StarfrontUpdate-0.4.23\plugin-backup.
Live bridge reconnected and reports plugin0.4.23.0, stopped, camera disconnected,
cooler off. No exposure/equipment commands. Source label is a separate TextBlock;
actual physical desktop rendering has not been inspected after installation.

Pre-restart user tree was a five-node custom notification test named Sequence.
No recent saved test file found in Desktop/Documents/Downloads; asked Kevin for
saved filename to reload. NINA is currently idle on its empty Advanced Sequence;
do not substitute the historical2026-10-23 nightly file for his test. No saved
sequence files changed. Evidence in Operations .codex-artifacts/2026-10-24-native-notifications/migration/label-fix.


## October 24: roof wording for safety push notifications

Kevin said not to save/reload the test sequence and requested `Roof closed` for
unsafe and `Roof open` for safe (dictated `saved`, interpreted as safe). Changed
notification titles only; connected-monitor transition detection, preferences,
problem/safety kinds and body explanation remain unchanged. No invented roof
sensor or equipment action. 103 Core tests pass, including both requested titles.
Deployed only the self-contained Linux gateway runtime; preserved private config,
all six static assets, phone enrollment and history. Authenticated live service
active/bridge connected to unchanged plugin0.4.23.0, sequence stopped. New gateway
Core DLL SHA25628e2eea6c59f2df223cd9c5532f350917998e5e965815e2d1176ffeeb72ec87d
matches tested artifact. NINA was not restarted; no sequence saved or loaded.
No physical roof/safety transitions forced to test pushes. Evidence: Operations
.codex-artifacts/2026-10-24-native-notifications/roof-wording-verification.json.


## October24: web notification details and NUC sequence picker

Kevin requested web sequence rows show native notification message and photo
checkbox state, then a picker for sequence JSONs on the NUC. Implemented native
notification summary (message plus Attach photo:on/off/unknown) with subdued,
wrapping text. Full512-character native message projection in staged plugin;
other text budgets unchanged. New TypeScript sequence-library module inserts Load
button, searchable grouped file list, manual refresh and load confirmation.
Load never starts imaging, is blocked during acquisition, and revalidates profile,
NINA session and loaded sequence. New CSS scoped/injected by module; existing
static CSS/HTML unchanged. Small mobile wrapping fix prevents toolbar overflow.

Core SequenceLibrary permits active profile's DefaultSequenceFolder and
SequencerTargetsFolder only; logical Targets/file.json IDs,128file listing with
explicit truncation flag,1MiB limit, traversal/absolute/linked-file rejection.
Saved DeepSkyObjectContainer files are wrapped in a fresh root with Start/Targets/
End, following NINA's existing native load behavior; complete roots load intact.
No arbitrary filesystem browser, upload, polling or automatic sequence start.

113Core tests,12web presentation tests and strictTS pass. Package0.4.24.0 compiled;
Parallels installed it while idle, verified listing/refresh, native root load,
saved target load/wrapping,512character text +true photo flag and traversal
rejection. No sequence started; restored blank simulator root and removed only
owned testJSONs. Browser plugin unavailable; Playwright CLI isolated fixture
verified text/photo on-off, file search/confirmation/refresh, running guard,
1440desktop and390mobile without overflow/page errors. Evidence/screenshots in
Operations .codex-artifacts/2026-10-24-sequence-library and this repo artifacts/2026-10-24-sequence-library.

Published ONLY app.js on LXC, retaining old bundle in rollback-assets/
20261024-sequence-library-app.js. Authenticated live-served bundle SHA256
bf69fd7d330a8482356d9b19351ba501ed91da4fc7414d723b90e5cecc6945f4 matches tested file.
No service/NINA restart for frontend publication; existing configuration and
other assets untouched. Production still plugin0.4.23.0; its current metadata
lists only main-folder files, so Targets listing/full-message upgrade remains
pending plugin deployment.0.4.24 ZIP +installer staged on NUC Temp/AstroTools.

Latest production read: stopped, all equipment disconnected, but user has added
one notification test step to the otherwise blank root (5nodes). Asked explicit
async question whether restart may clear it, options Restart and clear the test
step / Keep it until I save it. Await answer before running staged
C:\Users\astroadmin\AstroTools\native-notification-migration-20261024\install-library-plugin.ps1.
Recheck acquisition/equipment before restart. Do not discard the current test
step or infer approval from elapsed time. No production sequence loaded/started
for this request yet. Source changes uncommitted; unrelated edits preserved.


## October24: sequence library production update completed

Kevin answered Restart and clear the test step. Reverified stopped/cooler off/all
six primary devices disconnected before graceful interactive restart. Installed
staged0.4.24.0; all five installed files match exact tested package; control.json
unchanged. Previous plugin retained in NUC Temp/StarfrontUpdate-0.4.24/plugin-backup.
NINA14956 ->7564; live gateway connected to0.4.24, empty4node sequence stopped,
no camera/other device connection, no exposure or capture start. Texas profile
and seven filters verified. Metadata now lists62available JSONs,59Targets and
3main-folder files. Two Targets entries excluded by size/name library limits;
UI text explicitly says some files omitted by library limits (not count cap).
No production load was issued merely to test availability; native simulation
already verified root and saved-target loads.

Final deployed JS SHA256e0066009d89c9cb0932b76871c9fb3dea759028ec6bb15c98147d9f32b47f3ed,
with previous bundle in rollback-assets/20261024-sequence-library-limits-app.js.
No gateway restart or private config/other asset changes. Operations artifacts
.codex-artifacts/2026-10-24-sequence-library/production-verification.json and
production-installed.json record completion. Earlier pending-restart note is
superseded. All source remains uncommitted; no commit/push requested this turn.

## October 24: dashboard sequence controls and nested roof/flip editors deployed

Kevin authorized these UI changes and incremental deployment. Plugin0.4.25.0
is installed in Parallels and on the NUC. Notification message (512 characters)
and Attach photo are editable with typed native readback. Collapse/Expand All
is right-aligned beside Reset; Follow Current is removed (Current step remains).
Roof badge toggles the selected safety monitor connection and reports connected
safe/unsafe as Open/Closed; disconnected/stale reports Unknown. Instruction
icons now distinguish action families; these are local SVG equivalents, not
copies of NINA artwork. Exit conditions align in rows at the TOP of each set.
Time conditions expose native fixed/astronomical reference, clock/offset fields,
native rollover time and RemainingTime-derived countdown. Countdown animates
locally every250ms; no status polling was introduced.

DIY Meridian Flip and Powerups When/IfCommand nested Instructions are projected,
even before TriggerRunner is populated. Trigger names open a pinned modal on
click/keyboard or450ms hover, retaining a scoped outline with parameter editing
and insert/remove actions. Roof-close wording applies to WhenUnsafe; Once Safe
retains its native semantics. Real Unsafe Management contains stop guiding,
tracking off, then Once Safe recovery (tracking, centering, autofocus, guiding).
All14 nested elements per unsafe trigger are visible, including conditions.
UseSideOfPier is readonly profile metadata: Powerups' setter does nothing.
Powerups Wait+ edits WaitExpr.Expression rather than its misleading Time getter.

Powerups TemplateByReference serializes an empty placeholder intentionally.
Telemetry reads never resolve templates. Explicit stopped-only resolveTemplate
loads the existing NINA library template through native SelectedTemplate, with
root/revision checks, exact plugin/type allowlist, size/depth bounds and no
replacement of an already loaded body. UI offers Load referenced actions when
needed. Nested unresolved references are rejected. Edits affect the loaded
session; reopening a saved by-reference sequence reloads its library template.
The template library itself is never modified by this editor.

Full preserved sequence projects237 nodes /100631 node JSON bytes, untruncated
and editable, with three resolved Unsafe Management references and six time
conditions. Sequence topic budget is128KiB (projection120KiB), default topics
remain64KiB and total retained payload256KiB; node bound256 unchanged. No
unbounded histories, recording, queues or new telemetry polling were added.
This is functional verification, not a new overnight memory observation.

Validation:117 core tests,33 web tests/95 assertions, strict TypeScript and
plugin Release build pass (47 inherited warnings, zero errors). Actual browser
against Parallels verified notification edits/photo on/off, collapse persistence,
roof simulator connect/disconnect, countdown advancement, time/offset edits,
DIY timing, insert/edit/remove in both trigger scopes and actual full-template
Once Safe recovery, desktop/mobile layout and no page errors. Full-night source
was loaded for inspection only, never executed. Simulation restored blank,
stopped/disconnected; task-owned fixtures/template copy and helper servers
removed/stopped. Existing local monitor services were not stopped.

Production: current unsaved sequence (including its notification test) was saved
to Dashboard-Preserved-20261024.json before restart; original2026-10-23.json was
not overwritten. Graceful NINA7564 ->13436, five installed plugin files match
exact tested package; control.json hash unchanged. Preserved sequence reloaded
successfully. Final237 nodes include all three resolved unsafe templates,
notification preserved; sequence stopped, all devices disconnected. Roof safety
monitor had been connected before restart and is disconnected afterwards; no
real device connect/disconnect command, exposure or sequence start was issued
for acceptance. No plugin backup requested or created for this installation.

Latest app.js/styles.css/index.html deployed atomically to existing Starfront
LXC web gateway content root; no gateway restart or config/PIN changes. Public
signed-in dashboard verified full237-node production telemetry, native0.4.25,
roof/Once Safe hierarchy, notification/photo/time editors, rollover/countdown,
Collapse All and Follow removal without any command POST. Existing Cloudflare
inline analytics CSP error and favicon404 remain; dashboard app itself works.
Automatic recorder remains separate/armed; no recording or sequence was started.

Evidence and exact deployment receipt: Starfront Operations
.codex-artifacts/2026-10-24-dashboard-edits/ (test logs, installed manifests,
full-tree report, browser results, screenshots, deployment-verification.json).
JS SHA256855a3413cf295f762fffacb8a6a91d86320cabad5161fca018f7181c31551af5;
CSS535c0fd445613661c453be19993f97415d5f0b8c9a17fa5ad43f3d8556c6ed57.
All source remains uncommitted; unrelated edits preserved.

## October24: green trigger insertion and camera-framing target panel

Trigger detail outlines now reuse the main sequence's compact green insertion
line and categorized picker, with hover, keyboard focus and touch affordances;
removed repeated text insertion rows. Parameter/structural stopped-only guards
unchanged. Actual Parallels add-group -> insert nested instruction -> native
readback -> remove test subtree passed; both trigger scopes share this renderer.

Removed target date input; visibility still automatically uses the observing
night in the site's timezone. Camera-framing survey image now appears left of
altitude/meridian graph (stacked on narrow panels). Uses sequence RA/Dec/PA and
profile-matched saved Starfront rig optics: ASI6200MM9576x6388,3.76um,448mm,
calculated TAN field4.6024x3.0711degrees. Exact production profile UUID
b318e855-d2c5-49c8-ba16-079314e282ea; unknown profiles show unavailable rather
than borrowing the Texas field. Configured FOV is an estimate and must be updated
if optics/camera change. Survey attribution/PA/FOV shown; no captured-image claim.

New independent gateway0.4.26 SurveyFraming uses the same CDS/DSS2 technology as
StarfrontMac TargetSkyPreview with explicit TAN WCS, camera aspect and PA. Fixed
upstream endpoint, finite bounds,512px longest side,512KiB JPEG limit,25s fetch
budget including body,2 concurrent/8 queued and8-image4MiB cache. No NUC reads
beyond existing telemetry, new plugin code, polling, image logs or disk cache.
Framing profiles optional in gateway config; other config fields unchanged.
Gateway self-contained Linux bundle deployed; resource caps unchanged. Initial
framework-dependent package failed to start (brief502), corrected with bundled
runtime; final gateway/live browser checks pass. NINA/plugin0.4.25 untouched.

Website proxy adds protected GET-only framing/profiles and framing/image routes,
forwards geometry query, private24h successful survey JPEG cache; all other
routes retain no-store and existing auth/origin/allowlist. Site source helper
committed only proxy/test changes asb23ee708, then shared tonight-planning loaded
status updates asf656a8de. Coordinated with Tonights Planning (2); that chat
published combined saved version110/appgver_1e3cb70405bc8191a741632a038c689a.
Verified packaged night page/JSON/worker match current build, public new image
and green-line picker work without command POSTs. All website assets retained.
Archive first exceeded256MiB due Mac metadata; COPYFILE_DISABLE=1 repack passed.

Checks:35web/104assertions,8service tests,4proxy/23assertions, strict TS pass.
Parallels actual sequence target atRA21.6493h/Dec57.4891/PA30 loaded real DSS2
JPEG, north-up variant and cache hit verified, desktop/mobile screenshots and
no page errors. Final live browser verified Texas framing/date removal/left
placement, categorized green trigger line, connected unchanged plugin/stopped
sequence and zero native command POSTs. Tonight's planning chat separately
loaded its new dated sequence; this task did not alter it. Simulation restored
blank/stopped; task-owned proxy and test gateway stopped. Existing monitors kept.
Evidence: Operations .codex-artifacts/2026-10-24-dashboard-framing/.
Final screenshot revealed adjacent rise/set labels overlapping in the narrower
beside-image graph; annotation rows now stagger and retain plotting clearance.
Strict TS,35 web tests and full live read-only checks passed again after the
app.js-only follow-up. Final hashes/screenshot are in the same deployment receipt.

## October24: sequence UI audit and compact panel parity

Kevin requested a complete sequence presentation audit and a reduced right-hand
panel retaining existing controls/visuals. Added docs/SEQUENCE-UI-AUDIT.md with
findings, changes and acceptance matrix. Compact view now exposes edit menus and
all structural controls directly, wraps details, preserves repeat/sequential/
parallel badges, labeled exit sections, trigger status/current/completed/error
marks and exposure progress. Nested borders replace excessive row indentation.
Target camera preview and smaller graph sit together (stack at very narrow
widths), retaining coordinates/PA/FOV/twilight/site data. Compact toolbar uses
three columns or two below350px, retaining labels and all actions.

Full view also wraps text. Component-width queries stack nested trigger columns
before they squeeze instruction rows. Narrow full-screen layouts reuse compact
styling. Open trigger dialogs clamp within the viewport when resized. Same DOM,
native node IDs, handlers, editing guards and shared dialogs; no new models,
polling, backend code or retained history. Existing unsupported fields remain
read-only. Browser plugin unavailable; regular Playwright used.

Actual Parallels237-node fixture parity:93edit menus,312structural controls,
21badges,20trigger-state labels,3target panels,17exit sections match compact/full.
No clipping compact320–1920px and full320–1920px (eight sizes). Header labels fit
six sizes; popup resize390x844 passes. Native message/clock/trigger editors,
rollover/local countdown, grouped nested insert/edit/remove, duplicate, Alt-key
reorder and pointer drag native readbacks passed. Offline viewer actions disabled.
Separate native wait-only sequence verified start confirmation/current waiting,
completion, reset/stop and disabled edit controls while active. Full-night fixture
was inspected/edited only, never executed.35web tests/104assertions and strict TS
pass. No new long memory/stability claim.

Only app.js/styles.css deployed to existing LXC content root; no service/plugin/
NINA restart, settings changes or Site Worker publish. Live dashboard passed
compact/full parity on tonight's current sequence (57edit menus/225structural
controls/14badges/13trigger states/2targets/10exit sections), target image+graph,
unsafe/Once Safe editor, phone bounds, no app page errors and zero native command
POSTs. Sequence still stopped; user/tonight-planning work preserved. Simulator
restored blank/stopped/disconnected; owned fixture files/template and helper
services cleaned up. No real equipment or sequence actions issued for acceptance.

Evidence: Operations .codex-artifacts/2026-10-24-sequence-ui-audit/ with audit,
results, screenshots and deployment-verification.json. JS SHA256
870db0a262a3127ba1ae61d191ea202373277685325050c0f542ca3d5db6f9db;
CSS09130fea134430325f826f2964b0d0042f7898c4a70e9a185d634749e207f519.
All fork source remains uncommitted; unrelated edits preserved.

## October24: preserve sequence scroll and timed-wait countdowns

Kevin reported reading an expanded sequence being interrupted by scroll jumps,
plus missing Wait for Time countdowns. Read-only live observation reproduced a
19px drift during refresh; simulator bottom-of-tree regression exposed a1430px
jump. Target images/graphs were recreated on telemetry renders, native scroll
anchoring added offsets, and the editor's MutationObserver added insertion lines
only after scroll restoration had clamped against the temporarily shorter tree.
Unchanged target panels now retain their DOM, bounded to current visible targets;
obsolete ResizeObserver entries are removed. Cache includes target settings/site/
profile/observing date. Sequence tree native anchoring is disabled. Final scroll
restore runs once before paint after editor decoration, cancels on user input or
explicit Current step navigation; no continuous scroll enforcement or history.
Countdowns are present before tree restoration so wrapped row height is stable.

WaitForTime has no native RemainingTime getter (confirmed installed NINA source
and actual simulator telemetry). Browser uses its reported resolved hours/minutes/
seconds, rollover and NINA host IANA zone, matching the observing-day date rule.
Native remainingSeconds remains authoritative when supplied. Timed waits now show
local ticking countdowns in rows and settings, alongside rollover. Offline clocks
retain existing unavailable behavior. No plugin/service/API changes or restart.
38web tests/112assertions and strict TypeScript passed, including midnight,
rollover equality, elapsed target, DST and invalid inputs. Actual Parallels204-node
fixture stayed stopped: regular and near-bottom scroll held through real native
WebSocket frames/tree replacements in compact and expanded views; same target
DOM retained. Wait row and settings countdowns, next-morning target, desktop/mobile
layout/screenshots and no app page errors verified. Full-night fixture never ran.
Simulator restored blank/stopped and owned fixture files removed.

Frontend app.js/styles.css deployed atomically to existing LXC content root.
JS27a8e817b8a8d01f5ba901f9cfb494a3cc49e41f12799351d4b34620e806c942;
CSS0dbfad25378275cce2682f20b83f6f4a903b0d5cef85d67b7947015b02ea3b37.
Browser plugin unavailable; existing Playwright used. Evidence in Operations
.codex-artifacts/2026-10-24-sequence-scroll/. No unrelated source changes committed.
Final live read-only check passed: compact/expanded scrollTop2500 remained exact
across native WebSocket updates and tree replacements; target DOM retained,
Wait for Time countdown advanced, desktop/mobile screenshots and no app page
errors. Zero live native command POSTs. Existing unrelated Cloudflare injected
inline analytics CSP warning remains; no CSP changes made. Task test gateway and
HTTP staging helper stopped, private temporary gateway config removed. Production
NINA and loaded sequence were not restarted or modified by this acceptance run.

## October24: remove redundant container-type capsules

Kevin requested removing Sequential/Parallel capsules below container titles.
Renderer now only creates a set badge for numeric loop repeat counts; container
icons, names, nesting and controls remain. Strict TS and38web tests/112assertions
passed. app.js-only deployed to LXC (SHA194d6f0d87795b05f1db8bfd9f48df689b57425d99fdfd344096f90848e2eaa1).
Read-only live Playwright verification: compact/full each retain14sets/14icons
and six repeat badges; zero Sequential/Parallel badges. Screenshot
/tmp/starfront-no-sequence-capsules.png. Initial load-event navigation timed out;
DOMContentLoaded retry and rendered interaction passed. No NINA restart or
native commands. No simulation execution needed for this label-only removal.

## October24: native iOS sequence groups, triggers and target planning

Kevin requested the web sequence presentation in the iOS app, including target
camera survey and meridian/altitude curve. Implemented native SwiftUI groups,
collapsible containers, conditions above instructions, repeat counts, roof/DIY
trigger cards and nested action sheets. Stable native node IDs replace offset
identity; folding is retained during telemetry, pruned on sequence/session change.
Current step expands ancestors and selects the innermost enclosing trigger sheet.
Sequence Start/Stop/Reset and existing guards/pairings remain. This is native
presentation/inspection, not new native sequence parameter editing endpoints.

App source: Operations/apps/StarfrontIOS/{Sources/SequenceOutlineView.swift,
SequencePresentation.swift,TargetPlanning.swift,TargetPlanningView.swift}.
Shared calculation bridge web/ios-visibility.ts imports existing visibility math;
its generated96KiB resource runs in one JavaScriptCore context, with native Canvas
rendering (no WebViews). Camera survey uses identical TAN WCS/PA geometry against
fixed CDS endpoint,512px long side,512KiB body,25s timeout, redirect rejection,
no cookies/credentials and eight-JPEG/4MiB cache. View tasks cancel on target or
profile change. Exact profile ID mapping in bundled framing-profiles.json matches
Texas LXC verified ASI6200MM/448mm geometry; isolated Parallels test field is explicit.
Unknown profiles report unavailable rather than inheriting Texas optics. Graphs
include altitude, meridian/peak, rise/set, twilight and observatory time zone.
Bundled upstream MIT license preserved. No extra NUC processing or polling.

Swift6 contract suite passes, with new grouping/ancestry, nested trigger selection,
midnight/rollover/DST countdown, actual JavaScriptCore shared engine and camera
WCS tests. Both signed simulator and phone builds pass. Actual iOS27 simulator
against Parallels237-node stopped fixture: fold-all, real DSS2 survey, native
altitude/meridian, roof/DIY nested action sheets and ticking clocks passed in light
and dark modes. Eleven-second update interval retained exact visible-row position.
Visual review fixed background striping and final time-label clipping, followed
by dedicated final chart acceptance. Full night fixture inspected only, not run.
XcodeBuildMCP AXe taps/swipes reported success but did not activate controls;
scoped disposable XCTest runner successfully exercised actual interactions.
Initial unsigned simulator build could not save Keychain; ad-hoc signed simulator
build restored normal rig pairing. Production auth/connection code unchanged.

Signed iOS2026102403 installed and launched on communicator00008130-001A4D622461401C.
Saved rig library unchanged; no provisioning override, real equipment commands,
plugin installation/restart or production web deployment for this task. Physical
phone rendering was not independently captured; simulator screenshots separate.
Evidence: Operations/.codex-artifacts/2026-10-24-ios-sequence-ui/.
Final nested Once Safe acceptance passed: it is a native container (not a separate
trigger button); its collapse removes the autofocus recovery row and expand
restores it. An extra harness initially expected a button and then scoped its
query to the wrong ScrollView; corrected app-level selectors pass. Product
already exposed its full nested actions. Added pure nearest-trigger ancestry
check for Current navigation.38web tests/112assertions and strict shared-bridge
TS also pass. Final build2026102403 installed at phone container84876218-FB42-4938-
86DC-D20B8A09345B, launch receipt successful. No physical screen capture claimed.
Simulator restored blank/stopped/disconnected, owned template/fixture and temporary
XCTest runner removed, appearance restored to light. Owned relay18891 and staging
HTTP18779 helpers stopped; unrelated simulators/services preserved. Final receipts,
Swift/UI logs and light/dark/final-chart/recovery screenshots saved in evidence.

## October24: website Start confirmation and idle Stop fixes

Kevin pressed Start on the website and reported no start plus enabled-looking
Stop. Read-only native NUC snapshot confirmed sequenceRunning=false, no active
nodes, and no retained accepted Start operation. No observatory retry was issued.
Reproduced client bug with production command routes intercepted in the test
browser: leave Start confirmation open through12seconds of countdown ticks, then
Confirm produces zero POSTs and "profile or sequence changed". The old guard
compared entire nodes, including changing remainingSeconds. Stop separately used
external-stop capability as sufficient eligibility even while actually idle.

New web/sequence-command-state.ts uses native sequenceId/configurationRevision
for confirmation freshness; older-host fallback excludes runtime counts/clocks
but preserves configured fields, structure and disabled state. Session/profile
checks remain. Stop requires actual running state or an accepted owned Start;
GUI/external-run support remains, idle/unknown guards are explicit. Disabled Stop
uses neutral gray styling. Only web monitor bundle/styles changed; iOS/plugin/
service binaries unchanged. Kevin explicitly identified website as affected UI.

41web tests/125assertions and strict TS pass. Actual Parallels stopped204-node
fixture:12second dialog held through real countdown/native pushes still submits
the intended Start/skipValidation payload to a test interceptor, no full-night
execution. Separate native24second wait-only sequence: idle Stop disabled, Start
accepted, waiting/current markers, Stop enabled, editing blocked, natural finish
and disabled Stop, Reset/second Start/Stop and idle guards pass. An initial broad
UI harness read structural controls before its decoration microtask; waiting for
its disabled DOM state passed the rerun without editor product changes. Simulator
restored blank/stopped; owned files/helpers removed and private config deleted.

App.js/styles.css deployed to LXC without restart. Live read-only page verified
native stopped, Start enabled, Stop disabled/gray with "Sequence is stopped"
title, Start confirmation opens/cancels, zero command POSTs and no app page errors.
No NUC sequence/equipment commands or NINA restart. Evidence Operations/
.codex-artifacts/2026-10-24-sequence-start-fix/. JS6d5d0896994bb79cf88634bd9a00888ca701260b3ddf33945247f001a0a6b458;
CSSc457f49459fbbbe3e481f0b81814af58eb3b0a470b3c6dae21230d4a7bfab3dd.


## October24: static sequence status indicators

Kevin requested replacing bouncing web spinners with the iOS status symbols and
placing status at the far right of expanded rows. Sequence rows now show fixed
SVG play/clock/check/alert/minus/skipped/dotted-pending symbols with accessible
state labels. Removed sequence spinner animation; separate mount-motion animation
retained. Expanded desktop, trigger/condition cards and mobile reserve the final
column for status after edit/structure controls; docked controls remain unchanged.

41web tests/125assertions and strict TypeScript pass. Actual Parallels owned
WaitForTimeSpan inside SequentialContainer verified running play + waiting clock,
no animation/transform, stable coordinates through4.2seconds of native updates,
right alignment with no control overlap at1440/390px, compact restore and zero
page errors. Full stopped night fixture:136visible rows aligned at both widths.
First full-fixture check caught an absolutely positioned trigger menu overlap;
fixed it and verified corrected trigger-name grid placement by screenshot.
Full-night fixture was not executed. Simulator restored blank/stopped, owned
fixture files and temporary helpers removed. Browser plugin unavailable; regular
Playwright CLI used for rendered validation.

App/styles deployed to LXC without restart. Read-only production check:92visible
rows, Pending after Kevin's stop/reset/disconnect, no spinners/misalignment/page
errors, expand/restore passed, zero command POSTs. No observatory operations or
NINA restart. Evidence Operations/.codex-artifacts/2026-10-24-sequence-status-icons/.
JS1c8a81cfe34e5dd880cd0eb38878a5263e10a480c62a117299e4c549e8265ba4;
CSS05b7c67684604e6ee613ad8f3c5e267977db993f902d11836c09f40b4cd6a52f.


## October24: Blender rig model, mount presets and Park removal

Kevin requested higher-fidelity web scope rendering (suggested Blender), a straight
floor-bolted pier without feet, small gray target RA/Dec beneath Slew SE/SW, removal
of Park/Unpark, the same model in iOS, and web deployment. Local equipment inventory
identifies RedCat91, AM5N, ASI6200MM Pro, EFW7, UniGuide32/ASI220MM Mini and EAF.
Model approximates component geometry from these records; it is not a calibrated
equatorial-mechanics or collision model. Existing live alt/az optical pointing and
view-only orbit/reset behavior remain separate from confirmed equipment commands.

Actual Blender5.2.1 authoring script scripts/build-mount-model.py exports
web/assets/starfront-rig.json:98parts/37,192triangles/2,023,099bytes. Beveled/smooth
red optical tube and harmonic mount, machined rings/flanges, dovetail/clamps, EAF,
guider, filter wheel, camera fan/ports, floor plate/anchor bolts; no legs. Same
geometry copied to iOS bundled resources. Web uses pinned Three0.186.1 and retained
WebGL2 views; no external assets/CDN. Full MIT license included in app bundle and
docs/THREEJS-LICENSE.txt. SVG fallback also corrected to floor-mounted pier.

New web/mount-renderer.ts retains at most card/dialog views, shared immutable
geometry, one coalesced requested frame per change, capped pixel density, no
animation loop. Dashboard preserves mount canvas across native pushes. Dialog
close/switch disposes materials/textures/observer/renderer/context and detaches
canvas; visibility and context-restore events request redraw. Stable native
pointing/status/view skips rendering even when other telemetry changes.

Park/Unpark removed from both web and iOS mount panels; backend command catalog/
sequence instructions untouched. SE/SW captions show active-site J2000 RA/Dec
using shared astronomy calculation; click recalculates target for confirmation.
iOS gained same two presets through bounded existing JavaScriptCore bridge.
Native MountModelView uses SceneKit/Metal, rendersContinuously=false, retained
scene via UIViewRepresentable coordinator, cleared on dismantle; no WebView.
Existing SwiftUI drag/accessibility/reset remain. Asset decoded once.

42web tests/192assertions and strict TS (including resolveJsonModule and
allowSyntheticDefaultImports) pass. Swift6 contracts pass including new shared
horizon conversion/time-dependent preset bridge checks. Actual Parallels native
simulator mount initially failed Connect because owned OmniSim helper was down;
restored local official helper, exact allowlisted lazy rediscovery Connect
d25cc2ad-9d27-47a4-abcd-ff744024a3c7 succeeded. Web desktop1440/mobile390: model,
actual pointing, orbit/reset, coordinate captions, Park/Unpark absence passed.
SE confirmation POST intercepted/compared with target fields, never dispatched.
12open/close cycles:2canvases open/1closed,16geometries/7textures per view stable;
unchanged telemetry produces no continuous frames. No unexpected console/runtime
errors; deliberate intercepted HTTP400 logged once. Harness initially needed
reload after deliberate rejection notice and awaiting native dialog-close event;
these were test sequencing issues, not equipment failures.

Native iOS27 simulator against Parallels: detailed model rendered, drag135→201
without changing reported pointing, Reset135 verified, SE/SW captions visible,
SE confirmation includes calculated RA/Dec. Confirmation not executed; app
terminated/relaunched to dismiss it. XcodeBuildMCP instantaneous tap reported
success without navigation;150ms touch-down/up and scoped swipe worked. No
production equipment commands. Signed iOS2026102404 built, installed and launched
on communicator00008130-001A4D622461401C, container27818561-28C7-4ACE-BC33-236C957D4090.
Saved pairing preserved; physical phone rendering not captured.

Deployed app.js/styles.css only to existing LXC, no service/NINA restart. Actual
public site verified Blender-WebGL renderer, target captions, zero Park/Unpark
buttons, orbit/reset and close, zero command POSTs/page errors. JS74bc1af814f4679e
6d33e6b62eb623eeb19a4acf03168dab627103b461a778cf; CSSe68150d70aad1ddd64485e75e7ff4dfc
1989b1d26a0d2b1f108a8feacbc2d2e7. Simulator mount disconnected successfully via
36cb5a52-7cf2-4a2d-aade-152e65c37db5; blank sequence stopped. Owned temporary
helpers stopped/private gateway config removed. Evidence Operations/
.codex-artifacts/2026-10-24-blender-mount/ includes Blender file, screenshots, QA
scripts, build/install/launch receipts and verification.json. Browser plugin
unavailable; regular Playwright CLI used. No physical pier-position calibration
or long-duration leak claim follows from these bounded lifecycle tests.

Final mobile visual review found long coordinate values colliding in existing
four-across metrics. Scoped mount-dialog metrics now use two columns below450px;
live390px read-only browser check with extreme coordinate text fixtures fits
153px columns without overflow. Published CSS final SHA4b194ce0899433b354a068973a1
df8baab4edcf8e7d7e8d017fc84c6709fc2d3 (supersedes preceding CSS hash). No command
issued. Final simulation browser tab closed; owned helpers stopped and config
deleted. Installed phone app launched successfully; observatory untouched.


## October24: stop any slew, level orbit, complete miniature and phone VPN diagnosis

Kevin requested a prominent red Stop only during slewing at the web mount dialog
head, main mount card and iOS popup head. He explicitly authorized stopping slews
started by NINA/sequencer too. Plugin0.4.26 advertises externalMountStop. Exact
selected-device/profile/simulation guards remain; an external stop cancels the
native running sequence and its owned Start token before StopSlew, preventing
continued sequence motion. Idle race does not cancel a sequence. StopSlew executes
even if cancellation fails; acknowledgement waits for native motion, sequence and
owned mount operation to settle, with bounded30s failure. No confirmation for Stop.
123core tests,43web tests/1488assertions, strict TS and Swift6 contracts passed.

Actual Parallels0.4.26: web card/dialog external Alpaca slews stopped immediately
and buttons vanished; no confirmation/duplicate requests. Running native120s wait
plus external slew: Stop7ef2afe1-59f0-4579-b6e7-0b817d567b44 succeeded, native motion
and runner false; owned Start4cbe2722-0592-4573-ad9b-1963b2827c34 cancelled, no error.
iOS actual visible red top Stop: c99b4c1d-ca1d-4ce9-b07c-66f156d57828 succeeded,
external driver motion stopped and button removed. First slow manual UI attempt
missed the finite slew; repeated with atomic observe/tap and verified receipt.
Test helper initially picked third-party WaitForTimeSpan+ (same short class name);
simulation guard rejected it. Explicit native NINA catalog type used successfully.
Simulator restored blank4nodes/stopped/mount disconnected; OmniSim rate restored20.

Web fit uses transformed scene bounds and actual mini-card dimensions; full model
and status fit118px card. Both native/web camera orbit explicitly preserve world-up.
Native SceneKit resizes refit on actual layout; visual test caught zero initial
bounds making model tiny, fixed with weak resize callback. iOS disconnected rig
uses dark grayscale materials. Actual connected/disconnected screenshots verified.
No animation loop added. New web assets deployed and live read-only checked:
JSce5df50cedb1361ea6f6d23d50484aa09c1816b4ea1decff85782fbd30664d18;
CSS78cc5cea640d47baa9079a76b66aaeb6622e78d9ff42a90cd3ce6d1479961651.
No live equipment commands for web checks. Phone signed2026102406 installed;
latest launch rejected because device locked (do not claim launch verified).

Phone Reconnecting investigation: correct saved endpoint NUC100.89.247.90:1889
socket/v1, NSURLErrorDomain-1001 timeout. Bounded DEBUG connection cache and optional
one-shot NWConnection probes contain no token and retain only one overwritten
file. Phone public HTTPS succeeds over en0, NUC API+SSH and Mac SSH timeout over
utun5, even after Kevin toggled Tailscale. Mac reaches NUC in62ms. iPhone tailnet
peer100.124.150.76 appears Online but does not answer Tailscale ping. Tailscale app
is installed; devicectl default hides store apps, include-all-apps verified it.
This establishes a private-route connectivity failure; actual VPN cause remains
unconfirmed. User asked whether it worked10minutes earlier; Safari NUC status URL
check pending. Do not change pairing, ACLs or VPN settings based only on this.

Production update in progress: NUC0.4.25 was stopped/no exposure/slew, only safety
connected,155loaded nodes root2026-10-24/configurationRevision3. Existing saved
Dashboard-Preserved file was root2026-10-23, so current edits were saved through
native Save As to Documents/N.I.N.A/2026-10-24.json (149697bytes, root verified)
before the authorized graceful restart. No software backup requested/created.
Installer checks exact prior version/profile/revision/count/stopped/active operations,
package hash and control-config hash. Explicit scheduled-task battery settings
needed: Parallels default tasks silently did not run while Mac on battery. No
force-kill used. NUC update/restore outcome must still be recorded below.

Evidence: Operations/.codex-artifacts/2026-10-24-any-slew-stop/. No real observatory
slew or Stop test was issued; simulator success is not real-hardware validation.

Production completion:0.4.26 installed/verified, graceful NINA PID13436→3800,
all5file hashes match manifest/control configuration unchanged. Initial attempt to
invoke installer under restricted script policy failed before changes; explicit
per-process ExecutionPolicy Bypass used (no machine policy change). Reloaded saved
current sequence via f6abf26b-48e1-42f5-83ea-0470aed2d8f1 succeeded. All155types/names,
parent grouping and editable settings match pre-restart; one RA rounded by native
HMS serialization2.3e-10hours (0.0000124arcsec), within1e-8numeric tolerance.
Sequence left stopped; no physical motion/capture/Stop test. Previously connected
safety monitor became disconnected on restart; roof may show Unknown until reconnected.
Actual public dashboard read-only:0.4.26,155nodes,stopped,externalMountStop=true,
zero command POSTs. One verification helper needed dashboard-prefixed API URL,
corrected; no application changes for this test issue. Own remote scheduled tasks
and temporary UI/package scripts removed; saved2026-10-24.json retained. Local
simulation helpers stopped after restoring blank/disconnected state and slew rate.
Signed phone2026102406 installed; most recent launch rejected Locked. VPN/Safari
check still pending; do not claim phone live connectivity is restored.

## October 25 — observed sequence timing and right-edge status marks

Gateway0.4.27 adds bounded (256-node, current-session/sequence) receipt-time
history to pushed sequence nodes. Timing is `gateway-received`, with startedAt,
endedAt, or seenActiveAt for a step already active at first observation. Initial
completed steps and transitions missed across disconnects are not backdated.
Reset/pending clears prior step timing; session/sequence replacement isolates it.
History is in gateway memory, so reconnecting viewers share it but a service
restart does not reconstruct old logs. No NINA polling or plugin update added.
Web rows show compact Observed Start/End/Seen times in the site's timezone;
docked running/completed/waiting symbols sit at the far right, after edit controls.
iOS2026102501 uses gateway timing when supplied and equivalent phone receipt-time
tracking for direct-plugin connections. No historical timing is invented.

Validation:125Core tests, gateway synthetic lifecycle with pending→running→done
receipt readback,14TS presentation tests, strict TS build, Swift contract checks,
signed physical build. Playwright verified docked symbols within5px of row end
and rendered docked/expanded timing fixtures. Browser plugin unavailable;
Playwright fallback used. Gateway0.4.27 deployed on Starfront LXC, binary/static
hashes verified via authenticated HTTP; NUC plugin remains0.4.26, stopped sequence
and equipment untouched. Phone2026102501 installation succeeded after USB/unlock;
launch receipt is under Operations/.codex-artifacts/2026-10-25-sequence-times/.


## October 25 — LXC recorder reconnection recovery

The separate Operations StarfrontRecorder 0.2.0 automatically recorded the October24
sequence from20:09CDT until telemetry stopped01:10:55CDT October25; gap-start01:11:32.
The process stayed healthy/disconnected and never recovered, although its endpoint
worked at inspection. NINA logs continued through the gap without warnings/errors;
the original transport-interruption trigger remains unconfirmed. The deployed old
Linux binary reproduced a retry bug with a silent handshake: one attempt/82seconds.
Its watchdog relied on close callbacks that did not arrive. Recorder0.2.1 actively
retires silent sockets, owns one bounded retry, rejects stale callbacks and exposes
connection diagnostics and explicitly historical running state.17tests/99assertions
and strictTS pass; compiled Linux fixture retried/recovered and automatically
recorded/closed simulated execution. Updated only recorder service/binary on CT105;
no plugin/NINA/equipment/tunnel/dashboard changes for this recovery. Final live,
armed, stopped sequence/no active recording/error. Preserved every original
compressed byte and verified41900contiguous zero-indexed journal records; original
recordingd79fea7e-be6f-44ae-80b7-8f0601a56414 now interrupted with explicit gap.
No missing telemetry was backfilled. Production config unchanged; rollback under
~/.local/share/starfront-recorder/deployments/20261025-reconnect. Evidence Operations
.codex-artifacts/2026-10-25-recorder-recovery/REPORT.md; sanitized deployment receipt
ops/deployments/2026-10-25-recorder-recovery.json. New push alerts/replay UI not added.
