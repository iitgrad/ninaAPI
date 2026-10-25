import {createParameterControl} from './parameter-control';
import type {Topic} from './dashboard';
type Field={key:string;label:string;type:string;value:unknown;minimum?:number;maximum?:number;unit?:string;choices?:{value:number;label:string}[]};
type Schema={type:string;label:string;group:boolean;supported:boolean;fields:Field[]};
type Draft={type:string;fields:Record<string,unknown>;items?:Draft[]};
type Entry={type:string;label:string;group:string;kind:string};
type Position={parentId:string;afterNodeId:string};
type API={states:()=>Record<string,Topic>;live:()=>boolean;reason:()=>string;schema:(type:string)=>Promise<Schema>;submit:(parameters:Record<string,unknown>)=>Promise<string|undefined>};
export function mountSequenceDraft(api:API){
 const dialog=document.createElement('dialog');dialog.className='sequence-editor step-settings';dialog.setAttribute('aria-label','Configure new step');
 const heading=document.createElement('h2'),body=document.createElement('div'),status=document.createElement('p'),footer=document.createElement('footer');
 status.setAttribute('role','status');footer.className='parameter-footer';dialog.append(heading,body,status,footer);document.body.append(dialog);
 const button=(text:string,action:()=>void)=>{const b=document.createElement('button');b.type='button';b.textContent=text;b.onclick=action;return b;};
 let draft:Draft|undefined,schemas=new Map<string,Schema>(),position:Position|undefined,tokens:Record<string,unknown>={},receipt:string|undefined,loading=false;
 const discard=button('Discard draft',()=>{if(!receipt&&!loading){draft=undefined;dialog.close();}});
 const close=button('Keep draft',()=>dialog.close()),add=button('Add',()=>void submit());footer.append(discard,close,add);
 async function schema(type:string){const result=await api.schema(type);schemas.set(type,result);return result;}
 async function create(type:string){const s=await schema(type);return {type,fields:Object.fromEntries(s.fields.map(f=>[f.key,f.value])),...(s.group?{items:[]}: {})};}
 function draw(node:Draft,host:HTMLElement,depth=0){
  const s=schemas.get(node.type)!;const section=document.createElement('section');section.style.marginLeft=depth?'12px':'0';section.style.borderLeft=depth?'1px solid #647482':'';section.style.padding='8px';host.append(section);
  const title=document.createElement('h3');title.textContent=s.label;section.append(title);
  if(!s.supported){const text=document.createElement('p');text.textContent='Configure this instruction in N.I.N.A. while stopped; its settings are not supported here yet.';section.append(text);return;}
  for(const field of s.fields){const label=document.createElement('label');label.textContent=field.label;const control=createParameterControl({...field,value:node.fields[field.key]},()=>{node.fields[field.key]=field.type==='boolean'?(control.input as HTMLInputElement).checked:field.type==='string'?control.input.value:Number(control.input.value);});label.append(control.host);section.append(label);}
  for(const child of node.items??[]){draw(child,section,depth+1);section.append(button('Remove draft child',()=>{node.items!.splice(node.items!.indexOf(child),1);render();}));}
  if(s.group&&depth<14){
   const catalog=api.states().sequenceCatalog?.value?.items as Entry[]??[],entries=catalog.filter(e=>['instruction','container'].includes(e.kind));
   const category=document.createElement('select'),choice=document.createElement('select');category.setAttribute('aria-label','Instruction category');choice.setAttribute('aria-label','Child instruction');
   for(const name of new Set(entries.map(e=>e.group))){const o=document.createElement('option');o.textContent=name;category.add(o);}
   const populate=()=>{choice.replaceChildren();for(const entry of entries.filter(e=>e.group===category.value)){const o=document.createElement('option');o.value=entry.type;o.textContent=entry.label;choice.add(o);}};category.onchange=populate;populate();
   section.append(category,choice,button('Add draft child',()=>{void create(choice.value).then(child=>{node.items!.push(child);render();}).catch(error=>status.textContent=String(error));}));
  }
 }
 function valid(node:Draft):boolean{return !!schemas.get(node.type)?.supported&&(!node.items||node.items.length>0&&node.items.every(valid));}
 function render(){discard.disabled=loading||!!receipt;body.replaceChildren();if(draft){heading.textContent='Add '+schemas.get(draft.type)?.label;draw(draft,body);}add.disabled=loading||!!receipt||!draft||!valid(draft)||!api.live()||!!api.reason();}
 async function submit(){
  if(!draft||!position||receipt||!valid(draft))return;
  loading=true;render();status.textContent='Adding…';
  try{receipt=await api.submit({...tokens,...position,draft});status.textContent=receipt?'Waiting for insertion receipt…':'Draft kept. Check Operations before retrying.';}
  catch(error){status.textContent=String(error)+' Draft kept; choose another insertion position if needed.';}
  finally{loading=false;render();}
 }
 return {
  async open(type:string,next:Position){
   if(receipt){status.textContent='Check the pending insertion before choosing another position.';if(!dialog.open)dialog.showModal();return;}
   const sequence=api.states().sequence?.value??{};position=next;tokens={sequenceId:sequence.sequenceId,expectedRevision:sequence.configurationRevision,executionId:sequence.executionId};
   if(!draft){try{draft=await create(type);}catch(error){status.textContent=String(error);return;}}else status.textContent='Draft retained · insertion position updated';
   render();if(!dialog.open)dialog.showModal();
  },
  update(){if(!dialog.open&&!receipt)return;render();if(!receipt)return;
   const operation=(api.states().operations?.value?.items as {requestId:string;status:string;completedAt?:string;error?:string}[]??[]).find(o=>o.requestId===receipt);
   if(!operation?.completedAt)return;
   receipt=undefined;
   if(operation.status==='succeeded'){draft=undefined;dialog.close();status.textContent='Added';}
   else{status.textContent=(operation.error??'Insertion failed')+'. Draft retained. Choose another position.';render();}
  }
 };
}
