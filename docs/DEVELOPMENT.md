# Starfront Control development and verification

## Identity and compatibility

| Item | Starfront Control |
| --- | --- |
| Plugin name | Starfront Control |
| Plugin GUID | `897ef051-204c-4dfb-9697-d3209a15adc0` |
| Assembly | `StarfrontControl.dll` |
| Running simulation version | `0.2.0.0` |
| Staged follow-up version | `0.2.2.0` (not installed) |
| Target | `net8.0-windows`, WPF/Windows Forms |
| Minimum N.I.N.A. version declared upstream | `3.2.0.9001` (runtime compatibility unverified) |
| Default port | `1889` |
| Server enabled on first load | No |
| Settings group | `StarfrontControl.Properties.Settings` |
| Port-discovery request/reply topics | `StarfrontControl.RequestPort` / `StarfrontControl.Port` |
| Intended future install directory | `%LOCALAPPDATA%\NINA\Plugins\3.0.0\Starfront Control` |

Development starts from `dev` commit `353a853`. Its only difference from release tag `2.2.15.2` is the changelog. The plugin keeps the existing `ninaAPI` source namespaces to avoid an unrelated mass rewrite; its assembly and plugin identities are separate. Embedded resource URIs and the options-template key have been updated for the new identity.

The custom networked rotator and filter-wheel providers have Starfront names and distinct device IDs. Existing profiles/sequences referring to Advanced API assemblies or device IDs are not automatically migrated. Loading both plugins together, settings isolation in the N.I.N.A. host, and sequence-template discovery still require Windows verification. Preserve upstream MPL notices and attribution.

## Build without installation

The portable core and tests target .NET 8. The test project permits major runtime roll-forward so this Mac's .NET 10 SDK/runtime can execute them. A .NET 8-or-newer SDK is needed. On macOS:

```sh
dotnet test tests/StarfrontControl.Core.Tests/StarfrontControl.Core.Tests.csproj --configuration Release
dotnet build ninaAPI/ninaAPI.csproj --configuration Release -p:EnableWindowsTargeting=true
```

On Windows:

```powershell
dotnet build StarfrontControl.sln --configuration Release
dotnet test tests/StarfrontControl.Core.Tests/StarfrontControl.Core.Tests.csproj --configuration Release
```

Plugin output is under `ninaAPI/bin/Release/net8.0-windows/`. `StarfrontControl.Core.dll` is a required companion assembly. The output is a compilation artifact, not a validated installation package; dependency staging and N.I.N.A.'s runtime assembly resolution still need testing.

Cross-compilation currently reports warnings for legacy assembly references, older-framework packages, unawaited upstream calls and platform annotations. NuGet also reports high-severity advisory `GHSA-555c-2p6r-68mm` for the unchanged `Microsoft.Windows.Compatibility` 6.0.1 dependency. Dependency compatibility/security review remains required before deployment; a successful compile does not resolve these warnings.

The old automatic post-build copy has been removed. The historical `ninaAPI/buildAndCopy.bat` now builds only; it does not copy files, create GitHub releases or update upstream manifests. The generic upstream manifest-generation script remains available but is not invoked by builds. No install/uninstall or release publishing was performed for this milestone.

## Implemented resource changes

- WebSocket event history uses the portable, thread-safe `BoundedHistory<T>` with capacity 2,048. Writers overwrite the oldest entry; reads enumerate a stable chronological snapshot without holding the writer lock during serialization. `EvictedCount` tracks lifetime evictions. This intentionally changes `/event-history` from whole-process history to a recent window.
- The log processor no longer keeps its unused static list of past recognized events.
- Networked rotator/filter-wheel socket disposal removes static event subscriptions. Polar-alignment socket disposal unsubscribes from the same broker it subscribed to.
- Screenshot and capture-result bitmaps and the raw-FITS memory stream have scoped disposal.

The event limit bounds entry count, not arbitrary payload size or total process memory. Event payload size limits, image history, thumbnail retention, live-stack retention, pending sends and sequence serialization remain separate work. Upstream legacy routes include GET control actions and expensive polling paths. They are disabled by default in the new server; enabling them places them behind its binding/authentication module, but does not repair their payload/retention behavior or give their commands the new lifecycle guarantees. Do not publish this development server as a public remote-control interface.

## Portable snapshot component and adapter

`TelemetryStateStore` is the transport-independent latest-state cache intended for the new adapter/service. It is now connected to the new bounded control adapter. Its draft full-snapshot envelope contains `schemaVersion`, a per-instance `sessionId`, a monotonic `revision`, and named states with source `observedAt` timestamps and JSON values.

Initial limits are 32 topics, 128 characters per topic name, 64 KiB UTF-8 JSON per topic and 256 KiB total retained input JSON. Inputs must be JSON objects with depth at most 32. An overflow is rejected atomically. These are initial component policies, not measured observatory resource budgets. Stored JSON strings, the serialized reconnect frame, metadata, temporary parsing allocations, and snapshots held by callers are additional memory.

An update prepares one immutable serialized frame. Repeated reads return the same snapshot without serializing or allocating. Older observations cannot overwrite newer state. Exact duplicate deliveries do not increment the revision; a newly observed unchanged value does, preserving its actual observation timestamp. A service heartbeat must not be passed off as a fresh device observation. New instances start a new session with empty state.

A future N.I.N.A. adapter must submit compact copied state, avoid arbitrary reflected application graphs, and isolate publication/serialization from acquisition callbacks with bounded work. The integrated adapter, gateway and CLI provide the initial camera/mount subset described below; the core component alone does not establish host behavior.

## What portable tests establish

- A million appended events retain only the configured recent window, in order.
- Repeated polling does not add history entries.
- Concurrent readers and writers receive consistent snapshots; readers do not hold the producer lock while enumerating.
- Evicted and cleared payloads and replaced state snapshots become eligible for collection.
- Latest-state replacement does not mutate previously returned snapshots.
- Snapshot reads allocate zero bytes in the measured read loop.
- Topic/UTF-8 payload limits, out-of-order observations, duplicates and session restarts behave as specified.

These are deterministic component/stress tests. They do not reproduce the incident, establish process-wide memory stability or test actual camera images, WPF, ASCOM, N.I.N.A. loading, socket disposal, network behavior or equipment.

October 22 foundation verification: 21 tests passed, zero failed; the Windows-targeted cross-build on macOS passed with zero errors and 66 warnings. Ignored local evidence is in `artifacts/2026-10-22-foundation/verification.json`, `plugin-cross-build.log` and `core-tests.trx`, including artifact hashes and sizes.

## Integrated simulation build (October 22)

`src/StarfrontControl.Service` hosts the authenticated loopback monitor and one upstream socket. `src/StarfrontControl.Cli` provides the installed CLI. `web/monitor.ts` compiles to the static `wwwroot/app.js`; edit TypeScript, then rebuild. `scripts/build.sh` builds the web assets, checks TypeScript, runs component tests and builds all three applications without installation. `scripts/install-cli.sh` explicitly installs the CLI on the Mac. Neither script deploys the plugin or touches equipment.

The new plugin interface exposes authenticated cached GET `/api/v1/status`, `/api/v1/diagnostics`, `/api/v1/operations/{UUID}`, POST `/api/v1/commands`, POST `/api/v1/commands/validate`, and WebSocket `/socket/v1`. Commands have `{requestId, device, action, deviceId?}`. `device` is camera or mount; `action` is connect or disconnect. Connect requires an explicit catalog ID. HTTP202 is acceptance; terminal operation status is delivered in pushed snapshots or queried by UUID. A timeout does not cancel an operation. The deduplication window is bounded and resets with the process. Do not retry a request with a new UUID merely because its response was lost.

Guest `%LOCALAPPDATA%\StarfrontControl\control.json` contains `enabled`, `bindAddress`, `token`, `simulationOnly`, `allowedDeviceIds` and `enableLegacyApi`. Fresh defaults disable the listener, use loopback, disable legacy routes and allow no devices. Test credentials are random and ACL-restricted. The configured adapter only observes copied scalar notifications; status requests do not query device drivers. N.I.N.A. still performs its normal internal device updates.

Gateway configuration is `~/.config/starfront-control/service.json` with `bridgeUrl`, `bridgeToken`, distinct `token`, and `listenUrl` (default `http://127.0.0.1:1890`). Keep the file private. A development gateway must bind loopback. Run its built DLL with `--contentRoot` pointing at `src/StarfrontControl.Service` for static assets. `STARFRONT_SERVICE_CONFIG` can select another private file. The CLI uses `init --url http://127.0.0.1:1890 --token-file PRIVATE_FILE`, then `doctor --json`, `devices --json`, `status --json`, `watch` or `open`. `--wait` follows pushed completion; `--dry-run` validates a command. The CLI does not bypass the configured simulation allowlist.

Plugin0.2.0.0 loaded and the initial camera/mount operations ran in N.I.N.A.3.2.1.3002 on Parallels Windows ARM. The native simulator camera and official local OmniSim telescope were connected by CLI and webpage. Desktop1584×992 and mobile390px layouts were exercised through Playwright CLI. The initial concept is `docs/design/monitor-concept.png`; the implementation retains its dark palette, equipment rows, diagnostic graph and operation table. Real simulator values and available capabilities replace illustrative concept values. The monitor is an initial functional panel, not the production Starfront website.

The latest component run passed31 tests. The latest plugin cross-build passed with66 warnings; gateway build had zero warnings/errors. Runtime checks verified100 initial snapshots over100 sequential plugin reconnects, ending at one retained client, and authenticated request/body/Origin handling. Evidence: `artifacts/2026-10-22-integration/`. Source/build/runtime success does not establish real-equipment behavior.

See [RESOURCE-OBSERVATION.md](RESOURCE-OBSERVATION.md) for instrumentation, fixed log budgets, the running two-hour observation and unresolved validation. The unchanged-plugin comparison, realistic imaging, active sequences, more lifecycle/failure testing and dependency warning assessment remain pending. Production deployment and Advanced API removal are not authorized.

## Staged follow-up package

`scripts/package-plugin.sh [OUTPUT.zip]` builds an explicitly versioned five-file plugin package without copying to N.I.N.A. It refuses to overwrite an existing ZIP. The package contains only StarfrontControl.dll, StarfrontControl.Core.dll, EmbedIO.dll, Swan.Lite.dll and the plugin DLL configuration. Do not ship host NINA assemblies with it. The current staged 0.2.2.0 package and component hashes are in the integration artifact directory's `next-package.json`; the original running 0.2.0.0 package hashes are in `running-package.json`. Packaging has not installed the follow-up.

The staged CLI adds `--version --json` before authentication. Plugin application telemetry and gateway doctor derive their versions from the actual assemblies. The staged CLI/gateway synthetic checks passed; Windows lifecycle verification remains pending until the observation finishes.

The latest staged gateway selects workstation GC (`ServerGarbageCollection=false`) after the isolated local comparison described in RESOURCE-OBSERVATION.md. It reports actual mode and collection counts. This changes only the gateway's runtime configuration, not the N.I.N.A. process. The current bundle is0.2.2.0; earlier staging artifacts remain as evidence.

## Verified simulation follow-up: 0.2.2.0

After the user-ended observation, the staged 0.2.2.0 plugin was installed in the isolated Parallels VM. All five installed file sizes and SHA256 hashes matched the staged package. N.I.N.A. plugin UI showed 0.2.2.0; gateway doctor and installed CLI also report 0.2.2.0. Gateway now uses workstation GC; N.I.N.A.'s GC mode was not changed. The gateway runs from `artifacts/2026-10-22-integration/next-service-0.2.2`, with its matching browser assets. Private configuration and exact simulator allowlist were preserved.

Three runtime API Enabled off/on cycles passed with active clients and unchanged N.I.N.A. PID8848: each off persisted false, removed the port1889 listener and refused connections; each on persisted true, opened one listener, created a new telemetry session and automatically recovered pushed status. Evidence: `listener-lifecycle-0.2.2.json`. The UI driver needed foreground focus and DPI-aware coordinates; early driver attempts are not plugin failures.

Camera disconnect/reconnect succeeded; equivalent brace/uppercase request UUIDs deduplicated to the same operation (`commands-0.2.2.json`). Mount disconnect/reconnect succeeded with both transitions independently observed on a WebSocket (`mount-reconnect-0.2.2.json`). Authentication, Origin rejection, request-size limits and 100 reconnects passed again on the installed build, releasing both plugin client registries to one with zero publish errors (`live-checks-0.2.2.json`).

The actual updated browser recovered from offline state, displayed fresh connected simulator telemetry and operation results, and showed process diagnostics (`monitor-0.2.2.png`, final status/doctor JSON). Final source/component/synthetic evidence remains 32 core tests, six observation-analysis tests, CLI smoke and gateway lifecycle tests. No source changes were made in this follow-up beyond documentation; test helpers and evidence are local artifacts.

This completes the initial isolated plugin/gateway/WebSocket/monitor/CLI acceptance scope. The 93-minute resource measurement belongs to 0.2.0.0; the updated build has functional and lifecycle verification, not an equivalent long observation. Full exposures, advanced sequences, matched upstream performance and inherited dependency warnings remain deferred. No observatory work, real equipment, commit or push occurred.

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

### Temperature ramp handoff (0.4.20)

`camera/cancelTemperature` is a parameterless interrupt available only for a current Starfront-owned `camera/cool` or `camera/warm` operation. It cancels that operation's token and waits up to30seconds for its owner to exit. It never aborts exposures and rejects sequence/acquisition activity or unrelated/absent owners. It does not switch the cooler off directly or start another ramp. New ramps use the existing cool/warm endpoints after cancellation completion. Same-device interrupt reservations prevent overlapping interrupts; selected-device identity and simulation allowlists remain enforced.

The iOS app exposes the opposite mode during a verified temperature ramp only when this capability is advertised. It serializes cancellation→terminal-success→fresh owner-free telemetry→new ramp, retaining rig/session/camera identity, live heartbeat, capability and acquisition guards. Unknown/rejected cancellation, timeout, disconnect, changed identity or busy state sends no replacement. No automatic command retry. Local100ms waits watch pushed state only, stop within35seconds, and perform no HTTP polling. Active mode has a persistent colored outline and text; completed warming displays Cooler off from actual coolerOn=false. Existing60-second ramp request is unchanged; NINA settling can take longer.
