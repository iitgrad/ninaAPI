# Native Starfront notifications

The existing StarfrontControl.Service hosts an additive `/mobile/notifications`
namespace. Its upstream BridgeConnection publishes the same bounded pushed
snapshots into a 32-entry notification inbox; no new upstream connection, polling
or equipment control is introduced. Portable NotificationProjection derives
sequence transitions, targets, failed instructions, connected safety transitions,
flat-filter activity and first/every-fifth LIGHT previews from those snapshots.
Reconnection baselines suppress stale replays; this is state-transition detection,
not a guarantee of capturing every intermediate event during outages/coalescing.

APNs uses direct HTTP/2 + ES256 signing. Private configuration resides in
`~/.config/starfront-web/Notifications/apns.json` with TeamId, KeyId, KeyPath,
Topic, PublicOrigin, ImageInterval fields. The downloaded .p8 is outside source,
mode0600, in the same0700 directory. This rollout's key is dedicated to the
Starfront app's sandbox topic; no other key was changed or revoked.

Enrollment POST `/pair` uses the existing dashboard PIN with its persisted
20-attempt/15-minute limit. It returns a random256-bit notification-only device
credential; only its hash is stored. The remaining status/events/preferences,
token rotation, test, receipt and unregister routes require that device credential
in X-Starfront-Device-Id and X-Starfront-Device-Key. None of these credentials
works for dashboard, telemetry or equipment APIs. Cross-origin browser requests
are rejected. Up to8 devices are retained. Old/invalid APNs tokens are retired.
One test request per10 seconds per device is allowed.

The sender keeps200 event records, exposes100 to the phone and writes per-device
APNs acceptance/failure outcomes. Accepted-by-Apple is not proof of phone display.
Foreground/tap callbacks record received-in-app/opened-on-phone. Interrupted or
failed sends are not blindly replayed. Network failure cannot fail a sequence.

Up to512 previews of100KB each are retained on private disk for7 days behind
192-bit random media capabilities; the oldest is evicted if the item limit is
reached. Capability keys and original JPEG bytes survive gateway restarts. The
index is mode0600 inside a0700 directory. Preview content remains the latest
pushed camera preview, not a processed stack or a guaranteed filename-matched
raw frame. The iOS service extension accepts only this exact HTTPS host/path.
The app first uses the original iOS notification attachment or its bounded
on-device JPEG cache, then the server. Expired server-only images cannot be
recreated after their original attachment is gone.

Notification copies use a gentler midtones curve: a20% input level maps to8%,
with black and white endpoints preserved and no additional shadow clipping.
The service transforms only the bounded pushed JPEG, once per fresh preview,
using SkiaSharp3.119.4. Inputs must be JPEG, at most100KB and800px per dimension;
output remains at most100KB. Invalid previews are omitted. This changes neither
FITS, N.I.N.A. stretch/capture settings nor dashboard JPEGs. Existing notification
attachments/cache entries retain their original bytes; new notifications use
the darker copy. No denoising or stack processing is applied.

The iOS source remains in Starfront Operations/apps/StarfrontIOS. Its original
bundle/team/rig Keychain identity are preserved; notification credentials have a
separate Keychain service. During the initial additive rollout no Ground Station setting, plugin installation,
saved sequence or Discord instruction was disabled or removed. See the subsequent
authorized migration below. Migrate
those only after native phone delivery is verified and Kevin authorizes removal.

Validation: portable projection tests; service authentication/persistence/APNs
signature tests; isolated real-HTTP admission tests without any equipment calls;
signed device and simulator builds. Dated deployment and phone-install evidence
is under Starfront Operations/.codex-artifacts/2026-10-24-native-notifications.


## October 24 migration and custom sequence instruction

After Kevin confirmed the native phone test worked, he authorized removing the
old senders. Ground Station was moved outside NINA's plugin directory into the
NUC migration backup; automatic Discord images were disabled in the active rig
profile. The current 2026-10-23 sequence (both root-library and Targets copies)
was cleaned of 24 Discord messages and one failure trigger, and six reusable
templates were cleaned of 18 Discord instructions. Historical night sequences
were preserved. New plans should use the cleaned working sequence/templates;
do not reintroduce Ground Station or Discord dependencies.

Plugin 0.4.22 adds **Starfront Control → Send Notification**. Drag it anywhere in
an Advanced Sequence, enter a literal message (1–512 characters), and optionally
check **Include latest camera preview**. It queues a local pushed event without
waiting for APNs. The gateway treats explicit messages as sequence notifications;
the phone's Sequence preference and overall enable switch apply. No webhook,
server credentials or extra polling is required in the instruction. It does not
capture a new exposure. Without a recent preview, the notification remains text.
The existing Send WebSocket Event instruction keeps its original behavior.

The local event window retains the latest 16 messages with monotonic numbers.
Repeated identical text remains distinct. The gateway deduplicates observations
and establishes a baseline after reconnect, so it does not replay old messages.
This bounded, best-effort path is not a guaranteed delivery queue across outages.
Native host discovery/clone/execution of two messages was verified in Parallels;
no production sequence was started to test this instruction. 103 Core and two
sender tests pass. Exact package and live installation evidence is in dated
native-notifications artifacts in both repositories.

Safety push titles now use the site terminology **Roof closed** for a connected
monitor's safe-to-unsafe transition and **Roof open** for unsafe-to-safe. These
labels reflect the existing safety signal; no additional roof sensor, polling or
roof-control action was introduced. Disconnected/unknown is not called unsafe.

The web sequence display shows each Send Notification message and explicit
Attach photo:on/off state. The staged0.4.24 plugin also exposes up to512 message
characters, preserving the instruction's supported text length.
