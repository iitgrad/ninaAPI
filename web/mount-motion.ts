import {Quaternion} from 'three';

// Time-based damping keeps retargeting continuous and independent of frame rate.
// Quaternions take the short path, including when azimuth crosses north.
export class MountMotion {
 readonly current=new Quaternion();
 private target=new Quaternion();
 private lastTime=0;
 private initialized=false;
 private moving=false;
 setTarget(target:Quaternion,time:number,immediate=false){
  this.target.copy(target);
  if(!this.initialized||immediate){
   this.current.copy(target);this.moving=false;
  }else this.moving=this.current.angleTo(this.target)>0;
  this.initialized=true;this.lastTime=time;
 }
 advance(time:number,immediate=false){
  if(!this.moving)return false;
  const elapsed=Math.max(0,time-this.lastTime);this.lastTime=time;
  this.current.slerp(this.target,immediate?1:1-Math.exp(-elapsed/180));
  if(this.current.angleTo(this.target)<.0001){this.current.copy(this.target);this.moving=false;}
  return this.moving;
 }
}
