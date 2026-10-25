export type StepTiming={source?:string;startedAt?:string|null;endedAt?:string|null;seenActiveAt?:string|null};
export function stepTiming(t:StepTiming|undefined,zone='America/Chicago'){
 const clock=(s:string)=>{try{const d=new Date(s);return Number.isFinite(d.getTime())?new Intl.DateTimeFormat('en-GB',{timeZone:zone,hour:'2-digit',minute:'2-digit',second:'2-digit',hour12:false}).format(d):'';}catch{return '';}};
 if(!t)return null;
 const start=t.startedAt&&clock(t.startedAt),end=t.endedAt&&clock(t.endedAt),seen=t.seenActiveAt&&clock(t.seenActiveAt);
 const text=start?`Start ${start}${end?' · End '+end:''}`:end?`End ${end}`:seen?`Seen ${seen}`:'';
 return text?{text,title:`Observed via ${t.source==='gateway-received'?'gateway WebSocket':'live updates'} · ${zone}. Receipt times, not N.I.N.A. log timestamps. ${t.startedAt??t.seenActiveAt??''} ${t.endedAt??''}`} : null;
}
