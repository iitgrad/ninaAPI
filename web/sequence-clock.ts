export function sequenceCountdown(deadline:number,now=Date.now(),live=true):string {
 if(!live)return 'Remaining time unavailable · offline';
 if(!Number.isFinite(deadline))return 'Remaining time unavailable';
 const seconds=Math.max(0,Math.ceil((deadline-now)/1000));
 if(!seconds)return 'Time reached';
 const h=Math.floor(seconds/3600),m=Math.floor(seconds%3600/60),s=seconds%60;
 return 'Remaining '+[h,m,s].map(v=>String(v).padStart(2,'0')).join(':');
}
export function countdownElement(remaining:unknown,observedAt:unknown):HTMLSpanElement|undefined{
 if(typeof remaining!=='number'||!Number.isFinite(remaining)||typeof observedAt!=='string')return;
 const deadline=Date.parse(observedAt)+remaining*1000;if(!Number.isFinite(deadline))return;
 const span=document.createElement('span');span.className='sequence-countdown';span.dataset.deadline=String(deadline);span.textContent=sequenceCountdown(deadline);return span;
}
export function updateSequenceClocks(live:boolean){
 for(const span of document.querySelectorAll<HTMLElement>('.sequence-countdown')){
  const text=sequenceCountdown(Number(span.dataset.deadline),Date.now(),live);if(span.textContent!==text)span.textContent=text;
 }
}

// WaitForTime has no RemainingTime property in NINA. Reproduce its observing
// day rule using the NINA host's reported zone, never the browser's local zone.
export function rolloverDeadline(details:Record<string,unknown>,observedAt:string,zone:string):number {
 const {hours,minutes,seconds,rolloverTime}=details;
 if(![hours,minutes,seconds].every(v=>typeof v==='number'&&Number.isInteger(v))||Number(hours)<0||Number(hours)>23||Number(minutes)<0||Number(minutes)>59||Number(seconds)<0||Number(seconds)>59||typeof rolloverTime!=='string')return NaN;
 const match=/^(\d{2}):(\d{2}):(\d{2})$/.exec(rolloverTime);if(!match)return NaN;
 const rollover=+match[1]*3600 + +match[2]*60 + +match[3],target=Number(hours)*3600+Number(minutes)*60+Number(seconds),observed=Date.parse(observedAt);if(!Number.isFinite(observed)||rollover>=86400)return NaN;
 try{
  const format=new Intl.DateTimeFormat('en-CA',{timeZone:zone,year:'numeric',month:'2-digit',day:'2-digit',hour:'2-digit',minute:'2-digit',second:'2-digit',hourCycle:'h23'});
  const parts=(time:number)=>Object.fromEntries(format.formatToParts(time).map(p=>[p.type,p.value]));
  const now=parts(observed),clock=+now.hour*3600 + +now.minute*60 + +now.second;
  const dayShift=clock<rollover&&target>=rollover?-1:clock>=rollover&&target<rollover?1:0;
  const wall=Date.UTC(+now.year,+now.month-1,+now.day+dayShift,Number(hours),Number(minutes),Number(seconds));let result=wall;
  for(let i=0;i<3;i++){const p=parts(result);result+=wall-Date.UTC(+p.year,+p.month-1,+p.day,+p.hour,+p.minute,+p.second);}
  return result;
 }catch{return NaN;}
}
export function timedCountdownElement(details:Record<string,unknown>,observedAt:unknown,zone:unknown){
 if(typeof details.remainingSeconds==='number')return countdownElement(details.remainingSeconds,observedAt);
 if(typeof observedAt!=='string'||typeof zone!=='string')return;
 const deadline=rolloverDeadline(details,observedAt,zone);
 return countdownElement((deadline-Date.parse(observedAt))/1000,observedAt);
}
