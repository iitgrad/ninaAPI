type Sequence = {sequenceId?:unknown;configurationRevision?:unknown;nodes?:unknown};
// Native configuration revisions exclude progress and clock ticks. Comparing the
// whole telemetry tree makes a confirmation stale while the user is reading it.
export function sequenceCommandIdentity(sequence:Sequence):string {
 if(typeof sequence.sequenceId==='string'&&typeof sequence.configurationRevision==='number'&&Number.isInteger(sequence.configurationRevision)&&sequence.configurationRevision>0)return JSON.stringify([sequence.sequenceId,sequence.configurationRevision]);
 const runtime=new Set(['remainingSeconds','completedIterations','progressExposureCount','exposureCount','iterations']);
 return JSON.stringify((Array.isArray(sequence.nodes)?sequence.nodes:[]).map(n=>({id:n.id,parentId:n.parentId,type:n.type,kind:n.kind,name:n.name,enabled:n.enabled??(n.state==='disabled'?false:undefined),editableFields:n.editableFields,details:Object.fromEntries(Object.entries(n.details??{}).filter(([key])=>!runtime.has(key)))})));
}
export function sequenceStopState(running:unknown,ownedStart:boolean,externalStop:boolean):string {
 if(running!==true&&!ownedStart)return running===false?'Sequence is stopped.':'Waiting for N.I.N.A. acquisition state.';
 return externalStop||ownedStart?'':'Update the plugin to stop sequences started in N.I.N.A.';
}
