import { ThermalHistory, thermalPaths } from "./camera-thermal";
import {defaultMountCamera} from "./mount-view";
import {updateMountView,disposeMountView} from "./mount-renderer";
import {horizontalTarget,validPointingSite} from "./mount-pointing";
import { format, numeric, type Topic } from "./dashboard";
type Data = Record<string, any>;
type API = {
  states: () => Record<string, Topic>;
  session: () => string;
  live: () => boolean;
  reason: (device: string, action: string) => string;
  run: (device: string, action: string, parameters?: Data) => void;
};
const devices: Record<string, [string, string]> = {
  camera: ["Camera", "◉"],
  mount: ["Mount", "⌖"],
  filterWheel: ["Filter wheel", "✺"],
  focuser: ["Focuser", "↕"],
  rotator: ["Rotator", "↻"],
  guider: ["Guider", "✧"],
  weather: ["Weather", "☁"],
  safetyMonitor: ["Roof safety", "⌂"],
  dome: ["Dome", "◠"],
  flatDevice: ["Flat panel", "▤"],
  switch: ["Power", "ϟ"],
};
const primary = ["camera", "mount", "filterWheel", "focuser", "rotator", "guider"];
const topicName = (device: string) =>
  device === "safetyMonitor" ? "safety" : device;
const safe = (v: unknown) =>
  String(v ?? "").replace(
    /[&<>"']/g,
    (c) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[
        c
      ]!,
  );
const el = <T extends HTMLElement = HTMLElement>(
  root: HTMLElement,
  id: string,
) => root.querySelector<T>(`[data-field="${id}"]`)!;
export function mountDevicePanels(api: API) {
  let storage: Storage | undefined;
  try { storage = window.localStorage; } catch { /* Browser storage can be disabled. */ }
  const history = new ThermalHistory(storage);
  let mountCamera={...defaultMountCamera};
  let selected = "",
    context = "",
    chartStamp = "",
    panelShape = "";
  let invalidated = false;
  let lastStates: Record<string, Topic> | null = null,
    lastLive = false,
    lastSelected = "",
    lastReason = "";
  const dialog = document.createElement("dialog");
  dialog.className = "device-dialog parameter-dialog";
  dialog.setAttribute("aria-labelledby", "device-dialog-title");
  dialog.innerHTML =
    '<button type="button" class="mount-emergency-stop" data-field="mount-stop" hidden>■ STOP MOUNT</button><header class="device-dialog-header"><div><span class="device-kicker">EQUIPMENT</span><h2 id="device-dialog-title"></h2></div><button class="icon-button" data-close aria-label="Close equipment controls">×</button></header><div class="device-identity"><span data-field="device-name"></span><div class="device-connection-controls"><span class="device-connection" data-field="connection"></span><button data-field="connection-action"></button></div></div><div class="device-panel-body"></div><footer><span data-field="outcome" role="status"></span></footer><p class="device-reason" data-field="reason" role="status"></p>';
  document.body.append(dialog);
  const mountStop=el<HTMLButtonElement>(dialog,"mount-stop");
  mountStop.setAttribute("aria-label","Stop mount slew");
  mountStop.onclick=()=>{if(!invalidated&&selected==="mount")api.run("mount","stop");};
  const mountCard=document.querySelector<HTMLElement>("#equipment-mount")!;
  const cardStop=document.createElement("button");cardStop.type="button";cardStop.className="mount-emergency-stop mount-card-stop";
  cardStop.textContent="■ STOP MOUNT";cardStop.setAttribute("aria-label","Stop mount slew");cardStop.hidden=true;
  cardStop.onclick=e=>{e.stopPropagation();api.run("mount","stop");};mountCard.append(cardStop);
  dialog.querySelector<HTMLElement>("[data-close]")!.onclick = () =>
    dialog.close();
  dialog.addEventListener("click", (e) => {
    if (e.target === dialog) {
      const r = dialog.getBoundingClientRect();
      if (
        e.clientX < r.left ||
        e.clientX > r.right ||
        e.clientY < r.top ||
        e.clientY > r.bottom
      )
        dialog.close();
    }
  });
  const body = dialog.querySelector<HTMLElement>(".device-panel-body")!;
  dialog.addEventListener("close",()=>disposeMountView(body.querySelector<HTMLElement>('[data-field=mount-view]')));
  document.querySelector(".environment-strip")?.remove();
  for (const [key, [title, icon]] of Object.entries(devices)) {
    if (primary.includes(key)) {
      const card = document.getElementById("equipment-" + key)!;
      card.querySelector<HTMLElement>(".equipment-menu")!.onclick = () =>
        open(key);
      const heading = card.querySelector<HTMLElement>(".equipment-title")!;
      heading.tabIndex = 0;
      heading.role = "button";
      heading.setAttribute("aria-label", title + " controls");
      heading.onclick = (e) => open(key, e.detail === 0);
      heading.onkeydown = (e) => {
        if (e.key === "Enter" || e.key === " ") {
          e.preventDefault();
          open(key, true);
        }
      };
      card.addEventListener("click", (e) => {
        if (
          e.target instanceof Element &&
          !e.target.closest("button,[role=button]")
        )
          open(key);
      });
    }
  }
  const state = (d = selected) => api.states()[topicName(d)]?.value ?? {};
  const connect = () => {
    if (!invalidated)
      api.run(selected, state().connected === true ? "disconnect" : "connect");
  };
  el(dialog, "connection-action").onclick = connect;
  function metric(label: string, key: string, unit = "") {
    return `<div class="device-metric"><small>${label}</small><strong data-reading="${key}" data-unit="${unit}">—</strong></div>`;
  }
  function button(label: string, action: string, params: Data = {}, cls = "") {
    const b = document.createElement("button");
    b.textContent = label;
    b.className = "panel-action " + cls;
    b.dataset.action = action;
    b.onclick = () => api.run(selected, action, params);
    return b;
  }
  function actions(
    rows: [string, string, Data?][],
    target: HTMLElement = body,
  ) {
    const box = document.createElement("div");
    box.className = "device-action-row";
    for (const [label, action, params] of rows)
      box.append(button(label, action, params));
    target.append(box);
  }
  function slider(
    label: string,
    min: number,
    max: number,
    value: number,
    unit: string,
    applyLabel: string,
    action: string,
    param: string,
    step = 1,
  ) {
    const box = document.createElement("section");
    box.className = "device-slider";
    box.innerHTML = `<label>${label}<output>${format(value, step < 1 ? 1 : 0, unit)}</output></label><input aria-label="${safe(label)}" type="range" min="${min}" max="${max}" value="${Math.min(max, Math.max(min, value))}" step="${step}"><div class="range-ends"><span>${format(min, 0, unit)}</span><span>${format(max, 0, unit)}</span></div>`;
    const input = box.querySelector("input")!,
      out = box.querySelector("output")!;
    input.oninput = () => {
      out.value = format(Number(input.value), step < 1 ? 1 : 0, unit);
    };
    const apply = button(applyLabel, action);
    apply.onclick = () =>
      api.run(selected, action, {
        [param]: Number(input.value),
        ...(selected === "camera" && action === "cool"
          ? { rampSeconds: 60 }
          : {}),
      });
    box.append(apply);
    body.append(box);
    return box;
  }
  function open(key: string, keyboard = false) {
    // Pointer opening must not leave focus on the background card when closed.
    // Keyboard opening preserves the browser's normal focus restoration.
    if (!keyboard && !dialog.open && document.activeElement instanceof HTMLElement)
      document.activeElement.blur();
    selected = key;
    lastStates = null;
    invalidated = false;
    panelShape = JSON.stringify(
      key === "switch"
        ? ((state(key).items ?? []) as Data[]).map((x: Data) => [
            x.id,
            x.name,
            x.minimum,
            x.maximum,
            x.stepSize,
          ])
        : key === "filterWheel"
          ? api.states().administration?.value.filters
          : [],
    );
    context = api.session() + "|" + choice()?.id;
    chartStamp = "";
    document.getElementById("device-dialog-title")!.textContent =
      devices[key][0];
    disposeMountView(body.querySelector<HTMLElement>('[data-field=mount-view]'));
    body.replaceChildren();
    const s = state();
    if (key === "camera") {
      body.innerHTML = `<div class="device-metrics">${metric("Sensor", "temperature", "°C")}${metric("Cooler", "coolerPower", "%")}${metric("Target", "temperatureSetPoint", "°C")}</div><div class="thermal-legend"><span>● Temperature</span><span>┄ Cooler power</span></div><div data-field="thermal-chart" class="thermal-chart"></div>`;
      const box = slider(
        "Target temperature",
        -30,
        10,
        s.connected === true && numeric(s.temperatureSetPoint)
          ? Math.max(-30, Math.min(10, s.temperatureSetPoint))
          : -10,
        "°C",
        "❄ Cool",
        "cool",
        "temperature",
      );
      box.append(button("☀ Warm", "warm", { rampSeconds: 60 }, "warm"));
      const capture = document.createElement("section");
      capture.className = "capture-controls";
      capture.innerHTML =
        '<h3>Single capture</h3><label>Exposure <input aria-label="Exposure seconds" type="number" min="0.001" max="3600" step="any" value="3"> s</label><label class="save-choice"><input type="checkbox" aria-label="Save captured image"> Save</label>';
      const b = button("◉ Capture", "capture");
      b.onclick = () => {
        const i =
          capture.querySelector<HTMLInputElement>("input[type=number]")!;
        if (i.reportValidity())
          api.run(key, "capture", {
            exposure: Number(i.value),
            count: 1,
            save: capture.querySelector<HTMLInputElement>(
              "input[type=checkbox]",
            )!.checked,
            imageType: "LIGHT",
          });
      };
      capture.append(b);
      capture.append(button("■ Abort", "abort"));
      body.append(capture);
    } else if (key === "filterWheel") {
      const wheel = document.createElement("div");
      wheel.className = "filter-picker";
      const filters = (api.states().administration?.value.filters ??
        []) as Data[];
      for (const f of filters) {
        const b = button(String(f.name), "select", { position: f.position });
        b.dataset.position = String(f.position);
        b.style.setProperty(
          "--filter-color",
          (
            {
              R: "#e87676",
              G: "#75c99e",
              B: "#78aefa",
              H: "#e89091",
              Ha: "#e89091",
              S: "#eab674",
              SII: "#eab674",
              O: "#6ed0d6",
              OIII: "#6ed0d6",
            } as Data
          )[f.name] ?? "#c8d5e4",
        );
        wheel.append(b);
      }
      body.append(wheel);
      if (!filters.length)
        body.append(document.createTextNode("Filter list unavailable"));
    } else if (key === "focuser") {
      body.innerHTML = `<div class="device-metrics">${metric("Position", "position")}${metric("Temperature", "temperature", "°C")}</div>`;
      slider(
        "Focus position",
        0,
        numeric(s.maxStep) && s.maxStep > 0
          ? Math.min(1000000, s.maxStep)
          : 100000,
        Number(s.position) || 0,
        "",
        "Move",
        "move",
        "position",
      );
      actions([
        ["✧ Autofocus", "autofocus"],
        ["■ Stop", "stop"],
      ]);
      const focus = document.createElement("div");
      focus.dataset.field = "focus-graph";
      focus.className = "device-guide";
      body.append(focus);
    } else if (key === "rotator") {
      body.innerHTML =
        '<div data-field="orientation" class="orientation"></div>';
      slider(
        "Position angle",
        0,
        360,
        Number(s.position) || 0,
        "°",
        "Rotate",
        "move",
        "angle",
      );
      actions([["■ Stop", "stop"]]);
    } else if (key === "mount") {
      body.innerHTML = `<div class="mount-orbit-tools"><span>Drag to rotate</span><button type="button" aria-label="Reset telescope view">↺ Reset view</button></div><div data-field="mount-view" class="mount-control-view" tabindex="0" role="group" aria-label="Telescope 3D view" aria-description="Drag to rotate the view. Arrow keys rotate. This does not move the telescope."></div><div class="device-metrics">${metric("RA", "rightAscension", " h")}${metric("Dec", "declination", "°")}${metric("Altitude", "altitude", "°")}${metric("Azimuth", "azimuth", "°")}</div>`;
      const view=body.querySelector<HTMLElement>('[data-field=mount-view]')!;
      const redraw=()=>{updateMountView(view,state('mount'),api.live(),false,mountCamera);};
      const orbit=(az:number,el:number)=>{mountCamera={azimuth:(az%360+360)%360,elevation:Math.max(5,Math.min(80,el))};redraw();};
      body.querySelector<HTMLButtonElement>('[aria-label="Reset telescope view"]')!.onclick=()=>{mountCamera={...defaultMountCamera};redraw();};
      let drag:{x:number;y:number;az:number;el:number}|null=null;
      view.onpointerdown=e=>{if(e.button!==0)return;drag={x:e.clientX,y:e.clientY,az:mountCamera.azimuth,el:mountCamera.elevation};view.setPointerCapture(e.pointerId);};
      view.onpointermove=e=>{if(drag)orbit(drag.az+(e.clientX-drag.x)*.5,drag.el-(e.clientY-drag.y)*.3);};
      view.onpointerup=view.onpointercancel=()=>{drag=null;};
      view.onkeydown=e=>{const delta=({ArrowLeft:[-10,0],ArrowRight:[10,0],ArrowUp:[0,5],ArrowDown:[0,-5]} as Record<string,number[]>)[e.key];if(delta){e.preventDefault();orbit(mountCamera.azimuth+delta[0],mountCamera.elevation+delta[1]);}};
      actions([
        ["▶ Track", "tracking", { enabled: true }],
        ["Ⅱ Track off", "tracking", { enabled: false }],
        ["⌂ Home", "home"],
      ]);
      const form = document.createElement("form");
      form.className = "coordinate-controls";
      form.innerHTML =
        '<label>RA · h<input type="number" aria-label="Target right ascension hours" min="0" max="24" step="any" required></label><label>Dec · °<input type="number" aria-label="Target declination degrees" min="-90" max="90" step="any" required></label><button class="panel-action" data-action="slew">⌖ Slew</button>';
      form.onsubmit = (e) => {
        e.preventDefault();
        if (form.reportValidity())
          api.run(key, "slew", {
            ra: Number(form.querySelectorAll("input")[0].value),
            dec: Number(form.querySelectorAll("input")[1].value),
          });
      };
      body.append(form);
      const inputs=form.querySelectorAll('input');
      inputs[0].value=numeric(s.rightAscension)?s.rightAscension.toFixed(5):'';
      inputs[1].value=numeric(s.declination)?s.declination.toFixed(5):'';
      const presets=document.createElement('div');presets.className='mount-sky-presets';
      for(const [name,azimuth] of [['SE',135],['SW',225]] as const){
        const preset=button(`Slew ${name} · 45° up`,'slew');preset.dataset.horizon=String(azimuth);
        preset.onclick=()=>{
          const site=api.states().administration?.value.site;
          if(!validPointingSite(site))return;
          const target=horizontalTarget(site,azimuth,45);
          inputs[0].value=target.ra.toFixed(5);inputs[1].value=target.dec.toFixed(5);
          api.run('mount','slew',target);
        };
        const wrapper=document.createElement('div');wrapper.className='mount-sky-preset';
        const coordinates=document.createElement('small');coordinates.dataset.presetCoordinates=String(azimuth);
        wrapper.append(preset,coordinates);presets.append(wrapper);
      }
      body.append(presets);
    } else if (key === "guider") {
      body.innerHTML =
        '<div data-field="guide-graph" class="device-guide"></div>';
      actions([
        ["▶ Guide", "start"],
        ["■ Stop", "stop"],
      ]);
    } else if (key === "weather") {
      body.innerHTML = `<div class="device-metrics weather-metrics">${metric("Temperature", "temperature", "°C")}${metric("Humidity", "humidity", "%")}${metric("Cloud", "cloudCover", "%")}${metric("Wind", "windSpeed")}${metric("Dew point", "dewPoint", "°C")}${metric("Rain", "rainRate")}</div>`;
    } else if (key === "safetyMonitor") {
      body.innerHTML = '<div class="roof-graphic" data-field="roof"></div>';
    } else if (key === "dome") {
      body.innerHTML =
        '<div data-field="orientation" class="orientation"></div>';
      slider(
        "Dome azimuth",
        0,
        360,
        Number(s.azimuth) || 0,
        "°",
        "Slew",
        "slew",
        "azimuth",
      );
      actions([
        ["Open", "open"],
        ["Close", "close"],
        ["Follow", "follow", { enabled: true }],
        ["Follow off", "follow", { enabled: false }],
        ["Park", "park"],
        ["Home", "home"],
        ["■ Stop", "stop"],
      ]);
    } else if (key === "flatDevice") {
      body.innerHTML = `<div class="device-metrics">${metric("Brightness", "brightness")}${metric("Light", "lightOn")}</div>`;
      slider(
        "Brightness",
        Number(s.minBrightness) || 0,
        Math.min(10000, Number(s.maxBrightness) || 255),
        Number(s.brightness) || 0,
        "",
        "Apply",
        "brightness",
        "value",
      );
      actions([
        ["☀ Light on", "light", { enabled: true }],
        ["Light off", "light", { enabled: false }],
        ["Open cover", "cover", { closed: false }],
        ["Close cover", "cover", { closed: true }],
      ]);
    } else if (key === "switch") {
      for (const [index, item] of ((s.items ?? []) as Data[]).entries()) {
        const box = slider(
          String(item.name ?? "Channel " + (index + 1)),
          Number(item.minimum) || 0,
          Number(item.maximum) || 1,
          Number(item.value) || 0,
          "",
          "Set",
          "set",
          "value",
          Number(item.stepSize) || 1,
        );
        box.querySelector<HTMLButtonElement>(".panel-action")!.onclick = () =>
          api.run(key, "set", {
            index,
            value: Number(box.querySelector("input")!.value),
          });
      }
      if (!body.children.length)
        body.append(document.createTextNode("No writable channels reported"));
    }
    update();
    if (!dialog.open) dialog.showModal();
  }
  function choice() {
    return ((api.states().devices?.value.items ?? []) as Data[]).find(
      (d) => d.device === selected && d.selected,
    );
  }
  function thermal(container: HTMLElement, mini = false) {
    const a = history.samples,
      valid = a.filter((s) => s.temperature !== null),
      lo = Math.min(-15, ...valid.map((s) => s.temperature!)) - 2,
      hi = Math.max(10, ...valid.map((s) => s.temperature!)) + 2;
    const start = a[0]?.at ?? Date.now() - 60000,
      end = Math.max(start + 60000, a.at(-1)?.at ?? Date.now()),
      x = (t: number) => 40 + ((t - start) / (end - start)) * 420,
      y = (n: number) => 142 - ((n - lo) / (hi - lo)) * 120,
      p = (n: number) => 142 - n * 1.2;
    const labels = mini
      ? ""
      : Array.from({ length: 5 }, (_, i) => {
          const v = hi - (i * (hi - lo)) / 4,
            yy = 22 + i * 30;
          return `<line x1="40" x2="460" y1="${yy}" y2="${yy}"/><text x="33" y="${yy + 4}" text-anchor="end">${v.toFixed(0)}°</text><text x="466" y="${yy + 4}">${100 - i * 25}%</text>`;
        }).join("");
    const clocks = mini
      ? ""
      : `<text x="40" y="169">${new Date(start).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}</text><text x="460" y="169" text-anchor="end">${new Date(end).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}</text><text x="40" y="12">°C</text><text x="460" y="12" text-anchor="end">%</text>`;
    container.innerHTML = `<svg viewBox="0 0 510 180" role="img" aria-label="Camera temperature and cooler power over time">${labels}<path class="thermal-temperature" d="${thermalPaths(a, "temperature", x, y)}"/><path class="thermal-power" d="${thermalPaths(a, "power", x, p)}"/>${a.length === 1 && a[0].temperature !== null ? `<circle cx="${x(a[0].at)}" cy="${y(a[0].temperature)}" r="3"/>` : ""}${clocks}${valid.length ? "" : '<text x="255" y="90" text-anchor="middle">Waiting for temperature telemetry</text>'}</svg>`;
  }
  function update() {
    const states = api.states(),
      liveNow = api.live(),
      reasonNow = selected
        ? api.reason(
            selected,
            state().connected === true ? "disconnect" : "connect",
          )
        : "";
    const mount=states.mount?.value??{},slewing=liveNow&&mount.connected===true&&mount.slewing===true;
    cardStop.hidden=!slewing;mountStop.hidden=!(slewing&&selected==="mount");
    const stopReason=api.reason("mount","stop");
    cardStop.disabled=!!stopReason;cardStop.title=stopReason;
    mountStop.disabled=invalidated||!!stopReason;mountStop.title=invalidated?"Device changed. Reopen controls.":stopReason;
    if (
      states === lastStates &&
      liveNow === lastLive &&
      selected === lastSelected &&
      reasonNow === lastReason
    )
      return;
    lastStates = states;
    lastLive = liveNow;
    lastSelected = selected;
    lastReason = reasonNow;
    const camera = states.camera?.value ?? {},
      id =
        ((states.devices?.value.items ?? []) as Data[]).find(
          (d) => d.device === "camera" && d.selected,
        )?.id ??
        camera.name ??
        "";
    if (api.session() && id) history.receive(
      JSON.stringify([api.session(), (states.administration?.value.profile as Data | undefined)?.id ?? "", id]),
      Date.parse(states.diagnostics?.observedAt ?? ""),
      api.live() && camera.connected === true,
      camera.temperature,
      camera.coolerPower,
    );
    const mini = document.querySelector<HTMLElement>(
      "#equipment-camera .equipment-values",
    );
    if (mini) {
      mini.classList.add("camera-thermal-mini");
      thermal(mini, true);
      const caption = document.createElement("div");
      caption.className = "thermal-mini-caption";
      caption.textContent =
        api.live() && camera.connected === true
          ? `${format(camera.temperature, 1, "°C")} · ${format(camera.coolerPower, 0, "%")}`
          : "Camera offline";
      mini.append(caption);
    }
    const mountMini=document.querySelector<HTMLElement>('#equipment-mount .equipment-values');
    if(mountMini){mountMini.classList.add('mount-mini');updateMountView(mountMini,states.mount?.value??{},liveNow,true);}
    if (!selected) return;
    const s = state(),
      live = api.live(),
      connected = live && s.connected === true;
    const shape = JSON.stringify(
      selected === "switch"
        ? ((s.items ?? []) as Data[]).map((x: Data) => [
            x.id,
            x.name,
            x.minimum,
            x.maximum,
            x.stepSize,
          ])
        : selected === "filterWheel"
          ? states.administration?.value.filters
          : [],
    );
    if (
      dialog.open &&
      context === api.session() + "|" + choice()?.id &&
      shape !== panelShape
    ) {
      open(selected);
      return;
    }
    el(dialog, "device-name").textContent = String(
      choice()?.name ?? s.name ?? "No device selected",
    );
    el(dialog, "connection").textContent = !live
      ? "Offline"
      : s.connected === true
        ? "Connected"
        : s.connected === false
          ? "Disconnected"
          : "Not reported";
    el(dialog, "connection").classList.toggle("is-connected", connected);
    const connection = el<HTMLButtonElement>(dialog, "connection-action"),
      action = s.connected === true ? "disconnect" : "connect";
    connection.hidden = selected === "safetyMonitor";
    connection.textContent = action === "connect" ? "Connect" : "Disconnect";
    connection.disabled = invalidated || !!api.reason(selected, action);
    connection.classList.toggle("is-connect", action === "connect");
    connection.title = invalidated ? "Device changed. Reopen controls." : api.reason(selected, action);
    for (const node of body.querySelectorAll<HTMLElement>("[data-reading]"))
      node.textContent = connected
        ? format(
            s[node.dataset.reading!],
            node.dataset.reading === "rightAscension" ? 4 : node.dataset.reading === "declination" ? 3 : ["coolerPower", "position", "brightness"].includes(
              node.dataset.reading!,
            )
              ? 0
              : 1,
            node.dataset.unit,
          )
        : "—";
    for (const b of body.querySelectorAll<HTMLButtonElement>("[data-action]")) {
      const a = b.dataset.action!;
      let why = invalidated
        ? "Device changed. Reopen controls."
        : api.reason(selected, a);
      if(b.dataset.horizon && !validPointingSite(states.administration?.value.site))
        why = "Observing-site coordinates required for sky-direction presets.";
      if (
        !((states.commands?.value.items ?? []) as Data[]).some(
          (c) => c.device === selected && c.action === a,
        )
      )
        why = "Action unavailable on this host";
      if (
        selected === "camera" &&
        ["cool", "warm"].includes(a) &&
        s.canSetTemperature !== true
      )
        why = "Temperature control unavailable";
      if (
        selected === "dome" &&
        ["open", "close"].includes(a) &&
        s.canSetShutter !== true
      )
        why = "Shutter control unavailable";
      if (
        selected === "flatDevice" &&
        a === "cover" &&
        s.supportsOpenClose !== true
      )
        why = "Cover control unavailable";
      if (
        selected === "flatDevice" &&
        a === "light" &&
        s.supportsOnOff !== true
      )
        why = "Light control unavailable";
      b.disabled = !!why;
      b.title = why;
      b.classList.toggle(
        "is-selected",
        selected === "filterWheel" && Number(b.dataset.position) === s.position,
      );
    }
    const active = ((states.operations?.value.items ?? []) as Data[]).find(
      (o) => o.device === selected,
    );
    el(dialog, "outcome").textContent = active
      ? `${active.action} · ${active.status}${active.error ? " · " + active.error : ""}`
      : "";
    el(dialog, "reason").textContent = api.reason(
      selected,
      connected ? "disconnect" : "connect",
    );
    const next = api.session() + "|" + choice()?.id;
    if (context !== next) {
      invalidated = true;
      connection.disabled = true;
      body
        .querySelectorAll<HTMLButtonElement>("[data-action]")
        .forEach((b) => (b.disabled = true));
      el(dialog, "reason").textContent =
        "Device changed. Close and reopen controls.";
    }
    const chart = body.querySelector<HTMLElement>("[data-field=thermal-chart]");
    const sig = api.session() + String(history.samples.at(-1)?.at) + live;
    if (chart && sig !== chartStamp) {
      thermal(chart);
      chartStamp = sig;
    }
    const angle = body.querySelector<HTMLElement>("[data-field=orientation]");
    const mountModel=body.querySelector<HTMLElement>('[data-field=mount-view]');
    if(mountModel&&dialog.open){
      updateMountView(mountModel,s,live,false,mountCamera);
      const site=states.administration?.value.site,when=new Date();
      for(const label of body.querySelectorAll<HTMLElement>('[data-preset-coordinates]')){
        if(validPointingSite(site)){
          const target=horizontalTarget(site,Number(label.dataset.presetCoordinates),45,when);
          label.textContent=`RA ${target.ra.toFixed(4)} h · Dec ${target.dec>=0?'+':''}${target.dec.toFixed(3)}°`;
        }else label.textContent='Observing site unavailable';
      }
    }
    if (angle) {
      const v = connected
        ? selected === "rotator"
          ? s.position
          : s.azimuth
        : null;
      angle.innerHTML = `<svg viewBox="0 0 180 180" role="img" aria-label="${safe(devices[selected][0])} orientation"><circle cx="90" cy="90" r="66"/><path d="M90 15v14M90 151v14M15 90h14M151 90h14"/><text x="90" y="11" text-anchor="middle">${selected === "rotator" ? "0°" : "N"}</text>${numeric(v) ? `<g transform="rotate(${v} 90 90)"><path class="orientation-pointer" d="M90 35l-10 47h20z"/></g>` : ""}<text x="90" y="112" text-anchor="middle" class="orientation-value">${format(v, 1, "°")}</text></svg>`;
    }
    const roof = body.querySelector<HTMLElement>("[data-field=roof]");
    if (roof) {
      roof.classList.toggle("is-safe", connected && s.isSafe === true);
      roof.innerHTML = `<span aria-hidden="true">⌂</span><strong>${!connected ? "Unknown" : s.isSafe === true ? "Safe" : s.isSafe === false ? "Unsafe" : "Unknown"}</strong>`;
    }
    for (const [field, id] of [
      ["guide-graph", "guide-chart"],
      ["focus-graph", "focus-chart"],
    ]) {
      const target = body.querySelector<HTMLElement>(`[data-field=${field}]`);
      if (target) {
        const original = document.getElementById(id);
        target.replaceChildren();
        if (original) target.append(original.cloneNode(true));
        target.querySelectorAll("[id]").forEach((n) => n.removeAttribute("id"));
      }
    }
  }
  return update;
}
