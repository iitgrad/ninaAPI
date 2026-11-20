import type {SequenceNode} from './dashboard';
import {rolloverDeadline} from './sequence-clock';
import {sequenceImagingProgress} from './sequence-imaging-progress';

export type FilterCount={name:string;count:number|null};
export type NightWindow={id:string;name:string;start:number;end:number;captured:number|null;filters?:FilterCount[];partial?:boolean};
export function filterCounts(nodes:SequenceNode[]):FilterCount[] {
 const children=new Map<string|null,SequenceNode[]>();
 for(const n of nodes){const rows=children.get(n.parentId)??[];rows.push(n);children.set(n.parentId,rows);}
 const totals=new Map<string,number|null>();
 function visit(parent:string|null, inherited:string|null, seen:Set<string>){
  let current=inherited;
  for(const n of children.get(parent)??[]){
   if(seen.has(n.id)||n.kind==='trigger'||n.state==='disabled')continue;
   seen.add(n.id);
   if(n.type==='SwitchFilter') current=typeof n.details.filter==='string'&&n.details.filter.trim()?n.details.filter.trim():null;
   if(['TakeExposure','TakeSubframeExposure'].includes(n.type)) {
    const key=current??'Unknown filter',raw=n.details.exposureCount;
    const count=typeof raw==='number'&&Number.isSafeInteger(raw)&&raw>=0?raw:null;
    const prior=totals.get(key);totals.set(key,count===null||prior===null?null:(prior??0)+count);
   }
   if(n.kind==='container') visit(n.id,current,seen);
  }
 }
 visit(null,null,new Set());return [...totals].map(([name,count])=>({name,count}));
}
const filterColors=['#5ed7cb','#7faefa','#c592ef','#f7c453','#fb8d82','#9bdd85','#e99bc9','#90b4c5'];
export function filterPie(filters:FilterCount[]) {
 const total=filters.reduce((sum,f)=>sum+(f.count??0),0);let offset=0;
 const stops=filters.flatMap((f,i)=>{if(f.count===null||f.count===0||!total)return [];const start=offset;offset+=f.count/total*100;return [`${filterColors[i%filterColors.length]} ${start}% ${offset}%`];});
 return {total,gradient:stops.length?`conic-gradient(${stops.join(', ')})`:null,hasUnknown:filters.some(f=>f.count===null)};
}
export function nightTimeline(nodes:SequenceNode[],observedAt:string,zone:string,truncated=false):NightWindow[]{
 const root=nodes.find(n=>n.type==='SequenceRootContainer');
 // Pin the observing night: a finished plan must not jump to tonight after rollover.
 const night=root?.name.match(/^\d{4}-\d{2}-\d{2}$/)?.[0];
 const anchor=root?.timing?.startedAt??root?.timing?.seenActiveAt??observedAt;
 const children=new Map<string,SequenceNode[]>();
 for(const n of nodes)if(n.parentId){const rows=children.get(n.parentId)??[];rows.push(n);children.set(n.parentId,rows);}
 const windows:NightWindow[]=[];
 function visit(n:SequenceNode,subtree:SequenceNode[],seen:Set<string>){
  if(seen.has(n.id)||n.kind==='trigger'||n.state==='disabled')return;
  seen.add(n.id);subtree.push(n);for(const child of children.get(n.id)??[])visit(child,subtree,seen);
 }
 for(const target of nodes.filter(n=>n.type==='DeepSkyObjectContainer'&&n.state!=='disabled')){
  // Targets inside recovery/conditional branches are not scheduled target blocks.
  let parent=target.parentId;const ancestry=new Set([target.id]);let conditional=false;
  while(parent){const n=nodes.find(n=>n.id===parent);if(!n||ancestry.has(parent)){conditional=true;break;}ancestry.add(parent);if(n.kind==='trigger'||n.state==='disabled'){conditional=true;break;}parent=n.parentId;}
  if(conditional)continue;
  const subtree:SequenceNode[]=[];visit(target,subtree,new Set());
  const clock=(n:SequenceNode)=>n.details.timeReferenceFixed===true?rolloverDeadline(n.details,anchor,zone,night):NaN;
  const starts=subtree.filter(n=>n.type==='WaitForTime').map(clock).filter(Number.isFinite);
  const ends=subtree.filter(n=>n.type==='TimeCondition').map(clock).filter(Number.isFinite);
  if(!starts.length||!ends.length)continue;
  const start=Math.min(...starts),end=Math.max(...ends);
  if(end<=start||end-start>86400000)continue;
  const countNodes=subtree.map(n=>n.id===target.id?{...n,parentId:null}:n);
  windows.push({id:target.id,name:target.name,start,end,captured:truncated&&!countNodes.some(n=>["TakeExposure","TakeSubframeExposure"].includes(n.type))?null:sequenceImagingProgress(countNodes,truncated).captured,filters:filterCounts(countNodes),partial:truncated});
 }
 return windows.sort((a,b)=>a.start-b.start);
}
export function scheduledPercent(windows:NightWindow[],now:number):number|null{
 const duration=windows.reduce((s,w)=>s+w.end-w.start,0);
 if(!duration||!Number.isFinite(now))return null;
 const elapsed=windows.reduce((s,w)=>s+Math.max(0,Math.min(w.end-w.start,now-w.start)),0);
 return Math.round(elapsed/duration*100);
}
let nightTimelineState:{windows:NightWindow[];observed:number;zone:string}|null=null;
const nightTimelineColors=['#2dd4bf','#60a5fa','#c084fc','#fbbf24'];
export function updateNightTimelineClock(live:boolean,now=Date.now()){
 const host=document.getElementById('sequence-night-timeline');if(!host||host.hidden||!nightTimelineState)return;
 const at=live?now:Math.min(now,nightTimelineState.observed);
 const percent=scheduledPercent(nightTimelineState.windows,at);
 const label=document.getElementById('sequence-percent');if(label)label.textContent=percent===null?'':`${percent}% scheduled time elapsed`;
 for(const block of host.querySelectorAll<HTMLElement>('[data-night-window]')){
  const start=Number(block.dataset.start),end=Number(block.dataset.end);
  const p=Math.max(0,Math.min(1,(at-start)/(end-start)))*100;
  block.querySelector<HTMLElement>('.night-window-fill')!.style.width=p+'%';
 }
 host.setAttribute('aria-label',`Planned observing time: ${percent??0}% elapsed${live?'':' at last observation'}`);
}
export function renderNightTimeline(windows:NightWindow[],observedAt:string,zone:string,live:boolean){
 let host=document.getElementById('sequence-night-timeline');
 if(!host){host=document.createElement('div');host.id='sequence-night-timeline';document.getElementById('sequence-coverage')!.parentElement!.after(host);}
 host.hidden=!windows.length;
 if(!windows.length){nightTimelineState=null;return;}
 nightTimelineState={windows,observed:Date.parse(observedAt),zone};
 const signature=JSON.stringify([windows,zone]);
 if(host.dataset.signature===signature){updateNightTimelineClock(live);return;}
 host.dataset.signature=signature;
 if(!document.getElementById('sequence-night-timeline-style')){
  const style=document.createElement('style');style.id='sequence-night-timeline-style';style.textContent=`
#sequence-night-timeline{margin:10px 0 14px;font-size:12px;color:var(--muted)}
#sequence-night-timeline .night-caption{margin-bottom:7px}
#sequence-night-timeline .night-bar{position:relative;height:34px;border-radius:4px;background:var(--border)}
#sequence-night-timeline .night-window{position:absolute;top:0;height:100%;background:color-mix(in srgb,var(--night-color) 25%,#10212b);border:0;border-right:2px solid var(--panel,#101922);box-sizing:border-box;padding:0;cursor:pointer;text-align:left;color:#fff;min-width:0}
#sequence-night-timeline .night-window-fill{position:absolute;inset:0 auto 0 0;background:var(--night-color);opacity:.75;pointer-events:none}
#sequence-night-timeline .night-window-label{position:relative;display:flex;gap:3px;white-space:nowrap;padding:0 6px;font-size:11px;font-weight:700;color:#fff;text-shadow:0 1px 3px #000,0 0 4px #000;line-height:34px} #sequence-night-timeline .night-window-name{overflow:hidden;text-overflow:ellipsis;min-width:0} #sequence-night-timeline .night-window-count{flex-shrink:0}
#sequence-night-timeline .night-tooltip{visibility:hidden;opacity:0;transition:visibility 0s linear .25s,opacity .12s linear .25s;position:absolute;z-index:20;top:calc(100% + 4px);left:0;width:min(340px, calc(100vw - 32px));box-sizing:border-box;padding:14px 16px;border:1px solid var(--border);border-radius:6px;background:#10212b;color:#e4edf4;box-shadow:0 4px 12px #0008;white-space:normal;pointer-events:none;line-height:1.45;font-size:13px}
#sequence-night-timeline .night-window:hover .night-tooltip,#sequence-night-timeline .night-window:focus-visible .night-tooltip,#sequence-night-timeline .night-window.is-open .night-tooltip{visibility:visible;opacity:1;transition-delay:.25s}
#sequence-night-timeline .night-window:focus-visible{outline:2px solid #fff;outline-offset:2px}
#sequence-night-timeline .night-tooltip strong{display:block}
#sequence-night-timeline .night-tooltip span{display:block}
#sequence-night-timeline .night-tooltip .filter-chart{display:flex;align-items:center;gap:16px;margin-top:12px}
#sequence-night-timeline .filter-pie{width:120px;height:120px;flex:0 0 120px;border-radius:50%;box-shadow:0 0 0 1px #ffffff20}
#sequence-night-timeline .night-tooltip .filter-key{flex:1;min-width:0;display:grid;gap:5px}
#sequence-night-timeline .filter-row{display:flex!important;align-items:center;gap:7px}
#sequence-night-timeline .filter-swatch{display:inline-block!important;width:10px;height:10px;border-radius:50%;flex:0 0 10px}
#sequence-night-timeline .filter-name{flex:1;overflow:hidden;text-overflow:ellipsis}
#sequence-night-timeline .filter-count{font-variant-numeric:tabular-nums;font-weight:700}
#sequence-night-timeline .filter-note{margin-top:8px;color:#b8cad6;font-size:11px}
@media(max-width:420px){#sequence-night-timeline .filter-pie{width:94px;height:94px;flex-basis:94px}}
`;document.head.append(style);
 }
 const format=new Intl.DateTimeFormat([], {timeZone:zone,hour:'numeric',minute:'2-digit'});
 const zoneName=new Intl.DateTimeFormat('en',{timeZone:zone,timeZoneName:'short'}).formatToParts(new Date(windows[0].start)).find(p=>p.type==='timeZoneName')?.value??zone;
 const caption=document.createElement('div');caption.className='night-caption';caption.textContent=`Planned schedule · ${zoneName} · colored fill shows time elapsed, not execution or image completion`;
 const bar=document.createElement('div');bar.className='night-bar';
 const start=Math.min(...windows.map(w=>w.start)),end=Math.max(...windows.map(w=>w.end));
 windows.forEach((w,i)=>{
  const color=nightTimelineColors[i%nightTimelineColors.length];const range=`${format.format(w.start)}–${format.format(w.end)}`;
  const captured=w.captured===null?'image count unavailable':`${w.captured.toLocaleString()} images`;
  const block=document.createElement('button');block.type='button';block.className='night-window';block.dataset.nightWindow=w.id;block.dataset.start=String(w.start);block.dataset.end=String(w.end);
  block.style.left=((w.start-start)/(end-start)*100)+'%';block.style.width=((w.end-w.start)/(end-start)*100)+'%';block.style.setProperty('--night-color',color);
  const labelText=`${w.name} : ${captured}`;block.setAttribute('aria-label',`${labelText}. ${range} ${zoneName}. ${(w.filters??[]).map(f=>`${f.name}: ${f.count===null?'unavailable':f.count+' images'}`).join(', ')}${w.partial?'. Partial sequence data':''}`);
  const fill=document.createElement('span');fill.className='night-window-fill';
  const label=document.createElement('span');label.className='night-window-label';const name=document.createElement('span');name.className='night-window-name';name.textContent=w.name;const count=document.createElement('span');count.className='night-window-count';count.textContent=`: ${w.captured===null?'?':w.captured.toLocaleString()}`;label.append(name,count);
  const tip=document.createElement('span');tip.className='night-tooltip';if(i>=windows.length/2){tip.style.left='auto';tip.style.right='0';}tip.setAttribute('aria-hidden','true');
  const heading=document.createElement('strong');heading.textContent=w.name;tip.append(heading);
  const schedule=document.createElement('span');schedule.textContent=`${range} ${zoneName} · ${captured}`;tip.append(schedule);
  const filters=w.filters??[],pie=filterPie(filters);
  if(filters.length){
   const chart=document.createElement('span');chart.className='filter-chart';
   const graphic=document.createElement('span');graphic.className='filter-pie';graphic.style.background=pie.gradient??'#344d5c';chart.append(graphic);
   const key=document.createElement('span');key.className='filter-key';
   filters.forEach((f,index)=>{const row=document.createElement('span');row.className='filter-row';const dot=document.createElement('span');dot.className='filter-swatch';dot.style.background=filterColors[index%filterColors.length];const label=document.createElement('span');label.className='filter-name';label.textContent=f.name;const count=document.createElement('span');count.className='filter-count';count.textContent=f.count===null?'—':f.count.toLocaleString();row.append(dot,label,count);key.append(row);});
   chart.append(key);tip.append(chart);
   if(!pie.total||pie.hasUnknown){const note=document.createElement('span');note.className='filter-note';note.textContent=pie.total?'Chart shows known counts only; — means unavailable.':pie.hasUnknown?'Filter counts unavailable.':'No images captured yet.';tip.append(note);}
  } else {const note=document.createElement('span');note.className='filter-note';note.textContent='Filter counts unavailable';tip.append(note);}
  if(w.partial){const note=document.createElement('span');note.className='filter-note';note.textContent='Partial sequence data';tip.append(note);}
  block.onclick=()=>block.classList.toggle('is-open');block.onblur=()=>block.classList.remove('is-open');
  block.append(fill,label,tip);bar.append(block);
 });
 host.replaceChildren(caption,bar);updateNightTimelineClock(live);
}
