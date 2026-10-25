import {stepTiming} from './sequence-timing';
import {timedCountdownElement} from './sequence-clock';
import {sequenceIcon} from './sequence-icons';
import {targetHeader,clearTargetHeaders,finishTargetHeaders} from "./target-header";
import type {Site} from "./target-visibility";
import {sequenceName,sequenceDetail,type VisualNode,type CoolingContext} from "./sequence-presentation";
export {sequenceName,sequenceDetail} from "./sequence-presentation";
const el = (tag:string, cls:string, text?:string) => { const e=document.createElement(tag);e.className=cls;if(text!=null)e.textContent=text;return e; };
const number=(v:unknown):v is number=>typeof v==="number"&&Number.isFinite(v);
const fmt=(v:number)=>v.toLocaleString(undefined,{maximumFractionDigits:2});
function field(n:VisualNode,key:string){return n.editableFields?.find(f=>f.key===key)?.value;}
function glyph(n:VisualNode){
  const svg=document.createElementNS("http://www.w3.org/2000/svg","svg");
  svg.setAttribute("viewBox","0 0 24 24");svg.setAttribute("aria-hidden","true");svg.classList.add("sequence-glyph");
  const icon=sequenceIcon(n.type,n.kind);svg.dataset.icon=icon.key;
  svg.innerHTML=icon.svg;return svg;
}
export function watcherState(item:VisualNode,scope:VisualNode,live:boolean){
  if(item.state==="disabled")return "Disabled";
  if(!live)return "Cached";
  if(["running","waiting"].includes(item.state))return "Executing";
  return ["running","waiting"].includes(scope.state)?"Watching":"Configured";
}
export function renderSequenceView(tree:HTMLElement,nodes:VisualNode[],options:{
  collapsed:Set<string>;live:boolean;observedAt?:string;site?:Site;profileId?:string;expanded:boolean;cooling?:CoolingContext;toggle:(id:string)=>void;
}){
  clearTargetHeaders();
  const children=new Map<string|null,VisualNode[]>(),byId=new Map(nodes.map(n=>[n.id,n]));
  for(const n of nodes){const key=n.parentId&&byId.has(n.parentId)?n.parentId:null;const list=children.get(key)??[];list.push(n);children.set(key,list);}
  const activeExposureId=nodes.find(n=>n.kind==="instruction"&&n.state==="running"&&/Exposure/.test(n.type))?.id;
  const visited=new Set<string>();
  function row(item:VisualNode,depth:number,order:number,scope?:VisualNode){
    const area=/^(Start|Target|End)AreaContainer$/.test(item.type);
    const r=el("div","sequence-node "+item.kind+" state-"+item.state+(area?" sequence-area":""));
    r.dataset.nodeId=item.id;r.dataset.type=item.type;r.style.setProperty("--depth",String(Math.min(depth,8)));
    r.title=item.state==="pending"?"Not started":item.state;
    const current=["running","waiting"].includes(item.state);
    if(item.kind==="instruction"&&current)r.setAttribute("aria-current","step");
    if(item.kind==="container"){
      const toggle=el("button","tree-toggle",options.collapsed.has(item.id)?"›":"⌄") as HTMLButtonElement;
      toggle.type="button";toggle.setAttribute("aria-expanded",String(!options.collapsed.has(item.id)));
      toggle.setAttribute("aria-label",(options.collapsed.has(item.id)?"Expand ":"Collapse ")+item.name);
      toggle.onclick=()=>options.toggle(item.id);r.append(toggle);
    }else r.append(el("span","step-number",item.kind==="instruction"?String(order):""));
    r.append(glyph(item));
    const label=el("strong","sequence-name",sequenceName(item));r.append(label);
    // Match the native iOS outline: fixed symbols, without animated status marks.
    const symbols:Record<string,string>={
      running:'<path d="m7 4 14 8-14 8z" fill="currentColor" stroke="none"/>',
      waiting:'<circle cx="12" cy="12" r="9"/><path d="M12 6v6l4 3"/>',
      completed:'<path d="m4 12 5 5L20 6"/>',
      failed:'<circle cx="12" cy="12" r="9"/><path d="M12 7v6m0 4v.1"/>',
      disabled:'<circle cx="12" cy="12" r="9"/><path d="M7 12h10"/>',
      skipped:'<path d="M6 3v9a4 4 0 0 0 4 4h10m-4-4 4 4-4 4"/>',
      pending:'<circle cx="12" cy="12" r="8" stroke-dasharray="1 4" stroke-linecap="round"/>',
    };
    const mark=el("span","sequence-mark");
    mark.innerHTML='<svg viewBox="0 0 24 24" aria-hidden="true">'+(symbols[item.state]??symbols.pending)+'</svg>';
    mark.setAttribute("role","img");mark.setAttribute("aria-label",item.state==="completed"?"Successfully completed":item.state==="failed"?"Error":item.state.charAt(0).toUpperCase()+item.state.slice(1));r.append(mark);
    let target=byId.get(item.parentId??"");for(let depth=0;target&&target.type!=="DeepSkyObjectContainer"&&depth<17;depth++)target=byId.get(target.parentId??"");
    const summary=sequenceDetail(item,target?.type==="DeepSkyObjectContainer"?target:undefined,options.cooling);const detail=el("span","sequence-detail"+(["SendToDiscordWebhook","SendNotificationInstruction"].includes(item.type)?" sequence-message":""),summary);detail.title=summary;if(item.type==='TimeCondition'||item.type==='WaitForTime'){const clock=timedCountdownElement(item.details,options.observedAt,options.site?.timeZone);if(clock)detail.append(document.createTextNode(' · '),clock);}r.append(detail);
    const timing=stepTiming(item.timing,options.site?.timeZone);if(timing){const time=el("span","sequence-timing","Observed · "+timing.text);time.title=timing.title;r.append(time);}
    r.title=[sequenceName(item),summary,item.state==="pending"?"":item.state].filter(Boolean).join(" · ");
    const status=el("span","sequence-state",area&&!(children.get(item.id)?.length)?"Empty":"");
    r.append(status);
    if(item.kind==="trigger"&&scope){
      r.append(el("span","watcher-state",watcherState(item,scope,options.live)));
      r.dataset.watching=String(options.live&&["running","waiting"].includes(scope.state)&&item.state!=="disabled");
    }
    if(item.id===activeExposureId){
      const progress=el("span","sequence-exposure-inline");progress.id="sequence-exposure-inline";progress.hidden=true;
      const track=el("span","sequence-exposure-track"),bar=el("i","");bar.id="sequence-exposure-bar";track.append(bar);
      const reading=el("span","");reading.id="sequence-exposure-reading";progress.append(track,reading);r.append(progress);
    }
    if(item.canStructure){
      const actions=el("span","sequence-structure-actions");
      for(const [action,label,icon]of [["move","Move","⠿"],["duplicate","Duplicate","⧉"],["delete","Delete","×"]]){
        const b=el("button","sequence-"+action,icon) as HTMLButtonElement;b.type="button";const shapes:Record<string,string>={move:'<circle cx="9" cy="5" r="1"/><circle cx="15" cy="5" r="1"/><circle cx="9" cy="12" r="1"/><circle cx="15" cy="12" r="1"/><circle cx="9" cy="19" r="1"/><circle cx="15" cy="19" r="1"/>',duplicate:'<rect x="8" y="8" width="12" height="12" rx="1"/><path d="M16 8V4H4v12h4"/>',delete:'<path d="M4 7h16M9 7V4h6v3M7 7l1 13h8l1-13M10 10v7m4-7v7"/>'};b.innerHTML='<svg viewBox="0 0 24 24" aria-hidden="true">'+shapes[action]+'</svg>';b.dataset.structureAction=action;b.setAttribute("aria-label",label+" "+sequenceName(item));b.title=label+" "+sequenceName(item)+(action==="move"?" · drag or Alt+↑/↓":"");actions.append(b);
      }r.append(actions);
    }
    const fields=item.editableFields??[];
    if(fields.length||(item.kind==="trigger"&&(children.get(item.id)?.length??0)>0)){const menu=el("button","sequence-step-menu","⋮") as HTMLButtonElement;menu.type="button";menu.setAttribute("aria-label","Edit "+sequenceName(item));r.append(menu);}
    return r;
  }
  function slot(parent:VisualNode,kind:string,cls:string){
    const s=el("div",cls);s.dataset.sequenceSlot=kind;s.dataset.parentId=parent.id;return s;
  }
  function build(item:VisualNode,depth:number,order=1,scope?:VisualNode):HTMLElement{
    if(visited.has(item.id)||depth>16)return el("span","");visited.add(item.id);
    const r=row(item,depth,order,scope);
    if(item.kind!=="container"){r.dataset.branchId=item.id;return r;}
    const area=/^(Start|Target|End)AreaContainer$/.test(item.type),target=item.type==="DeepSkyObjectContainer";
    const list=children.get(item.id)??[],conditions=list.filter(n=>n.kind==="condition"),triggers=list.filter(n=>n.kind==="trigger");
    const group=el("section","sequence-branch "+(area?"sequence-area-block":target?"sequence-target":"sequence-set"));
    group.dataset.branchId=item.id;group.append(r);
    const loop=conditions.find(n=>n.type==="LoopCondition");
    group.classList.toggle("is-loop",!!loop);group.classList.toggle("is-parallel",item.type==="ParallelContainer");
    if(!area&&!target){
      const count=loop?.details.iterations??(loop?field(loop,"Iterations"):undefined),done=loop?.details.completedIterations;
      if(loop&&number(count)){
        const badge=el("span","sequence-set-badge","↻ "+(number(done)?fmt(done)+" / ":"")+fmt(count)+" repeats");
        const detail=r.querySelector(".sequence-detail")!;if(detail.textContent)detail.prepend(badge,document.createTextNode(" · "));else detail.append(badge);
      }
    }
    if(options.collapsed.has(item.id))return group;
    if(target)group.append(targetHeader(item,options.site,options.profileId,options.observedAt?Date.parse(options.observedAt):undefined));
    const layout=el("div","sequence-scope-layout"+(triggers.length?" has-triggers":""));group.append(layout);
    const body=el("div","sequence-set-main"),ordinary=slot(item,"ordinary","sequence-ordinary");body.append(ordinary);layout.append(body);
    list.filter(n=>["container","instruction"].includes(n.kind)).forEach((n,i)=>ordinary.append(build(n,area?depth:depth+1,i+1,item)));
    const exits=slot(item,"condition","sequence-exits"+(!conditions.length?" is-empty":""));
    if(conditions.length)exits.append(el("span","sequence-exit-title","⑂ EXIT CONDITIONS"));
    for(const condition of conditions)exits.append(build(condition,depth+1,0,item));
    if(conditions.length||item.insertKinds?.includes("condition"))body.prepend(exits);
    const watching=slot(item,"trigger","sequence-watchers"+(!triggers.length?" is-empty":""));
    if(triggers.length)watching.append(el("h3","sequence-watchers-title","⌁ SET TRIGGERS"));
    for(const trigger of triggers)watching.append(build(trigger,depth+1,0,item));
    if(triggers.length)layout.append(watching);
    else if(item.insertKinds?.includes("trigger"))body.append(watching);
    return group;
  }
  const result=document.createDocumentFragment();
  for(const item of children.get(null)??[]){
    if(item.type==="SequenceRootContainer"){visited.add(item.id);for(const child of children.get(item.id)??[])result.append(build(child,0));}
    else result.append(build(item,0));
  }
  tree.replaceChildren(result);
  finishTargetHeaders();
}
