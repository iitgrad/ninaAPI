import {test,expect} from 'bun:test';
import {DEFAULT_REPLAY_SPEED,recordingStart,zoomWindow,revealTime,selectionWindow} from './replay-timeline';
test('playback starts at the first available snapshot at real-time speed',()=>{
 expect(DEFAULT_REPLAY_SPEED).toBe(1);expect(recordingStart({start:1000,firstTelemetryAt:1004})).toBe(1004);
 expect(recordingStart({start:1000,firstTelemetryAt:0})).toBe(1000);
});
test('timeline zoom retains full-night bounds and minimum useful range',()=>{
 const full={start:1000,end:101000};expect(zoomWindow(full,.5,1000,full)).toEqual({start:1000,end:51000});
 expect(zoomWindow(full,.5,101000,full)).toEqual({start:51000,end:101000});
 expect(zoomWindow(full,4,50000,full)).toEqual(full);
 expect(zoomWindow(full,.00001,50000,full).end-zoomWindow(full,.00001,50000,full).start).toBe(10000);
});
test('drag selection works in either direction and playback pans an isolated window',()=>{
 const full={start:1000,end:101000};expect(selectionWindow(71000,21000,full)).toEqual({start:21000,end:71000});
 const view={start:21000,end:41000};expect(revealTime(view,30000,full)).toBe(view);
 expect(revealTime(view,90000,full)).toEqual({start:80000,end:100000});
});
