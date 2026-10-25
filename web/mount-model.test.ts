import {test,expect} from 'bun:test';
import rig from './assets/starfront-rig.json';
test('Blender export fits the retained geometry budget and has valid GPU buffers',()=>{
 let triangles=0;
 for(const mesh of rig.meshes){
  const bytes=Buffer.from(mesh.vertices,'base64'),positions=new Float32Array(bytes.buffer,bytes.byteOffset,bytes.byteLength/4);
  const indices=Buffer.from(mesh.indices,'base64'),elements=new Uint32Array(indices.buffer,indices.byteOffset,indices.byteLength/4);
  expect(positions.length%6).toBe(0);expect(elements.length%3).toBe(0);
  expect(positions.every(Number.isFinite)).toBe(true);
  expect(elements.every(n=>n<positions.length/6)).toBe(true);
  expect(positions.every((v,i)=>Math.abs(v)<(i%6<3?3:1.001))).toBe(true);
  triangles+=elements.length/3;
 }
 expect(triangles).toBeLessThan(40000);expect(rig.meshes.length).toBeLessThan(24);
});
