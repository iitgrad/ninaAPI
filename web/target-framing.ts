export type FramingProfile={profileId:string;rigName:string;fieldWidthDegrees:number;fieldHeightDegrees:number};
let profiles:Promise<FramingProfile[]>|undefined;
const base=()=>document.body.dataset.gatewayBase??'';
export function framingUrl(raHours:number,dec:number,rotation:number,profile:FramingProfile,gatewayBase=''){
 if(![raHours,dec,rotation,profile.fieldWidthDegrees,profile.fieldHeightDegrees].every(Number.isFinite)||raHours<0||raHours>24||Math.abs(dec)>90||profile.fieldWidthDegrees<=0||profile.fieldHeightDegrees<=0)return null;
 const params=new URLSearchParams({ra:String((raHours*15)%360),dec:String(dec),rotation:String(((rotation%360)+360)%360),width:String(profile.fieldWidthDegrees),height:String(profile.fieldHeightDegrees)});
 return gatewayBase+'/api/v1/framing/image?'+params;
}
/** One deadline covers configuration and the image, including network hangs. */
export async function framingResponse(url:string,signal:AbortSignal){
 const response=await fetch(url,{signal});
 if(!response.ok)throw Error('Survey unavailable');
 const blob=await response.blob();
 if(blob.size>2_000_000||!['image/svg+xml','image/jpeg'].includes(blob.type.split(';')[0]))throw Error('Invalid survey image');
 return blob;
}
export function targetFraming(ra:unknown,dec:unknown,rotation:unknown,profileId?:string){
 const host=document.createElement('figure');host.className='target-framing';const image=document.createElement('img');image.decoding='async';image.alt='Survey preview of the target in the camera field';image.hidden=true;
 const status=document.createElement('span');status.className='target-framing-status';const caption=document.createElement('figcaption');caption.textContent='DSS2 · CDS / NASA SkyView · survey reference';host.append(image,status,caption);
 if(typeof ra!=='number'||typeof dec!=='number'||typeof rotation!=='number'||!profileId){status.textContent='Camera framing requires target coordinates and a rig profile.';return host;}
 let current:AbortController|undefined;
 const release=()=>{image.removeAttribute("src");};
 const load=async()=>{
  current?.abort();const controller=new AbortController();current=controller;
  status.hidden=false;status.textContent='Loading camera framing…';image.hidden=true;release();
  const failure=(message:string)=>{
   if(current!==controller)return;
   image.hidden=true;release();status.hidden=false;status.textContent=message+' · ';
   const retry=document.createElement('button');retry.type='button';retry.textContent='Retry';retry.onclick=()=>void load();status.append(retry);
  };
  const deadline=setTimeout(()=>{controller.abort();failure('Survey timed out');},60_000);
  try{
   // Failed configuration requests are evicted so Retry can actually recover.
   profiles??=fetch(base()+'/api/v1/framing/profiles',{signal:controller.signal}).then(async r=>{if(!r.ok)throw Error('Framing configuration unavailable');return await r.json() as FramingProfile[];}).catch(error=>{profiles=undefined;throw error;});
   const list=await profiles;controller.signal.throwIfAborted();
   const profile=list.find(p=>p.profileId.toLowerCase()===profileId.toLowerCase());if(!profile)throw Error('Camera field of view is not configured for this profile.');
   const url=framingUrl(ra,dec,rotation,profile,base());if(!url)throw Error('Camera framing unavailable.');
   caption.textContent=`${profile.fieldWidthDegrees.toFixed(2)}° × ${profile.fieldHeightDegrees.toFixed(2)}° · PA ${rotation.toFixed(1)}° · DSS2 / CDS or NASA SkyView (reference colors)`;
   host.title=profile.rigName+' · estimated camera field from configured optics; survey image, not a captured exposure';
   const blob=await framingResponse(url,controller.signal);controller.signal.throwIfAborted();
   if(current!==controller)return;
   const encoded=await new Promise<string>((resolve,reject)=>{const reader=new FileReader();reader.onload=()=>resolve(String(reader.result));reader.onerror=()=>reject(Error('Invalid survey image'));reader.readAsDataURL(blob);});
   controller.signal.throwIfAborted();if(current!==controller)return;
   image.src=encoded;await image.decode();controller.signal.throwIfAborted();
   if(current!==controller)return;
   image.hidden=false;status.hidden=true;
  }catch(error){
   if(current!==controller)return;
   failure(controller.signal.aborted?'Survey timed out':error instanceof Error?error.message:'Survey unavailable');
  }finally{clearTimeout(deadline);}
 };
 void load();
 // The sequence renderer disposes removed target figures; free the blob and
 // cancel requests without waiting for their deadline.
 host.addEventListener('dispose',()=>{current?.abort();current=undefined;release();},{once:true});
 return host;
}
