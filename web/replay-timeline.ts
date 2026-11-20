export const DEFAULT_REPLAY_SPEED=1;
export type TimeWindow={start:number;end:number};
export function recordingStart(recording:{start:number;firstTelemetryAt:number}):number{return Math.max(recording.start,recording.firstTelemetryAt||recording.start);}
export function fitWindow(start:number,end:number,bounds:TimeWindow):TimeWindow{
 const duration=Math.min(bounds.end-bounds.start,Math.max(10000,end-start));
 const left=Math.max(bounds.start,Math.min(start,bounds.end-duration));
 return {start:left,end:left+duration};
}
export function zoomWindow(view:TimeWindow,factor:number,at:number,bounds:TimeWindow):TimeWindow{
 const width=Math.min(bounds.end-bounds.start,Math.max(10000,(view.end-view.start)*factor));return fitWindow(at-width/2,at+width/2,bounds);
}
export function revealTime(view:TimeWindow,at:number,bounds:TimeWindow):TimeWindow{
 return at>=view.start&&at<=view.end?view:zoomWindow(view,1,at,bounds);
}
export function selectionWindow(from:number,to:number,bounds:TimeWindow):TimeWindow{
 return fitWindow(Math.min(from,to),Math.max(from,to),bounds);
}
