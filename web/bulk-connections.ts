import type {Topic} from './dashboard';
type States=Record<string,Topic>;
type Choice={device:string;id:string;name?:string;selected:boolean;allowed:boolean};
export type ConnectionChoice=Choice;
const order=['camera','filterWheel','focuser','rotator','mount','guider','dome','flatDevice','switch','weather'];
export function connectionCandidates(states:States,action:'connect'|'disconnect'):Choice[]{
 const choices=(states.devices?.value.items??[]) as Choice[];
 return (action==='connect'?order:[...order].reverse()).flatMap(device=>{
  const choice=choices.find(c=>c.device===device&&c.selected&&c.allowed&&c.id&&!['No_Device','No_Guider'].includes(c.id));
  if(!choice)return [];
  if(action==='connect'&&device==='weather'&&choice.id==='ASCOM.Device1.ObservingConditions'&&choice.name?.includes('Pegasus'))return [];
  const connected=states[device]?.value.connected;
  return connected===(action==='disconnect')?[choice]:[];
 });
}
export function mountBulkConnections(api:{states:()=>States;session:()=>string;replay:()=>boolean;reason:(device:string,action:string)=>string;send:(device:string,action:string,id:string)=>Promise<string|undefined>;busy:(value:boolean)=>void;notice:(text:string)=>void}){
 const toolbar=document.createElement('div');toolbar.className='equipment-bulk-actions';
 toolbar.style.cssText='display:flex;align-items:center;gap:6px';
 const buttons=(['connect','disconnect'] as const).map(action=>{
  const button=document.createElement('button');button.type='button';button.id='equipment-'+action+'-all';button.textContent=action==='connect'?'Connect All':'Disconnect All';
  button.onclick=()=>confirmAction(action);toolbar.append(button);return button;
 });
 document.querySelector('.header-right')!.prepend(toolbar);
 const dialog=document.createElement('dialog');dialog.className='parameter-dialog';dialog.setAttribute('aria-labelledby','bulk-confirm-title');
 dialog.innerHTML='<h2 id="bulk-confirm-title"></h2><div class="device-action-row"><button data-cancel>Cancel</button><button data-confirm></button></div>';
 document.body.append(dialog);dialog.querySelector<HTMLButtonElement>('[data-cancel]')!.onclick=()=>dialog.close();
 let running=false;
 function identity(){return JSON.stringify([api.session(),(api.states().administration?.value.profile as {id?:string}|undefined)?.id]);}
 function available(action:'connect'|'disconnect'){const candidates=connectionCandidates(api.states(),action);return candidates.length>0&&candidates.every(c=>!api.reason(c.device,action));}
 function update(){toolbar.hidden=api.replay();buttons.forEach((b,i)=>b.disabled=running||!available(i===0?'connect':'disconnect'));}
 function confirmAction(action:'connect'|'disconnect'){
  if(running||!available(action))return;
  const candidates=connectionCandidates(api.states(),action),context=identity();
  document.getElementById('bulk-confirm-title')!.textContent=action==='connect'?'Connect all equipment?':'Disconnect all equipment?';
  const confirm=dialog.querySelector<HTMLButtonElement>('[data-confirm]')!;confirm.textContent=action==='connect'?'Connect All':'Disconnect All';
  confirm.onclick=()=>{dialog.close();void execute(action,candidates,context);};dialog.showModal();
 }
 async function execute(action:'connect'|'disconnect',candidates:Choice[],context:string){
  if(running)return;running=true;api.busy(true);update();
  try{
   for(const c of candidates){
    if(identity()!==context||api.replay())throw Error('Connection or profile changed.');
    const selected=((api.states().devices?.value.items??[]) as Choice[]).find(x=>x.device===c.device&&x.selected);
    if(selected?.id!==c.id)throw Error('Configured equipment changed.');
    // A device can have reached the requested state while confirmation was open.
    if(api.states()[c.device]?.value.connected===(action==='connect'))continue;
    const reason=api.reason(c.device,action);if(reason)throw Error(reason);
    const id=await api.send(c.device,action,c.id);if(!id)throw Error('Command was not submitted.');
    const deadline=Date.now()+90000;let completed=false;
    while(Date.now()<deadline){
     if(identity()!==context||api.replay())throw Error('Connection or profile changed.');
     const result=((api.states().operations?.value.items??[]) as {requestId:string;status:string;error?:string}[]).find(o=>o.requestId===id);
     if(result&&['succeeded','failed','rejected','cancelled','unknown'].includes(result.status)){
      if(result.status!=='succeeded')throw Error(result.error??'Equipment command did not complete.');completed=true;break;
     }
     await new Promise(resolve=>setTimeout(resolve,100)); // Observe pushed receipts, never poll NINA.
    }
    if(!completed)throw Error('Result pending. No further commands were sent.');
   }
  }catch(error){api.notice(error instanceof Error?error.message:'Equipment operation stopped.');}
  finally{running=false;api.busy(false);update();}
 }
 return update;
}
