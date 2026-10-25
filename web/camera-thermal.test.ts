import {test,expect} from 'bun:test';
import {ThermalHistory,thermalPaths} from './camera-thermal';
function storage(){let value:string|null=null;return {getItem:()=>value,setItem:(_key:string,next:string)=>{value=next;}};}
const now=1800000000000;
test('refresh restores recent sensor readings without inventing new ones',()=>{
 const disk=storage(),first=new ThermalHistory(disk);
 first.receive('camera-a',now-10000,true,-8,75,now-10000);
 first.receive('camera-a',now-5000,true,-9,70,now-5000);
 const refreshed=new ThermalHistory(disk);
 refreshed.receive('camera-a',now-5000,true,-9,70,now);
 expect(refreshed.samples).toEqual(first.samples);
 // Stale telemetry still restores valid history, but cannot add a new reading.
 const offline=new ThermalHistory(disk);offline.receive('camera-a',now-60000,false,99,99,now);
 expect(offline.samples).toEqual(first.samples);
});
test('history expires against wall clock even without a fresh sensor reading',()=>{
 const disk=storage(),first=new ThermalHistory(disk);first.receive('a',now,true,-10,50,now);
 const refreshed=new ThermalHistory(disk);refreshed.receive('a',now,false,null,null,now+3600001);
 expect(refreshed.samples).toEqual([]);
});
test('camera/profile/session changes cannot reuse another context history',()=>{
 const disk=storage(),first=new ThermalHistory(disk);first.receive('a',now,true,-10,50,now);
 const refreshed=new ThermalHistory(disk);refreshed.receive('b',now,true,15,0,now);
 expect(refreshed.samples).toEqual([{at:now,temperature:15,power:0}]);
});
test('sample count is bounded and missing intervals remain gaps after refresh',()=>{
 const disk=storage(),first=new ThermalHistory(disk);
 for(let i=0;i<800;i++)first.receive('a',now+i*5000,true,-10,50,now+i*5000);
 const last=now+799*5000;expect(first.samples.length).toBe(720);
 const refreshed=new ThermalHistory(disk);refreshed.receive('a',last+30000,true,-9,40,last+30000);
 expect(refreshed.samples.length).toBeLessThanOrEqual(720);
 expect(thermalPaths(refreshed.samples.slice(-2),'temperature',x=>x,y=>y).match(/M/g)?.length).toBe(2);
});
test('corrupt or blocked storage does not prevent live telemetry',()=>{
 for(const disk of [{getItem:()=>'{broken',setItem:()=>{}},{getItem:()=>{throw Error('denied');},setItem:()=>{throw Error('quota');}}]){
  const history=new ThermalHistory(disk);history.receive('a',now,true,-10,50,now);expect(history.samples.length).toBe(1);
 }
});
test('restoration rejects invalid, out of order and future cached readings',()=>{
 const disk=storage();disk.setItem('',JSON.stringify({context:'a',samples:[{at:now-5000,temperature:-10,power:50},{at:now-7000,temperature:-10,power:50},{at:now-1000,temperature:'bad',power:50},{at:now+6000,temperature:-10,power:50}]}));
 const history=new ThermalHistory(disk);history.receive('a',NaN,false,null,null,now);expect(history.samples).toEqual([{at:now-5000,temperature:-10,power:50}]);
});
