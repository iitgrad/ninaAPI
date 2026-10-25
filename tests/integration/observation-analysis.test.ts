import { expect, test } from 'bun:test';
import { analyze, type Report, type Sample } from '../../scripts/analyze-observation';
const MiB = 1024 * 1024;
function fixture() {
  const start = Date.parse('2026-01-01T00:00:00Z');
  const samples: Sample[] = Array.from({ length: 241 }, (_, index) => {
    const at = new Date(start + index * 30000).toISOString();
    const metrics = (processId: number) => ({ processId, observedAt: at, processPrivateBytes: 200 * MiB, workingSetBytes: 210 * MiB, managedLiveEstimateBytes: 40 * MiB, allocatedBytesTotal: index * 10 * MiB });
    return { at, elapsedSeconds: index * 30, plugin: metrics(101), service: metrics(102), bridgeConnected: true };
  });
  const report: Report = { status: 'completed', startedAt: new Date(start).toISOString(), endsAt: new Date(start + 7200000).toISOString(), sampleCount: samples.length, counters: { requestFailures: 0, socketFailures: 0 }, issues: [] };
  return { report, samples };
}
test('normal cumulative allocation growth is not labeled a retention failure', () => {
  const { report, samples } = fixture();
  expect(analyze(report, samples).assessment).toBe('no-trigger-observed');
});
test('process restarts invalidate a continuous stability claim', () => {
  const { report, samples } = fixture();
  samples.at(-1)!.plugin.processId = 999;
  const result = analyze(report, samples);
  expect(result.assessment).toBe('investigate');
  expect(result.issues.some(issue => issue.includes('process identity changed'))).toBe(true);
});
test('a completed label cannot substitute for elapsed coverage', () => {
  const { report, samples } = fixture();
  const result = analyze({ ...report, sampleCount: 20 }, samples.slice(0, 20));
  expect(result.completed).toBe(false);
  expect(result.assessment).toBe('incomplete');
});
test('sustained retained-memory growth triggers investigation', () => {
  const { report, samples } = fixture();
  for (const sample of samples) sample.plugin.processPrivateBytes = (200 + sample.elapsedSeconds / 20) * MiB;
  const result = analyze(report, samples);
  expect(result.assessment).toBe('investigate');
  expect(result.issues.some(issue => issue.includes('low-water increased'))).toBe(true);
});
test('stale diagnostics and sampling gaps remain visible', () => {
  const { report, samples } = fixture();
  samples[100].plugin.observedAt = samples[90].at;
  samples.splice(20, 4); report.sampleCount = samples.length;
  const result = analyze(report, samples);
  expect(result.assessment).toBe('investigate');
  expect(result.issues.some(issue => issue.includes('stale'))).toBe(true);
  expect(result.issues.some(issue => issue.includes('sampling gap'))).toBe(true);
});
test('unavailable mandatory measurements cannot yield a clean assessment', () => {
  const { report, samples } = fixture();
  samples[80].plugin.processPrivateBytes = null;
  expect(analyze(report, samples).assessment).toBe('investigate');
});
test('dashboard queue budget is read from diagnostics and older samples keep the original budget', () => {
  const { report, samples } = fixture();
  samples[80].plugin.pendingTopics = 12;
  samples[80].plugin.pendingTopicCapacity = 24;
  expect(analyze(report, samples).assessment).toBe('no-trigger-observed');
  samples[80].plugin.pendingTopics = 25;
  expect(analyze(report, samples).assessment).toBe('investigate');
  delete samples[80].plugin.pendingTopicCapacity;
  samples[80].plugin.pendingTopics = 9;
  expect(analyze(report, samples).assessment).toBe('investigate');
});
