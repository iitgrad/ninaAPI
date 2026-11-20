import {test,expect} from 'bun:test';
import {nightTimeline,scheduledPercent,filterCounts,filterPie} from './sequence-night-timeline';
import type {SequenceNode} from './dashboard';
const n=(id:string,type:string,parentId:string|null,details:Record<string,unknown>={},kind='instruction',state='pending'):SequenceNode=>({id,type,parentId,details,kind,state,name:id,sourceStatus:''});
const time=(hours:number,minutes=0)=>({hours,minutes,seconds:0,rolloverTime:'06:57:20',timeReferenceFixed:true});
function plan(){return [
 {...n('root','SequenceRootContainer',null,{},'container'),name:'2026-11-04'},
 ...[['Veil',19,9,21,9,52],['M31 F',21,9,23,29,57],['M31 B',23,29,1,49,61],['Rosette',1,49,5,34,96]].flatMap(([name,a,b,c,d,images])=>[
 n(String(name),'DeepSkyObjectContainer','root',{},'container','completed'),n(name+'-start','WaitForTime',String(name),time(Number(a),Number(b))),
 n(name+'-end','TimeCondition',String(name),time(Number(c),Number(d)),'condition'),n(name+'-image','TakeExposure',String(name),{exposureCount:images}),
 n(name+'-trigger','Unsafe',''+name,{},'trigger'),n(name+'-conditional','TakeExposure',name+'-trigger',{exposureCount:400}),
 ])];}
test('planned target windows cross midnight, retain their observing date after morning rollover and count actual images',()=>{
 const early=nightTimeline(plan(),'2026-11-05T12:00:00Z','America/Chicago');
 const late=nightTimeline(plan(),'2026-11-05T15:00:00Z','America/Chicago');
 expect(late).toEqual(early);expect(late.length).toBe(4);
 expect(late.map(w=>w.captured)).toEqual([52,57,61,96]);
 expect(new Date(late[0].start).toISOString()).toBe('2026-11-05T01:09:00.000Z');
 expect(new Date(late[3].end).toISOString()).toBe('2026-11-05T11:34:00.000Z');
});
test('time progress before, during, between and after windows uses scheduled duration',()=>{
 const w=[{id:'a',name:'A',start:1000,end:2000,captured:0},{id:'b',name:'B',start:3000,end:6000,captured:0}];
 expect(scheduledPercent(w,0)).toBe(0);expect(scheduledPercent(w,1500)).toBe(13);
 expect(scheduledPercent(w,2500)).toBe(25);expect(scheduledPercent(w,4500)).toBe(63);expect(scheduledPercent(w,7000)).toBe(100);
 expect(scheduledPercent([],1000)).toBeNull();
});
test('DST conversion uses the site zone and calendar date, not a 24-hour UTC shift',()=>{
 const nodes=[{...n('r','SequenceRootContainer',null,{},'container'),name:'2026-10-31'},n('t','DeepSkyObjectContainer','r',{},'container'),n('s','WaitForTime','t',time(23)),n('e','TimeCondition','t',time(2),'condition'),n('i','TakeExposure','t',{exposureCount:0})];
 const w=nightTimeline(nodes,'2026-11-01T16:00:00Z','America/Chicago');
 expect(new Date(w[0].start).toISOString()).toBe('2026-11-01T04:00:00.000Z');expect(new Date(w[0].end).toISOString()).toBe('2026-11-01T08:00:00.000Z');
});
test('conditional target branches, missing boundaries and dynamic event times do not become planned blocks',()=>{
 const rows=plan();rows.find(x=>x.id==='Rosette-end')!.details.timeReferenceFixed=false;
 rows.find(x=>x.id==='M31 B-start')!.type='WaitForTimeSpan';
 rows.find(x=>x.id==='M31 F')!.parentId='Veil-trigger';
 expect(nightTimeline(rows,'2026-11-05T15:00:00Z','America/Chicago').map(w=>w.id)).toEqual(['Veil']);
 expect(nightTimeline(rows,'invalid','Not/AZone')).toEqual([]);
});

test('filter totals use reported exposure counters and skip trigger branches',()=>{
 const rows=[n('target','DeepSkyObjectContainer',null,{},'container'),n('group','SequentialContainer','target',{},'container'),n('filter','SwitchFilter','group',{filter:'Ha'}),n('image','TakeExposure','group',{exposureCount:7}),n('trigger','Unsafe','group',{},'trigger'),n('conditional','TakeExposure','trigger',{exposureCount:50}),n('group2','SequentialContainer','target',{},'container'),n('unknown','TakeExposure','group2',{exposureCount:3})];
 expect(filterCounts(rows)).toEqual([{name:'Ha',count:7},{name:'Unknown filter',count:3}]);
 rows[3].details.exposureCount=undefined;expect(filterCounts(rows)[0].count).toBeNull();
});

test('filter pie represents known counts and flags unavailable values',()=>{
 const p=filterPie([{name:'Ha',count:3},{name:'O',count:1},{name:'S',count:null}]);
 expect(p.total).toBe(4);expect(p.hasUnknown).toBe(true);expect(p.gradient).toContain('0% 75%');expect(p.gradient).toContain('75% 100%');
 expect(filterPie([{name:'Ha',count:0}]).gradient).toBeNull();
});
