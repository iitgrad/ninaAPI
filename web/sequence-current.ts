import type {SequenceNode} from './dashboard';
export function currentSequenceNode(nodes: SequenceNode[], activeIds: unknown): SequenceNode | undefined {
  const ids = new Set(Array.isArray(activeIds) ? activeIds.filter(x => typeof x === 'string') : []);
  const running = nodes.filter(n => ids.has(n.id) || ['running', 'waiting'].includes(n.state));
  // NINA can report several concurrent items. Prefer a concrete instruction or
  // trigger over a containing group; preserve display order for ties.
  return running.find(n => n.kind === 'instruction') ?? running.find(n => n.kind === 'trigger') ?? running.at(-1);
}
export function sequenceAncestors(nodes: SequenceNode[], id: string): string[] {
  const byId = new Map(nodes.map(n => [n.id, n]));
  const seen = new Set<string>();
  let parent = byId.get(id)?.parentId;
  while (parent && !seen.has(parent)) { seen.add(parent); parent = byId.get(parent)?.parentId; }
  return [...seen];
}
