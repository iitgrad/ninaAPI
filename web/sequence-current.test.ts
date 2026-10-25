import {test,expect} from 'bun:test';
import {currentSequenceNode,sequenceAncestors} from './sequence-current';
import type {SequenceNode} from './dashboard';
const n=(id:string,parentId:string|null,kind='container',state='pending'):SequenceNode=>({id,parentId,kind,state,name:id,type:id,sourceStatus:'',details:{}});
test('current step chooses a waiting child over its running containers',()=>{
 const nodes=[n('root',null,'container','running'),n('target','root','container','running'),n('wait','target','instruction','waiting')];
 expect(currentSequenceNode(nodes,['wait'])?.id).toBe('wait');
 expect(sequenceAncestors(nodes,'wait')).toEqual(['target','root']);
});
test('old hosts and partial projections fall back to active container, never another pending step',()=>{
 expect(currentSequenceNode([n('target',null,'container','running'),n('later','target','instruction')],undefined)?.id).toBe('target');
 expect(currentSequenceNode([n('later',null,'instruction')],[])).toBeUndefined();
});
test('cycles cannot grow ancestor traversal',()=>expect(sequenceAncestors([n('a','b'),n('b','a')],'a')).toEqual(['b','a']));
