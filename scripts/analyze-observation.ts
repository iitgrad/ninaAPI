// Reads saved measurements only. Does not query or control any running service.
import { join } from 'node:path';

type Metric = Record<string, unknown>;
export type Sample = { at: string; elapsedSeconds: number; plugin: Metric; service: Metric; bridgeConnected: boolean };
export type Report = { status: string; startedAt: string; endsAt: string; sampleCount: number; counters: { requestFailures: number; socketFailures: number }; issues: string[] };
const MiB = 1024 * 1024;
const memoryFields = ['processPrivateBytes', 'workingSetBytes', 'managedLiveEstimateBytes'];
const fields = [...memoryFields, 'gcHeapBytes', 'gcCommittedBytes', 'gcFragmentedBytes', 'allocatedBytesTotal', 'cpuSecondsTotal', 'handles', 'threads', 'gen0', 'gen1', 'gen2', 'pendingTopics', 'clients', 'retainedTransportClients', 'retainedOperations', 'ninaLogBytes', 'ownLogBytes', 'ownMetricLogBytes', 'publishErrors', 'diagnosticErrors', 'diagnosticWriteErrors'];
function numeric(metric: Metric, field: string): number | undefined {
  const value = metric[field];
  return typeof value === 'number' && Number.isFinite(value) ? value : undefined;
}
function slope(points: { seconds: number; value: number }[]): number | null {
  if (points.length < 2) return null;
  const meanX = points.reduce((sum, p) => sum + p.seconds, 0) / points.length;
  const meanY = points.reduce((sum, p) => sum + p.value, 0) / points.length;
  const denominator = points.reduce((sum, p) => sum + (p.seconds - meanX) ** 2, 0);
  return denominator ? points.reduce((sum, p) => sum + (p.seconds - meanX) * (p.value - meanY), 0) / denominator * 3600 : null;
}

export function analyze(report: Report, samples: Sample[]) {
  const issues = new Set(report.issues);
  const plannedSeconds = (Date.parse(report.endsAt) - Date.parse(report.startedAt)) / 1000;
  const coverageSeconds = samples.length ? samples.at(-1)!.elapsedSeconds - samples[0].elapsedSeconds : 0;
  let largestSampleGapSeconds = 0;
  let largestDiagnosticAgeSeconds = 0;
  if (!Number.isFinite(plannedSeconds) || plannedSeconds <= 0) issues.add('Invalid planned duration.');
  if (!samples.length) issues.add('No samples.');
  if (samples.length !== report.sampleCount) issues.add('Report/sample counts differ.');
  for (let index = 0; index < samples.length; index++) {
    const sample = samples[index];
    if (!Number.isFinite(Date.parse(sample.at)) || !Number.isFinite(sample.elapsedSeconds)) issues.add('Invalid sample timestamp.');
    if (index) {
      const gap = sample.elapsedSeconds - samples[index - 1].elapsedSeconds;
      if (gap < 0) issues.add('Sample time moved backwards.');
      largestSampleGapSeconds = Math.max(largestSampleGapSeconds, gap);
    }
    if (!sample.bridgeConnected) issues.add('Bridge was offline.');
    for (const component of ['plugin', 'service'] as const) {
      const metric = sample[component];
      const requiredMemory = component === 'plugin' ? memoryFields : ['workingSetBytes', 'managedLiveEstimateBytes'];
      for (const field of requiredMemory)
        if (numeric(metric, field) === undefined || numeric(metric, field)! <= 0) issues.add(`${component}: ${field} measurement missing or invalid.`);
      if (typeof metric.observedAt !== 'string' || !Number.isFinite(Date.parse(metric.observedAt))) {
        issues.add(`${component}: missing diagnostic timestamp.`);
      } else {
        const age = (Date.parse(sample.at) - Date.parse(metric.observedAt)) / 1000;
        largestDiagnosticAgeSeconds = Math.max(largestDiagnosticAgeSeconds, age);
        if (age > 35 || age < -5) issues.add(`${component}: diagnostic timing was stale or inconsistent.`);
      }
      for (const error of ['publishErrors', 'diagnosticErrors', 'diagnosticWriteErrors'])
        if ((numeric(metric, error) ?? 0) > 0) issues.add(`${component}: ${error} recorded.`);
      for (const field of ['ownLogBytes', 'ownMetricLogBytes'])
        if ((numeric(metric, field) ?? 0) > 20 * MiB) issues.add(`${component}: ${field} exceeded 20 MiB.`);
    }
  }
  if (largestSampleGapSeconds > 90) issues.add('A sampling gap exceeded 90 seconds.');
  if (samples.some(sample => (numeric(sample.plugin, 'pendingTopics') ?? 0) > (numeric(sample.plugin, 'pendingTopicCapacity') ?? 8) || (numeric(sample.plugin, 'retainedTransportClients') ?? 0) > 8 || (numeric(sample.plugin, 'retainedOperations') ?? 0) > 128)) issues.add('A plugin collection exceeded its configured bound.');
  if (report.counters.requestFailures > 0 || report.counters.socketFailures > 0) issues.add('Workload requests or sockets failed.');
  if (![report.counters.requestFailures, report.counters.socketFailures].every(value => Number.isFinite(value) && value >= 0)) issues.add('Workload failure counters are missing or invalid.');
  const components: Record<string, unknown> = {};
  for (const component of ['plugin', 'service'] as const) {
    const processIds = [...new Set(samples.map(sample => numeric(sample[component], 'processId')))];
    const unchangedProcess = processIds.length === 1 && processIds[0] !== undefined;
    if (!unchangedProcess) issues.add(`${component}: process identity changed or was missing.`);
    const measurements: Record<string, unknown> = {};
    for (const field of fields) {
      const points = samples.flatMap(sample => {
        const value = numeric(sample[component], field);
        return value === undefined ? [] : [{ seconds: sample.elapsedSeconds, value }];
      });
      if (!points.length) { measurements[field] = null; continue; }
      const warm = points.filter(point => point.seconds >= 900);
      // Require two non-overlapping 15-minute windows after warm-up.
      const windowReady = unchangedProcess && warm.length >= 60 && warm.at(-1)!.seconds - warm[0].seconds >= 1770;
      const firstWindow = windowReady ? warm.filter(point => point.seconds < warm[0].seconds + 900) : [];
      const lastWindow = windowReady ? warm.filter(point => point.seconds > warm.at(-1)!.seconds - 900) : [];
      const lowWaterChange = windowReady ? Math.min(...lastWindow.map(p => p.value)) - Math.min(...firstWindow.map(p => p.value)) : null;
      const isMemory = memoryFields.includes(field);
      const trend = unchangedProcess ? slope(warm) : null;
      if (isMemory && lowWaterChange !== null && lowWaterChange > 100 * MiB) issues.add(`${component}.${field}: post-warm-up low-water increased more than 100 MiB.`);
      measurements[field] = {
        samples: points.length, first: points[0].value, last: points.at(-1)!.value,
        minimum: Math.min(...points.map(p => p.value)), maximum: Math.max(...points.map(p => p.value)),
        delta: points.at(-1)!.value - points[0].value,
        ...(isMemory ? { postWarmupSlopeMiBPerHour: trend === null ? null : trend / MiB,
          postWarmupLowWaterChangeMiB: lowWaterChange === null ? null : lowWaterChange / MiB } : {})
      };
    }
    components[component] = { processIds, unchangedProcess, measurements };
  }
  const completed = report.status === 'completed' && Number.isFinite(plannedSeconds) && coverageSeconds >= plannedSeconds - 5;
  if (report.status === 'completed' && !completed) issues.add('Completed label does not cover the planned duration.');
  const assessment = !completed ? report.status === 'running' ? 'in-progress' : 'incomplete' : issues.size ? 'investigate' : 'no-trigger-observed';
  return { assessment, completed, plannedSeconds, coverageSeconds, sampleCount: samples.length, largestSampleGapSeconds, largestDiagnosticAgeSeconds,
    issues: [...issues], components,
    limitations: 'Whole-process simulation telemetry measurements. Cumulative allocations and CPU time normally grow. A stable run cannot prove every leak absent or establish image-acquisition, driver or observatory reliability.' };
}

if (import.meta.main) {
  const directory = Bun.argv[2];
  if (!directory) throw new Error('Usage: bun scripts/analyze-observation.ts OBSERVATION_DIRECTORY');
  const report = await Bun.file(join(directory, 'report.json')).json() as Report;
  const file = Bun.file(join(directory, 'samples.jsonl'));
  if (file.size > 16 * MiB) throw new Error('Sample input exceeds 16 MiB.');
  // A running writer appends a sample before atomically replacing its report.
  // Read only the complete prefix represented by that report snapshot.
  const lines = (await file.text()).trim().split('\n').filter(Boolean);
  const count = report.status === 'running' ? report.sampleCount : lines.length;
  const samples = lines.slice(0, count).map(line => JSON.parse(line) as Sample);
  console.log(JSON.stringify(analyze(report, samples), null, 2));
}
