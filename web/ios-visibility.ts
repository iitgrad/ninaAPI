// Shared calculation bridge for the native iOS chart; no browser or network APIs.
import {visibility,siteDate,type Site} from './target-visibility';
Object.assign(globalThis,{starfrontVisibility:(input:{ra:number;dec:number;site:Site;now:number})=>JSON.stringify(visibility(input.ra,input.dec,input.site,siteDate(new Date(input.now-12*3600000),input.site.timeZone)))});
import {horizontalTarget,type PointingSite} from './mount-pointing';
Object.assign(globalThis,{starfrontHorizonTarget:(input:{site:PointingSite;azimuth:number;altitude:number;now:number})=>JSON.stringify(horizontalTarget(input.site,input.azimuth,input.altitude,new Date(input.now)))});
