import {test,expect} from 'bun:test';
import {sequenceIcon} from './sequence-icons';
test('instruction symbols distinguish action families and stop variants',()=>{
 for(const [type,key] of Object.entries({StopGuiding:'target-off',StartGuiding:'target',WaitUntilSafe:'clock',SendNotificationInstruction:'bell',CoolCamera:'cool',WarmCamera:'warm',FindHome:'home',SetTracking:'mount',ConnectEquipment:'plug',DisconnectEquipment:'plug-off',ParallelContainer:'parallel',SequentialContainer:'sequential'}))expect(sequenceIcon(type).key).toBe(key);
 expect(sequenceIcon('UnknownPluginStep').key).toBe('instruction');
 expect(sequenceIcon('StopGuiding').svg).toContain('M3 21 21 3');
});
