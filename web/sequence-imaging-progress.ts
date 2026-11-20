import type {SequenceNode} from './dashboard';

const imagingExposureCount = (value: unknown): number | null =>
  typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 ? value : null;

// TakeExposure.ExposureCount is the native cumulative capture counter, even when
// its final loop iteration is pending/skipped. Never derive captures from status.
export function sequenceImagingProgress(nodes: SequenceNode[], truncated = false) {
  const index = new Map(nodes.map(n => [n.id, n]));
  const children = new Map<string, SequenceNode[]>();
  for (const n of nodes) if (n.parentId) {
    const siblings = children.get(n.parentId) ?? [];
    siblings.push(n); children.set(n.parentId, siblings);
  }
  let captured = 0, planned = 0, exposures = 0;
  let countKnown = true, planKnown = !truncated, timed = false;
  for (const n of nodes) {
    if (!['TakeExposure', 'TakeSubframeExposure'].includes(n.type)) continue;
    const ancestry: SequenceNode[] = [n];
    const visited = new Set([n.id]);
    let next = n.parentId, missing = false;
    while (next) {
      const parent = index.get(next);
      if (!parent || visited.has(next)) { missing = true; break; }
      visited.add(next); ancestry.push(parent); next = parent.parentId;
    }
    if (ancestry.some(a => a.kind === 'trigger' || a.state === 'disabled')) continue;
    exposures++;
    if (missing) planKnown = false;
    const taken = imagingExposureCount(n.details.exposureCount);
    if (taken === null) countKnown = false;
    else captured += taken;
    let repetitions = 1;
    for (const ancestor of ancestry.slice(1)) {
      let loopLimit: number | null = null;
      for (const condition of children.get(ancestor.id) ?? []) {
        if (condition.kind !== 'condition' || condition.state === 'disabled') continue;
        if (condition.type === 'LoopCondition') {
          const iterations = imagingExposureCount(condition.details.iterations);
          if (iterations === null) planKnown = false;
          else loopLimit = Math.min(loopLimit ?? iterations, iterations);
        } else {
          planKnown = false;
          if (condition.type === 'TimeCondition') timed = true;
        }
      }
      repetitions *= loopLimit ?? 1;
    }
    if (!Number.isSafeInteger(repetitions) || !Number.isSafeInteger(planned + repetitions)) planKnown = false;
    else planned += repetitions;
  }
  // Unexpanded exposure groups cannot be silently treated as an empty plan.
  if (nodes.some(n => ['SmartExposure', 'TakeManyExposures'].includes(n.type)) && !exposures) {
    planKnown = false; countKnown = false;
  }
  const actual = countKnown ? captured : null;
  const total = planKnown && exposures > 0 ? planned : null;
  const percent = actual !== null && total !== null && total > 0
    ? Math.round(Math.min(1, actual / total) * 100) : null;
  const qualifiers = [timed ? 'time-limited plan' : total === null && exposures ? 'planned count unavailable' : '',
    truncated ? 'shown portion' : ''].filter(Boolean);
  const summary = !exposures && !truncated && countKnown ? 'No imaging instructions'
    : actual === null ? 'Image count unavailable'
    : total === null ? `${actual.toLocaleString()} images taken`
    : `${actual.toLocaleString()} / ${total.toLocaleString()} images taken`;
  return {captured: actual, planned: total, percent, label: [summary, ...qualifiers].join(' · ')};
}
