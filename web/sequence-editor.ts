import {mountSequenceDraft} from './sequence-draft';
import {timedCountdownElement} from './sequence-clock';
import {sequenceDetail,sequenceName,type VisualNode} from './sequence-presentation';
import {createParameterControl,parameterIcon} from "./parameter-control";
import type {Topic} from './dashboard';
type Field={key:string;label:string;type:string;value:unknown;minimum?:number;maximum?:number;unit?:string;choices?:{value:number;label:string}[]};
type Node=VisualNode & {editableFields:Field[]};
type Entry={type:string;label:string;group:string;kind:string;nodeType?:string;source?:string;target?:boolean};
type Sequence={sequenceId?:string;configurationRevision?:number;executionId?:string;draftInsertion?:{supported:boolean;positions:{parentId:string;afterNodeId:string}[]};nodes?:Node[];insertCatalog?:Entry[];editing?:{supported:boolean;canEdit:boolean}};
type API={states:()=>Record<string,Topic>;session:()=>string;live:()=>boolean;reason:(device:string,action:string)=>string;run:(device:string,action:string,parameters:Record<string,unknown>)=>void;schema:(type:string)=>Promise<any>;submit:(parameters:Record<string,unknown>)=>Promise<string|undefined>};
export function mountSequenceEditor(api:API){
 const draft=mountSequenceDraft({states:api.states,live:api.live,schema:api.schema,submit:api.submit,reason:()=>api.reason('sequence','insertDraft')});
 const tree=document.getElementById('sequence-tree');if(!tree)return()=>{};
 const style=document.createElement('style');style.textContent=`
 .sequence-editor{width:min(960px,95vw);max-height:90vh;padding:20px;color:var(--text,#e5edf3);background:var(--panel,#18242d);border:1px solid #647482;border-radius:12px}.sequence-editor::backdrop{background:#0009}.sequence-editor header{display:flex;align-items:center;justify-content:space-between;gap:16px}.sequence-editor h2{margin:0}.sequence-editor-layout{display:grid;grid-template-columns:1.2fr 1fr;gap:20px;margin-top:12px}.sequence-edit-tree,.sequence-edit-form{max-height:65vh;overflow:auto}.sequence-edit-row{display:block;width:100%;text-align:left;padding:8px;border:1px solid transparent;border-radius:5px}.sequence-edit-row[aria-pressed=true]{border-color:#52cdbd;background:#23443f}.sequence-edit-children{margin-left:15px;border-left:1px solid #647482;padding-left:8px}.sequence-insert-line{display:block;width:100%;border:0;border-top:1px solid #647482;margin:5px 0;padding:3px;background:transparent;opacity:.55}.sequence-insert-line:hover,.sequence-insert-line:focus{opacity:1;border-color:#52cdbd}.sequence-edit-form label{display:block;margin:8px 0}.sequence-edit-form input,.sequence-edit-form select{max-width:100%;width:100%;padding:8px}.sequence-edit-form button{margin:5px 0;padding:7px}.sequence-edit-message{min-height:24px;color:#f4cf83}.sequence-editor small{display:block;opacity:.75}.sequence-edit-open{margin:8px 12px}@media(max-width:650px){.sequence-editor-layout{grid-template-columns:1fr}.sequence-edit-tree{max-height:35vh}.sequence-edit-form{max-height:35vh}}

 .sequence-picker{margin:0;right:auto;bottom:auto;position:fixed;z-index:10;background:#152734;border:1px solid #466476;border-radius:12px;box-shadow:0 16px 55px #0009;overflow:hidden;color:#e4eef5}.sequence-picker-heading{padding:12px 14px;font-size:12px;color:#94b3c8;border-bottom:1px solid #344e5e}.sequence-picker-columns{display:grid;grid-template-columns:minmax(130px,.85fr) minmax(150px,1.3fr);max-height:min(360px,55vh)}.sequence-picker-groups,.sequence-picker-items{padding:6px;overflow:auto}.sequence-picker-groups{border-right:1px solid #344e5e}.sequence-picker button{display:block;width:100%;text-align:left;padding:10px 12px;margin:0;border:0;border-radius:6px;background:transparent;color:inherit;font:inherit;font-size:13px;cursor:pointer}.sequence-picker button:hover,.sequence-picker button:focus-visible,.sequence-picker button[aria-expanded=true]{background:#25485c;outline:0;color:#9de9dc}.sequence-picker button:focus-visible{box-shadow:inset 0 0 0 2px #69cbbb}

 .step-settings .sequence-edit-tree{display:none}.step-settings .sequence-editor-layout{grid-template-columns:1fr}.step-settings{width:min(520px,95vw)}
 .sequence-inline-insert{display:flex;align-items:center;justify-content:center;width:calc(100% - 24px);height:18px;margin:0 12px;padding:0;position:relative;border:0;background:transparent;color:transparent;cursor:pointer;font-size:15px}.sequence-inline-insert::before{content:"";position:absolute;left:4px;right:4px;top:50%;height:2px;background:transparent}.sequence-inline-insert:hover,.sequence-inline-insert:focus-visible,.sequence-inline-insert[aria-expanded=true]{color:#9ce9d7;background:#2b8c7820;outline:0}.sequence-inline-insert:hover::before,.sequence-inline-insert:focus-visible::before,.sequence-inline-insert[aria-expanded=true]::before{background:#63dcad}.sequence-inline-insert:disabled{cursor:default}.sequence-inline-insert:disabled::before{background:transparent}.sequence-inline-insert:disabled{color:transparent;background:transparent}.sequence-inline-insert:focus-visible{box-shadow:inset 0 0 0 1px #63dcad}.sequence-node strong[role=button]{cursor:pointer}.sequence-node strong[role=button]:hover{text-decoration:underline}.sequence-inline-insert[aria-expanded=true]{scroll-margin:20px}
 @media(hover:none){.sequence-inline-insert:not(:disabled){color:#91c7bc}.sequence-inline-insert:not(:disabled)::before{background:#63dcad55}}
 `;document.head.append(style);
 const dialog=document.createElement('dialog');dialog.className='sequence-editor parameter-dialog';dialog.setAttribute('aria-labelledby','sequence-editor-title');
 dialog.innerHTML='<header class="parameter-heading"><span class="parameter-icon"></span><div class="parameter-heading-copy"><h2 id="sequence-editor-title">Step settings</h2></div><button class="parameter-close" aria-label="Close step settings" title="Close">×</button></header><div class="sequence-editor-layout"><div class="sequence-edit-tree" aria-label="Editable sequence"></div><section class="sequence-edit-form" aria-label="Element parameters"></section></div><p class="sequence-edit-message" role="status"></p><footer class="parameter-footer"><button class="parameter-done" type="button">Done</button></footer>';
 document.body.append(dialog);dialog.querySelector('.parameter-done')!.addEventListener('click',()=>dialog.close());dialog.querySelector('header button')!.addEventListener('click',()=>dialog.close());
 const outline=dialog.querySelector<HTMLElement>('.sequence-edit-tree')!,form=dialog.querySelector<HTMLElement>('.sequence-edit-form')!,message=dialog.querySelector<HTMLElement>('.sequence-edit-message')!;
 let triggerScope='',hoverTimer:ReturnType<typeof setTimeout>|undefined;
 let context='',shownRevision=-1,selected='',view:Sequence={},waiting:{parentId:string;index:number;type:string;before:Set<string>}|undefined;
 let expected:{nodeId:string;field:string;value:unknown}|undefined;
 const current=()=>{
  const sequence=api.states().sequence?.value as Sequence??{};
  const catalog=api.states().sequenceCatalog?.value;
  return {...sequence,insertCatalog:(catalog?.complete===true?catalog.items:sequence.insertCatalog) as Entry[]|undefined};
 };
 const blocked=()=>!api.live()?'Connection unavailable.':!current().editing?.supported?'Read only · partial sequence.':!current().editing?.canEdit?'Stop the sequence and any exposure before editing.':api.reason('sequence','editField');
 const stale=()=>view.sequenceId!==current().sequenceId||view.configurationRevision!==current().configurationRevision;
 const parameters=()=>({sequenceId:view.sequenceId,expectedRevision:view.configurationRevision});
 function button(label:string,handler:()=>void){const b=document.createElement('button');b.type='button';b.textContent=label;b.onclick=handler;return b;}
 function guard(){const reason=blocked()||(stale()?'The sequence changed. Refresh and review your edit.':'');if(reason){message.textContent=reason;return false;}return true;}
 function refresh(){closeFlyout();view=current();shownRevision=view.configurationRevision??-1;message.textContent=blocked();renderTree();edit(selected);}

 const triggerBody=(id:string)=>{const sequence=current();return sequence.nodes?.some(n=>n.id===id&&n.kind==='trigger')&&sequence.nodes?.some(n=>n.parentId===id);};
 let flyout:HTMLElement|undefined,flyoutAnchor:HTMLButtonElement|undefined;
 function closeFlyout(restore=false){flyout?.remove();flyout=undefined;if(restore)flyoutAnchor?.focus();flyoutAnchor?.setAttribute('aria-expanded','false');}
 dialog.addEventListener('close',()=>{closeFlyout();triggerScope='';dialog.classList.remove('trigger-settings');dialog.style.left='';dialog.style.top='';});
 document.addEventListener('sequence-layout-change',()=>closeFlyout());
 window.addEventListener('resize',()=>{closeFlyout();if(dialog.open&&triggerScope){const bounds=dialog.getBoundingClientRect();dialog.style.left=Math.max(12,Math.min(bounds.left,innerWidth-bounds.width-12))+'px';dialog.style.top=Math.max(12,Math.min(bounds.top,innerHeight-bounds.height-12))+'px';}});
 outline.addEventListener('scroll',()=>closeFlyout());
 document.addEventListener('pointerdown',e=>{if(flyout&&!flyout.contains(e.target as globalThis.Node)&&e.target!==flyoutAnchor)closeFlyout();});
 function choose(parent:Node,index:number,kinds:string[],anchor:HTMLButtonElement){
  if(!guard())return;closeFlyout();flyoutAnchor=anchor;anchor.setAttribute('aria-expanded','true');
  const catalogError=api.states().sequenceCatalog?.value?.error;
  if(catalogError){message.textContent=String(catalogError);closeFlyout();if(!dialog.open)dialog.showModal();return;}
  const entries=(view.insertCatalog??[]).filter(e=>kinds.includes(e.kind)||(kinds.includes('target')&&(e.target||e.type==='DeepSkyObjectContainer')));
  const popup=document.createElement('div');popup.className='sequence-picker';popup.setAttribute('aria-label','Add element in '+parent.name);flyout=popup;
  const heading=document.createElement('div');heading.className='sequence-picker-heading';heading.textContent=parent.name+' · Position '+(index+1);popup.append(heading);
  const columns=document.createElement('div');columns.className='sequence-picker-columns';popup.append(columns);
  const groups=document.createElement('div'),choices=document.createElement('div');
  groups.className='sequence-picker-groups';choices.className='sequence-picker-items';groups.setAttribute('role','menu');groups.setAttribute('aria-label','Element categories');choices.setAttribute('role','menu');columns.append(groups,choices);
  const names=[...new Set(entries.map(e=>e.group))];
  function showGroup(name:string,focus=false){
   for(const b of groups.querySelectorAll('button'))b.setAttribute('aria-expanded',String(b.dataset.group===name));
   choices.replaceChildren();choices.setAttribute('aria-label',name+' elements');
   for(const entry of entries.filter(e=>e.group===name)){
    const item=button(entry.label,()=>{
     if(entry.kind==='instruction'||entry.kind==='container'){
      closeFlyout(true);const children=(current().nodes??[]).filter(n=>n.parentId===parent.id&&['instruction','container'].includes(n.kind));
      void draft.open(entry.type,{parentId:parent.id,afterNodeId:index===0?'':children[index-1]?.id??''});return;
     }
     if(!guard())return;
     closeFlyout(true);waiting={parentId:parent.id,index,type:entry.nodeType??entry.type,before:new Set((view.nodes??[]).map(n=>n.id))};
     api.run('sequence','insert',{...parameters(),parentId:parent.id,index,type:entry.type});
    });item.setAttribute('role','menuitem');item.title=entry.source??'NINA';item.dataset.type=entry.type;choices.append(item);
   }
   if(focus)choices.querySelector<HTMLButtonElement>('button')?.focus();
  }
  for(const name of names){
   const category=button(name+' ›',()=>showGroup(name,true));category.dataset.group=name;category.setAttribute('role','menuitem');category.setAttribute('aria-haspopup','menu');category.onpointerenter=()=>showGroup(name);category.onfocus=()=>showGroup(name);groups.append(category);
  }
  if(!entries.length)heading.textContent='No supported elements at this position';
  popup.onkeydown=e=>{
   const target=e.target as HTMLElement,list=target.closest('[role=menu]'),items=[...(list?.querySelectorAll<HTMLButtonElement>('button')??[])],i=items.indexOf(target as HTMLButtonElement);
   if(e.key==='Escape'){e.preventDefault();e.stopPropagation();closeFlyout(true);}
   else if(['ArrowDown','ArrowUp','Home','End'].includes(e.key)){e.preventDefault();items[e.key==='Home'?0:e.key==='End'?items.length-1:(i+(e.key==='ArrowDown'?1:-1)+items.length)%items.length]?.focus();}
   else if(e.key==='ArrowRight'&&list===groups){e.preventDefault();showGroup(target.dataset.group!,true);}
   else if(e.key==='ArrowLeft'&&list===choices){e.preventDefault();groups.querySelector<HTMLButtonElement>('[aria-expanded=true]')?.focus();}
   else if(e.key==='Tab')closeFlyout();
  };
  if(dialog.open)dialog.append(popup);else{popup.popover='auto';document.body.append(popup);popup.showPopover();}
  showGroup(names[0]);
  const r=anchor.getBoundingClientRect(),width=Math.min(500,innerWidth-24);
  const anchorX=Number(anchor.dataset.anchorX)||r.left+Math.min(180,r.width/3);
  const expanded=!!anchor.closest('.sequence-panel.is-expanded');
  const beside=expanded&&anchorX+width+24<=innerWidth;
  popup.style.width=width+'px';popup.style.left=Math.max(12,Math.min(beside?anchorX+12:expanded?anchorX-width-12:r.left,innerWidth-width-12))+'px';
  popup.style.top=Math.max(12,Math.min(beside?r.top:r.bottom+4,innerHeight-Math.min(popup.scrollHeight,420)-12))+'px';
  groups.querySelector<HTMLButtonElement>('button')?.focus();
 }
 function insertion(parent:Node,index:number,kinds:string[],label:string){
  const b=button('+',()=>choose(parent,index,kinds,b));b.className='sequence-insert-line sequence-inline-insert';b.addEventListener('pointerdown',e=>{b.dataset.anchorX=String(e.clientX);});b.title='Add '+label+' · '+parent.name;b.disabled=!!blocked();b.setAttribute('aria-haspopup','menu');b.setAttribute('aria-expanded','false');b.setAttribute('aria-label','Insert '+label+' in '+parent.name+' at '+(index+1));return b;
 }
 function renderTree(){
  const scroll=outline.scrollTop;outline.replaceChildren();const nodes=view.nodes??[];
  function walk(item:Node,host:HTMLElement,depth:number){if(depth>16)return;
   const row=button(`${sequenceName(item)} · ${item.kind}`,()=>{selected=item.id;edit(selected);for(const b of outline.querySelectorAll('button.sequence-edit-row'))b.setAttribute('aria-pressed',String((b as HTMLElement).dataset.id===selected));});row.className='sequence-edit-row';row.dataset.id=item.id;row.setAttribute('aria-pressed',String(selected===item.id));host.append(row);
   if(triggerScope&&item.canStructure&&item.id!==triggerScope){const remove=button('Remove '+item.name,()=>{if(guard()){expected=undefined;structure('delete',item.id);}});remove.className='trigger-step-remove';remove.disabled=!!blocked();host.append(remove);}
   const children=nodes.filter(n=>n.parentId===item.id),inside=document.createElement('div');inside.className='sequence-edit-children';host.append(inside);
   const kinds=item.insertKinds??[];const ordinary=children.filter(n=>n.kind==='instruction'||n.kind==='container');
   for(let i=0;i<=ordinary.length;i++){if(kinds.some(k=>['instruction','container','target'].includes(k)))inside.append(insertion(item,i,kinds.filter(k=>['instruction','container','target'].includes(k)),kinds.includes('target')&&!kinds.includes('instruction')?'target':'instruction or group'));if(i<ordinary.length)walk(ordinary[i],inside,depth+1);}
   for(const kind of ['condition','trigger']){for(const child of children.filter(n=>n.kind===kind))walk(child,inside,depth+1);if(kinds.includes(kind))inside.append(insertion(item,children.filter(n=>n.kind===kind).length,[kind],kind));}
  }
  for(const root of nodes.filter(n=>triggerScope?n.id===triggerScope:!n.parentId))walk(root,outline,0);outline.scrollTop=scroll;
 }
 function edit(id:string){
  closeFlyout();form.replaceChildren();const item=view.nodes?.find(n=>n.id===id);if(!item){form.textContent='Select an element, or use + between instructions.';return;}
  dialog.querySelector('#sequence-editor-title')!.textContent=triggerScope?sequenceName(view.nodes?.find(n=>n.id===triggerScope)??item):sequenceName(item);
  dialog.querySelector('.parameter-icon')!.innerHTML=parameterIcon(item.type);
  if(item.type==='DIYMeridianFlipTrigger'&&typeof item.details.useSideOfPier==='boolean'){
   const note=document.createElement('p');note.textContent='Side of pier: '+(item.details.useSideOfPier?'on':'off')+' · from NINA profile';form.append(note);
  }
  if(item.type==='TimeCondition'||item.type==='WaitForTime'){
   const summary=document.createElement('p');summary.className='parameter-time-details';summary.textContent=sequenceDetail(item)+' · ';
   const clock=timedCountdownElement(item.details??{},api.states().sequence?.observedAt,(api.states().administration?.value?.site as {timeZone?:string}|undefined)?.timeZone);if(clock)summary.append(clock);form.append(summary);
  }
  if(item.type==='TemplateByReference'){
   const note=document.createElement('p');note.textContent='Template: '+String(item.details.templateName??'Unnamed')+'. Changes here affect this loaded session. Powerups reloads referenced templates from the library when reopening a saved sequence.';form.append(note);
   if(!item.details.templateLoaded){const load=button('Load referenced actions',()=>{if(guard())api.run('sequence','resolveTemplate',{...parameters(),nodeId:item.id});});load.disabled=!!blocked();form.append(load);}
  }
  if(!item.editableFields?.length){const p=document.createElement('p');p.textContent='No supported parameters.';form.append(p);}
  for(const field of item.editableFields??[]){
   const group=document.createElement('form');group.className='parameter-row';group.dataset.field=field.key;
   const label=document.createElement('label');label.textContent=field.label;label.htmlFor='parameter-'+field.key;
   const save=document.createElement('button');save.className='parameter-apply';save.type='submit';save.innerHTML='<svg viewBox="0 0 24 24" aria-hidden="true"><path d="m5 12 4 4L19 6"/></svg>';save.setAttribute('aria-label','Apply '+field.label);save.title='Apply '+field.label;
   const sync=()=>{const value=field.type==='boolean'?(control.input as HTMLInputElement).checked:field.type==='string'?control.input.value:Number(control.input.value);save.dataset.clean=String(value===field.value);save.disabled=!!blocked()||stale()||value===field.value;group.classList.toggle('is-dirty',value!==field.value);};
   const control=createParameterControl(field,sync);
   group.onsubmit=e=>{e.preventDefault();if(!guard()||!group.reportValidity())return;const value=field.type==='boolean'?(control.input as HTMLInputElement).checked:field.type==='string'?control.input.value:Number(control.input.value);expected={nodeId:item.id,field:field.key,value};api.run('sequence','editField',{...parameters(),nodeId:item.id,field:field.key,value});};
   group.append(label,control.host,save);form.append(group);sync();
  }
 }

 let decorated:Element|null=null;
 function decorateInline(){
  const first=tree!.querySelector('.sequence-node');
  if(first!==decorated){
   tree!.querySelectorAll('.sequence-inline-insert').forEach(b=>b.remove());
   decorated=first;

   const nodes=current().nodes??[];
   const slots=[...tree!.querySelectorAll<HTMLElement>('[data-sequence-slot]')];
   function add(parent:Node,index:number,kinds:string[],slot:HTMLElement,after?:HTMLElement,label='instruction'){
    const line=button('+',()=>{if(!api.live()||line.disabled)return;refresh();const actual=view.nodes?.find(n=>n.id===parent.id);if(actual)choose(actual,index,kinds,line);});
    line.addEventListener('pointerdown',e=>{line.dataset.anchorX=String(e.clientX);});
    line.className='sequence-inline-insert';line.dataset.parentId=parent.id;line.dataset.index=String(index);line.dataset.kinds=kinds.join(',');line.setAttribute('aria-label','Add '+label+' to '+parent.name+' at position '+(index+1));line.setAttribute('aria-haspopup','menu');line.setAttribute('aria-expanded','false');line.title='Add '+label+' · '+parent.name;
    if(label==='condition'||label==='trigger')line.textContent='+ '+label;
    if(after)after.after(line);else slot.prepend(line);
   }
   if(current().editing?.supported)for(const parent of nodes){
    const kinds=parent.insertKinds??[];
    const ordinarySlot=slots.find(s=>s.dataset.parentId===parent.id&&s.dataset.sequenceSlot==='ordinary');
    if(!ordinarySlot)continue;
    const ordinary=nodes.filter(n=>n.parentId===parent.id&&['container','instruction'].includes(n.kind));
    const insertKinds=kinds.filter(k=>['target','instruction','container'].includes(k));
    const label=kinds.includes('target')&&!kinds.includes('instruction')?'target':'instruction';
    if(insertKinds.length){
     add(parent,0,insertKinds,ordinarySlot,undefined,label);
     for(let i=0;i<ordinary.length;i++){
      const branch=[...ordinarySlot.children].find(e=>(e as HTMLElement).dataset.branchId===ordinary[i].id) as HTMLElement|undefined;
      if(branch)add(parent,i+1,insertKinds,ordinarySlot,branch,label);
     }
    }
    for(const kind of ['condition','trigger'])if(kinds.includes(kind)){
     const slot=slots.find(s=>s.dataset.parentId===parent.id&&s.dataset.sequenceSlot===kind);
     if(slot){
      const attachments=nodes.filter(n=>n.parentId===parent.id&&n.kind===kind);
      const title=slot.querySelector<HTMLElement>(':scope > .sequence-exit-title,:scope > .sequence-watchers-title')??undefined;
      add(parent,0,[kind],slot,title,kind);
      attachments.forEach((n,i)=>{const branch=[...slot.children].find(e=>(e as HTMLElement).dataset.branchId===n.id) as HTMLElement|undefined;if(branch)add(parent,i+1,[kind],slot,branch,kind);});
     }
    }
   }
  }
  const disabled=!!blocked();
  for(const b of tree!.querySelectorAll<HTMLButtonElement>('.sequence-inline-insert'))b.disabled=disabled||((b.dataset.kinds??'').split(',').some(k=>['instruction','container','target'].includes(k))&&!!api.reason('sequence','insertDraft'));
  for(const b of tree!.querySelectorAll<HTMLButtonElement>('[data-structure-action]'))if(b.disabled!==disabled)b.disabled=disabled;
 }
 let dragging:{id:string;sequenceId?:string;expectedRevision?:number;session:string}|undefined;
 function structure(action:string,id:string,extra:Record<string,unknown>={}){
  const now=current();if(blocked()||!now.nodes?.find(n=>n.id===id)?.canStructure)return;
  closeFlyout();api.run('sequence',action,{sequenceId:now.sequenceId,expectedRevision:now.configurationRevision,nodeId:id,...extra});
 }
 let pointerDrag:{pointerId:number;x:number;y:number;active:boolean;line?:HTMLButtonElement}|undefined;
 const clearDrop=()=>{for(const line of tree.querySelectorAll('.is-drop-target'))line.classList.remove('is-drop-target');};
 function finishDrag(){dragging=undefined;pointerDrag=undefined;tree!.classList.remove('is-dragging');clearDrop();}
 tree.addEventListener('pointerdown',e=>{
  const handle=(e.target as Element).closest<HTMLElement>('[data-structure-action=move]');
  const id=handle?.closest<HTMLElement>('.sequence-node')?.dataset.nodeId,now=current();
  if(e.button!==0||!id||blocked())return;
  e.preventDefault();handle!.setPointerCapture(e.pointerId);
  dragging={id,sequenceId:now.sequenceId,expectedRevision:now.configurationRevision,session:api.session()};
  pointerDrag={pointerId:e.pointerId,x:e.clientX,y:e.clientY,active:false};closeFlyout();
 });
 tree.addEventListener('pointermove',e=>{
  if(!dragging||!pointerDrag||pointerDrag.pointerId!==e.pointerId)return;
  if(!pointerDrag.active&&Math.hypot(e.clientX-pointerDrag.x,e.clientY-pointerDrag.y)<4)return;
  pointerDrag.active=true;tree.classList.add('is-dragging');clearDrop();pointerDrag.line=undefined;
  const bounds=tree.getBoundingClientRect();if(e.clientY<bounds.top+25)tree.scrollTop-=18;else if(e.clientY>bounds.bottom-25)tree.scrollTop+=18;
  const line=document.elementFromPoint(e.clientX,e.clientY)?.closest<HTMLButtonElement>('.sequence-inline-insert');
  if(!line||line.disabled||!tree.contains(line))return;
  const now=current(),item=now.nodes?.find(n=>n.id===dragging!.id);
  if(!item||dragging.session!==api.session()||dragging.sequenceId!==now.sequenceId||dragging.expectedRevision!==now.configurationRevision)return;
  let ancestor=now.nodes?.find(n=>n.id===line.dataset.parentId);for(let depth=0;ancestor&&depth<17;depth++){if(ancestor.id===item.id)return;ancestor=now.nodes?.find(n=>n.id===ancestor!.parentId);}
  const kinds=(line.dataset.kinds??'').split(',');if(!kinds.includes(item.kind)&&!(kinds.includes('target')&&item.type==='DeepSkyObjectContainer'))return;
  line.classList.add('is-drop-target');pointerDrag.line=line;
 });
 tree.addEventListener('pointerup',e=>{
  if(!dragging||!pointerDrag||pointerDrag.pointerId!==e.pointerId)return;
  const drag=dragging,line=pointerDrag.active?pointerDrag.line:undefined;finishDrag();
  const now=current();if(!line||blocked()||drag.session!==api.session()||drag.sequenceId!==now.sequenceId||drag.expectedRevision!==now.configurationRevision)return;
  api.run('sequence','move',{sequenceId:drag.sequenceId,expectedRevision:drag.expectedRevision,nodeId:drag.id,parentId:line.dataset.parentId,index:Number(line.dataset.index)});
 });
 tree.addEventListener('pointercancel',finishDrag);
 tree.addEventListener('lostpointercapture',finishDrag);
 document.addEventListener('keydown',e=>{if(e.key==='Escape'&&dragging){e.preventDefault();finishDrag();}});
 tree.addEventListener('keydown',e=>{
  const handle=(e.target as Element).closest<HTMLElement>('[data-structure-action=move]');
  if(!handle||!e.altKey||!['ArrowUp','ArrowDown'].includes(e.key))return;
  const id=handle.closest<HTMLElement>('.sequence-node')?.dataset.nodeId,now=current(),item=now.nodes?.find(n=>n.id===id);if(!item)return;
  const siblings=now.nodes!.filter(n=>n.parentId===item.parentId&&(['container','instruction'].includes(item.kind)?['container','instruction'].includes(n.kind):n.kind===item.kind));
  const i=siblings.findIndex(n=>n.id===id),index=e.key==='ArrowUp'?i-1:i+2;
  if(index>=0&&index<=siblings.length){e.preventDefault();structure('move',id!,{parentId:item.parentId,index});}
 });
 function showTrigger(id:string,anchor:HTMLElement){
  if(dialog.open)return;triggerScope=id;selected=id;refresh();
  dialog.classList.remove('step-settings');dialog.classList.add('trigger-settings');
  const r=anchor.getBoundingClientRect(),width=Math.min(860,innerWidth-24);
  dialog.style.left=Math.max(12,Math.min(r.left-width-16,innerWidth-width-12))+'px';
  dialog.style.top=Math.max(12,Math.min(r.top,innerHeight-500))+'px';
  dialog.showModal();
  dialog.style.top=Math.max(12,Math.min(r.top,innerHeight-dialog.getBoundingClientRect().height-12))+'px';
 }
 tree.addEventListener('pointerover',e=>{
  if(!matchMedia('(hover:hover)').matches||dialog.open)return;
  const anchor=(e.target as Element).closest<HTMLElement>('.sequence-node .sequence-name');
  if(!anchor)return;clearTimeout(hoverTimer);
  const id=anchor.closest<HTMLElement>('.sequence-node')!.dataset.nodeId!;
  if(!triggerBody(id))return;
  hoverTimer=setTimeout(()=>{if(anchor.matches(':hover'))showTrigger(id,anchor);},450);
 });
 tree.addEventListener('pointerout',e=>{if((e.target as Element).matches('.sequence-name'))clearTimeout(hoverTimer);});
 tree.addEventListener('click',e=>{
  const target=e.target as Element;
  const action=target.closest<HTMLElement>('[data-structure-action]');
  if(action){const id=action.closest<HTMLElement>('.sequence-node')?.dataset.nodeId;if(id&&action.dataset.structureAction!=='move')structure(action.dataset.structureAction!,id);return;}

  const label=target.closest('.sequence-step-menu')?.closest('.sequence-node')?.querySelector('strong')??target.closest('.sequence-node strong');
  const id=label?.closest<HTMLElement>('.sequence-node')?.dataset.nodeId;
  if(!id)return;
  if(triggerBody(id)){showTrigger(id,label as HTMLElement);return;}
  if(!current().nodes?.find(n=>n.id===id)?.editableFields?.length)return;
  if(current().nodes?.find(n=>n.id===id)?.type==='DIYMeridianFlipTrigger'){showTrigger(id,label as HTMLElement);return;}
  if(dialog.open)dialog.close();triggerScope='';selected=id;refresh();dialog.classList.add('step-settings');dialog.showModal();form.querySelector<HTMLElement>('input,select,textarea')?.focus({preventScroll:true});
 });
 tree.addEventListener('keydown',e=>{
  if((e.key==='Enter'||e.key===' ')&&(e.target as Element).matches('.sequence-node strong')){e.preventDefault();(e.target as HTMLElement).click();}
 });
 tree.addEventListener('scroll',()=>closeFlyout());
 let labeled:Element|null=null;
 function decorate(){
  decorateInline();
  const first=tree!.querySelector(".sequence-node");if(first===labeled)return;labeled=first;
  for(const label of tree!.querySelectorAll<HTMLElement>('.sequence-node strong')){
   const id=label.closest<HTMLElement>('.sequence-node')?.dataset.nodeId;
   if(current().nodes?.find(n=>n.id===id)?.editableFields?.length||triggerBody(id!)){label.tabIndex=0;label.setAttribute('role','button');label.title='Edit step settings';}
  }
 }

 return function update(){
  draft.update();
  const now=current(),nextContext=api.session()+':'+(now.sequenceId??'');
  if(context!==nextContext){context=nextContext;triggerScope='';if(dialog.open)dialog.close();selected='';waiting=undefined;expected=undefined;if(dialog.open)refresh();}
  decorate();
  if(flyout&&(blocked()||stale()))closeFlyout();
  if(!dialog.open){
   if(waiting&&now.configurationRevision!==shownRevision){
    const inserted=(now.nodes??[]).filter(n=>n.parentId===waiting!.parentId&&n.type===waiting!.type&&!waiting!.before.has(n.id));
    if(inserted.length===1){selected=inserted[0].id;waiting=undefined;refresh();}
   }
   return;
  }
  if(now.configurationRevision!==shownRevision){
   closeFlyout();
   const inserted=waiting&&(now.nodes??[]).filter(n=>n.parentId===waiting!.parentId&&n.type===waiting!.type&&!waiting!.before.has(n.id));
   const match=expected&&now.nodes?.find(n=>n.id===expected!.nodeId)?.editableFields?.find(f=>f.key===expected!.field);
   if(inserted?.length===1){selected=inserted[0].id;waiting=undefined;refresh();}
   else if(match&&(typeof expected!.value==='number'&&typeof match.value==='number'?Math.abs(match.value-expected!.value)<1e-9:match.value===expected!.value)){expected=undefined;refresh();}
   else if(triggerScope&&!expected){if(!now.nodes?.some(n=>n.id===selected))selected=triggerScope;refresh();}
   else{if(!message.querySelector('button'))message.replaceChildren(document.createTextNode('The sequence changed. '),button('Refresh sequence',()=>{waiting=undefined;expected=undefined;refresh();}));for(const b of form.querySelectorAll('button'))b.disabled=true;}
  }else{if(blocked())closeFlyout();message.textContent=blocked();for(const b of form.querySelectorAll('button'))b.disabled=!!blocked()||b.dataset.clean==='true';for(const b of outline.querySelectorAll<HTMLButtonElement>('.sequence-insert-line'))b.disabled=!!blocked();}
 };
}
