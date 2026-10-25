import {Observer,Spherical,VectorFromHorizon,Rotation_HOR_EQJ,RotateVector,EquatorFromVector} from 'astronomy-engine';
export type PointingSite={latitude:number;longitude:number;elevation:number};
export function validPointingSite(site:unknown):site is PointingSite{
 if(!site||typeof site!=='object')return false;
 const s=site as PointingSite;
 return [s.latitude,s.longitude,s.elevation].every(n=>typeof n==='number'&&Number.isFinite(n))&&Math.abs(s.latitude)<=90&&Math.abs(s.longitude)<=180;
}
// Return J2000 coordinates, matching the plugin's mount/slew contract.
export function horizontalTarget(site:PointingSite,azimuth:number,altitude:number,when=new Date()){
 if(!validPointingSite(site)||!Number.isFinite(when.getTime())||!Number.isFinite(azimuth)||azimuth<0||azimuth>=360||!Number.isFinite(altitude)||Math.abs(altitude)>90)throw Error('Valid observing site, time and pointing coordinates required.');
 const observer=new Observer(site.latitude,site.longitude,site.elevation);
 const horizon=VectorFromHorizon(new Spherical(altitude,azimuth,1),when,'normal');
 const eq=EquatorFromVector(RotateVector(Rotation_HOR_EQJ(when,observer),horizon));
 return {ra:eq.ra,dec:eq.dec};
}
