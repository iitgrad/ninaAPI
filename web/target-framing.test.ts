import {test,expect} from 'bun:test';
import {framingUrl} from './target-framing';
const rig={profileId:'rig',rigName:'Test rig',fieldWidthDegrees:4.6,fieldHeightDegrees:3.07};
test('camera framing uses sequence coordinates and PA with a protected same-origin URL',()=>{
 const url=new URL(framingUrl(21.6493,57.4891,90,rig,'/dashboard')!,'https://starfront.thewcl.com');
 expect(url.pathname).toBe('/dashboard/api/v1/framing/image');expect(Number(url.searchParams.get('ra'))).toBeCloseTo(324.7395,5);expect(url.searchParams.get('rotation')).toBe('90');expect(url.searchParams.get('width')).toBe('4.6');expect(url.searchParams.get('height')).toBe('3.07');
});
test('invalid coordinates are unavailable and RA24 wraps to zero',()=>{expect(framingUrl(NaN,0,0,rig)).toBeNull();expect(framingUrl(0,91,0,rig)).toBeNull();expect(new URL(framingUrl(24,0,-90,rig)!,'https://localhost').searchParams.get('rotation')).toBe('270');expect(new URL(framingUrl(24,0,0,rig)!,'https://localhost').searchParams.get('ra')).toBe('0');});
