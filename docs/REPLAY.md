# Recorded-night dashboard replay

The web dashboard has Live and Replay data sources. Replay uses the same equipment,
image, graph and sequence renderers. It never forwards a journal to the live NINA
bridge or the push-notification observer.

Open **Recordings**, choose a closed recording, and scrub the bottom timeline.
Playback supports 1×, 10×, 60× and 300×, previous/next event, event selection and
Return to Live. Times are labeled Central (America/Chicago). Simulation recordings
are labeled, active recordings cannot be opened, and the first view is the first
available telemetry snapshot. The underlying stream remains subscribed while
viewing history, so returning to Live uses the latest pushed state.

Replay is read-only. Both command availability and the common POST transport guard
reject equipment/configuration commands while replaying. Editors are hidden or
disabled; historical events do not generate push notifications. A gap shows missing
telemetry and the last recorded update, without inventing roof status or movement.
Image previews appear only when the original recording contains them. Autofocus
previews use the same archive path and do not require an ImageSaved/science FITS
event; regression tests verify forward and backward replay of focus frames. A past night
with no saved previews will have an empty image panel.

## LXC reader

Authenticated GET routes in the existing gateway:

- `/api/v1/recordings`: recording metadata.
- `/api/v1/recordings/{id}`: timeline, event markers and gaps.
- `/api/v1/recordings/{id}/state?at=EPOCH_MILLISECONDS`: reconstructed snapshot.

The website proxy explicitly allowlists these routes, requires its existing
credentials/session and rejects POST. No new public listener or NINA polling is
introduced. The API can later be reused by the iOS app; this release adds web UI.

The gateway reads the recorder's closed gzip NDJSON journals. It builds compressed
minute chunks with initial state seeds in a separate disposable cache, leaving
original recordings untouched. Each seek reads just the selected chunk. Camera temperature/cooler-power history
(up to720 samples over the preceding hour) is carried in the chunk seeds and
returned as `cameraThermal`. The Replay camera card plots it beneath the numeric
readings, including on direct and backward seeks. Only samples at or before the
selected instant are returned; gaps/disconnections break the curve, and camera
or plugin-session changes reset it. Cache format replay-2 rebuilds existing
disposable indexes automatically. Checkpoints,
removals, session changes, recorded image references and connection gaps are preserved.
Step receipt timing is derived only from observed transitions, with gaps breaking
continuity; initial active steps get Seen rather than an invented Start.

From gateway0.4.30, `ReplayIndexWarmup` watches local `manifest.json` changes and
prepares completed/interrupted recordings after they close. A coalesced signal
queue serializes work through the existing replay gate. Startup catches finishes
missed while the gateway was offline; a30-second local filesystem scan recovers
missed watcher events and a late-created recordings directory. It never queries
NINA or changes the recorder. Only the two most recent closed recordings are
prepared; unchanged manifests are not reprocessed. Failures preserve originals,
log a bounded warning, and leave on-demand replay available. Failed manifests are
not retried repeatedly; a metadata change or gateway restart permits another try.
Cancellation removes partial build output. Older evicted nights still index on demand.

Optional server configuration: `recordingsDirectory`, `replayCacheDirectory`.
Defaults are `~/.local/share/starfront-recorder/recordings` and
`~/.local/share/starfront-web/replay-cache`. Paths must not overlap. Original files
and metadata must not be symlinks. Cache creation reserves 512 MiB free disk, uses a
512 MiB build bound and retains at most two cached recordings; stale build caches
are disposable. Markers are bounded at 10,000 and metadata reports omissions. Input
journals are limited to 256 segments/1 GiB. Oversized or invalid journals fail visibly,
without altering originals. Interrupted final compressed members may be incomplete;
readable complete records remain usable and the timeline reports truncation.

## Verification

21 service tests include state reconstruction, checkpoint/removal boundaries,
backward seeks, image references, gaps, receipt timing, integrity checks and unsafe
path rejection, automatic finish detection, startup warming, unchanged-cache reuse
and malformed-journal isolation. Website proxy tests cover authentication, read-only routes and time
query forwarding. Browser acceptance uses an isolated synthetic live upstream and
an unchanged copy of the October24 night: select/scrub/play/pause/gap/return, disabled
controls, no equipment POSTs, no runtime errors, desktop and390px rendering.

Production LXC verification independently replayed the same recording, built its
index in25.62seconds under the existing CPU limit, and sought in4–67milliseconds.
Gateway peak memory was133,484,544bytes. Recorder PID and connection stayed unchanged.
Evidence: Operations/.codex-artifacts/2026-10-25-dashboard-replay/.
