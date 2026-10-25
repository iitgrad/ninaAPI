export type MountState={connected?:unknown;altitude?:unknown;azimuth?:unknown;slewing?:unknown;atHome?:unknown;atPark?:unknown;trackingEnabled?:unknown};
export type MountCamera={azimuth:number;elevation:number};
export const defaultMountCamera:MountCamera={azimuth:135,elevation:20};
type V={x:number;y:number;z:number};
const add=(a:V,b:V):V=>({x:a.x+b.x,y:a.y+b.y,z:a.z+b.z});
const mul=(v:V,n:number):V=>({x:v.x*n,y:v.y*n,z:v.z*n});
const dot=(a:V,b:V)=>a.x*b.x+a.y*b.y+a.z*b.z;
const cross=(a:V,b:V):V=>({x:a.y*b.z-a.z*b.y,y:a.z*b.x-a.x*b.z,z:a.x*b.y-a.y*b.x});
const unit=(v:V)=>mul(v,1/Math.hypot(v.x,v.y,v.z));
const finite=(n:unknown):n is number=>typeof n==='number'&&Number.isFinite(n);
export function projectMount(v:V,camera=defaultMountCamera){
 const a=camera.azimuth*Math.PI/180,e=camera.elevation*Math.PI/180;
 return {x:-Math.cos(a)*v.x+Math.sin(a)*v.y,y:Math.sin(e)*(Math.sin(a)*v.x+Math.cos(a)*v.y)-Math.cos(e)*v.z,depth:Math.cos(e)*(Math.sin(a)*v.x+Math.cos(a)*v.y)+Math.sin(e)*v.z};
}
export function pointingVector(altitude:number,azimuth:number,camera=defaultMountCamera){
 const a=altitude*Math.PI/180,z=azimuth*Math.PI/180;
 return projectMount({x:Math.sin(z)*Math.cos(a),y:Math.cos(z)*Math.cos(a),z:Math.sin(a)},camera);
}
export function mountView(s:MountState,live:boolean,mini=false,camera=defaultMountCamera){
 const known=live&&s.connected===true&&finite(s.altitude)&&finite(s.azimuth);
 const alt=(known?s.altitude as number:45)*Math.PI/180,az=(known?s.azimuth as number:0)*Math.PI/180;
 const direction={x:Math.sin(az)*Math.cos(alt),y:Math.cos(az)*Math.cos(alt),z:Math.sin(alt)};
 const status=!live?'Offline':s.connected!==true?'Disconnected':s.slewing?'Slewing':s.atHome?'Home':s.atPark?'Parked':s.trackingEnabled?'Tracking':'Stopped';
 const center={x:0,y:0,z:1.5};
 const project=(v:V)=>{const p=projectMount(v,camera);return {x:150+p.x*66,y:155+p.y*66,depth:p.depth};};
 const faces:{points:string;depth:number;color:string}[]=[];
 function cylinder(c:V,axis:V,length:number,radius:number,rgb:[number,number,number]){
  const u=unit(axis),b=unit(cross(u,Math.abs(u.z)>.9?{x:1,y:0,z:0}:{x:0,y:0,z:1})),d=cross(u,b);
  const rings=[-1,1].map(sign=>Array.from({length:16},(_,i)=>{const angle=i*Math.PI/8;return add(add(c,mul(u,sign*length/2)),add(mul(b,radius*Math.cos(angle)),mul(d,radius*Math.sin(angle))));}));
  function face(vertices:V[],shade:number){const pts=vertices.map(project);faces.push({points:pts.map(p=>`${p.x.toFixed(2)},${p.y.toFixed(2)}`).join(' '),depth:pts.reduce((n,p)=>n+p.depth,0)/pts.length,color:`rgb(${rgb.map(c=>Math.round(c*shade)).join(',')})`});}
  for(let i=0;i<16;i++)face([rings[0][i],rings[0][(i+1)%16],rings[1][(i+1)%16],rings[1][i]],.65+.3*(1+Math.cos(i*Math.PI/8))/2);
  face(rings[0],.7);face(rings[1],1);
 }
 const grey:[number,number,number]=[142,167,179];
 cylinder({x:0,y:0,z:.55},{x:0,y:0,z:1},1.1,.12,grey);
 cylinder({x:0,y:0,z:.035},{x:0,y:0,z:1},.07,.29,grey);
 for(const x of [-.18,.18])for(const y of [-.18,.18])cylinder({x,y,z:.07},{x:0,y:0,z:1},.06,.025,[191,202,208]);
 cylinder({x:0,y:0,z:1.24},{x:0,y:.85,z:.52},.55,.15,[191,202,208]);
 cylinder({x:-.12,y:0,z:1.5},{x:1,y:0,z:0},.6,.07,grey);
 cylinder({x:-.48,y:0,z:1.5},{x:1,y:0,z:0},.13,.14,[59,76,90]);
 cylinder(center,direction,1.12,.13,[202,63,76]);
 for(const n of [-.16,.16])cylinder(add(center,mul(direction,n)),direction,.045,.142,[210,218,223]);
 cylinder(add(center,mul(direction,.51)),direction,.18,.155,[39,56,69]);
 cylinder(add(center,mul(direction,-.66)),direction,.22,.105,[49,68,84]);
 const ground=Array.from({length:65},(_,i)=>project({x:1.35*Math.cos(i*Math.PI/32),y:1.35*Math.sin(i*Math.PI/32),z:0}));
 const compass=[['N',0,1.55],['E',1.55,0],['S',0,-1.55],['W',-1.55,0]] as const;
 const labels=compass.map(([label,x,y])=>{const p=project({x,y,z:0});return `<text x="${p.x}" y="${p.y}" text-anchor="middle" dominant-baseline="middle">${label}</text>`;}).join('');
 const axes=[[[0,-1.35,0],[0,1.35,0]],[[-1.35,0,0],[1.35,0,0]]].map(line=>{const ps=line.map(([x,y,z])=>project({x,y,z}));return `<line x1="${ps[0].x}" y1="${ps[0].y}" x2="${ps[1].x}" y2="${ps[1].y}"/>`;}).join('');
 return `<div class="mount-model${known?'':' is-offline'}${s.slewing===true&&known?' is-slewing':''}" data-pointing="${known?`${s.altitude},${s.azimuth}`:''}" data-view="${camera.azimuth.toFixed(1)},${camera.elevation.toFixed(1)}"><svg viewBox="0 0 300 270" role="img" aria-label="Telescope pointing · ${status}"><polygon points="${ground.map(p=>`${p.x},${p.y}`).join(' ')}" fill="#0c1d2b" stroke="#365669"/><g stroke="#2b495b">${axes}</g><g fill="#91aebd" font-size="11">${labels}</g>${faces.sort((a,b)=>a.depth-b.depth).map(f=>`<polygon points="${f.points}" fill="${f.color}" stroke="${f.color}" stroke-width=".3"/>`).join('')}</svg><span class="mount-motion${s.slewing&&known?' moving':''}">${status}</span></div>`;
}
