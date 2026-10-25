import {test,expect} from 'bun:test';
import {visibility,zonedTime} from './target-visibility';
const site={latitude:31,longitude:-100,elevation:600,timeZone:'America/Chicago',name:'Test site'};
test('night boundaries honor the observing time zone and DST',()=>{expect(new Date(zonedTime('2026-10-23',16,site.timeZone)).toISOString()).toBe('2026-10-23T21:00:00.000Z');expect(new Date(zonedTime('2026-12-23',16,site.timeZone)).toISOString()).toBe('2026-12-23T22:00:00.000Z');});
test('transit altitude matches latitude and declination; twilight order is physical',()=>{const v=visibility(0,0,site,'2026-10-23');expect(v.peak).toBeCloseTo(59,0);const ts=v.twilight.map(e=>e.time!);expect(ts.every(Number.isFinite)).toBe(true);expect(ts).toEqual([...ts].sort((a,b)=>a-b));expect(v.samples.every(p=>p.altitude>=-90&&p.altitude<=90)).toBe(true);});
test('circumpolar targets have no rise or set in the observing window',()=>{const v=visibility(2.5,89,site,'2026-10-23');expect(v.rise).toBeNull();expect(v.set).toBeNull();expect(v.samples.every(p=>p.altitude>0)).toBe(true);});
test('invalid inputs fail instead of inventing a visibility chart',()=>{expect(()=>visibility(0,0,{...site,latitude:120},'2026-10-23')).toThrow();});
