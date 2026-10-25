# Start here

Read [PROJECT-CONTEXT.md](PROJECT-CONTEXT.md) before planning or changing this fork. It records Kevin's objectives, prior findings, decisions, evidence locations and proposed next steps. Read the upstream [README.md](README.md) and relevant source as well.

## Working scope

- This is Kevin's fork of N.I.N.A. Advanced API. The chosen backend language is C#/.NET; do not initiate a Rust rewrite.
- The product is Starfront Control: reliable push telemetry plus an eventual CLI/custom GUI for N.I.N.A. control. Implement incrementally; development authorization does not authorize issuing commands to real equipment or deploying to the observatory.
- Creating this fork and documenting it did not authorize deploying a plugin, enabling dashboard polling, installing software on the observatory NUC, connecting hardware, or running a live sequence. Opening this project must not trigger those actions.
- Keep simulation profiles, outputs and credentials separate from production. Never treat a GET request as read-only merely because of its HTTP verb; this upstream API includes control actions exposed as GETs.
- Diagnose from evidence. Polling was suspected of contributing to resource exhaustion; it has not been established as the root cause of all observed hangs.
- Preserve upstream license/copyright notices and unrelated changes. Use a `codex/` branch for new development unless Kevin specifies another branch. Make routine decisions within authorized scope without repeated confirmation requests.
- Distinguish source inspection, successful compilation, simulation tests and real-equipment validation. None implies the next one has passed.
- Update PROJECT-CONTEXT.md as decisions or verification results change. Do not copy credentials, access tokens or remote-access passwords into tracked files.

## Resume checklist

1. Read the context document and inspect `git status`, branch and remotes.
2. Development started at dev commit `353a853`; its only difference from release tag `2.2.15.2` is the changelog. Check for later upstream changes before selecting another baseline.
3. Confirm the current request before implementation. Local foundation changes now exist; read PROJECT-CONTEXT.md and docs/DEVELOPMENT.md for what has been implemented, tested and deferred.
4. Prefer local source inspection and a separate Windows simulation environment before any observatory work.

### Dashboard follow-up, current0.3.3.0

The prior observation is finished by user request; do not restart its workload or heartbeat. Read the latest PROJECT-CONTEXT.md section and docs/DASHBOARD-COVERAGE.md. Full sequence/image/telemetry dashboard is now installed in simulation; advanced capture→wait→complete is verified. Actual commands remain camera/mount connect/disconnect; new panels do not imply new control endpoints. The current gateway content root is artifacts/2026-10-22-dashboard/service-0.3.3. Work remains local/uncommitted; preserve existing edits.

Gateway-only follow-up0.3.4.0 removes the unlock form and automatically establishes a same-origin loopback browser session. The running content root is artifacts/2026-10-22-direct-open/service. Plugin and CLI remain0.3.3.0. Ordinary dashboard opening requires no access key or CLI command; preserve API auth and Origin/Host checks.
