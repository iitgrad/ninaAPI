import {test,expect} from 'bun:test';
import {sequenceCommandIdentity,sequenceStopState} from './sequence-command-state';
test('confirmations track native configuration identity rather than countdown/progress',()=>{
 const before={sequenceId:'night',configurationRevision:3,nodes:[{id:'loop',details:{remainingSeconds:3600}}]};
 expect(sequenceCommandIdentity(before)).toBe(sequenceCommandIdentity({...before,nodes:[{id:'loop',details:{remainingSeconds:3590}}]}));
 expect(sequenceCommandIdentity(before)).not.toBe(sequenceCommandIdentity({...before,configurationRevision:4}));
 expect(sequenceCommandIdentity(before)).not.toBe(sequenceCommandIdentity({...before,sequenceId:'replacement'}));
});
test('older host fallback distinguishes settings edits from runtime updates',()=>{
 const n={id:'wait',type:'WaitForTime',details:{hours:21,remainingSeconds:60},editableFields:[{key:'Hours',value:21}]};
 expect(sequenceCommandIdentity({nodes:[n]})).toBe(sequenceCommandIdentity({nodes:[{...n,state:'waiting',details:{hours:21,remainingSeconds:59}}]}));
 expect(sequenceCommandIdentity({nodes:[n]})).not.toBe(sequenceCommandIdentity({nodes:[{...n,editableFields:[{key:'Hours',value:22}]}]}));
 expect(sequenceCommandIdentity({nodes:[n]})).not.toBe(sequenceCommandIdentity({nodes:[]}));
 expect(sequenceCommandIdentity({nodes:[n]})).not.toBe(sequenceCommandIdentity({nodes:[{...n,state:'disabled'}]}));
});
test('Stop is available for real running/pending starts, never merely a supported command',()=>{
 expect(sequenceStopState(false,false,true)).toBe('Sequence is stopped.');
 expect(sequenceStopState(true,false,true)).toBe('');
 expect(sequenceStopState(false,true,true)).toBe('');
 expect(sequenceStopState(undefined,false,true)).toContain('Waiting');
 expect(sequenceStopState(true,false,false)).toContain('Update');
 expect(sequenceStopState(true,true,false)).toBe('');
});
