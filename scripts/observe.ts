// Run with Bun. Reads private credentials from the service configuration;
// writes metrics and counters only. No equipment commands are issued.
import { mkdir, writeFile, appendFile, rename } from 'node:fs/promises';
import { join } from 'node:path';
import { homedir } from 'node:os';

type Metric = Record<string, number | boolean | string | null>;
type Sample = { at: string; elapsedSeconds: number; plugin: Metric; service: Metric; bridgeConnected: boolean };
const args = Bun.argv.slice(2);
const seconds = args.includes('--seconds') ? Number(args[args.indexOf('--seconds') + 1]) : 7200;
const output = args.includes('--out') ? args[args.indexOf('--out') + 1] : join('artifacts', `observation-${new Date().toISOString().replace(/[:.]/g, '-')}`);
if (!Number.isInteger(seconds) || seconds < 30 || seconds > 14400) throw new Error('Duration must be 30–14400 seconds.');
await mkdir(output, { recursive: false, mode: 0o700 });
const configuration = await Bun.file(process.env.STARFRONT_SERVICE_CONFIG ?? join(homedir(), '.config/starfront-control/service.json')).json();
const start = Date.now();
const stopAt = start + seconds * 1000;
const samples: Sample[] = []; // At most 481 records, each source has a fixed schema.
const counters = { requests: 0, requestFailures: 0, socketFrames: 0, socketFailures: 0, reconnects: 0 };
const sockets = new Set<WebSocket>();
const issues = new Set<string>();
let stopping = false;
let writtenBytes = 0;
const sleep = (ms: number) => Bun.sleep(ms);

async function get(base: string, path: string, token: string) {
  const response = await fetch(new URL(path, base), { headers: { Authorization: `Bearer ${token}` }, signal: AbortSignal.timeout(5000) });
  if (!response.ok) throw new Error('Request failed');
  return response.json();
}
function socket(base: string, token: string, path: string): Promise<WebSocket> {
  return new Promise((resolve, reject) => {
    const url = new URL(path, base); url.protocol = url.protocol === 'https:' ? 'wss:' : 'ws:';
    const ws = new WebSocket(url, { headers: { Authorization: `Bearer ${token}` } });
    sockets.add(ws);
    const timeout = setTimeout(() => { ws.terminate(); reject(new Error('Socket handshake deadline')); }, 5000);
    ws.onopen = () => { clearTimeout(timeout); resolve(ws); };
    ws.onmessage = () => counters.socketFrames++;
    ws.onerror = () => { clearTimeout(timeout); counters.socketFailures++; reject(new Error('Socket failed')); };
    ws.onclose = () => { clearTimeout(timeout); sockets.delete(ws); reject(new Error('Socket closed before opening')); };
  });
}
function assess(): string[] {
  const warm = samples.filter(sample => sample.elapsedSeconds >= 900);
  if (warm.length < 60) return ['Waiting for warm-up and sufficient samples.'];
  const findings: string[] = [];
  const first = warm.slice(0, 30), last = warm.slice(-30);
  for (const component of ['plugin', 'service'] as const) {
    for (const field of ['processPrivateBytes', 'managedLiveEstimateBytes', 'workingSetBytes']) {
      const low = (group: Sample[]) => Math.min(...group.map(sample => sample[component][field]).filter((value): value is number => typeof value === 'number' && value > 0));
      const before = low(first), after = low(last);
      if (!Number.isFinite(before) || !Number.isFinite(after)) continue;
      const growth = (after - before) / 1048576;
      findings.push(`${component}.${field}: low-water change ${growth.toFixed(1)} MiB after warm-up.`);
      if (growth > 100) issues.add(`${component}.${field} grew more than 100 MiB after warm-up; investigate retention.`);
    }
  }
  return findings;
}
async function report(status: string) {
  const findings = assess();
  const data = { status, processId: process.pid, startedAt: new Date(start).toISOString(), endsAt: new Date(stopAt).toISOString(),
    updatedAt: new Date().toISOString(), sampleCount: samples.length, counters, issues: [...issues], findings,
    workload: { cachedPluginGetsPerSecond: 5, cachedGatewayGetsPerSecond: 2, gatewaySockets: 4, bridgeReconnectIntervalSeconds: 30 },
    latest: samples.at(-1), note: 'Whole-process measurements. Simulation telemetry workload; no exposures or equipment commands. Stable results cannot prove absence of every leak.' };
  const temporary = join(output, 'report.tmp');
  await writeFile(temporary, JSON.stringify(data, null, 2), { mode: 0o600 });
  await rename(temporary, join(output, 'report.json'));
}
async function sample() {
  try {
    const [state, doctor] = await Promise.all([
      get(configuration.listenUrl, '/api/v1/status', configuration.token),
      get(configuration.listenUrl, '/api/v1/doctor', configuration.token)
    ]);
    const plugin = state.snapshot?.states?.diagnostics?.value ?? {};
    const entry: Sample = { at: new Date().toISOString(), elapsedSeconds: Math.round((Date.now() - start) / 1000), plugin, service: doctor.diagnostics ?? {}, bridgeConnected: state.bridge.connected };
    const previous = samples.at(-1);
    if (!entry.bridgeConnected) issues.add('Bridge was offline during sampling.');
    if (!plugin.observedAt || Date.now() - Date.parse(String(plugin.observedAt)) > 35000) issues.add('Plugin diagnostics were stale.');
    if (previous && previous.plugin.processId !== plugin.processId) issues.add('N.I.N.A. process changed during observation.');
    if (previous && previous.service.processId !== entry.service.processId) issues.add('Gateway process changed during observation.');
    if (Number(plugin.pendingTopics) > 8 || Number(plugin.retainedOperations) > 128 || Number(plugin.retainedTransportClients) > 8) issues.add('A plugin resource exceeded its bound.');
    if (Number(plugin.ownLogBytes) > 20971520 || Number(entry.service.ownMetricLogBytes) > 20971520) issues.add('Diagnostic logs exceeded their budget.');
    if (Number(plugin.publishErrors) > 0 || Number(plugin.diagnosticErrors) > 0 || Number(entry.service.diagnosticWriteErrors) > 0) issues.add('Diagnostic or publishing errors were recorded.');
    if (Number(plugin.ninaLogScanTruncated)) issues.add('N.I.N.A. log scan exceeded 1024 files; total is incomplete.');
    if (samples.length && Number(plugin.ninaLogBytes) - Number(samples[0].plugin.ninaLogBytes) > 10 * 1048576) issues.add('N.I.N.A. logs grew more than 10 MiB during this run.');
    const line = JSON.stringify(entry) + '\n';
    writtenBytes += Buffer.byteLength(line);
    if (writtenBytes > 16 * 1048576) throw new Error('Observation output budget exceeded');
    samples.push(entry);
    await appendFile(join(output, 'samples.jsonl'), line, { mode: 0o600 });
  } catch { issues.add('A metrics sample could not be captured.'); }
  await report('running');
}
async function polling(base: string, token: string, period: number) {
  while (!stopping && Date.now() < stopAt) {
    const tick = Date.now();
    try { await get(base, '/api/v1/status', token); counters.requests++; } catch { counters.requestFailures++; }
    await sleep(Math.max(0, period - (Date.now() - tick)));
  }
}
async function steadySocket() {
  while (!stopping && Date.now() < stopAt) {
    try {
      const ws = await socket(configuration.listenUrl, configuration.token, '/socket');
      while (!stopping && Date.now() < stopAt && ws.readyState === WebSocket.OPEN) await sleep(1000);
      ws.close();
    } catch { }
    if (!stopping) await sleep(1000);
  }
}
async function reconnectingSocket() {
  while (!stopping && Date.now() < stopAt) {
    try {
      const ws = await socket(configuration.bridgeUrl, configuration.bridgeToken, '/socket/v1');
      counters.reconnects++;
      await sleep(Math.min(1000, Math.max(0, stopAt - Date.now()))); ws.close();
    } catch { }
    await sleep(Math.min(29000, Math.max(0, stopAt - Date.now())));
  }
}
process.on('SIGTERM', () => { stopping = true; });
process.on('SIGINT', () => { stopping = true; });
await writeFile(join(output, 'process.json'), JSON.stringify({ processId: process.pid, startedAt: new Date(start).toISOString() }), { mode: 0o600 });
const jobs = [polling(configuration.bridgeUrl, configuration.bridgeToken, 200), polling(configuration.listenUrl, configuration.token, 500), ...Array.from({ length: 4 }, steadySocket), reconnectingSocket()];
try {
  while (!stopping && Date.now() < stopAt) { await sample(); await sleep(Math.min(30000, Math.max(0, stopAt - Date.now()))); }
} finally {
  stopping = true;
  for (const ws of sockets) ws.terminate();
  await Promise.allSettled(jobs);
  await sample();
  if (counters.requestFailures) issues.add('One or more workload requests failed.');
  if (counters.socketFailures) issues.add('One or more WebSocket connections failed.');
  await report(Date.now() >= stopAt ? 'completed' : 'interrupted');
  console.log(JSON.stringify({ output, sampleCount: samples.length, issueCount: issues.size }));
}
