import {Body,DefineStar,Observer,Equator,Horizon,SearchRiseSet,SearchHourAngle,SearchAltitude} from 'astronomy-engine';
export type Site={latitude:number;longitude:number;elevation:number;name?:string;timeZone:string};
export type Visibility={start:number;end:number;samples:{time:number;altitude:number;sun:number}[];rise:number|null;set:number|null;transit:number;peak:number;twilight:{label:string;time:number|null}[]};
const cache=new Map<string,Visibility>();
export function siteDate(now:Date,zone:string){return new Intl.DateTimeFormat('en-CA',{timeZone:zone,year:'numeric',month:'2-digit',day:'2-digit'}).format(now);}
// Convert a wall-clock time in the observing site's zone, including DST.
export function zonedTime(day:string,hour:number,zone:string){
 const [y,m,d]=day.split('-').map(Number),wall=Date.UTC(y,m-1,d,hour);let result=wall;
 const f=new Intl.DateTimeFormat('en-CA',{timeZone:zone,year:'numeric',month:'2-digit',day:'2-digit',hour:'2-digit',minute:'2-digit',second:'2-digit',hourCycle:'h23'});
 for(let i=0;i<3;i++){const p=Object.fromEntries(f.formatToParts(new Date(result)).map(p=>[p.type,p.value]));const asUTC=Date.UTC(+p.year,+p.month-1,+p.day,+p.hour,+p.minute,+p.second);result+=wall-asUTC;}
 return result;
}
export function visibility(ra:number,dec:number,site:Site,day:string):Visibility{
 if(![ra,dec,site.latitude,site.longitude,site.elevation].every(Number.isFinite)||ra<0||ra>24||Math.abs(dec)>90||Math.abs(site.latitude)>90||Math.abs(site.longitude)>180)throw Error('Invalid target or site coordinates');
 const key=JSON.stringify([ra,dec,site,day]);const saved=cache.get(key);if(saved)return saved;
 const start=zonedTime(day,16,site.timeZone),next=new Date(day+'T12:00:00Z');next.setUTCDate(next.getUTCDate()+1);const end=zonedTime(next.toISOString().slice(0,10),10,site.timeZone);
 const observer=new Observer(site.latitude,site.longitude,site.elevation);
 // Calls are synchronous: the shared custom-star slot never survives an async boundary.
 DefineStar(Body.Star1,ra,dec,1000);
 const samples=[];
 for(let time=start;time<=end;time+=5*60000){const date=new Date(time),eq=Equator(Body.Star1,date,observer,true,true),sun=Equator(Body.Sun,date,observer,true,true);samples.push({time,altitude:Horizon(date,observer,eq.ra,eq.dec).altitude,sun:Horizon(date,observer,sun.ra,sun.dec).altitude});}
 const transit=SearchHourAngle(Body.Star1,observer,0,new Date(start)),limit=(end-start)/86400000;
 const crossing=(body:Body,direction:number,alt?:number)=>{const event=alt==null?SearchRiseSet(body,observer,direction,new Date(start),limit):SearchAltitude(body,observer,direction,new Date(start),limit,alt);return event?.date.getTime()??null;};
 const value={start,end,samples,rise:crossing(Body.Star1,1),set:crossing(Body.Star1,-1),transit:transit.time.date.getTime(),peak:transit.hor.altitude,twilight:[{label:'Nautical dusk',time:crossing(Body.Sun,-1,-12)},{label:'Astronomical dusk',time:crossing(Body.Sun,-1,-18)},{label:'Astronomical dawn',time:crossing(Body.Sun,1,-18)},{label:'Nautical dawn',time:crossing(Body.Sun,1,-12)}]};
 if(cache.size>=32)cache.delete(cache.keys().next().value!);cache.set(key,value);return value;
}
const svgNS='http://www.w3.org/2000/svg';
function svg(tag:string,attrs:Record<string,string|number>,text?:string){const e=document.createElementNS(svgNS,tag);for(const [k,v] of Object.entries(attrs))e.setAttribute(k,String(v));if(text)e.textContent=text;return e;}
export function renderVisibility(host:HTMLElement,ra:number,dec:number,site:Site,day:string,width=1000){
 const compact=width<520;
 host.classList.toggle('is-compact',compact);
 const v=visibility(ra,dec,site,day),W=Math.max(180,Math.round(width)),H=width<360?140:compact?180:230,L=34,R=20,T=compact?28:42,B=28,x=(t:number)=>L+(t-v.start)/(v.end-v.start)*(W-L-R),y=(a:number)=>T+(90-a)/90*(H-T-B);
 const graph=svg('svg',{viewBox:`0 0 ${W} ${H}`,width:W,height:H,role:'img','aria-label':`Target altitude and twilight on ${day} at ${site.name||'profile observing site'}`});
 const clock=(t:number)=>new Intl.DateTimeFormat(undefined,{timeZone:site.timeZone,hour:'2-digit',minute:'2-digit',hourCycle:'h23'}).format(t);
 for(let i=0;i<v.samples.length-1;i++){const p=v.samples[i],sun=p.sun;graph.append(svg('rect',{x:x(p.time),y:T,width:x(v.samples[i+1].time)-x(p.time)+.5,height:H-T-B,fill:sun>-6?'#536477':sun>-12?'#34465e':sun>-18?'#222f49':'#111e30'}));}
 for(const a of [0,30,60,90]){graph.append(svg('line',{x1:L,x2:W-R,y1:y(a),y2:y(a),stroke:'#587086','stroke-opacity':'.4','stroke-dasharray':a===30?'4 5':'0'}),svg('text',{x:L-7,y:y(a)+4,'text-anchor':'end',fill:'#8da6bb','font-size':12},a+'°'));}
 for(let t=v.start;t<=v.end;t+=(compact?6:W<850?3:2)*3600000)graph.append(svg('text',{x:x(t),y:H-7,fill:'#9db4c8','font-size':11,'text-anchor':'middle'},clock(t)));
 const d=v.samples.map((p,i)=>(i?'L':'M')+x(p.time).toFixed(1)+','+y(Math.max(0,p.altitude)).toFixed(1)).join(' ');
 graph.append(svg('path',{d,fill:'none',stroke:'#68daca','stroke-width':3,'stroke-linejoin':'round'}));
 const events=[{time:v.rise,label:'Rise'},{time:v.transit,label:`Meridian ${v.peak.toFixed(0)}°`},{time:v.set,label:'Set'}];
 const labels:{x:number;y:number}[]=[];
 for(const event of events)if(event.time!=null&&event.time>=v.start&&event.time<=v.end){const px=x(event.time);graph.append(svg('line',{x1:px,x2:px,y1:T,y2:H-B,stroke:'#78b9ce','stroke-dasharray':'3 5','stroke-opacity':'.6'}));if(!compact){const lx=Math.min(W-85,Math.max(85,px)),ly=labels.some(label=>Math.abs(label.x-lx)<130&&label.y===16)?30:16;labels.push({x:lx,y:ly});graph.append(svg('text',{x:lx,y:ly,'text-anchor':'middle',fill:'#b4d8e1','font-size':11},event.label+' '+clock(event.time)));}}
 const now=Date.now();if(now>=v.start&&now<=v.end)graph.append(svg('line',{x1:x(now),x2:x(now),y1:T,y2:H-B,stroke:'#f3c978','stroke-width':1.5}));
 host.replaceChildren(graph);
 const eventLegend=document.createElement('div');eventLegend.className='target-events';
 for(const event of events){const span=document.createElement('span');span.textContent=event.label+' '+(event.time==null?'—':clock(event.time));eventLegend.append(span);}host.append(eventLegend);
 const legend=document.createElement('div');legend.className='target-twilight';
 for(const event of v.twilight){const span=document.createElement('span');span.textContent=event.label+' '+(event.time==null?'—':clock(event.time));legend.append(span);}
 host.append(legend);const note=document.createElement('small');note.textContent=`${site.name||'Profile site'} · ${site.latitude.toFixed(3)}°, ${site.longitude.toFixed(3)}° · ${site.timeZone} · Geometric altitude / standard rise-set`;host.append(note);
}
