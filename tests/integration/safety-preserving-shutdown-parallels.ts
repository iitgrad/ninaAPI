import {strict as assert} from 'node:assert';import {writeFileSync} from 'node:fs';
const base='http://10.211.55.10:1889',out='artifacts/2026-10-25-live-insertion',events:any[]=[];
async function req(p:string,b?:any){const r=await fetch(base+p,{method:b?'POST':'GET',headers:{'Content-Type':'application/json'},body:b?JSON.stringify(b):undefined});return {code:r.status,data:JSON.parse((await r.text()).replace(/^\uFEFF/,''))};}
async function state(){const s=(await req('/api/v1/status')).data;assert.equal(s.states.application.value.environment,'simulation');return s;}
async function run(device:string,action:string,parameters?:any,deviceId?:string){const c={requestId:crypto.randomUUID(),device,action,parameters,deviceId};const r=await req('/api/v1/commands',c);events.push({c,r});assert.equal(r.code,202,JSON.stringify(r));for(let i=0;i<150;i++){const o=(await req('/api/v1/operations/'+c.requestId)).data;if(o.completedAt){events.push({o});assert.equal(o.status,'succeeded',JSON.stringify(o));await Bun.sleep(200);return;}await Bun.sleep(100);}throw Error('Timeout');}
const q=(s:any)=>s.states.sequence.value;let id='';
try{
 let s=await state();assert(!s.states.application.value.sequenceRunning);assert.equal(q(s).nodes.length,4);assert(s.states.safety.value.connected);assert.equal(s.states.safety.value.isSafe,false);
 const camera=s.states.devices.value.items.find((d:any)=>d.device==='camera'&&d.selected&&d.allowed);await run('camera','connect',undefined,camera.id);
 s=await state();const entry=s.states.sequenceCatalog.value.items.find((e:any)=>e.nodeType==='DisconnectOtherEquipmentInstruction');assert(entry);
 const start=q(s).nodes.find((n:any)=>n.type==='StartAreaContainer');await run('sequence','insertDraft',{sequenceId:q(s).sequenceId,expectedRevision:q(s).configurationRevision,executionId:q(s).executionId,parentId:start.id,afterNodeId:'',draft:{type:entry.type,fields:{}}});
 s=await state();id=q(s).nodes.find((n:any)=>n.type==='DisconnectOtherEquipmentInstruction').id;
 await run('sequence','start');s=await state();assert.equal(s.states.camera.value.connected,false);assert.equal(s.states.safety.value.connected,true);assert.equal(s.states.safety.value.isSafe,false);
 writeFileSync(out+'/safety-shutdown-after.json',JSON.stringify(s,null,2));console.log('Native Disconnect Other Equipment executed: virtual camera disconnected; roof monitor remained connected and CLOSED/unsafe.');
}finally{const s=await state();if(id&&!s.states.application.value.sequenceRunning){await run('sequence','delete',{sequenceId:q(s).sequenceId,expectedRevision:q(s).configurationRevision,nodeId:id});await run('sequence','reset');}writeFileSync(out+'/safety-shutdown-commands.json',JSON.stringify(events,null,2));}
