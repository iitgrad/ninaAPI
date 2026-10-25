export type FramingProfile={profileId:string;rigName:string;fieldWidthDegrees:number;fieldHeightDegrees:number};
let profiles:Promise<FramingProfile[]>|undefined;
const base=()=>document.body.dataset.gatewayBase??'';
export function framingUrl(raHours:number,dec:number,rotation:number,profile:FramingProfile,gatewayBase=''){
 if(![raHours,dec,rotation,profile.fieldWidthDegrees,profile.fieldHeightDegrees].every(Number.isFinite)||raHours<0||raHours>24||Math.abs(dec)>90||profile.fieldWidthDegrees<=0||profile.fieldHeightDegrees<=0)return null;
 const params=new URLSearchParams({ra:String((raHours*15)%360),dec:String(dec),rotation:String(((rotation%360)+360)%360),width:String(profile.fieldWidthDegrees),height:String(profile.fieldHeightDegrees)});
 return gatewayBase+'/api/v1/framing/image?'+params;
}
export function targetFraming(ra:unknown,dec:unknown,rotation:unknown,profileId?:string){
 const host=document.createElement('figure');host.className='target-framing';const image=document.createElement('img');image.loading='lazy';image.decoding='async';image.alt='Survey preview of the target in the camera field';image.hidden=true;
 const status=document.createElement('span');status.className='target-framing-status';status.textContent='Loading camera framing…';const caption=document.createElement('figcaption');caption.textContent='DSS2 / CDS · survey preview';host.append(image,status,caption);
 if(typeof ra!=='number'||typeof dec!=='number'||typeof rotation!=='number'||!profileId){status.textContent='Camera framing requires target coordinates and a rig profile.';return host;}
 profiles??=fetch(base()+'/api/v1/framing/profiles').then(r=>{if(!r.ok)throw Error('Framing configuration unavailable');return r.json() as Promise<FramingProfile[]>;}).catch(()=>[]);
 void profiles.then(list=>{
  const profile=list.find(p=>p.profileId.toLowerCase()===profileId.toLowerCase());if(!profile){status.textContent='Camera field of view is not configured for this profile.';return;}
  const url=framingUrl(ra,dec,rotation,profile,base());if(!url){status.textContent='Camera framing unavailable.';return;}
  caption.textContent=`${profile.fieldWidthDegrees.toFixed(2)}° × ${profile.fieldHeightDegrees.toFixed(2)}° · PA ${rotation.toFixed(1)}° · DSS2 / CDS`;
  host.title=profile.rigName+' · estimated camera field from configured optics; survey image, not a captured exposure';
  image.onload=()=>{image.style.opacity='1';image.hidden=false;status.hidden=true;};image.onerror=()=>{image.hidden=true;status.hidden=false;status.textContent='Survey unavailable';const retry=document.createElement('button');retry.type='button';retry.textContent='Retry';retry.onclick=()=>{status.textContent='Loading camera framing…';image.hidden=false;image.style.opacity='0';image.src=url;};status.append(retry);};image.width=512;image.height=Math.round(512*profile.fieldHeightDegrees/profile.fieldWidthDegrees);image.style.opacity='0';image.hidden=false;image.src=url;
 });return host;
}
