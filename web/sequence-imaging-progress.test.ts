import {test, expect} from 'bun:test';
import {sequenceImagingProgress} from './sequence-imaging-progress';
import type {SequenceNode} from './dashboard';
const node=(id:string,type:string,parentId:string|null=null,details:Record<string,unknown>={},kind='instruction',state='pending'):SequenceNode=>({id,type,parentId,details,kind,state,name:type,sourceStatus:''});
test('fixed nested repeat counts compare captures, not completed instruction statuses',()=>{
 const nodes=[node('root','SequenceRootContainer',null,{},'container'),node('outer','LoopCondition','root',{iterations:3},'condition'),node('group','SmartExposure','root',{},'container'),node('inner','LoopCondition','group',{iterations:4},'condition'),node('camera','TakeExposure','group',{exposureCount:7}),node('setup','RunAutofocus','root',{},'instruction','completed')];
 const p=sequenceImagingProgress(nodes);expect(p.captured).toBe(7);expect(p.planned).toBe(12);expect(p.percent).toBe(58);expect(p.label).toBe('7 / 12 images taken');
});
test('timed plans show actual cumulative counts from pending/skipped final iterations without a percentage',()=>{
 const nodes=[node('r','SequentialContainer',null,{},'container'),node('deadline','TimeCondition','r',{},'condition'),node('a','TakeExposure','r',{exposureCount:39},'instruction','skipped'),node('b','TakeExposure','r',{exposureCount:6})];
 expect(sequenceImagingProgress(nodes)).toMatchObject({captured:45,planned:null,percent:null,label:'45 images taken · time-limited plan'});
});
test('trigger branches and disabled exposures do not count; wrapper and inner capture are not doubled',()=>{
 const nodes=[node('group','SmartExposure',null,{exposureCount:20},'container'),node('capture','TakeExposure','group',{exposureCount:2}),node('t','AutofocusAfterExposures',null,{},'trigger'),node('conditional','TakeExposure','t',{exposureCount:99}),node('disabled','TakeExposure',null,{exposureCount:42},'instruction','disabled')];
 expect(sequenceImagingProgress(nodes)).toMatchObject({captured:2,planned:1,percent:100});
});
test('partial projection never claims a complete denominator',()=>{
 expect(sequenceImagingProgress([node('a','TakeExposure',null,{exposureCount:266})],true)).toMatchObject({captured:266,planned:null,percent:null});
 expect(sequenceImagingProgress([],true).label).toContain('shown portion');
});
test('missing counters, orphan ancestry and unknown loop conditions are not guessed',()=>{
 expect(sequenceImagingProgress([node('a','TakeExposure')]).captured).toBeNull();
 expect(sequenceImagingProgress([node('a','TakeExposure','missing',{exposureCount:3})]).planned).toBeNull();
 expect(sequenceImagingProgress([node('r','SequentialContainer',null,{},'container'),node('condition','SafetyMonitorCondition','r',{},'condition'),node('a','TakeExposure','r',{exposureCount:3})]).planned).toBeNull();
 expect(sequenceImagingProgress([node('g','SmartExposure',null,{},'container')]).captured).toBeNull();
});
test('empty sequences and resets show no fabricated captures',()=>{
 expect(sequenceImagingProgress([]).label).toBe('No imaging instructions');
 expect(sequenceImagingProgress([node('a','TakeExposure',null,{exposureCount:0})])).toMatchObject({captured:0,planned:1,percent:0});
});

test('multiple iteration limits on one group use the earliest stopping count',()=>{
 const nodes=[node('r','SequentialContainer',null,{},'container'),node('first','LoopCondition','r',{iterations:10},'condition'),node('second','LoopCondition','r',{iterations:4},'condition'),node('image','TakeExposure','r',{exposureCount:2})];
 expect(sequenceImagingProgress(nodes)).toMatchObject({captured:2,planned:4,percent:50});
});
