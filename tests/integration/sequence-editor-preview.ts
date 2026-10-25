// Local-only acceptance harness. Every command is pinned to the isolated Parallels profile.
import {resolve} from 'node:path';
const native='http://10.211.55.10:1889';
let latest:any={};
const decode=(s:string)=>JSON.parse(s.replace(/^\uFEFF/,''));
const upstream=new WebSocket(native.replace('http:','ws:')+'/socket/v1');
const viewers=new Set<any>();upstream.onmessage=e=>{latest=decode(String(e.data));for(const w of viewers)w.send(JSON.stringify(latest));};
const server=Bun.serve({hostname:'127.0.0.1',port:1897,async fetch(request,server){
 const path=new URL(request.url).pathname;
 if(request.headers.get('host')!=='127.0.0.1:1897')return new Response('',{status:403});
 if((request.method==='POST'||path==='/socket')&&request.headers.get('origin')!=='http://127.0.0.1:1897')return new Response('',{status:403});
 if(path==='/socket'){if(server.upgrade(request))return;return new Response('',{status:400});}
 if(path==='/')return new Response('<!doctype html><html><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Sequence editor · Parallels acceptance</title><body style="background:#111e28;color:white;font:16px system-ui"><h1>Sequence editor · Parallels simulation</h1><p id="result" role="status">Connecting</p><div id="sequence-tree"></div><script src="/editor.js"></script></body></html>',{headers:{'Content-Type':'text/html'}});
 if(path==='/editor.js')return new Response(Bun.file(resolve('artifacts/2026-10-23-sequence-editor/editor.js')));
 if(path==='/command'&&request.method==='POST'){
  const s=decode(await(await fetch(native+'/api/v1/status')).text());
  if(s.states?.application?.value.environment!=='simulation'||s.states?.administration?.value.profile?.id!=='cd432469-2b6e-442c-987e-78504975ff3e')return new Response('Simulation guard failed',{status:409});
  const c=await request.json() as any;if(c.device!=='sequence'||!['editField','insert'].includes(c.action))return new Response('Unsupported',{status:400});
  const r=await fetch(native+'/api/v1/commands',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(c)});return new Response((await r.text()).replace(/^\uFEFF/,''),{status:r.status,headers:{'Content-Type':'application/json'}});
 }
 if(path.startsWith('/operation/')){const id=path.split('/').at(-1)!;if(!/^[a-f0-9-]{36}$/.test(id))return new Response('',{status:400});const r=await fetch(native+'/api/v1/operations/'+id);return new Response((await r.text()).replace(/^\uFEFF/,''),{status:r.status,headers:{'Content-Type':'application/json'}});}
 return new Response('',{status:404});
},websocket:{open(w){viewers.add(w);w.send(JSON.stringify(latest));},close(w){viewers.delete(w)},message(){}}});
console.log('Simulation editor harness http://127.0.0.1:1897');
