import {test,expect} from 'bun:test';
import {sequenceCountdown} from './sequence-clock';
test('sequence clock advances locally, stops at zero and marks offline data',()=>{
 expect(sequenceCountdown(3723000,0)).toBe('Remaining 01:02:03');
 expect(sequenceCountdown(3723000,1000)).toBe('Remaining 01:02:02');
 expect(sequenceCountdown(1000,2000)).toBe('Time reached');
 expect(sequenceCountdown(1000,0,false)).toContain('offline');
});
import {rolloverDeadline} from './sequence-clock';
const timed=(hours:number,minutes=0)=>({hours,minutes,seconds:0,rolloverTime:'07:50:00'});
test('wait targets before rollover belong to the following morning',()=>{
 expect(new Date(rolloverDeadline(timed(1),'2026-10-24T20:00:00Z','America/Chicago')).toISOString()).toBe('2026-10-25T06:00:00.000Z');
 expect(new Date(rolloverDeadline(timed(21,30),'2026-10-24T20:00:00Z','America/Chicago')).toISOString()).toBe('2026-10-25T02:30:00.000Z');
});
test('after midnight evening targets stay in the previous observing day',()=>{
 const now='2026-10-25T07:00:00Z';
 expect(sequenceCountdown(rolloverDeadline(timed(21,30),now,'America/Chicago'),Date.parse(now))).toBe('Time reached');
 expect(new Date(rolloverDeadline(timed(6,8),now,'America/Chicago')).toISOString()).toBe('2026-10-25T11:08:00.000Z');
});
test('rollover equality starts the next observing day and honors DST',()=>{
 expect(new Date(rolloverDeadline(timed(6),'2026-10-24T12:50:00Z','America/Chicago')).toISOString()).toBe('2026-10-25T11:00:00.000Z');
 expect(new Date(rolloverDeadline(timed(6),'2026-10-31T20:00:00Z','America/Chicago')).toISOString()).toBe('2026-11-01T12:00:00.000Z');
 expect(Number.isNaN(rolloverDeadline(timed(25),'2026-10-24T20:00:00Z','America/Chicago'))).toBe(true);
 expect(Number.isNaN(rolloverDeadline(timed(1),'2026-10-24T20:00:00Z','invalid/zone'))).toBe(true);
});
