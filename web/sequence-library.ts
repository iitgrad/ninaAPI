import type {Topic} from './dashboard';
type API={states:()=>Record<string,Topic>;session:()=>string;reason:(device:string,action:string)=>string;run:(device:string,action:string,parameters?:Record<string,unknown>)=>void;save:(parameters:Record<string,unknown>)=>Promise<string|undefined>};
export function mountSequenceLibrary(api:API){
 const saveButton=document.createElement('button'),saveAsButton=document.createElement('button');saveButton.textContent='Save';saveAsButton.textContent='Save As';
 document.getElementById('sequence-start')!.before(saveButton,saveAsButton);
 const saveDialog=document.createElement('dialog');saveDialog.className='sequence-editor step-settings';saveDialog.setAttribute('aria-label','Save sequence to NUC');
 saveDialog.innerHTML='<h2>Save to NUC</h2><p>Targets</p><label>Name <input aria-label="Sequence filename" maxlength="95"></label><label><input type="checkbox" aria-label="Replace existing sequence"> Replace existing file</label><p role="status"></p><footer class="parameter-footer"><button data-cancel>Cancel</button><button data-save>Save</button></footer>';
 document.body.append(saveDialog);
 const nameInput=saveDialog.querySelector<HTMLInputElement>('input')!,replaceInput=saveDialog.querySelector<HTMLInputElement>('[type=checkbox]')!,saveMessage=saveDialog.querySelector<HTMLElement>('[role=status]')!,saveConfirm=saveDialog.querySelector<HTMLButtonElement>('[data-save]')!;
 let saveTokens:Record<string,unknown>={},saveReceipt:string|undefined;
 const openSave=(as:boolean)=>{const sequence=api.states().sequence?.value??{};saveTokens={sequenceId:sequence.sequenceId,expectedRevision:sequence.configurationRevision};nameInput.value=as?'':String(api.states().administration?.value.sequenceFileName??'');replaceInput.checked=!as;saveReceipt=undefined;saveMessage.textContent='';saveConfirm.disabled=false;saveDialog.showModal();nameInput.focus();};
 saveButton.onclick=()=>openSave(false);saveAsButton.onclick=()=>openSave(true);saveDialog.querySelector('[data-cancel]')!.addEventListener('click',()=>saveDialog.close());
 saveConfirm.onclick=()=>{let fileName=nameInput.value.trim();if(!fileName)return;if(!fileName.toLowerCase().endsWith('.json'))fileName+='.json';saveConfirm.disabled=true;saveMessage.textContent='Saving…';void api.save({...saveTokens,fileName,replace:replaceInput.checked}).then(id=>{saveReceipt=id;saveMessage.textContent=id?'Waiting for save receipt…':'Save not confirmed. Check Operations.';}).catch(error=>{saveConfirm.disabled=false;saveMessage.textContent=String(error);});};
 const trigger=document.createElement('button');trigger.id='sequence-load';trigger.type='button';trigger.textContent='↥ Load';document.getElementById('sequence-start')!.before(trigger);
 const style=document.createElement('style');style.textContent=`.sequence-actions{flex-wrap:wrap}@media(max-width:600px){.sequence-panel{min-width:0;width:100%;box-sizing:border-box}.sequence-header,.sequence-title{flex-wrap:wrap}.sequence-message{overflow-wrap:anywhere}}#sequence-load-dialog{width:min(540px,calc(100vw - 32px));box-sizing:border-box}#sequence-load-dialog header{display:flex;align-items:center;justify-content:space-between}#sequence-load-dialog h2{margin:0}#sequence-load-dialog input,#sequence-load-dialog select{width:100%;box-sizing:border-box;margin:10px 0}#sequence-load-dialog select{min-height:260px;padding:6px;background:#0c1c29;color:#dce8f2}#sequence-load-dialog option{padding:7px}#sequence-load-dialog footer{display:flex;justify-content:flex-end;gap:8px;margin-top:14px}#sequence-load-dialog [role=status]{color:#9eafbd;font-size:12px}`;document.head.append(style);
 const dialog=document.createElement('dialog');dialog.id='sequence-load-dialog';dialog.className='parameter-dialog';dialog.setAttribute('aria-labelledby','sequence-load-title');
 dialog.innerHTML='<header><h2 id="sequence-load-title">Load from NUC</h2><button type="button" data-close aria-label="Close sequence picker">×</button></header><input type="search" aria-label="Find a sequence" placeholder="Find a sequence…"><select size="10" aria-label="Sequence JSON file"></select><p role="status"></p><p style="font-size:12px;color:#9eafbd">Replaces the loaded sequence. Does not start imaging.</p><footer><button type="button" data-refresh>Refresh files</button><button type="button" data-load>Load sequence</button></footer>';
 document.body.append(dialog);
 const query=dialog.querySelector<HTMLInputElement>('input')!,list=dialog.querySelector<HTMLSelectElement>('select')!,message=dialog.querySelector<HTMLElement>('[role=status]')!,load=dialog.querySelector<HTMLButtonElement>('[data-load]')!,refresh=dialog.querySelector<HTMLButtonElement>('[data-refresh]')!;
 let identity='',fingerprint='';
 const context=()=>api.session()+':'+String((api.states().administration?.value.profile as {id?:string}|undefined)?.id??'')+':'+String(api.states().sequence?.value.sequenceId??'');
 const files=()=>((api.states().administration?.value.sequences??[])as unknown[]).filter((f):f is string=>typeof f==='string');
 function update(){
  const saveReason=api.reason('sequence','save');saveAsButton.disabled=!!saveReason;saveButton.disabled=!!saveReason||!api.states().administration?.value.sequenceFileName;
  saveAsButton.title=saveReason;saveButton.title=saveReason||'Save the loaded sequence to its Targets file';
  if(saveDialog.open&&saveReceipt){const op=(api.states().operations?.value.items as {requestId:string;status:string;completedAt?:string;error?:string}[]??[]).find(o=>o.requestId===saveReceipt);if(op?.completedAt){saveReceipt=undefined;if(op.status==='succeeded')saveDialog.close();else{saveConfirm.disabled=false;saveMessage.textContent=op.error??'Save failed; name retained.';}}}

  const reason=api.reason('sequence','load');trigger.disabled=!!reason;trigger.title=reason||'Choose a saved JSON sequence on the NUC';
  if(!dialog.open)return;
  const stale=context()!==identity,items=files(),key=JSON.stringify([items,query.value]);
  if(key!==fingerprint){
   fingerprint=key;const previous=list.value;list.replaceChildren();
   for(const [prefix,label] of [['Targets/','Targets'],['','Sequences']]as const){
    const group=document.createElement('optgroup');group.label=label;
    for(const name of items.filter(f=>(prefix?f.startsWith(prefix):!f.startsWith('Targets/'))&&f.toLowerCase().includes(query.value.toLowerCase()))){const option=document.createElement('option');option.value=name;option.textContent=prefix?name.slice(prefix.length):name;group.append(option);}
    if(group.children.length)list.append(group);
   }
   if(items.includes(previous))list.value=previous;
  }
  load.disabled=!!reason||stale||!list.value;refresh.disabled=!!api.reason('application','refresh')||stale;
  message.textContent=stale?'The profile or sequence changed. Reopen this picker.':reason||(!items.length?'No JSON sequences found in the configured folders.':`${items.length} files on NUC${api.states().administration?.value.sequencesTruncated?' · some files omitted by library limits':''}`);
 }
 trigger.onclick=()=>{identity=context();fingerprint='';query.value='';dialog.showModal();update();query.focus();};
 dialog.querySelector('[data-close]')!.addEventListener('click',()=>dialog.close());query.oninput=update;list.onchange=update;
 refresh.onclick=()=>{if(!refresh.disabled)api.run('application','refresh',{});};
 load.onclick=()=>{update();if(load.disabled)return;const fileName=list.value;dialog.close();api.run('sequence','load',{fileName});};
 return update;
}
