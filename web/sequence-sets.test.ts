import {test,expect} from 'bun:test';
import {imagingSets} from './sequence-sets';
import type {SequenceNode} from './dashboard';
const n=(id:string,type:string,parentId:string|null,kind='container',state='pending',details:Record<string,unknown>={}):SequenceNode=>({id,name:id,type,parentId,kind,state,details,sourceStatus:''});
test('summarizes current target imaging groups using actual counters and excludes triggers',()=>{
 const nodes=[n('root','SequenceRootContainer',null),n('target','DeepSkyObjectContainer','root','container','running'),n('ha','SequentialContainer','target','container','running'),n('loop','LoopCondition','ha','condition','pending',{iterations:4}),n('image','TakeExposure','ha','instruction','running',{exposureCount:2}),n('o','SequentialContainer','target'),n('oimage','TakeExposure','o','instruction','pending',{exposureCount:0}),n('trigger','Unsafe','target','trigger'),n('recovery','TakeExposure','trigger','instruction','pending',{exposureCount:50})];
 expect(imagingSets(nodes,nodes[4],false)).toMatchObject([{id:'ha',active:true,captured:2,planned:4},{id:'o',active:false,captured:0,planned:1}]);
 expect(imagingSets(nodes,nodes[4],true)[0].planned).toBeNull();
 expect(imagingSets([...nodes,n('deadline','TimeCondition','target','condition')],nodes[4],false)[0].planned).toBeNull();
 expect(imagingSets(nodes,undefined,false)).toEqual([]);
});
