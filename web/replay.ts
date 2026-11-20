import type {ThermalSample} from "./camera-thermal";
import {DEFAULT_REPLAY_SPEED,recordingStart,zoomWindow,revealTime,selectionWindow,type TimeWindow} from "./replay-timeline";
export type Recording = {id:string;name:string;rig:string;environment:string;state:string;startedAt:string;finishedAt?:string;frames:number;previews:number};
export type Marker={at:number;kind:string;label:string};
export type ReplayIndex={id:string;recording:Recording;start:number;end:number;firstTelemetryAt:number;lastTelemetryAt:number;events:Marker[];gaps:{start:number;end:number}[];omittedEvents:number;truncated:boolean};
export type ReplayFrame={mode:'replay';cameraThermal?:ThermalSample[];recordingId:string;at:number;lastTelemetryAt:number;gap:boolean;snapshot:{sessionId:string;revision:number;states:Record<string,{observedAt:string;value:Record<string,unknown>}>}};
export function mountReplay(hooks:{read:(path:string,signal?:AbortSignal)=>Promise<unknown>;mode:(active:boolean)=>void;frame:(frame:ReplayFrame)=>void;follow:(enabled:boolean)=>void}) {
 let active=false,index:ReplayIndex|undefined,at=0,playing=false,version=0,abort:AbortController|undefined,speed=DEFAULT_REPLAY_SPEED,lastReal=0;
 let view:TimeWindow={start:0,end:1};
 let playbackGeneration=0;
 const clock=new Intl.DateTimeFormat(undefined,{timeZone:'America/Chicago',hour:'2-digit',minute:'2-digit',second:'2-digit'});
 const date=new Intl.DateTimeFormat(undefined,{timeZone:'America/Chicago',dateStyle:'medium'});
 const button=document.createElement('button');button.id='open-recordings';button.textContent='◷ Recordings';button.title='Browse recorded observing nights';
 document.querySelector('.header-right')!.prepend(button);
 const picker=document.createElement('dialog');picker.className='recordings-dialog';picker.setAttribute('aria-label','Night recordings');
 picker.innerHTML='<header><div><small>OBSERVATORY ARCHIVE</small><h2>Night recordings</h2></div><button class="close-recordings" aria-label="Close recordings">×</button></header><p class="recordings-status" role="status"></p><div class="recordings-list"></div>';
 document.body.append(picker);picker.querySelector<HTMLButtonElement>('.close-recordings')!.onclick=()=>picker.close();
 const bar=document.createElement('section');bar.id='replay-timeline';bar.hidden=true;bar.setAttribute('aria-label','Recording playback');
 bar.innerHTML='<div class="replay-heading"><strong>REPLAY</strong><span id="replay-night"></span><time id="replay-clock"></time><span id="replay-state" role="status"></span><button id="return-live">Return to Live ↗</button></div><div class="replay-controls"><button id="replay-play" aria-label="Play recording">▶</button><button id="replay-previous" aria-label="Previous event">│◀</button><button id="replay-next" aria-label="Next event">▶│</button><select id="replay-speed" aria-label="Playback speed"><option value="1" selected>1×</option><option value="10">10×</option><option value="60">60×</option><option value="300">300×</option></select><button id="replay-zoom-out" aria-label="Zoom out timeline" title="Zoom out timeline">−</button><button id="replay-zoom-in" aria-label="Zoom in timeline" title="Zoom in timeline">+</button><button id="replay-full" title="Show full night">Full</button><button id="replay-follow" aria-pressed="true" title="Expand and center the recorded active step">◎ Follow</button><div class="replay-track"><div class="replay-marks" aria-hidden="true"></div><input id="replay-scrubber" aria-label="Recording time" type="range" step="1000"><div class="replay-scale"></div></div><select id="replay-events" aria-label="Jump to recorded event"><option value="">Events</option></select><div id="replay-overview" role="group" aria-label="Full-night overview; drag to select a time range" title="Drag across an area to zoom. Click to seek."><i class="overview-window"></i><i class="overview-playhead"></i></div></div>';
 document.body.append(bar);
 const find=<T extends HTMLElement>(id:string)=>bar.querySelector<T>('#'+id)!;
 const slider=find<HTMLInputElement>('replay-scrubber'),status=find('replay-state'),play=find<HTMLButtonElement>('replay-play');
 const label=(text:string)=>{status.textContent=text;};
 const bounds=():TimeWindow=>({start:index?recordingStart(index):0,end:index?.end??1});
 function overview(){if(!index)return;const span=Math.max(1,index.end-index.start),box=bar.querySelector<HTMLElement>('.overview-window')!,head=bar.querySelector<HTMLElement>('.overview-playhead')!;box.style.left=(view.start-index.start)/span*100+'%';box.style.width=(view.end-view.start)/span*100+'%';head.style.left=(at-index.start)/span*100+'%';}
 function showWindow(next:TimeWindow){view=next;slider.min=String(next.start);slider.max=String(next.end);track();slider.value=String(at);overview();}

 function stop(){++playbackGeneration;playing=false;play.textContent='▶';play.setAttribute('aria-label','Play recording');}
 function leave(){++version;abort?.abort();stop();active=false;index=undefined;bar.hidden=true;document.body.classList.remove('replay-mode');hooks.mode(false);}
 find('return-live').onclick=leave;
 async function seek(time:number) {
  if(!active||!index)return;
  const ticket=++version;abort?.abort();abort=new AbortController();at=Math.max(recordingStart(index),Math.min(index.end,time));const nextView=revealTime(view,at,bounds());if(nextView!==view)showWindow(nextView);slider.value=String(at);overview();
  try {
   const frame=await hooks.read('/api/v1/recordings/'+index.id+'/state?at='+Math.round(at),abort.signal) as ReplayFrame;
   if(ticket!==version||!active)return;
   hooks.frame(frame);find('replay-clock').textContent=clock.format(frame.at)+' Central';
   label(frame.gap?'Recording gap · last update '+(frame.lastTelemetryAt?clock.format(frame.lastTelemetryAt):'unavailable'):'');
   status.classList.toggle('gap',frame.gap);
  }catch(error){if(ticket===version&&active){stop();label(error instanceof Error?error.message:'Could not load recording');}}
 }
 function track() {
  if(!index)return;
  const duration=Math.max(1,view.end-view.start),marks=bar.querySelector<HTMLElement>('.replay-marks')!;marks.replaceChildren();
  for(const gap of index.gaps.filter(g=>g.end>=view.start&&g.start<=view.end)){const start=Math.max(view.start,gap.start),end=Math.min(view.end,gap.end);const span=document.createElement('i');span.className='replay-gap';span.style.left=((start-view.start)/duration*100)+'%';span.style.width=((end-start)/duration*100)+'%';marks.append(span);}
  const chosen=index.events.filter(e=>e.kind!=='step').concat(index.events.filter(e=>e.kind==='step')).slice(0,180);
  for(const event of chosen.filter(e=>e.at>=view.start&&e.at<=view.end)){const mark=document.createElement('i');mark.className='replay-mark kind-'+event.kind;mark.style.left=((event.at-view.start)/duration*100)+'%';marks.append(mark);}
  const scale=bar.querySelector<HTMLElement>('.replay-scale')!;scale.replaceChildren();
  for(let i=0;i<=4;i++){const t=document.createElement('span');t.textContent=clock.format(view.start+duration*i/4);scale.append(t);}
  const events=find<HTMLSelectElement>('replay-events');events.replaceChildren(new Option('Events · '+index.events.length,''));
  for(const e of index.events)events.add(new Option(clock.format(e.at)+' · '+e.label,String(e.at)));
 }
 async function select(recording:Recording) {
  picker.close();stop();abort?.abort();const ticket=++version;active=true;index=undefined;document.body.classList.add('replay-mode');bar.hidden=false;
  find('replay-night').textContent=date.format(Date.parse(recording.startedAt));label('Indexing recording…');hooks.mode(true);hooks.follow(true);find("replay-follow").setAttribute("aria-pressed","true");
  slider.disabled=true;play.disabled=true;
  try {
   const result=await hooks.read('/api/v1/recordings/'+recording.id) as ReplayIndex;
   if(!active||ticket!==version)return;
   index=result;at=index.start;slider.disabled=false;play.disabled=false;view=bounds();showWindow(view);await seek(recordingStart(index));
  }catch(e){if(active&&ticket===version)label(e instanceof Error?e.message:'Could not open recording');}
 }
 button.onclick=async()=>{
  const listing=picker.querySelector<HTMLElement>('.recordings-list')!,message=picker.querySelector<HTMLElement>('.recordings-status')!;listing.replaceChildren();message.textContent='Loading recordings…';picker.showModal();
  try {
   const entries=await hooks.read('/api/v1/recordings') as Recording[];
   message.textContent=entries.length?'Times shown in America/Chicago. Shaded regions mark missing telemetry.':'No recordings yet.';
   for(const r of entries){const row=document.createElement('button');row.className='recording-entry';row.disabled=r.state==='recording';row.dataset.recordingId=r.id;
    const title=document.createElement('strong');title.textContent=date.format(Date.parse(r.startedAt));const sub=document.createElement('span');sub.textContent=clock.format(Date.parse(r.startedAt))+' · '+r.rig+(r.environment==='simulation'?' · Simulation':'');
    const badge=document.createElement('small');badge.textContent=r.state==='recording'?'Recording now':r.state;row.append(title,sub,badge);row.onclick=()=>void select(r);listing.append(row);}
  }catch(e){message.textContent=e instanceof Error?e.message:'Recording archive unavailable';}
 };
 let scrubTimer:ReturnType<typeof setTimeout>|undefined;
 slider.oninput=()=>{stop();clearTimeout(scrubTimer);scrubTimer=setTimeout(()=>void seek(Number(slider.value)),100);};
 slider.onchange=()=>{clearTimeout(scrubTimer);void seek(Number(slider.value));};
 find<HTMLSelectElement>('replay-events').onchange=event=>{const val=(event.target as HTMLSelectElement).value;if(val){stop();void seek(Number(val));}};
 for(const [id,direction] of [['replay-previous',-1],['replay-next',1]] as const)find(id).onclick=()=>{if(!index)return;stop();const events=index.events.map(e=>e.at);const next=direction>0?events.find(t=>t>at+1):events.filter(t=>t<at-1).at(-1);void seek(next??(direction>0?index.end:index.start));};
 find<HTMLSelectElement>('replay-speed').onchange=e=>{speed=Number((e.target as HTMLSelectElement).value);lastReal=performance.now();};
 find('replay-zoom-in').onclick=()=>{if(index)showWindow(zoomWindow(view,.5,at,bounds()));};
 find('replay-zoom-out').onclick=()=>{if(index)showWindow(zoomWindow(view,2,at,bounds()));};
 find('replay-full').onclick=()=>{if(index)showWindow(bounds());};
 find('replay-follow').onclick=()=>{const b=find('replay-follow'),enabled=b.getAttribute('aria-pressed')!=='true';b.setAttribute('aria-pressed',String(enabled));hooks.follow(enabled);};
 const overviewTrack=find('replay-overview');let drag:{x:number;at:number;start:number;end:number}|undefined;
 const overviewTime=(x:number)=>{const box=overviewTrack.getBoundingClientRect();return index!.start+Math.max(0,Math.min(1,(x-box.left)/box.width))*(index!.end-index!.start);};
 overviewTrack.onpointerdown=e=>{if(!index)return;stop();overviewTrack.setPointerCapture(e.pointerId);drag={x:e.clientX,at:overviewTime(e.clientX),start:view.start,end:view.end};};
 overviewTrack.onpointermove=e=>{if(!drag||!index)return;if(Math.abs(e.clientX-drag.x)>5){view=selectionWindow(drag.at,overviewTime(e.clientX),bounds());overview();}};
 overviewTrack.onpointerup=e=>{if(!drag||!index)return;const selected=Math.abs(e.clientX-drag.x)>5,start=selected?view.start:overviewTime(e.clientX);if(selected)showWindow(view);else view={start:drag.start,end:drag.end};drag=undefined;void seek(start);};
 overviewTrack.onpointercancel=()=>{if(drag){showWindow({start:drag.start,end:drag.end});drag=undefined;}};
 async function tick(run:number){if(!playing||!active||!index||run!==playbackGeneration)return;const now=performance.now(),next=at+(now-lastReal)*speed;lastReal=now;await seek(next);if(at>=index.end)stop();if(playing&&run===playbackGeneration)setTimeout(()=>void tick(run),250);}
 play.onclick=()=>{if(playing){stop();return;}if(!index)return;if(at>=index.end)at=recordingStart(index);playing=true;lastReal=performance.now();play.textContent='Ⅱ';play.setAttribute('aria-label','Pause recording');void tick(++playbackGeneration);};
 return {active:()=>active,at:()=>at};
}
