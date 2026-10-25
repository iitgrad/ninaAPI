import {strict as assert} from 'node:assert';
import {writeFileSync} from 'node:fs';
const base='http://10.211.55.10:1889',out='artifacts/2026-10-23-sequence-catalog',evidence:any[]=[];
async function request(path:string,body?:unknown){const r=await fetch(base+path,{method:body?'POST':'GET',headers:{'Content-Type':'application/json'},body:body?JSON.stringify(body):undefined});return {http:r.status,data:JSON.parse((await r.text()).replace(/^\uFEFF/,''))};}
async function state(){const {data:s}=await request('/api/v1/status');assert.equal(s.states.application.value.environment,'simulation');assert.equal(s.states.application.value.version,'0.4.17.0');assert.equal(s.states.administration.value.profile.id,'cd432469-2b6e-442c-987e-78504975ff3e');assert.equal(s.states.application.value.sequenceRunning,false);assert.equal(s.states.camera.value.isExposing,false);return s;}
const seq=(s:any)=>s.states.sequence.value;
async function command(action:string,parameters:object,expected=202){await state();const c={requestId:crypto.randomUUID(),device:'sequence',action,parameters},r=await request('/api/v1/commands',c);assert.equal(r.http,expected,JSON.stringify(r));evidence.push({command:c,response:r});if(expected!==202)return;for(let i=0;i<100;i++){const {data:op}=await request('/api/v1/operations/'+c.requestId);if(op.completedAt){evidence.at(-1).terminal=op;assert.equal(op.status,'succeeded',JSON.stringify(op));return;}await Bun.sleep(100);}throw Error('Operation timeout');}
const token=(s:any)=>({sequenceId:seq(s).sequenceId,expectedRevision:seq(s).configurationRevision});
async function insert(parentId:string,type:string){const s=await state(),catalog=s.states.sequenceCatalog.value;const entry=catalog.items.find((e:any)=>e.type===type||e.nodeType===type);assert(entry,'catalog missing '+type);const before=new Set(seq(s).nodes.map((n:any)=>n.id));await command('insert',{...token(s),parentId,type:entry.type,index:0});for(let i=0;i<100;i++){const after=await state(),item=seq(after).nodes.find((n:any)=>n.parentId===parentId&&n.type===entry.nodeType&&!before.has(n.id));if(item)return item;await Bun.sleep(100);}throw Error('Inserted node not projected');}
async function edit(nodeId:string,field:string,value:unknown){await command('editField',{...token(await state()),nodeId,field,value});for(let i=0;i<100;i++){const n=seq(await state()).nodes.find((n:any)=>n.id===nodeId);if(n?.editableFields?.some((f:any)=>f.key===field&&f.value===value))return;await Bun.sleep(100);}throw Error('Edit readback timeout');}
// Wait only for startup telemetry, without issuing any commands.
for(let attempt=0;attempt<120;attempt++){const r=await request('/api/v1/status');if(r.data.states?.application&&r.data.states?.administration&&r.data.states?.sequenceCatalog&&r.data.states?.sequence?.value?.available)break;if(attempt===119)throw Error('Simulator startup timeout');await Bun.sleep(250);}
try{
 const initial=await state(),catalog=initial.states.sequenceCatalog.value;assert(catalog.complete);assert(catalog.count>100);assert(catalog.items.some((e:any)=>e.source==='WhenPlugin'));assert(new Set(catalog.items.map((e:any)=>e.type)).size===catalog.count);assert(JSON.stringify(catalog).length<60*1024);
 const targets=seq(initial).nodes.find((n:any)=>n.type==='TargetAreaContainer'),target=await insert(targets.id,'DeepSkyObjectContainer');
 for(const [field,value] of [['TargetName','M31 · Andromeda'],['RightAscension',.7123],['Declination',41.269],['Rotation',12]]as const)await edit(target.id,field,value);
 const group=await insert(target.id,'SequentialContainer');await edit(group.id,'Name','Hα integration');
 const exposure=await insert(group.id,'TakeExposure');await edit(exposure.id,'ExposureTime',180);
 const loop=await insert(group.id,'LoopCondition');await edit(loop.id,'Iterations',20);
 const dither=await insert(group.id,'DitherAfterExposures');await edit(dither.id,'AfterExposures',5);
 await insert(group.id,'AutofocusAfterExposures');
 const pluginEntry=catalog.items.find((e:any)=>e.source==='WhenPlugin'&&e.nodeType==='CVContainer');
 const cv=await insert(target.id,pluginEntry.type);assert(cv.insertKinds.includes('instruction'));
 for(const name of ['SetVariable','WaitUntil']){const e=catalog.items.find((e:any)=>e.source==='WhenPlugin'&&e.nodeType===name);const n=await insert(cv.id,e.type);assert.equal(n.type,e.nodeType);assert.equal(n.editableFields.length,0,'Unknown plugin fields remain read-only');}
 const trigger=catalog.items.find((e:any)=>e.source==='WhenPlugin'&&e.nodeType==='ConditionalTrigger');const tr=await insert(cv.id,trigger.type);assert.equal(tr.kind,'trigger');
 await command('insert',{...token(await state()),parentId:cv.id,type:'System.Diagnostics.Process',index:0},400);
 const after=await state();assert(after.states.sequenceCatalog.value.complete);assert.equal(after.states.sequenceCatalog.value.revision,catalog.revision);assert.equal(after.states.administration.value.site.timeZone,'America/Chicago');writeFileSync(out+'/verified-tree.json',JSON.stringify(after,null,2));console.log('Verified complete catalog:',catalog.count,'entries; built-in and Powerups insertion; target coordinates; no sequence execution.');
}finally{writeFileSync(out+'/catalog-command-results.json',JSON.stringify(evidence,null,2));}
