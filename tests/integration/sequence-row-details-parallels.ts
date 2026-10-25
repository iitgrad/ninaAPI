import {strict as assert} from 'node:assert';
import {writeFileSync} from 'node:fs';
const base='http://10.211.55.10:1889',out='artifacts/2026-10-23-sequence-row-details',evidence:any[]=[];
async function request(path:string,body?:unknown){const r=await fetch(base+path,{method:body?'POST':'GET',headers:{'Content-Type':'application/json'},body:body?JSON.stringify(body):undefined});return {http:r.status,data:JSON.parse((await r.text()).replace(/^\uFEFF/,''))};}
async function state(){const {data:s}=await request('/api/v1/status');assert.equal(s.states.application.value.environment,'simulation');assert.equal(s.states.application.value.version,'0.4.19.0');assert.equal(s.states.administration.value.profile.id,'cd432469-2b6e-442c-987e-78504975ff3e');assert.equal(s.states.application.value.sequenceRunning,false);assert.equal(s.states.camera.value.isExposing,false);return s;}
const seq=(s:any)=>s.states.sequence.value;
async function command(action:string,parameters:object,expected=202){await state();const c={requestId:crypto.randomUUID(),device:'sequence',action,parameters},r=await request('/api/v1/commands',c);assert.equal(r.http,expected,JSON.stringify(r));evidence.push({command:c,response:r});if(expected!==202)return;for(let i=0;i<100;i++){const {data:op}=await request('/api/v1/operations/'+c.requestId);if(op.completedAt){evidence.at(-1).terminal=op;assert.equal(op.status,'succeeded',JSON.stringify(op));return;}await Bun.sleep(100);}throw Error('Operation timeout');}
const token=(s:any)=>({sequenceId:seq(s).sequenceId,expectedRevision:seq(s).configurationRevision});
async function insert(parentId:string,type:string){const s=await state(),catalog=s.states.sequenceCatalog.value;const entry=catalog.items.find((e:any)=>e.type===type)??catalog.items.find((e:any)=>e.nodeType===type);assert(entry,'catalog missing '+type);const before=new Set(seq(s).nodes.map((n:any)=>n.id));await command('insert',{...token(s),parentId,type:entry.type,index:0});for(let i=0;i<100;i++){const after=await state(),item=seq(after).nodes.find((n:any)=>n.parentId===parentId&&n.type===entry.nodeType&&!before.has(n.id));if(item)return item;await Bun.sleep(100);}throw Error('Inserted node not projected');}
async function edit(nodeId:string,field:string,value:unknown){await command('editField',{...token(await state()),nodeId,field,value});for(let i=0;i<100;i++){const n=seq(await state()).nodes.find((n:any)=>n.id===nodeId);if(n?.editableFields?.some((f:any)=>f.key===field&&f.value===value))return;await Bun.sleep(100);}throw Error('Edit readback timeout');}
// Wait only for startup telemetry, without issuing any commands.
for(let attempt=0;attempt<120;attempt++){const r=await request('/api/v1/status');if(r.data.states?.application&&r.data.states?.administration&&r.data.states?.sequenceCatalog&&r.data.states?.sequence?.value?.available)break;if(attempt===119)throw Error('Simulator startup timeout');await Bun.sleep(250);}
try {
 const initial=await state(),start=seq(initial).nodes.find((n:any)=>n.type==='StartAreaContainer');
 const group=await insert(start.id,'SequentialContainer');await edit(group.id,'Name','Detailed instruction verification');
 const cool=await insert(group.id,'CoolCamera');await edit(cool.id,'Temperature',-10);await edit(cool.id,'Duration',5);
 const wait=await insert(group.id,'WaitForTimeSpan');await edit(wait.id,'Time',30);
 const track=await insert(group.id,'SetTracking'),clock=await insert(group.id,'WaitForTime'),guide=await insert(group.id,'StartGuiding');
 const exposure=await insert(group.id,'TakeExposure');await edit(exposure.id,'ExposureTime',.025);await edit(exposure.id,'Gain',100);await edit(exposure.id,'Offset',50);
 await insert(group.id,'SwitchFilter');
 const after=await state(),nodes=seq(after).nodes,detail=(id:string)=>nodes.find((n:any)=>n.id===id).details;
 assert.equal(detail(cool.id).temperature,-10);assert.equal(detail(cool.id).duration,5);
 assert.equal(typeof detail(track.id).trackingMode,'string');assert.equal(typeof detail(clock.id).hours,'number');assert.equal(typeof detail(clock.id).timeReferenceFixed,'boolean');
 assert.equal(typeof detail(guide.id).forceCalibration,'boolean');assert.equal(detail(exposure.id).exposureTime,.025);assert.equal(typeof detail(exposure.id).imageType,'string');assert.equal(detail(exposure.id).binX,1);
 const fields=nodes.find((n:any)=>n.id===exposure.id).editableFields;assert(!Object.hasOwn(fields[0],'choices'),'unused null metadata omitted');assert.equal(seq(after).truncated,false);
 writeFileSync(out+'/sim-verified.json',JSON.stringify(after,null,2));console.log('Simulator detail telemetry verified; no equipment command or sequence execution.');
}finally{writeFileSync(out+'/sim-commands.json',JSON.stringify(evidence,null,2));}
