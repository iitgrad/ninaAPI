import {mkdtemp,writeFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join,resolve} from 'node:path';
import {createHmac} from 'node:crypto';
const dir=await mkdtemp(join(tmpdir(),'starfront-dashboard-'));
const token='synthetic-dashboard-server-credential-long-enough';
const pub='https://starfront.thewcl.com';
const reservation=Bun.serve({port:0,hostname:'127.0.0.1',fetch:()=>new Response()});const url=`http://127.0.0.1:${reservation.port}`;reservation.stop(true);
const native=Bun.serve({port:0,hostname:'127.0.0.1',fetch(req,server){if(req.headers.get('host')!=='native.test:1889')return new Response(null,{status:403});if(server.upgrade(req))return;return Response.json({schemaVersion:1})},websocket:{open(ws){ws.send(JSON.stringify({schemaVersion:1,sessionId:'test',revision:1,states:{application:{value:{environment:'simulation'}}}}))},message(){}}});
const config=join(dir,'config.json');
await writeFile(config,JSON.stringify({token,bridgeToken:token,listenUrl:url,bridgeUrl:`http://127.0.0.1:${native.port}`,bridgeHostHeader:'native.test:1889',publicDashboardOrigin:pub,publicPinHash:createHmac('sha256',token).update('pin:1234').digest('hex')}));
const process=Bun.spawn(['/Users/kevinmcquown/.local/share/starward-dotnet/dotnet',resolve('artifacts/2026-10-23-website/service-test/StarfrontControl.Service.dll'),'--contentRoot',resolve('src/StarfrontControl.Service')],{env:{...Bun.env,STARFRONT_SERVICE_CONFIG:config},stdout:'ignore',stderr:'ignore'});
function assert(x:unknown,message:string){if(!x)throw Error(message)}
async function req(path:string,body?:unknown,cookie?:string,origin=pub){return fetch(url+path,{method:body===undefined?'GET':'POST',headers:{Authorization:`Bearer ${token}`,'X-Starfront-Web':'1',Origin:origin,...(cookie?{Cookie:cookie}:{}),...(body!==undefined?{'Content-Type':'application/json'}:{})},body:body===undefined?undefined:JSON.stringify(body)})}
try{
 for(let i=0;i<100;i++){try{if((await req('/')).ok)break}catch{}await Bun.sleep(100)}
 assert((await fetch(url+'/')).status===401,'origin is exposed without server credential');
 assert((await req('/api/v1/status')).status===401,'telemetry exposed without PIN session');
 const gate=await (await req('/')).text();assert(gate.includes('pin-form')&&!gate.includes('1234'),'login gate missing or PIN leaked');
 assert((await req('/auth/dashboard-login',{code:'9999'})).status===401,'wrong PIN accepted');
 assert((await req('/auth/dashboard-login',{code:'1234'},undefined,'https://evil.test')).status===403,'foreign origin accepted');
 const login=await req('/auth/dashboard-login',{code:'1234'});assert(login.ok,'valid PIN rejected');const cookie=login.headers.get('Set-Cookie')!;
 assert(cookie.includes('httponly')&&cookie.includes('secure')&&cookie.includes('path=/dashboard'),'cookie scope/security missing');
 const saved=cookie.split(';')[0];
 for(let i=0;i<50;i++){const s=await (await req('/api/v1/status',undefined,saved)).json() as any;if(s.bridge.connected){assert(s.snapshot.sessionId==='test','wrong snapshot');break}if(i===49)throw Error('native Host forwarding failed');await Bun.sleep(100)}
 assert((await req('/api/v1/status',undefined,saved+'x')).status===401,'forged cookie accepted');
 const expiredPayload=String(Math.floor(Date.now()/1000)-10)+'.'+'A'.repeat(32);
 const expired='__Secure-starfront-dashboard='+expiredPayload+'.'+createHmac('sha256',token).update('dashboard:'+expiredPayload).digest('hex').toUpperCase();
 assert((await req('/api/v1/status',undefined,expired)).status===401,'expired cookie accepted');
 const socket=new WebSocket(url.replace('http:','ws:')+'/socket',{headers:{Authorization:`Bearer ${token}`,'X-Starfront-Web':'1',Origin:pub,Cookie:saved}});
 await new Promise<void>((resolve,reject)=>{const timer=setTimeout(()=>reject(Error('socket deadline')),3000);socket.onmessage=()=>{clearTimeout(timer);socket.close();resolve()};socket.onerror=()=>reject(Error('socket failed'))});
 assert((await req('/auth/dashboard-logout',{},saved)).headers.get('set-cookie')?.includes('expires='),'logout did not clear cookie');
 let last=0;for(let i=0;i<20;i++)last=(await req('/auth/dashboard-login',{code:'0000'})).status;
 assert(last===429,'persistent global rate limit not enforced');
 console.log('Dashboard access: origin token, code, cookie/forgery, Host rewrite, WebSocket, logout, rate limit passed');
}finally{process.kill();await process.exited;native.stop(true);await rm(dir,{recursive:true,force:true})}
