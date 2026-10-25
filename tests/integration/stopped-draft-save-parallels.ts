import {strict as assert} from 'node:assert';import {writeFileSync} from 'node:fs';
const base='http://10.211.55.10:1889',out='artifacts/2026-10-25-live-insertion',events:any[]=[];
async function req(path:string,b?:any){const r=await fetch(base+path,{method:b?'POST':'GET',headers:{'Content-Type':'application/json'},body:b?JSON.stringify(b):undefined});return {code:r.status,data:JSON.parse((await r.text()).replace(/^\uFEFF/,''))};}
async function state(){const s=(await req('/api/v1/status')).data;assert.equal(s.states.application.value.environment,'simulation');assert.equal(s.states.administration.value.profile.id,'cd432469-2b6e-442c-987e-78504975ff3e');return s;}
const q=(s:any)=>s.states.sequence.value,token=(s:any)=>({sequenceId:q(s).sequenceId,expectedRevision:q(s).configurationRevision,executionId:q(s).executionId});
async function cmd(device:string,action:string,parameters?:any,expected=202){const c={requestId:crypto.randomUUID(),device,action,parameters};const r=await req('/api/v1/commands',c);events.push({c,r});assert.equal(r.code,expected,JSON.stringify(r));return c.requestId;}
async function done(id:string,expected='succeeded'){for(let i=0;i<150;i++){const r=await req('/api/v1/operations/'+id);if(r.data.completedAt){events.push({operation:r.data});assert.equal(r.data.status,expected,JSON.stringify(r));return;}await Bun.sleep(100);}throw Error('Receipt timeout');}
async function wait(test:(s:any)=>boolean){for(let i=0;i<200;i++){const s=await state();if(test(s))return s;await Bun.sleep(100);}throw Error('State timeout');}
for(let i=0;i<120;i++){const r=await req('/api/v1/status');if(r.data.states?.application?.value&&r.data.states?.sequence?.value?.available)break;await Bun.sleep(250);}
let groupId='';
try {
 let s=await state();assert(!s.states.application.value.sequenceRunning);assert.equal(q(s).nodes.length,4);
 const before=q(s).nodes.length;const schema=await req('/api/v1/sequence/insertion-schema?type=WaitForTimeSpan');assert.equal(schema.code,200);assert.equal(q(await state()).nodes.length,before);
 const start=q(s).nodes.find((n:any)=>n.type==='StartAreaContainer');const position={parentId:start.id,afterNodeId:''};
 const draft={type:'SequentialContainer',fields:{Name:'Stopped editor acceptance'},items:[{type:'WaitForTimeSpan',fields:{Time:3}}]};
 await cmd('sequence','insertDraft',{...token(s),...position,draft:{...draft,items:[]}},400);
 await cmd('sequence','insertLive',{...token(s),...position,draft},400);
 await done(await cmd('sequence','insertDraft',{...token(s),...position,draft}));
 const old=token(s);s=await wait(s=>q(s).nodes.length===6);groupId=q(s).nodes.find((n:any)=>n.name==='Stopped editor acceptance').id;
 await cmd('sequence','insertDraft',{...old,...position,draft},409);
 const fileName='Starfront-Stopped-Acceptance-'+Date.now()+'.json';
 const run=await cmd('sequence','start');s=await wait(s=>s.states.application.value.sequenceRunning&&q(s).nodes.some((n:any)=>n.type==='WaitForTimeSpan'&&n.sourceStatus==='RUNNING'));
 assert.equal(q(s).editing.canEdit,false);assert.equal(q(s).draftInsertion.supported,false);assert.equal(q(s).liveInsertion.supported,false);
 await cmd('sequence','insertDraft',{...token(s),...position,draft},409);await cmd('sequence','save',{sequenceId:q(s).sequenceId,expectedRevision:q(s).configurationRevision,fileName,replace:false},409);
 await wait(s=>!s.states.application.value.sequenceRunning&&q(s).nodes[0].sourceStatus==='FINISHED');await done(run);s=await state();
 const statesBefore=q(s).nodes.map((n:any)=>[n.id,n.sourceStatus]);
 await done(await cmd('sequence','save',{sequenceId:q(s).sequenceId,expectedRevision:q(s).configurationRevision,fileName,replace:false}));
 s=await wait(s=>s.states.administration.value.sequenceFileName===fileName);assert.deepEqual(q(s).nodes.map((n:any)=>[n.id,n.sourceStatus]),statesBefore);
 await done(await cmd('sequence','save',{sequenceId:q(s).sequenceId,expectedRevision:q(s).configurationRevision,fileName,replace:false}),'rejected');
 await done(await cmd('sequence','save',{sequenceId:q(s).sequenceId,expectedRevision:q(s).configurationRevision,fileName,replace:true}));
 await Bun.sleep(2500);s=await state();assert(!s.states.application.value.sequenceRunning);
 writeFileSync(out+'/stopped-saved.json',JSON.stringify(s,null,2));
 await done(await cmd('sequence','load',{fileName:'Targets/'+fileName}));s=await wait(s=>q(s).sequenceId!==statesBefore[0][0]);assert(!s.states.application.value.sequenceRunning);
 assert(q(s).nodes.some((n:any)=>n.name==='Stopped editor acceptance'));assert.equal(q(s).nodes.find((n:any)=>n.type==='WaitForTimeSpan').editableFields.find((f:any)=>f.key==='Time').value,3);
 groupId=q(s).nodes.find((n:any)=>n.name==='Stopped editor acceptance').id;writeFileSync(out+'/stopped-reloaded.json',JSON.stringify(s,null,2));
 await cmd('safetyMonitor','disconnect',{},400);
 console.log('Stopped drafts and populated groups validated, running insertion/save rejected, completion preserved, native Targets save/replacement/reload verified, no watcher crash.');
}finally{const s=await state();if(!s.states.application.value.sequenceRunning&&groupId){await done(await cmd('sequence','delete',{sequenceId:q(s).sequenceId,expectedRevision:q(s).configurationRevision,nodeId:groupId}));await done(await cmd('sequence','reset'));}writeFileSync(out+'/stopped-commands.json',JSON.stringify(events,null,2));}
