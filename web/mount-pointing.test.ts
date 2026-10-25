import {test,expect} from 'bun:test';
import {Observer,VectorFromSphere,Rotation_EQJ_HOR,RotateVector,HorizonFromVector,Spherical} from 'astronomy-engine';
import {horizontalTarget,validPointingSite} from './mount-pointing';
const site={latitude:31.194,longitude:-99.616,elevation:600};
const date=new Date('2026-10-23T22:00:00Z');
test('SE/SW presets round trip through J2000 into the requested actual horizon',()=>{
 for(const az of [135,225]){
  const target=horizontalTarget(site,az,45,date);
  const vector=VectorFromSphere(new Spherical(target.dec,target.ra*15,1),date);
  const h=HorizonFromVector(RotateVector(Rotation_EQJ_HOR(date,new Observer(site.latitude,site.longitude,site.elevation)),vector),'normal');
  expect(h.lat).toBeCloseTo(45,8);expect(h.lon).toBeCloseTo(az,8);
 }
});
test('RA updates with time while each preset retains its sky direction',()=>{
 const first=horizontalTarget(site,135,45,date),later=horizontalTarget(site,135,45,new Date(+date+3600000));
 const hours=(later.ra-first.ra+24)%24;expect(hours).toBeGreaterThan(1);expect(hours).toBeLessThan(1.01);
 const laterDate=new Date(+date+3600000);
 const v=VectorFromSphere(new Spherical(later.dec,later.ra*15,1),laterDate);
 const h=HorizonFromVector(RotateVector(Rotation_EQJ_HOR(laterDate,new Observer(site.latitude,site.longitude,site.elevation)),v),'normal');
 expect(h.lat).toBeCloseTo(45,8);expect(h.lon).toBeCloseTo(135,8);
});
test('missing and invalid site data cannot generate an arbitrary slew',()=>{
 expect(validPointingSite(undefined)).toBe(false);expect(validPointingSite({...site,latitude:NaN})).toBe(false);expect(validPointingSite({...site,longitude:181})).toBe(false);
 expect(()=>horizontalTarget({...site,latitude:91},135,45,date)).toThrow();
 expect(()=>horizontalTarget(site,135,91,date)).toThrow();
});
