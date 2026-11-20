import {test,expect} from 'bun:test';
import {connectionCandidates} from './bulk-connections';
const states=(connected:boolean)=>({devices:{value:{items:['camera','mount','safetyMonitor','blocked','weather'].map(device=>({device,id:device==='weather'?'ASCOM.Device1.ObservingConditions':device,name:device==='weather'?'Pegasus weather':device,selected:true,allowed:device!=='blocked'}))}},camera:{value:{connected}},mount:{value:{connected}},safety:{value:{connected:true}},weather:{value:{connected:false}}}) as any;
test('disconnect batch omits always-connected safety and reverses device order',()=>expect(connectionCandidates(states(true),'disconnect').map(x=>x.device)).toEqual(['mount','camera']));
test('connect batch omits roof, retired weather, blocked and already-connected devices',()=>expect(connectionCandidates(states(false),'connect').map(x=>x.device)).toEqual(['camera','mount']));
test('unknown connection state never gets an inferred disconnect',()=>expect(connectionCandidates({} as any,'disconnect')).toEqual([]));
