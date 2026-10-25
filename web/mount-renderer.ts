/*! @license
The MIT License

Copyright © 2010-2026 three.js authors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.

*/
import * as THREE from 'three';
import {fitMountCamera} from './mount-camera';
import rig from './assets/starfront-rig.json';
import {mountView,defaultMountCamera,type MountState,type MountCamera} from './mount-view';

// Two persistent views at most (equipment card + open dialog). All drawing is
// demand-driven; native telemetry never allocates a renderer or model per update.
const views=new WeakMap<HTMLElement,MountRenderer|null>();
const geometries=rig.meshes.map(part=>{
 const decode=(s:string)=>Uint8Array.from(atob(s),c=>c.charCodeAt(0)).buffer;
 const geometry=new THREE.BufferGeometry(),buffer=new THREE.InterleavedBuffer(new Float32Array(decode(part.vertices)),6);
 geometry.setAttribute('position',new THREE.InterleavedBufferAttribute(buffer,3,0));
 geometry.setAttribute('normal',new THREE.InterleavedBufferAttribute(buffer,3,3));
 geometry.setIndex(new THREE.BufferAttribute(new Uint32Array(decode(part.indices)),1));
 geometry.computeBoundingSphere();return geometry;
});
function known(s:MountState,live:boolean){return live&&s.connected===true&&typeof s.altitude==='number'&&Number.isFinite(s.altitude)&&typeof s.azimuth==='number'&&Number.isFinite(s.azimuth);}
export function updateMountView(host:HTMLElement,s:MountState,live:boolean,mini=false,camera=defaultMountCamera){
 if(!views.has(host)){
  // Keep the SVG fallback when WebGL2 is unavailable. It has the same telemetry
  // and orbit semantics and is also a permanent pier, without tripod legs.
  host.innerHTML=mountView(s,live,mini,camera);
  try{views.set(host,new MountRenderer(host,mini));}catch{views.set(host,null);host.dataset.mountRenderer='svg';}
 }
 const view=views.get(host);
 if(view)view.update(s,live,camera);
 else host.innerHTML=mountView(s,live,mini,camera);
}
export function disposeMountView(host:HTMLElement|null){if(host){views.get(host)?.dispose();views.delete(host);host.replaceChildren();}}
class MountRenderer{
 private renderer:THREE.WebGLRenderer;
 private scene=new THREE.Scene();
 private camera=new THREE.OrthographicCamera(-1.7,1.7,1.6,-1.6,.1,30);
 private optics=new THREE.Group();
 private materials:THREE.Material[]=[];
 private textures:THREE.Texture[]=[];
 private localGeometry:THREE.BufferGeometry[]=[];
 private resize:ResizeObserver;
 private frame=0;
 private frames=0;
 private signature='';
 private alive=true;
 private currentCamera={...defaultMountCamera};
 private model:HTMLElement;
 private status:HTMLElement;
 constructor(private host:HTMLElement,private mini:boolean){
  this.renderer=new THREE.WebGLRenderer({alpha:true,antialias:!mini,powerPreference:'low-power'});
  this.renderer.setPixelRatio(Math.min(window.devicePixelRatio||1,mini?1.5:2));
  this.renderer.outputColorSpace=THREE.SRGBColorSpace;
  this.renderer.toneMapping=THREE.ACESFilmicToneMapping;this.renderer.toneMappingExposure=1.3;
  this.renderer.shadowMap.enabled=!mini;this.renderer.shadowMap.type=THREE.PCFShadowMap;
  this.model=host.querySelector('.mount-model')!;this.status=this.model.querySelector('.mount-motion')!;
  this.renderer.domElement.setAttribute('role','img');
  this.renderer.domElement.setAttribute('aria-label','RedCat 91 on AM5N · floor-bolted pier');
  this.model.querySelector('svg')!.replaceWith(this.renderer.domElement);
  host.dataset.mountRenderer='blender-webgl';
  this.scene.add(new THREE.HemisphereLight(0xcceaff,0x252030,2.3));
  const key=new THREE.DirectionalLight(0xffebdc,4);key.position.set(-3,5,4);key.castShadow=!mini;
  key.shadow.mapSize.set(1024,1024);key.shadow.camera.left=-2;key.shadow.camera.right=2;key.shadow.camera.top=3;key.shadow.camera.bottom=-2;key.shadow.normalBias=.025;key.shadow.bias=-.0001;this.scene.add(key);
  const rim=new THREE.DirectionalLight(0x92c8ff,3);rim.position.set(3,3,-3);this.scene.add(rim);
  const fill=new THREE.DirectionalLight(0xffffff,1.5);fill.position.set(1,1,4);this.scene.add(fill);
  this.optics.position.y=rig.pivotHeight;this.scene.add(this.optics);
  const materialMap=new Map<string,THREE.MeshStandardMaterial>();
  for(const [name,m] of Object.entries(rig.materials)){
   const material=new THREE.MeshStandardMaterial({color:new THREE.Color(...m.color as [number,number,number]),metalness:m.metalness,roughness:m.roughness});
   this.materials.push(material);materialMap.set(name,material);
  }
  rig.meshes.forEach((p,i)=>{const mesh=new THREE.Mesh(geometries[i],materialMap.get(p.material));mesh.castShadow=!mini;mesh.receiveShadow=!mini;(p.group==='optics'?this.optics:this.scene).add(mesh);});
  const groundGeometry=new THREE.CircleGeometry(1.22,96);this.localGeometry.push(groundGeometry);
  const groundMaterial=new THREE.MeshStandardMaterial({color:0x132635,roughness:.94,metalness:.1});this.materials.push(groundMaterial);
  const ground=new THREE.Mesh(groundGeometry,groundMaterial);ground.rotation.x=-Math.PI/2;ground.position.y=-.004;ground.receiveShadow=true;this.scene.add(ground);
  const ringGeometry=new THREE.RingGeometry(1.205,1.22,96);this.localGeometry.push(ringGeometry);
  const ringMaterial=new THREE.MeshBasicMaterial({color:0x3d6171,side:THREE.DoubleSide});this.materials.push(ringMaterial);
  const ring=new THREE.Mesh(ringGeometry,ringMaterial);ring.rotation.x=-Math.PI/2;ring.position.y=.001;this.scene.add(ring);
  const compass=new THREE.Group();this.scene.add(compass);
  for(const [name,x,z]of [['N',0,-1.36],['E',1.36,0],['S',0,1.36],['W',-1.36,0]] as const){
   const c=document.createElement('canvas');c.width=64;c.height=64;const ctx=c.getContext('2d')!;ctx.font='500 42px system-ui';ctx.fillStyle='#9bbac9';ctx.textAlign='center';ctx.textBaseline='middle';ctx.fillText(name,32,32);
   const texture=new THREE.CanvasTexture(c);texture.colorSpace=THREE.SRGBColorSpace;this.textures.push(texture);
   const material=new THREE.SpriteMaterial({map:texture,transparent:true,depthWrite:false});this.materials.push(material);
   const sprite=new THREE.Sprite(material);sprite.position.set(x,.035,z);sprite.scale.set(.17,.17,.17);compass.add(sprite);
  }
  this.resize=new ResizeObserver(()=>this.request());this.resize.observe(host);
  document.addEventListener('visibilitychange',this.visible);
  this.renderer.domElement.addEventListener('webglcontextlost',this.contextLost);
  this.renderer.domElement.addEventListener('webglcontextrestored',this.contextRestored);
 }
 private visible=()=>{if(!document.hidden)this.request();};
 private contextLost=(e:Event)=>{e.preventDefault();this.host.dataset.mountRenderer='context-lost';};
 private contextRestored=()=>{this.host.dataset.mountRenderer='blender-webgl';this.request();};
 update(s:MountState,live:boolean,camera:MountCamera){
  const valid=known(s,live),status=!live?'Offline':s.connected!==true?'Disconnected':!valid?'Pointing unavailable':s.slewing?'Slewing':s.atHome?'Home':s.atPark?'Parked':s.trackingEnabled?'Tracking':'Stopped';
  const signature=JSON.stringify([valid?s.altitude:45,valid?s.azimuth:0,status,camera.azimuth,camera.elevation]);if(signature===this.signature)return;this.signature=signature;
  const alt=(valid?s.altitude as number:45)*Math.PI/180,az=(valid?s.azimuth as number:0)*Math.PI/180;
  this.optics.quaternion.setFromUnitVectors(new THREE.Vector3(0,0,-1),new THREE.Vector3(Math.sin(az)*Math.cos(alt),Math.sin(alt),-Math.cos(az)*Math.cos(alt)));
  this.currentCamera={...camera};this.model.classList.toggle('is-offline',!valid);
  this.model.dataset.pointing=valid?`${s.altitude},${s.azimuth}`:'';this.model.dataset.view=`${camera.azimuth.toFixed(1)},${camera.elevation.toFixed(1)}`;
  this.status.textContent=status;this.status.classList.toggle('moving',valid&&s.slewing===true);
  this.renderer.domElement.setAttribute('aria-label',`RedCat 91 on AM5N · floor-bolted pier · ${status}`);
  this.request();
 }
 private request(){if(this.alive&&!this.frame)this.frame=requestAnimationFrame(()=>{this.frame=0;this.draw();});}
 private draw(){
  if(!this.alive||!this.host.isConnected||!this.host.getBoundingClientRect().width||document.hidden)return;
  const width=this.host.clientWidth,height=this.mini?Math.max(16,this.host.clientHeight-12):330;
  this.renderer.setSize(width,height,false);const aspect=width/height,halfHeight=1.52;
  this.camera.left=-halfHeight*aspect;this.camera.right=halfHeight*aspect;this.camera.top=halfHeight;this.camera.bottom=-halfHeight;this.camera.updateProjectionMatrix();
  const az=this.currentCamera.azimuth*Math.PI/180,el=this.currentCamera.elevation*Math.PI/180;
  this.camera.position.set(-Math.sin(az)*Math.cos(el)*6,1.12+Math.sin(el)*6,Math.cos(az)*Math.cos(el)*6);this.camera.up.set(0,1,0);this.camera.lookAt(0,1.12,0);
  this.scene.updateMatrixWorld(true);
  fitMountCamera(this.camera,new THREE.Box3().setFromObject(this.scene),width/height);
  this.renderer.render(this.scene,this.camera);
  this.host.dataset.renderFrames=String(++this.frames);this.host.dataset.renderGeometries=String(this.renderer.info.memory.geometries);this.host.dataset.renderTextures=String(this.renderer.info.memory.textures);
 }
 dispose(){
  this.alive=false;cancelAnimationFrame(this.frame);this.resize.disconnect();document.removeEventListener('visibilitychange',this.visible);
  this.renderer.domElement.removeEventListener('webglcontextlost',this.contextLost);this.renderer.domElement.removeEventListener('webglcontextrestored',this.contextRestored);
  for(const material of this.materials)material.dispose();for(const texture of this.textures)texture.dispose();for(const g of this.localGeometry)g.dispose();
  this.renderer.dispose();this.renderer.forceContextLoss();
 }
}
