import type {SequenceNode} from './dashboard';
import {sequenceImagingProgress} from './sequence-imaging-progress';
import {sequenceAncestors} from './sequence-current';

/** Summarize the immediate imaging groups within the currently active target. */
export function imagingSets(nodes:SequenceNode[], active:SequenceNode|undefined, truncated:boolean) {
 const index=new Map(nodes.map(n=>[n.id,n]));
 const path=active?[active.id,...sequenceAncestors(nodes,active.id)]:[];
 const target=path.map(id=>index.get(id)).find(n=>n?.type==='DeepSkyObjectContainer');
 if(!target)return [];
 const groups=new Map<string,SequenceNode[]>();
 for(const n of nodes) {
  if(!['TakeExposure','TakeSubframeExposure'].includes(n.type))continue;
  const ancestors=sequenceAncestors(nodes,n.id).map(id=>index.get(id)).filter((x):x is SequenceNode=>!!x);
  if(!ancestors.some(x=>x.id===target.id)||ancestors.some(x=>x.kind==='trigger'||x.state==='disabled'))continue;
  const group=ancestors.find(x=>x.kind==='container'&&x.id!==target.id);
  if(!group)continue;
  const rows=groups.get(group.id)??[];rows.push(n);groups.set(group.id,rows);
 }
 return [...groups].map(([id,exposures])=>{
  const group=index.get(id)!;
  const subtree=nodes.filter(n=>n.id===id||sequenceAncestors(nodes,n.id).includes(id)).map(n=>n.id===id?{...n,parentId:null}:n);
  const outer = sequenceAncestors(nodes,id);
  // Outer repeat/time conditions can make a group's own repeat limit only a
  // per-cycle value, while N.I.N.A.'s capture counter is cumulative.
  const outerCondition=nodes.some(n=>n.kind==='condition'&&n.state!=='disabled'&&n.parentId!==null&&outer.includes(n.parentId));
  const progress=sequenceImagingProgress(subtree,truncated||outerCondition);
  return {id,name:group.name,active:path.includes(id),state:group.state,captured:progress.captured,planned:progress.planned,exposures:exposures.length};
 });
}
