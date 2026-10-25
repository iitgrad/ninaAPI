import {renderVisibility,updateVisibilityClock,siteDate,type Site} from './target-visibility';
type Target={id:string;editableFields?:{key:string;value:unknown}[]};
import {targetFraming} from './target-framing';
const plots=new Map<HTMLElement,(width:number)=>void>();
const resize=new ResizeObserver(entries=>{for(const entry of entries){if(entry.contentRect.width>0)plots.get(entry.target as HTMLElement)?.(entry.contentRect.width);}});
// Keep unchanged image/graph DOM through telemetry renders. Recreating an empty
// graph temporarily shortens the tree and clamps the reader's scroll position.
const headers=new Map<string,{key:string;panel:HTMLDivElement}>();
let used=new Set<string>();
export function clearTargetHeaders(){used=new Set();}
export function finishTargetHeaders(){
 for(const [id,entry] of headers){if(used.has(id))continue;const chart=entry.panel.querySelector<HTMLElement>('.target-altitude');if(chart){resize.unobserve(chart);plots.delete(chart);}headers.delete(id);}
}
export function targetHeader(target:Target,site?:Site,profileId?:string,now=Date.now()){
 const day=site?siteDate(new Date(now-12*3600000),site.timeZone):undefined;
 const key=JSON.stringify([target.editableFields,site,profileId,day]);used.add(target.id);
 const cached=headers.get(target.id);if(cached?.key===key){updateVisibilityClock(cached.panel,now);return cached.panel;}
 if(cached){const old=cached.panel.querySelector<HTMLElement>('.target-altitude');if(old){resize.unobserve(old);plots.delete(old);}}
 const panel=document.createElement('div');panel.className='target-planning';headers.set(target.id,{key,panel});
 const value=(key:string)=>target.editableFields?.find(f=>f.key===key)?.value;
 const ra=value('RightAscension'),dec=value('Declination'),rotation=value('Rotation');
 const coordinates=document.createElement('div');coordinates.className='target-coordinates';
 function coordinate(label:string,text:string){const b=document.createElement('button');b.type='button';b.innerHTML='<small></small><strong></strong>';b.querySelector('small')!.textContent=label;b.querySelector('strong')!.textContent=text;b.title='Edit target coordinates and rotation';b.onclick=()=>panel.closest('.sequence-target')?.querySelector<HTMLButtonElement>(':scope > .sequence-node .sequence-step-menu')?.click();coordinates.append(b);}
 const sex=(n:number,hours:boolean)=>{const total=Math.round(Math.abs(n)*3600),a=Math.floor(total/3600),b=Math.floor(total%3600/60),c=total%60;return `${!hours?(n<0?'−':'+'):''}${String(a).padStart(2,'0')}${hours?'h':'°'} ${String(b).padStart(2,'0')}${hours?'m':'′'} ${String(c).padStart(2,'0')}${hours?'s':'″'}`;};
 coordinate('RIGHT ASCENSION',typeof ra==='number'?sex(ra,true):'—');coordinate('DECLINATION',typeof dec==='number'?sex(dec,false):'—');coordinate('ROTATION',typeof rotation==='number'?rotation.toFixed(1)+'°':'—');
 panel.append(coordinates);const visuals=document.createElement('div');visuals.className='target-visuals';const chart=document.createElement('div');chart.className='target-altitude';visuals.append(targetFraming(ra,dec,rotation,profileId),chart);panel.append(visuals);
 if(site&&typeof ra==='number'&&typeof dec==='number'){
  let width=0;
  const draw=(available=width)=>{width=available;if(width<=0)return;try{renderVisibility(chart,ra,dec,site,day!,width,now);}catch{chart.textContent='Visibility unavailable for this site or date.';}};
  plots.set(chart,available=>{if(Math.round(available)!==Math.round(width))draw(available);});resize.observe(chart);
  draw();
 }else{chart.textContent='Observing-site telemetry required for visibility.';chart.classList.add('target-site-missing');}
 return panel;
}
