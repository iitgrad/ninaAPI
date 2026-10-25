import {test,expect} from 'bun:test';
import {Box3,OrthographicCamera,Vector3} from 'three';
import {fitMountCamera} from './mount-camera';
test('orbit has world-up with no roll and every model corner fits even tiny cards',()=>{
 const bounds=new Box3(new Vector3(-1.4,0,-1.4),new Vector3(1.4,2.7,1.4));
 for(const aspect of [.4,1,5,12])for(const elevation of [5,20,80])for(let az=0;az<360;az+=30){
  const camera=new OrthographicCamera(-2,2,2,-2,.1,30),a=az*Math.PI/180,e=elevation*Math.PI/180;
  camera.up.set(0,1,0);camera.position.set(-Math.sin(a)*Math.cos(e)*6,1.12+Math.sin(e)*6,Math.cos(a)*Math.cos(e)*6);camera.lookAt(0,1.12,0);fitMountCamera(camera,bounds,aspect);
  expect(Math.abs(camera.matrixWorld.elements[1])).toBeLessThan(.000001);
  for(const x of [-1.4,1.4])for(const y of [0,2.7])for(const z of [-1.4,1.4]){
   const p=new Vector3(x,y,z).project(camera);expect(Math.max(Math.abs(p.x),Math.abs(p.y))).toBeLessThan(.94);
  }
 }
});
