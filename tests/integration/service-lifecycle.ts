// Isolated gateway lifecycle against a synthetic plugin. Never uses real credentials.
import { mkdtemp, writeFile, rm, access } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
const executable = resolve(Bun.argv[2] ?? 'artifacts/2026-10-22-integration/sandbox-service/StarfrontControl.Service.dll');
const directory = await mkdtemp(join(tmpdir(), 'starfront-service-test-'));
const token = 'synthetic-service-lifecycle-credential'.repeat(2);
const observedAt = '2026-01-01T00:00:00+00:00';
let online = true, forwarded = 0, upstreamConnections = 0;
const upstreamSockets = new Set<{ close(): void }>();
const snapshot = JSON.stringify({ schemaVersion: 1, sessionId: 'synthetic-session', revision: 1, states: { camera: { observedAt, value: { connected: true, name: 'Synthetic camera' } } } });
const upstream = Bun.serve({
  hostname: '127.0.0.1', port: 0,
  async fetch(request, server) {
    if (request.headers.get('upgrade') === 'websocket' && server.upgrade(request)) return;
    if (request.method === 'POST') { forwarded++; return Response.json({ accepted: true }, { status: 202 }); }
    return new Response(null, { status: 404 });
  },
  websocket: {
    open(socket) { upstreamConnections++; upstreamSockets.add(socket); if (online) socket.send(snapshot); else socket.close(); },
    close(socket) { upstreamSockets.delete(socket); }, message() { }
  }
});
const reservation = Bun.serve({ hostname: '127.0.0.1', port: 0, fetch: () => new Response() });
const listenUrl = `http://127.0.0.1:${reservation.port}`;
reservation.stop(true);
const configPath = join(directory, 'service.json');
await writeFile(configPath, JSON.stringify({ bridgeUrl: `http://127.0.0.1:${upstream.port}`, bridgeToken: token, token, listenUrl }), { mode: 0o600 });
let child: ReturnType<typeof Bun.spawn> | undefined;
function start() { child = Bun.spawn(['dotnet', executable, '--contentRoot', directory], { env: { ...process.env, STARFRONT_SERVICE_CONFIG: configPath }, stdout: 'ignore', stderr: 'ignore', stdin: 'ignore' }); }
async function request(path: string, options: RequestInit = {}) {
  return fetch(listenUrl + path, { ...options, headers: { Authorization: `Bearer ${token}`, ...options.headers }, signal: AbortSignal.timeout(2000) });
}
async function until(predicate: () => Promise<boolean>, label: string) {
  const end = Date.now() + 15000;
  while (Date.now() < end) { try { if (await predicate()) return; } catch { } await Bun.sleep(100); }
  throw new Error('Deadline: ' + label);
}
async function state() { return (await request('/api/v1/status')).json(); }
async function stop() {
  if (!child) return;
  const process = child; child = undefined; process.kill('SIGTERM');
  let timer: ReturnType<typeof setTimeout>;
  try {
    await Promise.race([process.exited, new Promise((_, reject) => { timer = setTimeout(() => { process.kill('SIGKILL'); reject(new Error('Gateway did not shut down gracefully')); }, 5000); })]);
  } finally { clearTimeout(timer!); }
}
try {
  start();
  await until(async () => (await state()).bridge.connected, 'initial upstream connection');
  for (const headers of [{}, { Origin: 'https://example.invalid' }, { Origin: listenUrl, Host: 'rebound.invalid' }]) {
    const denied = await fetch(listenUrl + '/auth/local-session', { method: 'POST', headers });
    if (denied.status !== 401) throw new Error('Cross-site or wrong-host automatic session accepted');
  }
  const local = await fetch(listenUrl + '/auth/local-session', { method: 'POST', headers: { Origin: listenUrl } });
  const localCookie = local.headers.get('set-cookie')?.split(';')[0];
  if (local.status !== 200 || !localCookie || (await fetch(listenUrl + '/api/v1/status', { headers: { Cookie: localCookie } })).status !== 200)
    throw new Error('Automatic local browser session failed');
  const ticket = await (await request('/api/v1/browser-session', { method: 'POST' })).json();
  const login = await request('/auth/session', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ ticket: ticket.ticket }) });
  const cookie = login.headers.get('set-cookie')?.split(';')[0];
  if (login.status !== 200 || !cookie) throw new Error('Browser ticket exchange');
  const session = await fetch(listenUrl + '/api/v1/status', { headers: { Cookie: cookie } });
  if (session.status !== 200) throw new Error('Session cookie authentication');
  const repeat = await request('/auth/session', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ ticket: ticket.ticket }) });
  if (repeat.status !== 401) throw new Error('One-use ticket was reusable');
  const sequence=(state:string)=>JSON.stringify({schemaVersion:1,sessionId:'synthetic-session',revision:2,states:{camera:{observedAt,value:{connected:true}},sequence:{observedAt,value:{sequenceId:'timing-test',nodes:[{id:'step',state}]}}}});
  for(const w of upstreamSockets)w.send(sequence('pending'));
  await until(async()=>!!(await state()).snapshot.states.sequence,'timing baseline');
  for(const w of upstreamSockets)w.send(sequence('running'));
  await until(async()=>!!(await state()).snapshot.states.sequence.value.nodes[0].timing?.startedAt,'observed start delivered');
  const started=(await state()).snapshot.states.sequence.value.nodes[0].timing.startedAt;
  for(const w of upstreamSockets)w.send(sequence('completed'));
  await until(async()=>!!(await state()).snapshot.states.sequence.value.nodes[0].timing?.endedAt,'observed end delivered');
  const timing=(await state()).snapshot.states.sequence.value.nodes[0].timing;
  if(timing.startedAt!==started||timing.source!=='gateway-received')throw Error('Shared timing provenance lost');
  online = false;
  for (const socket of upstreamSockets) socket.close();
  await until(async () => !(await state()).bridge.connected, 'upstream disconnect reflected');
  const offline = await state();
  if (offline.snapshot.states.camera.observedAt !== observedAt) throw new Error('Offline transition changed source timestamp');
  const blocked = await request('/api/v1/commands', { method: 'POST', body: '{}' });
  if (blocked.status !== 503 || forwarded !== 0) throw new Error('Offline write was forwarded');
  online = true;
  await until(async () => (await state()).bridge.connected, 'upstream reconnect');
  await new Promise<void>((resolve, reject) => {
    let received = false;
    const ws = new WebSocket(listenUrl.replace('http:', 'ws:') + '/socket', { headers: { Authorization: `Bearer ${token}` } });
    const timer = setTimeout(() => { ws.terminate(); reject(new Error('Browser socket deadline')); }, 5000);
    ws.onmessage = event => { received = JSON.parse(String(event.data)).bridge.connected === true; ws.close(); };
    ws.onclose = () => { clearTimeout(timer); if (received) resolve(); else reject(new Error('Reconnect snapshot missing')); }; ws.onerror = () => { clearTimeout(timer); reject(new Error('Browser socket')); };
  });
  await until(async () => (await (await request('/api/v1/doctor')).json()).clients === 0, 'browser subscription cleanup');
  await until(async () => { await access(join(directory, 'Diagnostics/Metrics/metrics-0.jsonl')); return true; }, 'metrics follow custom configuration directory');
  const doctor = await (await request('/api/v1/doctor')).json();
  if (doctor.diagnostics.serverGc !== false) throw new Error('Local gateway must use its configured workstation GC');
  const firstPid = child!.pid;
  await stop();
  start();
  await until(async () => (await state()).bridge.connected, 'gateway restart reconnect');
  if (child!.pid === firstPid) throw new Error('Gateway did not restart');
  await stop();
  console.log(JSON.stringify({ peer: 'synthetic loopback only', serviceVersion: doctor.version, serverGc: doctor.diagnostics.serverGc, browserTicketOneUse: true, cookieAuthentication: true, offlineStateRetainsSourceTime: true, offlineWritesNotForwarded: true, upstreamReconnect: true, browserSubscriptionReleased: true, customConfigLogsIsolated: true, gracefulStopAndRestart: true, upstreamConnections }));
} finally {
  if (child) { child.kill('SIGKILL'); await child.exited; }
  upstream.stop(true);
  await rm(directory, { recursive: true, force: true });
}
