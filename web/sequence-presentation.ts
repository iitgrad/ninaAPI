import type {SequenceNode} from './dashboard';
export type SequenceField={key:string;label:string;type:string;value:unknown;unit?:string;choices?:{value:number;label:string}[]};
export type VisualNode=SequenceNode&{editableFields?:SequenceField[];insertKinds?:string[];canStructure?:boolean};
const numeric=(v:unknown):v is number=>typeof v==='number'&&Number.isFinite(v);
const fmt=(v:number)=>v.toLocaleString(undefined,{maximumFractionDigits:4});
const clean=(v:unknown)=>typeof v==='string'?v.trim():'';
const words=(v:string)=>v.replace(/_/g,' ').replace(/([a-z])([A-Z])/g,'$1 $2');
function field(n:VisualNode,key:string){return n.editableFields?.find(f=>f.key===key)?.value;}
const defaults:Record<string,string>={SwitchFilter:'Select filter',TakeExposure:'Take exposure',SmartExposure:'Smart exposure',SequentialContainer:'Sequential set',ParallelContainer:'Parallel set',LoopCondition:'Repeat count',DitherAfterExposures:'Dither',AutofocusAfterExposures:'Autofocus',TakeManyExposures:'Multiple exposures',DeepSkyObjectContainer:'Target',SetTracking:'Set mount tracking',FindHome:'Home mount'};
export function sequenceName(n:VisualNode){
 if(['WhenUnsafe','WhenBecomesUnsafe'].includes(n.type))return 'When roof closes';
 if(['WhenSafe','WhenBecomesSafe'].includes(n.type))return 'When roof opens';
 if(['ConnectEquipment','DisconnectEquipment'].includes(n.type)){
  const equipment=clean(n.details.equipment);
  if(equipment){const action=n.type==='ConnectEquipment'?'Connect':'Disconnect';const generic=n.name.toLowerCase().replace(/\s/g,'')===n.type.toLowerCase();return generic?`${action} ${equipment.toLowerCase()}`:`${n.name} · ${equipment}`;}
 }
 return n.name.toLowerCase().replace(/\s/g,'')===n.type.toLowerCase()?defaults[n.type]??words(n.name):n.name;
}
export type CoolingContext={focuserTemperature?:number;weatherTemperature?:number};
export function sequenceDetail(n:VisualNode,target?:VisualNode,cooling:CoolingContext={}):string{
 const d=n.details,get=(key:string)=>field(n,key)??d[key[0].toLowerCase()+key.slice(1)],parts:string[]=[];
 const add=(label:string,v:unknown,unit='')=>{if(numeric(v))parts.push(`${label}${label?' ':''}${fmt(v)}${unit}`);else if(typeof v==='boolean')parts.push(`${label} ${v?'on':'off'}`);else if(clean(v))parts.push(`${label}${label?' ':''}${clean(v)}`);};
 if(['ConnectEquipment','DisconnectEquipment'].includes(n.type))return clean(d.equipment)?'':'Equipment not reported';
 if(n.type==='TemplateByReference'&&clean(d.templateName))return 'Template: '+clean(d.templateName);
 if(n.type==='WaitForTimeSpan'&&clean(d.waitExpression))return 'Wait '+clean(d.waitExpression)+' s';
 if(n.type==='LoopCondition'){add('Exit after',get('Iterations'),' repeats');return parts.join(' · ');}
 if(n.type==='WaitForTime'||n.type==='TimeCondition'){
  const ref=clean(d.timeReference),h=d.hours,m=d.minutes,s=d.seconds,offset=d.minutesOffset;
  const clock=[h,m,s].every(numeric)?[h,m,s].map(v=>String(v).padStart(2,'0')).join(':'):'';
  if(d.timeReferenceFixed===true||ref==='Time'||ref==='TimeProvider'){if(clock)parts.push((n.type==='TimeCondition'?'Until ':'At ')+clock);}
  else if(ref){parts.push(ref);if(numeric(offset)&&offset!==0)parts.push((offset>0?'+':'')+fmt(offset)+' min');}
  if(!parts.length)parts.push('Time settings not reported');if(clean(d.rolloverTime))parts.push('Rollover '+clean(d.rolloverTime));return parts.join(' · ');
 }
 if(n.type==='Center'||n.type==='CenterAndRotate'){if(d.inherited===true&&target)parts.push(target.name);add('RA',d.rightAscension,' h');add('Dec',d.declination,'°');return parts.join(' · ');}
 if(n.type==='SetTracking'){const mode=clean(d.trackingMode);return mode?'Tracking '+(/^(Stopped|Off|Disabled)$/i.test(mode)?'Off':words(mode)):'Tracking mode not reported';}
 if(n.type==='SendNotificationInstruction')return [clean(d.message),typeof d.attachPreview==='boolean'?`Attach photo: ${d.attachPreview?'on':'off'}`:'Attach photo: not reported'].filter(Boolean).join(' · ');
 if(n.type==='SendToDiscordWebhook')return clean(d.message);
 if(n.type==='StartGuiding'){if(typeof d.forceCalibration==='boolean')add('Force calibration',d.forceCalibration);return parts.join(' · ');}
 if(n.type==='DeepSkyObjectContainer'){add('RA',get('RightAscension'),' h');add('Dec',get('Declination'),'°');add('Rotation',get('Rotation'),'°');return parts.join(' · ');}
 if(n.type==='CoolCamera'||n.type==='WarmCamera'){
  if(n.type==='CoolCamera'){
   const expression=clean(d.temperatureExpression),match=/^(FocuserTemperature|FocuserTemp|Temperature)\s*([+-])\s*(\d+(?:\.\d+)?)$/.exec(expression);
   if(match){const delta=Number(match[3])*(match[2]==='-'?-1:1),focuser=match[1]!=='Temperature',ambient=focuser?cooling.focuserTemperature:cooling.weatherTemperature;
    parts.push(`Δ ${delta>0?'+':''}${fmt(delta)}°C from ${focuser?'focuser':'weather'}`);
    parts.push(numeric(ambient)?`Estimated target ${fmt(ambient+delta)}°C`:'Target pending sensor reading');
   }else if(expression)parts.push('Target = '+expression+' °C');else add('Target',get('Temperature'),'°C');
  }
  if(clean(d.durationExpression))parts.push('Minimum '+clean(d.durationExpression)+' min');else add('Ramp',get('Duration'),' min');
  return parts.length?parts.join(' · '):'Cooling settings not reported';
 }
 if(n.type==='SwitchFilter'){const f=n.editableFields?.find(f=>f.key==='Filter');return 'Filter '+(clean(d.filter)||f?.choices?.find(c=>c.value===f.value)?.label||'not reported');}
 if(n.kind==='trigger'&&get('AfterExposures')!=null){add('Every',get('AfterExposures'),' frames');return parts.join(' · ');}
 const seen=new Set<string>();
 for(const f of n.editableFields??[]){
  if(['Name','TargetName','RightAscension','Declination','Rotation'].includes(f.key))continue;
  seen.add(f.key);
  if((f.key==='Gain'||f.key==='Offset')&&f.value===-1){parts.push(f.label+' current');continue;}
  const value=f.type==='choice'?f.choices?.find(c=>c.value===f.value)?.label??f.value:f.value;
  add(f.label||words(f.key),value,f.unit?' '+f.unit:'');
 }
 for(const [key,label,unit]of [['ExposureTime','Exposure',' s'],['Gain','Gain',''],['Offset','Offset',''],['Time','Wait',' s'],['Position','Position','']]as const){
  if(!seen.has(key)){const value=get(key);if((key==='Gain'||key==='Offset')&&value===-1)parts.push(label+' current');else add(label,value,unit);}
 }
 if(clean(d.imageType))parts.push(clean(d.imageType));
 if(numeric(d.binX)&&numeric(d.binY))parts.push('Bin '+d.binX+'×'+d.binY);
 if(!seen.has('OnOff')&&typeof d.onOff==='boolean')add('Heater',d.onOff);
 if(!seen.has('USBLimit'))add('USB limit',d.usbLimit);
 if(!seen.has('Mode'))add('Readout mode',d.readoutMode);
 return parts.join(' · ');
}
