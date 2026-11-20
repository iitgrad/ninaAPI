// Move existing controls so their handlers, disabled states and confirmations stay intact.
export function organizeSequenceActions(save:HTMLButtonElement,saveAs:HTMLButtonElement){
 const actions=document.querySelector<HTMLElement>('.sequence-actions');if(!actions||actions.classList.contains('sequence-actions-organized'))return;
 const get=(id:string)=>document.getElementById(id)!;
 save.id='sequence-save';saveAs.id='sequence-save-as';saveAs.textContent='Save as';
 get('sequence-start').textContent='▶ Start';get('sequence-stop').textContent='■ Stop';get('sequence-reset').textContent='↺ Reset';
 function group(label:string,className:string,buttons:HTMLElement[]){
  const row=document.createElement('div');row.className=className;row.setAttribute('role','group');row.setAttribute('aria-label',label+' actions');
  const title=document.createElement('span');title.className='sequence-action-label';title.textContent=label;row.append(title,...buttons);return row;
 }
 const hint=get('sequence-action-hint');
 actions.replaceChildren(
  group('File','sequence-file-group',[get('sequence-load'),save,saveAs]),
  group('Run','sequence-run-group',[get('sequence-start'),get('sequence-stop'),get('sequence-reset')]),
  group('View','sequence-view-group',[get('sequence-fold-all')]),hint,
 );
 actions.classList.add('sequence-actions-organized');
 const style=document.createElement('style');style.id='sequence-actions-layout-style';style.textContent=`
.sequence-panel .sequence-actions.sequence-actions-organized{display:flex;flex-direction:column;align-items:stretch;gap:8px}
.sequence-actions-organized .sequence-file-group,.sequence-actions-organized .sequence-run-group{display:grid;grid-template-columns:36px repeat(3,minmax(0,1fr));align-items:center;gap:6px}
.sequence-actions-organized .sequence-view-group{display:flex;align-items:center;gap:6px;padding-top:2px;border-top:1px solid var(--border)}
.sequence-actions-organized .sequence-action-label{font-size:11px;color:var(--muted);font-weight:600;min-width:36px}
.sequence-panel .sequence-actions.sequence-actions-organized button{min-height:32px;padding:6px 10px;font-size:12px;white-space:nowrap;min-width:0}
.sequence-panel .sequence-actions.sequence-actions-organized :is(#sequence-start,#sequence-stop,#sequence-reset,#sequence-fold-all,#sequence-load,#sequence-save,#sequence-save-as){grid-column:auto;grid-row:auto;justify-self:stretch;margin:0}
.sequence-panel .sequence-actions.sequence-actions-organized #sequence-fold-all{flex:0 0 auto;min-height:28px;font-size:11px;padding:4px 9px}
.sequence-actions-organized #sequence-action-hint:empty{display:none}
.replay-mode .sequence-panel .sequence-actions.sequence-actions-organized{display:none}
@media(max-width:420px){.sequence-actions-organized .sequence-file-group,.sequence-actions-organized .sequence-run-group{grid-template-columns:30px repeat(3,minmax(0,1fr));gap:5px}.sequence-panel .sequence-actions.sequence-actions-organized button{padding:6px 4px}.sequence-actions-organized .sequence-action-label{min-width:30px}}
`;document.head.append(style);
}
