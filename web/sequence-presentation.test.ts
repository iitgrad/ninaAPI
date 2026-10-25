import {test,expect} from 'bun:test';
import {sequenceName,sequenceDetail,type VisualNode} from './sequence-presentation';
const node=(type:string,details:Record<string,unknown>={},fields:VisualNode['editableFields']=[]):VisualNode=>({id:'test',parentId:null,kind:'instruction',type,name:type.replace(/([a-z])([A-Z])/g,'$1 $2'),state:'pending',sourceStatus:'CREATED',details,editableFields:fields});
test('equipment selection appears in the primary label, including docked rows',()=>{expect(sequenceName(node('ConnectEquipment',{equipment:'Camera'}))).toBe('Connect camera');expect(sequenceName(node('DisconnectEquipment',{equipment:'Filter Wheel'}))).toBe('Disconnect filter wheel');expect(sequenceDetail(node('ConnectEquipment'))).toBe('Equipment not reported');});
test('custom names retain identity and equipment context',()=>{const n=node('ConnectEquipment',{equipment:'Mount'});n.name='Prepare telescope';expect(sequenceName(n)).toBe('Prepare telescope · Mount');});
test('exposure settings are labeled and fractional seconds are preserved',()=>{expect(sequenceDetail(node('TakeExposure',{exposureTime:.025,gain:100,offset:50,binX:2,binY:2,imageType:'LIGHT',exposureCount:0}))).toBe('Exposure 0.025 s · Gain 100 · Offset 50 · LIGHT · Bin 2×2');});
test('completed exposure counters never become planned frame counts',()=>{expect(sequenceDetail(node('TakeExposure',{exposureCount:17}))).toBe('');});
test('cooling target and ramp show units, including Powerups expressions',()=>{expect(sequenceDetail(node('CoolCamera',{temperature:-10,duration:5}))).toBe('Target -10°C · Ramp 5 min');expect(sequenceDetail(node('CoolCamera',{temperatureExpression:'FocuserTemp - 10',durationExpression:'5'}))).toBe('Δ -10°C from focuser · Target pending sensor reading · Minimum 5 min');});
test('typed booleans and choices use meaningful labels rather than raw values',()=>{expect(sequenceDetail(node('DewHeater',{},[{key:'OnOff',label:'Heater enabled',type:'boolean',value:false}]))).toBe('Heater enabled off');expect(sequenceDetail(node('SwitchFilter',{},[{key:'Filter',label:'Filter',type:'choice',value:4,choices:[{value:4,label:'Ha'}]}]))).toBe('Filter Ha');});
test('timed waits distinguish wall clock from twilight offsets',()=>{expect(sequenceDetail(node('WaitForTime',{hours:21,minutes:5,seconds:0,timeReference:'Time',timeReferenceFixed:true}))).toBe('At 21:05:00');expect(sequenceDetail(node('TimeCondition',{timeReference:'Astronomical dawn',minutesOffset:-30}))).toBe('Astronomical dawn · -30 min');});
test('zero settings remain visible without asserting execution success',()=>{expect(sequenceDetail(node('LoopCondition',{iterations:0}))).toBe('Exit after 0 repeats');expect(sequenceDetail({...node('DitherAfterExposures',{},[{key:'AfterExposures',label:'Interval',type:'integer',value:0}]),kind:'trigger'})).toBe('Every 0 frames');});
test('calibration and tracking settings preserve explicit false/enum values',()=>{expect(sequenceDetail(node('StartGuiding',{forceCalibration:false}))).toBe('Force calibration off');expect(sequenceDetail(node('SetTracking',{trackingMode:'SIDEREAL'}))).toBe('Tracking SIDEREAL');});

test('live cooling estimates use only supplied sensor context and preserve delta',()=>{const n=node('CoolCamera',{temperatureExpression:'FocuserTemperature - 30',durationExpression:'0'});expect(sequenceDetail(n,undefined,{focuserTemperature:22})).toBe('Δ -30°C from focuser · Estimated target -8°C · Minimum 0 min');expect(sequenceDetail(n)).toContain('Target pending sensor reading');});
test('tracking off and exact notification content are clear',()=>{expect(sequenceDetail(node('SetTracking',{trackingMode:'Stopped'}))).toBe('Tracking Off');expect(sequenceDetail(node('SendToDiscordWebhook',{message:'Waiting for roof'}))).toBe('Waiting for roof');});

test('native notification text and explicit photo state stay visible',()=>{
 expect(sequenceDetail(node('SendNotificationInstruction',{message:'Roof check complete',attachPreview:true}))).toBe('Roof check complete · Attach photo: on');
 expect(sequenceDetail(node('SendNotificationInstruction',{message:'Prepare next target',attachPreview:false}))).toBe('Prepare next target · Attach photo: off');
 expect(sequenceDetail(node('SendNotificationInstruction',{message:'Update'}))).toContain('not reported');
});

test('safety triggers use the roof vocabulary of this rig',()=>{
 expect(sequenceName(node('WhenUnsafe'))).toBe('When roof closes');
 expect(sequenceName(node('WhenSafe'))).toBe('When roof opens');
});
