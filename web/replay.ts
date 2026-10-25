export type Recording = {id:string;name:string;rig:string;environment:string;state:string;startedAt:string;finishedAt?:string;frames:number;previews:number};
export type Marker={at:number;kind:string;label:string};
export type ReplayIndex={id:string;recording:Recording;start:number;end:number;firstTelemetryAt:number;lastTelemetryAt:number;events:Marker[];gaps:{start:number;end:number}[];omittedEvents:number;truncated:boolean};
export type ReplayFrame={mode:'replay';recordingId:string;at:number;lastTelemetryAt:number;gap:boolean;snapshot:{sessionId:string;revision:number;states:Record<string,{observedAt:string;value:Record<string,unknown>}>}};
export function mountReplay(hooks:{read:(path:string,signal?:AbortSignal)=>Promise<unknown>;mode:(active:boolean)=>void;frame:(frame:ReplayFrame)=>void}) {
 let active=false,index:ReplayIndex|undefined,at=0,playing=false,version=0,abort:AbortController|undefined,speed=60,lastReal=0;
 const clock=new Intl.DateTimeFormat(undefined,{timeZone:'America/Chicago',hour:'2-digit',minute:'2-digit',second:'2-digit'});
 const date=new Intl.DateTimeFormat(undefined,{timeZone:'America/Chicago',dateStyle:'medium'});
 const button=document.createElement('button');button.id='open-recordings';button.textContent='◷ Recordings';button.title='Browse recorded observing nights';
 document.querySelector('.header-right')!.prepend(button);
 const picker=document.createElement('dialog');picker.className='recordings-dialog';picker.setAttribute('aria-label','Night recordings');
 picker.innerHTML='<header><div><small>OBSERVATORY ARCHIVE</small><h2>Night recordings</h2></div><button class="close-recordings" aria-label="Close recordings">×</button></header><p class="recordings-status" role="status"></p><div class="recordings-list"></div>';
 document.body.append(picker);picker.querySelector<HTMLButtonElement>('.close-recordings')!.onclick=()=>picker.close();
 const bar=document.createElement('section');bar.id='replay-timeline';bar.hidden=true;bar.setAttribute('aria-label','Recording playback');
 bar.innerHTML='<div class="replay-heading"><strong>REPLAY</strong><span id="replay-night"></span><time id="replay-clock"></time><span id="replay-state" role="status"></span><button id="return-live">Return to Live ↗</button></div><div class="replay-controls"><button id="replay-play" aria-label="Play recording">▶</button><button id="replay-previous" aria-label="Previous event">│◀</button><button id="replay-next" aria-label="Next event">▶│</button><select id="replay-speed" aria-label="Playback speed"><option value="1">1×</option><option value="10">10×</option><option value="60" selected>60×</option><option value="300">300×</option></select><div class="replay-track"><div class="replay-marks" aria-hidden="true"></div><input id="replay-scrubber" aria-label="Recording time" type="range" step="1000"><div class="replay-scale"></div></div><select id="replay-events" aria-label="Jump to recorded event"><option value="">Events</option></select></div>';
 document.body.append(bar);
 const find=<T extends HTMLElement>(id:string)=>bar.querySelector<T>('#'+id)!;
 const slider=find<HTMLInputElement>('replay-scrubber'),status=find('replay-state'),play=find<HTMLButtonElement>('replay-play');
 const label=(text:string)=>{status.textContent=text;};
 function stop(){playing=false;play.textContent='▶';play.setAttribute('aria-label','Play recording');}
 function leave(){++version;abort?.abort();stop();active=false;index=undefined;bar.hidden=true;document.body.classList.remove('replay-mode');hooks.mode(false);}
 find('return-live').onclick=leave;
 async function seek(time:number) {
  if(!active||!index)return;
  const ticket=++version;abort?.abort();abort=new AbortController();at=Math.max(index.start,Math.min(index.end,time));slider.value=String(at);label('Loading recorded state…');
  try {
   const frame=await hooks.read('/api/v1/recordings/'+index.id+'/state?at='+Math.round(at),abort.signal) as ReplayFrame;
   if(ticket!==version||!active)return;
   hooks.frame(frame);find('replay-clock').textContent=clock.format(frame.at)+' Central';
   label(frame.gap?'No recorded telemetry · last update '+(frame.lastTelemetryAt?clock.format(frame.lastTelemetryAt):'unavailable'):'Recorded telemetry · controls disabled');
   status.classList.toggle('gap',frame.gap);
  }catch(error){if(ticket===version&&active){stop();label(error instanceof Error?error.message:'Could not load recording');}}
 }
 function track() {
  if(!index)return;
  const duration=Math.max(1,index.end-index.start),marks=bar.querySelector<HTMLElement>('.replay-marks')!;marks.replaceChildren();
  for(const gap of index.gaps){const span=document.createElement('i');span.className='replay-gap';span.style.left=((gap.start-index.start)/duration*100)+'%';span.style.width=((gap.end-gap.start)/duration*100)+'%';marks.append(span);}
  const chosen=index.events.filter(e=>e.kind!=='step').concat(index.events.filter(e=>e.kind==='step')).slice(0,180);
  for(const event of chosen){const mark=document.createElement('i');mark.className='replay-mark kind-'+event.kind;mark.style.left=((event.at-index.start)/duration*100)+'%';marks.append(mark);}
  const scale=bar.querySelector<HTMLElement>('.replay-scale')!;scale.replaceChildren();
  for(let i=0;i<=4;i++){const t=document.createElement('span');t.textContent=clock.format(index.start+duration*i/4);scale.append(t);}
  const events=find<HTMLSelectElement>('replay-events');events.replaceChildren(new Option('Events · '+index.events.length,''));
  for(const e of index.events)events.add(new Option(clock.format(e.at)+' · '+e.label,String(e.at)));
 }
 async function select(recording:Recording) {
  picker.close();stop();abort?.abort();const ticket=++version;active=true;index=undefined;document.body.classList.add('replay-mode');bar.hidden=false;
  find('replay-night').textContent=date.format(Date.parse(recording.startedAt));label('Indexing recording…');hooks.mode(true);
  slider.disabled=true;play.disabled=true;
  try {
   const result=await hooks.read('/api/v1/recordings/'+recording.id) as ReplayIndex;
   if(!active||ticket!==version)return;
   index=result;at=index.start;slider.min=String(index.start);slider.max=String(index.end);slider.disabled=false;play.disabled=false;track();await seek(index.firstTelemetryAt||at);
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
 find<HTMLSelectElement>('replay-speed').onchange=e=>{speed=Number((e.target as HTMLSelectElement).value);};
 async function tick(){if(!playing||!active||!index)return;const now=performance.now(),next=at+(now-lastReal)*speed;lastReal=now;await seek(next);if(at>=index.end)stop();if(playing)setTimeout(()=>void tick(),250);}
 play.onclick=()=>{if(playing){stop();return;}if(!index)return;if(at>=index.end)at=index.start;playing=true;lastReal=performance.now();play.textContent='Ⅱ';play.setAttribute('aria-label','Pause recording');void tick();};
 return {active:()=>active,at:()=>at};
}
