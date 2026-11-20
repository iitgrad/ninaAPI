import {test,expect} from 'bun:test';
import {Quaternion,Vector3} from 'three';
import {MountMotion} from './mount-motion';

const rotation=(degrees:number)=>new Quaternion().setFromAxisAngle(new Vector3(0,1,0),degrees*Math.PI/180);
test('first reading is immediate; later readings ease to the exact target and stop',()=>{
 const motion=new MountMotion();motion.setTarget(rotation(20),0);
 expect(motion.current.angleTo(rotation(20))).toBeLessThan(1e-7);
 motion.setTarget(rotation(100),100);
 expect(motion.current.angleTo(rotation(20))).toBeLessThan(1e-7);
 expect(motion.advance(200)).toBe(true);
 expect(motion.current.angleTo(rotation(100))).toBeGreaterThan(.1);
 expect(motion.current.angleTo(rotation(100))).toBeLessThan(80*Math.PI/180);
 expect(motion.advance(3000)).toBe(false);
 expect(motion.current.equals(rotation(100))).toBe(true);
 expect(motion.advance(4000)).toBe(false);
});
test('mid-flight telemetry retargets from the displayed position without jumping',()=>{
 const motion=new MountMotion();motion.setTarget(rotation(0),0);motion.setTarget(rotation(90),0);motion.advance(100);
 const displayed=motion.current.clone();motion.setTarget(rotation(160),100);
 expect(motion.current.equals(displayed)).toBe(true);
 motion.advance(116);expect(motion.current.angleTo(displayed)).toBeLessThan(.2);
 motion.advance(3000);expect(motion.current.equals(rotation(160))).toBe(true);
});
test('north wrap takes the two-degree path instead of rotating around the mount',()=>{
 const motion=new MountMotion();motion.setTarget(rotation(359),0);motion.setTarget(rotation(1),0);motion.advance(125);
 expect(motion.current.angleTo(rotation(0))).toBeLessThan(.001);
});
test('damping follows elapsed time at both 30 and 120 frames per second',()=>{
 const sample=(frames:number)=>{const m=new MountMotion();m.setTarget(rotation(0),0);m.setTarget(rotation(90),0);for(let i=1;i<=frames;i++)m.advance(i*500/frames);return m.current;};
 expect(sample(15).angleTo(sample(60))).toBeLessThan(1e-7);
});
test('reduced motion, invalid state and reconnect can reset immediately',()=>{
 const motion=new MountMotion();motion.setTarget(rotation(0),0);motion.setTarget(rotation(90),0);
 expect(motion.advance(16,true)).toBe(false);expect(motion.current.equals(rotation(90))).toBe(true);
 motion.setTarget(rotation(45),20,true);expect(motion.current.equals(rotation(45))).toBe(true);
 expect(motion.advance(30)).toBe(false);
});
