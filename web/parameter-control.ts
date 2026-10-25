import {sequenceIcon} from './sequence-icons';
export type ParameterField={key:string;label:string;type:string;value:unknown;minimum?:number;maximum?:number;unit?:string;maxLength?:number;choices?:{value:number;label:string}[]};
export function parameterIcon(type:string){return '<svg viewBox="0 0 24 24" aria-hidden="true">'+sequenceIcon(type).svg+'</svg>';}
export function createParameterControl(field:ParameterField,onChange:()=>void){
 const host=document.createElement('div');host.className='parameter-control';
 const value=document.createElement('div');value.className='parameter-value';host.append(value);
 const input=field.type==='choice'?document.createElement('select'):field.type==='string'&&field.key==='Message'?document.createElement('textarea'):document.createElement('input');input.setAttribute('aria-label',field.label);input.required=field.type!=='boolean';
 if(input instanceof HTMLSelectElement){for(const c of field.choices??[]){const o=document.createElement('option');o.value=String(c.value);o.textContent=c.label;input.append(o);}value.classList.add('is-choice');}
 else if(input instanceof HTMLTextAreaElement){input.rows=5;input.maxLength=field.maxLength??512;value.classList.add('is-text');}
 else{input.type=field.type==='boolean'?'checkbox':field.type==='string'?'text':'number';input.step=field.type==='integer'?'1':'any';if(field.minimum!=null)input.min=String(field.minimum);if(field.maximum!=null)input.max=String(field.maximum);if(field.type==='string')input.maxLength=field.maxLength??96;value.classList.add(field.type==='boolean'?'is-boolean':field.type==='string'?'is-text':'is-numeric');}
 if(input instanceof HTMLInputElement&&field.type==='boolean'){input.checked=field.value===true;input.setAttribute('role','switch');}else input.value=field.value==null?'':String(field.value);
 const selection=()=>{for(const b of host.querySelectorAll<HTMLButtonElement>('.parameter-presets button'))b.setAttribute('aria-pressed',String(Number(b.dataset.value)===Number(input.value)));};
 const changed=()=>{selection();onChange();};
 input.id='parameter-'+field.key;input.addEventListener('input',changed);input.addEventListener('change',changed);
 if(input instanceof HTMLInputElement&&input.type==='number'){
  const step=(direction:number)=>{const current=Number(input.value);if(!Number.isFinite(current))return;const delta=field.type==='integer'?1:field.key==='RightAscension'||field.key==='Declination'?1/3600:field.key==='ExposureTime'&&Math.abs(current)<1?.001:field.key==='Duration'?.5:1;input.value=String(Math.min(field.maximum??Infinity,Math.max(field.minimum??-Infinity,Number((current+direction*delta).toFixed(6)))));changed();};
  for(const direction of [-1,1]){const b=document.createElement('button');b.type='button';b.className='parameter-step';b.textContent=direction<0?'−':'+';b.setAttribute('aria-label',(direction<0?'Decrease ':'Increase ')+field.label);b.onclick=()=>step(direction);if(direction<0)value.append(b);else value.append(input,b);}
 }else value.append(input);
 if(field.unit){const unit=document.createElement('span');unit.className='parameter-unit';unit.textContent=field.unit;host.append(unit);}
 if(field.key==='ExposureTime'){
  const presets=document.createElement('div');presets.className='parameter-presets';presets.setAttribute('aria-label','Exposure presets');
  for(const seconds of [.025,1,30,60])if(seconds>=(field.minimum??0)&&seconds<=(field.maximum??Infinity)){const b=document.createElement('button');b.type='button';b.textContent=seconds+' s';b.dataset.value=String(seconds);b.setAttribute('aria-label','Set exposure to '+seconds+' seconds');b.onclick=()=>{input.value=String(seconds);changed();input.focus();};presets.append(b);}
  host.append(presets);
 }
 selection();return {host,input};
}

