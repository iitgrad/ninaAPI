import {test, expect} from 'bun:test';
import {createTelemetryConnection} from './telemetry-connection';
function fixture() {
  let clock=0, renewals=0, fail=false;
  const sockets:any[]=[], timers:{run:()=>void;ms:number}[]=[], states:boolean[]=[];
  const connection=createTelemetryConnection({
    url:()=> 'wss://fixture/socket', now:()=>clock,
    renew:async()=>{renewals++;if(fail)throw Error('offline');},
    receive:data=>data==='valid',state:open=>states.push(open),
    delay:((run:()=>void,ms:number)=>{timers.push({run,ms});return timers.length;}) as any,
    socket:()=>{const s:any={closeCount:0,close(){this.closeCount++;}};sockets.push(s);return s;},
  });
  return {connection,sockets,timers,states,advance:(ms:number)=>clock+=ms,
    renewals:()=>renewals,fail:(value:boolean)=>fail=value};
}
const flush=async()=>{await Promise.resolve();await Promise.resolve();await Promise.resolve();};
test('half-open socket is retired after 30 seconds and only one retry is queued',async()=>{
 const f=fixture();f.connection.start();f.connection.start();expect(f.sockets.length).toBe(1);
 const oldMessage=f.sockets[0].onmessage;
 f.advance(29999);f.connection.check();expect(f.timers.length).toBe(0);
 f.advance(1);f.connection.check();f.connection.check();expect(f.timers.length).toBe(1);
 expect(f.sockets[0].closeCount).toBe(1);expect(f.sockets[0].onmessage).toBeNull();
 f.timers.shift()!.run();await flush();expect(f.renewals()).toBe(1);expect(f.sockets.length).toBe(2);
 oldMessage({data:'valid'}); // Superseded callbacks cannot update the new connection.
 f.advance(30000);f.connection.check();expect(f.timers.length).toBe(1);
});
test('valid snapshots keep a healthy socket alive; invalid messages cannot hide a stall',()=>{
 const f=fixture();f.connection.start();f.sockets[0].onopen();expect(f.states.at(-1)).toBe(true);
 f.advance(20000);f.sockets[0].onmessage({data:'valid'});
 f.advance(20000);f.connection.check();expect(f.timers.length).toBe(0);
 f.sockets[0].onmessage({data:'invalid'});f.advance(10000);f.connection.check();expect(f.timers.length).toBe(1);
});
test('returning to tab replaces socket and coalesces focus/visibility/pageshow events',async()=>{
 const f=fixture();f.connection.resume();expect(f.sockets.length).toBe(0);
 f.connection.start();f.connection.resume();f.connection.resume();await flush();f.connection.resume();
 expect(f.renewals()).toBe(1);expect(f.sockets.length).toBe(2);expect(f.sockets[0].closeCount).toBe(1);
});
test('close/error and authentication failures use bounded backoff; valid receipt resets it',async()=>{
 const f=fixture();f.connection.start();const error=f.sockets[0].onerror,close=f.sockets[0].onclose;
 error();close();expect(f.timers.length).toBe(1);expect(f.timers[0].ms).toBe(1000);
 f.fail(true);
 for(const expected of [2000,4000,8000,10000,10000]){
  f.timers.shift()!.run();await flush();expect(f.timers.length).toBe(1);expect(f.timers[0].ms).toBe(expected);
 }
 f.fail(false);f.timers.shift()!.run();await flush();const s=f.sockets.at(-1);s.onmessage({data:'valid'});s.onclose();
 expect(f.timers[0].ms).toBe(1000);
});
