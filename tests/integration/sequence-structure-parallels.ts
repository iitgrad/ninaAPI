import {strict as assert} from 'node:assert';
import {writeFileSync} from 'node:fs';
const base='http://10.211.55.10:1889',out='artifacts/2026-10-23-sequence-catalog',log:any[]=[];
async function req(path:string,body?:unknown){const r=await fetch(base+path,{method:body?'POST':'GET',headers:{'Content-Type':'application/json'},body:body?JSON.stringify(body):undefined});return{http:r.status,data:JSON.parse((await r.text()).replace(/^\uFEFF/,''))};}
async function state(){const {data:s}=await req('/api/v1/status');assert.equal(s.states.application.value.environment,'simulation');assert.equal(s.states.administration.value.profile.id,'cd432469-2b6e-442c-987e-78504975ff3e');assert.equal(s.states.application.value.sequenceRunning,false);return s;}
const seq=(s:any)=>s.states.sequence.value,token=(s:any)=>({sequenceId:seq(s).sequenceId,expectedRevision:seq(s).configurationRevision});
async function command(action:string,params:object,expected=202){const before=await state(),c={requestId:crypto.randomUUID(),device:'sequence',action,parameters:{...token(before),...params}},r=await req('/api/v1/commands',c);log.push({c,r});assert.equal(r.http,expected,JSON.stringify(r));if(expected!==202)return;for(let i=0;i<100;i++){const {data:op}=await req('/api/v1/operations/'+c.requestId);if(op.completedAt){log.at(-1).op=op;assert.equal(op.status,'succeeded',JSON.stringify(op));break;}await Bun.sleep(100);}for(let i=0;i<100;i++){const s=await state();if(seq(s).configurationRevision!==seq(before).configurationRevision)return s;await Bun.sleep(50);}throw Error('Configuration readback timeout');}
const children=(s:any,p:string)=>seq(s).nodes.filter((n:any)=>n.parentId===p&&['instruction','container'].includes(n.kind));
// Wait only for startup telemetry, without issuing any commands.
for(let attempt=0;attempt<120;attempt++){const r=await req('/api/v1/status');if(r.data.states?.application&&r.data.states?.administration&&r.data.states?.sequenceCatalog&&r.data.states?.sequence?.value?.available)break;if(attempt===119)throw Error('Simulator startup timeout');await Bun.sleep(250);}
try{
 const before=await state(),start=seq(before).nodes.find((n:any)=>n.type==='StartAreaContainer'),end=seq(before).nodes.find((n:any)=>n.type==='EndAreaContainer');
 let s=await command('insert',{parentId:start.id,type:'SequentialContainer',index:0});const group=children(s,start.id)[0];
 s=await command('insert',{parentId:group.id,type:'WaitForTimeSpan',index:0});const first=children(s,group.id)[0];
 s=await command('editField',{nodeId:first.id,field:'Time',value:2});
 s=await command('duplicate',{nodeId:first.id});let items=children(s,group.id);assert.equal(items.length,2);assert.notEqual(items[0].id,items[1].id);assert.equal(items[1].editableFields.find((f:any)=>f.key==='Time').value,2);const second=items[1];
 s=await command('move',{nodeId:second.id,parentId:group.id,index:0});assert.equal(children(s,group.id)[0].id,second.id);
 s=await command('move',{nodeId:second.id,parentId:group.id,index:2});assert.equal(children(s,group.id)[1].id,second.id);
 const stale=token(s);s=await command('move',{nodeId:second.id,parentId:end.id,index:0});assert(!children(s,group.id).some((n:any)=>n.id===second.id));assert.equal(children(s,end.id)[0].id,second.id);
 await command('delete',{...stale,nodeId:second.id},409);
 await command('move',{nodeId:group.id,parentId:group.id,index:0},400);
 await command('delete',{nodeId:start.id},400);
 s=await command('duplicate',{nodeId:group.id});const copy=children(s,start.id).find((n:any)=>n.id!==group.id);assert(copy);assert.equal(children(s,copy.id).length,1);assert.notEqual(children(s,copy.id)[0].id,first.id);
 s=await command('delete',{nodeId:copy.id});assert(!seq(s).nodes.some((n:any)=>n.id===copy.id));assert(!seq(s).nodes.some((n:any)=>n.parentId===copy.id));
 s=await command('insert',{parentId:group.id,type:'LoopCondition',index:0});const condition=seq(s).nodes.find((n:any)=>n.parentId===group.id&&n.kind==='condition');
 await command('duplicate',{nodeId:condition.id},400);
 s=await command('delete',{nodeId:second.id});assert(!seq(s).nodes.some((n:any)=>n.id===second.id));
 // Delete the test group, leaving the separately verified target tree untouched.
 s=await command('delete',{nodeId:group.id});assert(!seq(s).nodes.some((n:any)=>n.id===first.id));
 writeFileSync(out+'/structure-final.json',JSON.stringify(s,null,2));console.log('Structural editing verified:',log.length,'checks; clone identities/values, same-parent and cross-parent moves, subtree delete, stale/structural/cycle/duplicate-condition rejection.');
}finally{writeFileSync(out+'/structure-command-results.json',JSON.stringify(log,null,2));}
