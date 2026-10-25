import {test,expect} from 'bun:test';
import {mountView,pointingVector} from './mount-view';
test('horizon projection distinguishes cardinal directions and zenith',()=>{
 expect(pointingVector(0,90).x).toBeCloseTo(Math.SQRT1_2);expect(pointingVector(0,270).x).toBeCloseTo(-Math.SQRT1_2);
 expect(pointingVector(0,0).y).toBeLessThan(0);expect(pointingVector(0,180).y).toBeGreaterThan(0);
 expect(pointingVector(90,42).x).toBeCloseTo(0);expect(pointingVector(90,42).y).toBeCloseTo(-Math.cos(Math.PI/9));
 const northHome=pointingVector(31.194,0);expect(northHome.x).toBeGreaterThan(.5);expect(northHome.y).toBeLessThan(-.5);
});
test('actual telemetry drives movement while stale/disconnected position is suppressed',()=>{
 const s={connected:true,altitude:40,azimuth:100,slewing:true};
 expect(mountView(s,true)).toContain('data-pointing="40,100"');expect(mountView(s,true)).toContain('Slewing');
 expect(mountView(s,false)).toContain('data-pointing=""');expect(mountView({...s,connected:false},true)).toContain('Disconnected');
 expect(mountView({...s,slewing:false,atHome:true},true)).toContain('Home');
 expect(mountView({...s,altitude:null},true)).not.toContain('NaN');
});
