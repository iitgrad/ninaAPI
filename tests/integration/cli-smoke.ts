// Synthetic loopback peer: validates the built CLI without calling N.I.N.A.
import { resolve } from 'node:path';
const cli = resolve(Bun.argv[2] ?? 'src/StarfrontControl.Cli/bin/Release/net8.0/starfront-control.dll');
const identifier = 'abcdef12-3456-4789-abcd-123456789012';
let requests = 0;
let receivedId: string | undefined;
const server = Bun.serve({
  hostname: '127.0.0.1', port: 0,
  async fetch(request, server) {
    const path = new URL(request.url).pathname;
    if (path === '/socket' && server.upgrade(request)) return;
    if (path !== '/api/v1/commands/validate' || request.method !== 'POST') return new Response(null, { status: 404 });
    requests++;
    const command = await request.json() as { requestId: string };
    receivedId = command.requestId;
    return Response.json({ valid: true, dryRun: true, command });
  },
  websocket: { open(socket) { socket.send(JSON.stringify({ sample: 1 })); socket.send(JSON.stringify({ sample: 2 })); socket.close(); }, message() { } }
});
const environment = { ...process.env, STARFRONT_TOKEN: 'synthetic-cli-test-token-only'.repeat(2), STARFRONT_URL: `http://127.0.0.1:${server.port}` };
async function run(args: string[]) {
  const child = Bun.spawn(['dotnet', cli, ...args], { env: environment, stdout: 'pipe', stderr: 'pipe', stdin: 'ignore' });
  const timer = setTimeout(() => child.kill(), 8000);
  try { return { exit: await child.exited, output: await new Response(child.stdout).text() }; }
  finally { clearTimeout(timer); }
}
try {
  const valid = await run(['camera', 'connect', '--device-id', 'synthetic-camera', '--request-id', `{${identifier.toUpperCase()}}`, '--dry-run', '--json']);
  if (valid.exit !== 0 || receivedId !== identifier || JSON.parse(valid.output).command.requestId !== identifier) throw new Error('UUID canonicalization failed');
  const invalid = await run(['camera', 'connect', '--device-id', 'synthetic-camera', '--request-id', 'invalid', '--dry-run', '--json']);
  if (invalid.exit === 0 || requests !== 1 || !JSON.parse(invalid.output).error.includes('UUID')) throw new Error('Invalid UUID reached peer');
  const watch = await run(['watch']);
  const records = watch.output.trim().split('\n').map(line => JSON.parse(line));
  if (records[0]?.sample !== 1 || records[1]?.sample !== 2) throw new Error('Default watch output is not NDJSON');
  console.log(JSON.stringify({ peer: 'synthetic loopback only', uuidCanonicalized: true, invalidUuidRejectedBeforeRequest: true, watchNdjson: true }));
} finally { server.stop(true); }
