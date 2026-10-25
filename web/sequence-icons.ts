// Small, local SVG symbols follow the equipment/action vocabulary used by NINA.
const paths:Record<string,string>={
 camera:'<rect x="3" y="6" width="18" height="14" rx="2"/><circle cx="12" cy="13" r="4"/><path d="m7 6 2-3h6l2 3"/>',
 filter:'<circle cx="12" cy="12" r="9"/><circle cx="12" cy="7" r="1"/><circle cx="7" cy="14" r="1"/><circle cx="17" cy="14" r="1"/>',
 clock:'<circle cx="12" cy="12" r="9"/><path d="M12 6v6l4 3"/>',
 layers:'<path d="m12 3 10 6-10 6L2 9zM2 13l10 6 10-6M2 17l10 6 10-6"/>',
 sequential:'<path d="M5 4h14v4H5zM5 16h14v4H5zM12 8v8m-3-3 3 3 3-3"/>',
 parallel:'<path d="M12 2v4H5v12h7v4M12 6h7v12h-7M2 10h6M16 10h6"/>',
 target:'<circle cx="12" cy="12" r="7"/><path d="M12 1v6m0 10v6M1 12h6m10 0h6"/>',
 wave:'<path d="M1 12h5l3-8 5 16 3-8h6"/>',
 condition:'<path d="M12 3v5M5 19v-5l7-6 7 6v5"/><rect x="9" y="1" width="6" height="4" rx="1"/><rect x="2" y="18" width="6" height="4" rx="1"/><rect x="16" y="18" width="6" height="4" rx="1"/>',
 cool:'<path d="M12 2v20M3 7l18 10M3 17 21 7M9 4l3 3 3-3M9 20l3-3 3 3"/>',
 warm:'<path d="M9 14V5a3 3 0 0 1 6 0v9a5 5 0 1 1-6 0M12 8v10m5-11h4m-4 4h3"/>',
 plug:'<path d="M8 2v5m8-5v5M5 7h14v3a7 7 0 0 1-14 0zm7 10v5"/>',
 home:'<path d="m2 11 10-8 10 8M5 9v12h14V9M10 21v-7h4v7"/>',
 park:'<path d="M5 21V3h8a5 5 0 0 1 0 10H5M8 21h9"/>',
 mount:'<path d="m3 8 13-5 3 7-13 5zM10 14v4m-5 4 5-4 5 4M18 2l3 9"/>',
 focus:'<path d="M3 8V3h5m8 0h5v5m0 8v5h-5M8 21H3v-5"/><circle cx="12" cy="12" r="4"/>',
 rotate:'<path d="M20 9a8 8 0 1 0 0 6M20 3v6h-6"/>',
 bell:'<path d="M5 17h14l-2-4V8a5 5 0 0 0-10 0v5zm5 3a2 2 0 0 0 4 0M12 1v2"/>',
 shield:'<path d="m12 2 8 3v6c0 5-8 11-8 11S4 16 4 11V5zm-4 9 3 3 5-6"/>',
 repeat:'<path d="m17 2 4 4-4 4M3 11V6h18M7 22l-4-4 4-4m14-1v5H3"/>',
 power:'<path d="M12 2v10M6 5a9 9 0 1 0 12 0"/>',
 light:'<circle cx="12" cy="12" r="4"/><path d="M12 1v3m0 16v3M1 12h3m16 0h3M4 4l2 2m12 12 2 2M4 20l2-2M18 6l2-2"/>',
 script:'<path d="m8 6-6 6 6 6m8-12 6 6-6 6M14 3l-4 18"/>',
 instruction:'<rect x="4" y="3" width="16" height="18" rx="2"/><path d="M8 8h8M8 12h8M8 16h5"/>',
};
export function sequenceIcon(type:string,kind='instruction'):{key:string;svg:string}{
 let key=/Notification|Discord|Webhook|Email|Message/.test(type)?'bell':/Wait|TimeSpan|TimeCondition|WaitUntilSafe/.test(type)?'clock':/Cool/.test(type)?'cool':/Warm|Temperature/.test(type)?'warm':/Disconnect|Connect/.test(type)?'plug':/Guid|Dither/.test(type)?'target':/Auto[Ff]ocus|Focus|Backlash/.test(type)?'focus':/Home/.test(type)?'home':/Park/.test(type)?'park':/Slew|Tracking|Meridian|Coordinates|Telescope/.test(type)?'mount':/Filter/.test(type)?'filter':/Rotat/.test(type)?'rotate':/Exposure|Capture|Camera|Readout|Binning|Gain|Offset|USB/.test(type)?'camera':/Unsafe|Safety|Safe/.test(type)?'shield':/Loop|Repeat|Iterations/.test(type)?'repeat':/Light|Flat|Heater/.test(type)?'light':/Power|Shutdown/.test(type)?'power':/Script|Command/.test(type)?'script':type==='DeepSkyObjectContainer'?'target':type==='ParallelContainer'?'parallel':type==='SequentialContainer'?'sequential':kind==='container'?'layers':kind==='trigger'?'wave':kind==='condition'?'condition':'instruction';
 const stopped=/^(Stop|Disconnect|Disable)/.test(type);const svg=paths[key]+(stopped?'<path d="M3 21 21 3"/>':'');
 return {key:key+(stopped?'-off':''),svg};
}
