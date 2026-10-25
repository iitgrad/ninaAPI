import {mkdirSync,writeFileSync} from 'node:fs';
import {strict as assert} from 'node:assert';
const base='http://10.211.55.10:1889',out='artifacts/2026-10-23-sequence-editor';mkdirSync(out,{recursive:true});
const evidence:any[]=[];
async function req(path:string,body?:unknown){const r=await fetch(base+path,{method:body?'POST':'GET',headers:{'Content-Type':'application/json'},body:body?JSON.stringify(body):undefined});return{http:r.status,data:JSON.parse((await r.text()).replace(/^\uFEFF/,''))};}
async function state(){const {data:s}=await req('/api/v1/status');assert.equal(s.states.application.value.environment,'simulation');assert.equal(s.states.application.value.version,'0.4.16.0');assert.equal(s.states.administration.value.profile.id,'cd432469-2b6e-442c-987e-78504975ff3e');return s;}
const seq=(s:any)=>s.states.sequence.value;
async function waitFor(check:(s:any)=>boolean,limit=15000){const until=Date.now()+limit;while(Date.now()<until){const s=await state();if(check(s))return s;await Bun.sleep(100);}throw Error('State did not settle');}
async function terminal(id:string){for(let i=0;i<600;i++){const r=await req('/api/v1/operations/'+id);if(r.data.completedAt)return r.data;await Bun.sleep(100);}throw Error('Operation incomplete '+id);}
async function command(action:string,parameters?:object,expected=202){const c={requestId:crypto.randomUUID(),device:'sequence',action,parameters};const r=await req('/api/v1/commands',c);evidence.push({command:c,response:r});assert.equal(r.http,expected,JSON.stringify(r));if(expected!==202)return r;const t=await terminal(c.requestId);evidence.at(-1).terminal=t;assert.equal(t.status,'succeeded',JSON.stringify(t));return r;}
async function loadFixture(){const old=seq(await state()).sequenceId;await command('load',{fileName:'Dashboard-Test.json'});await waitFor(s=>seq(s).sequenceId!==old&&seq(s).nodes.some((n:any)=>n.type==='WaitForTimeSpan'));}
function token(s:any){const q=seq(s);return{sequenceId:q.sequenceId,expectedRevision:q.configurationRevision};}
async function insert(parentId:string,type:string,index=0){const before=await state();const old=new Set(seq(before).nodes.map((n:any)=>n.id));await command('insert',{...token(before),parentId,type,index});const after=await waitFor(s=>seq(s).nodes.some((n:any)=>n.parentId===parentId&&n.type===type&&!old.has(n.id)));return seq(after).nodes.find((n:any)=>n.parentId===parentId&&n.type===type&&!old.has(n.id));}
async function edit(nodeId:string,field:string,value:unknown){const before=await state();await command('editField',{...token(before),nodeId,field,value});await waitFor(s=>seq(s).nodes.find((n:any)=>n.id===nodeId)?.editableFields?.some((f:any)=>f.key===field&&(typeof value==='number'?Math.abs(f.value-value)<1e-9:f.value===value)));}
const sockets:WebSocket[]=[],frames:any[][]=[[],[]];
try{
 const deadline=Date.now()+60000;while(true){const r=await req('/api/v1/status');if(r.data.states?.application&&r.data.states?.sequence?.value.available&&r.data.states?.administration)break;if(Date.now()>deadline)throw Error('NINA startup did not become ready');await Bun.sleep(250);}
 const initial=await state();assert.equal(initial.states.application.value.sequenceRunning,false);assert.equal(seq(initial).editing.canEdit,true);writeFileSync(out+'/runtime-before.json',JSON.stringify(initial,null,2));
 for(let i=0;i<2;i++){const w=new WebSocket(base.replace('http:','ws:')+'/socket/v1');w.onmessage=e=>{const v=JSON.parse(String(e.data).replace(/^\uFEFF/,''));frames[i].push(v);if(frames[i].length>200)frames[i].shift();};sockets.push(w);}
 await waitFor(()=>sockets.every(w=>w.readyState===WebSocket.OPEN));
 await loadFixture();await waitFor(s=>seq(s).nodes.some((n:any)=>n.type==='WaitForTimeSpan'));
 let s=await state(),nodes=seq(s).nodes;const wait=nodes.find((n:any)=>n.type==='WaitForTimeSpan'),exposure=nodes.find((n:any)=>n.type==='TakeExposure');
 await command('edit',{nodeId:wait.id,field:'Time',value:2},409);await edit(wait.id,'Time',2);if(exposure){assert(!exposure.editableFields.some((f:any)=>f.key==='ExposureCount'),'Single exposure count is a runtime counter');await edit(exposure.id,'ExposureTime',.2);}
 const stale=token(await state());await edit(wait.id,'Time',3);await command('editField',{...stale,nodeId:wait.id,field:'Time',value:4},409);
 const concurrent=await state();const contenders=[11,12].map(value=>({requestId:crypto.randomUUID(),device:'sequence',action:'editField',parameters:{...token(concurrent),nodeId:wait.id,field:'Time',value}}));
 const admissions=await Promise.all(contenders.map(c=>req('/api/v1/commands',c)));const outcomes=await Promise.all(admissions.map((r,i)=>r.http===202?terminal(contenders[i].requestId):Promise.resolve(r.data)));
 assert.equal(outcomes.filter(o=>o.status==='succeeded').length,1,'Exactly one concurrent edit may succeed');assert(admissions.every(r=>r.http===202||r.http===409));evidence.push({concurrent:contenders,admissions,outcomes});
 const winner=outcomes.findIndex(o=>o.status==='succeeded');await waitFor(s=>seq(s).nodes.find((n:any)=>n.id===wait.id)?.editableFields?.find((f:any)=>f.key==='Time')?.value===contenders[winner].parameters.value);const versionAfter=seq(await state()).configurationRevision;assert.equal((await req('/api/v1/commands',contenders[winner])).http,202);assert.equal(seq(await state()).configurationRevision,versionAfter,'Duplicate UUID must not execute twice');
 const same=await state();await command('editField',{...token(same),nodeId:wait.id,field:'Time',value:-1},400);assert.equal(seq(await state()).configurationRevision,seq(same).configurationRevision);
 const root=seq(await state()).nodes.find((n:any)=>n.type==='SequenceRootContainer');await command('insert',{...token(await state()),parentId:root.id,type:'WaitForTimeSpan',index:0},400);
 const start=seq(await state()).nodes.find((n:any)=>n.type==='StartAreaContainer');
 const first=await insert(start.id,'WaitForTimeSpan',0),second=await insert(start.id,'WaitForTimeSpan',0);const order=seq(await state()).nodes.filter((n:any)=>n.parentId===start.id);assert.equal(order[0].id,second.id);assert.equal(order[1].id,first.id);
 const sequential=await insert(start.id,'SequentialContainer');await edit(sequential.id,'Name','Sequential test');
 const parallel=await insert(sequential.id,'ParallelContainer');await edit(parallel.id,'Name','Parallel test');
 const child=await insert(parallel.id,'WaitForTimeSpan');await edit(child.id,'Time',.2);
 const condition=await insert(sequential.id,'LoopCondition');assert.equal(condition.kind,'condition');await edit(condition.id,'Iterations',3);
 for(const type of ['AutofocusAfterExposures','DitherAfterExposures']){const trigger=await insert(sequential.id,type);assert.equal(trigger.kind,'trigger');await edit(trigger.id,'AfterExposures',7);}
 const targetArea=seq(await state()).nodes.find((n:any)=>n.type==='TargetAreaContainer');const target=await insert(targetArea.id,'DeepSkyObjectContainer');
 for(const [key,value]of [['TargetName','Andromeda test'],['RightAscension',.7123],['Declination',41.269],['Rotation',12]]as const)await edit(target.id,key,value);
 const filter=await insert(target.id,'SwitchFilter');await edit(filter.id,'Filter',4);await command('editField',{...token(await state()),nodeId:filter.id,field:'Filter',value:31},400);
 const cameraTypes=['TakeExposure','TakeManyExposures','SmartExposure','TakeSubframeExposure','CoolCamera','WarmCamera','DewHeater','SetReadoutMode','SetUSBLimit'];
 for(const type of cameraTypes){const item=await insert(target.id,type);if(type==='TakeManyExposures'||type==='SmartExposure'){assert.notEqual(item.name,'Instruction group');assert.equal(item.editableFields.find((f:any)=>f.key==='ExposureTime')?.value,1);assert.equal(item.editableFields.find((f:any)=>f.key==='ExposureCount')?.value,1);await edit(item.id,'ExposureTime',.5);await edit(item.id,'ExposureCount',3);}if(type==='CoolCamera'){await edit(item.id,'Temperature',-10);await edit(item.id,'Duration',2);}if(type==='WarmCamera')await edit(item.id,'Duration',1);if(type==='DewHeater')await edit(item.id,'OnOff',true);if(type==='SetUSBLimit')await edit(item.id,'USBLimit',40);}
 writeFileSync(out+'/edited-tree.json',JSON.stringify(await state(),null,2));
 const oldToken=token(await state());await loadFixture();await waitFor(s=>seq(s).sequenceId!==oldToken.sequenceId);await command('editField',{...oldToken,nodeId:wait.id,field:'Time',value:4},409);
 // A wait-only parallel group makes execution verification independent of hardware.
 s=await state();const start2=seq(s).nodes.find((n:any)=>n.type==='StartAreaContainer');const p=await insert(start2.id,'ParallelContainer');
 const parallelIds:string[]=[];for(let i=0;i<2;i++){const w=await insert(p.id,'WaitForTimeSpan',i);parallelIds.push(w.id);await edit(w.id,'Time',2);}
 // Existing fixture also includes a short camera exposure. Explicit simulation camera only.
 const camera=s.states.devices.value.items.find((d:any)=>d.device==='camera'&&d.selected);if(!s.states.camera.value.connected){const c={requestId:crypto.randomUUID(),device:'camera',action:'connect',deviceId:camera.id};assert.equal((await req('/api/v1/commands',c)).http,202);assert.equal((await terminal(c.requestId)).status,'succeeded');}
 const w=seq(await state()).nodes.find((n:any)=>n.type==='WaitForTimeSpan'&&n.parentId!==p.id);await edit(w.id,'Time',3);
 const beforeRun=await state(),run={requestId:crypto.randomUUID(),device:'sequence',action:'start'};assert.equal((await req('/api/v1/commands',run)).http,202);
 await waitFor(s=>s.states.application.value.sequenceRunning===true);
 await waitFor(s=>parallelIds.every(id=>seq(s).nodes.find((n:any)=>n.id===id)?.sourceStatus==='RUNNING'));evidence.push({parallelChildrenObservedRunningTogether:parallelIds});
 await command('editField',{...token(beforeRun),nodeId:w.id,field:'Time',value:4},409);
 await command('insert',{...token(beforeRun),parentId:p.id,type:'WaitForTimeSpan',index:0},409);
 const done=await terminal(run.requestId);assert.equal(done.status,'succeeded',JSON.stringify(done));evidence.push({command:run,terminal:done});
 const afterRun=await waitFor(s=>!s.states.application.value.sequenceRunning);assert.equal(seq(afterRun).configurationRevision,seq(beforeRun).configurationRevision,'Execution counters must not change configuration revision');await loadFixture();
 if(!initial.states.camera.value.connected){const c={requestId:crypto.randomUUID(),device:'camera',action:'disconnect'};assert.equal((await req('/api/v1/commands',c)).http,202);assert.equal((await terminal(c.requestId)).status,'succeeded');}
 await Bun.sleep(400);assert(frames.every(f=>f.some(x=>x.states?.sequence?.value?.configurationRevision>1)),'Both websocket viewers receive edits');
 writeFileSync(out+'/runtime-after.json',JSON.stringify(await state(),null,2));console.log('Parallels sequence editing/insertion passed:',evidence.length,'commands, two WebSocket viewers');
}finally{for(const w of sockets)w.close();writeFileSync(out+'/runtime-commands.json',JSON.stringify(evidence,null,2));writeFileSync(out+'/websocket-summary.json',JSON.stringify(frames.map(f=>({frames:f.length,sequences:f.filter(x=>x.states?.sequence).length})),null,2));}
