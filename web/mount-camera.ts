import {Box3,OrthographicCamera,Vector3} from 'three';
export function fitMountCamera(camera:OrthographicCamera,bounds:Box3,aspect:number){
 camera.up.set(0,1,0);camera.updateMatrixWorld(true);
 let extent=.1;
 for(const x of [bounds.min.x,bounds.max.x])for(const y of [bounds.min.y,bounds.max.y])for(const z of [bounds.min.z,bounds.max.z]){
  const p=new Vector3(x,y,z).applyMatrix4(camera.matrixWorldInverse);
  extent=Math.max(extent,Math.abs(p.y),Math.abs(p.x)/Math.max(.1,aspect));
 }
 extent*=1.08;camera.left=-extent*aspect;camera.right=extent*aspect;camera.top=extent;camera.bottom=-extent;camera.updateProjectionMatrix();
}
