import {timedCountdownElement} from './sequence-clock';
import {sequenceName,sequenceDetail,type CoolingContext} from "./sequence-presentation";
import type {Site} from "./target-visibility";
import {currentSequenceNode, sequenceAncestors} from "./sequence-current";
import { renderSequenceView } from "./sequence-view";
export type Topic = { observedAt: string; value: Record<string, unknown> };
export type SequenceNode = {
  timing?: import("./sequence-timing").StepTiming;
  id: string;
  parentId: string | null;
  name: string;
  type: string;
  kind: string;
  sourceStatus: string;
  state: string;
  details: Record<string, unknown>;
};
const node = <T extends HTMLElement = HTMLElement>(id: string) =>
  document.getElementById(id)! as T;
const text = (id: string, value: unknown) => {
  const next = value == null ? "—" : String(value);
  if (node(id).textContent !== next) node(id).textContent = next;
};
export const numeric = (value: unknown): value is number =>
  typeof value === "number" && Number.isFinite(value);
export function format(value: unknown, digits = 1, suffix = ""): string {
  if (value == null || (typeof value === "number" && !Number.isFinite(value)))
    return "—";
  if (numeric(value))
    return (
      value.toLocaleString(undefined, {
        maximumFractionDigits: digits,
        minimumFractionDigits: digits,
      }) + suffix
    );
  if (typeof value === "boolean") return value ? "On" : "Off";
  return String(value);
}
const icons: Record<string, string> = {
  camera:
    '<rect x="3" y="7" width="22" height="16" rx="2"/><circle cx="14" cy="15" r="5"/><path d="M7 7l2-3h7l2 3m2 5h2"/>',
  mount:
    '<path d="m4 10 15-6 5 10-15 6zM16 18l-3 5m0-2-6 7m6-7 6 7M2 9l5 12"/>',
  filterWheel:
    '<circle cx="14" cy="14" r="11"/><circle cx="14" cy="7" r="2"/><circle cx="7" cy="14" r="2"/><circle cx="21" cy="14" r="2"/><circle cx="14" cy="21" r="2"/>',
  focuser:
    '<path d="M10 5h8v12h-8zm4 12v7m-9 0h18M7 9h3m8 0h3"/><circle cx="14" cy="4" r="2"/>',
  rotator:
    '<circle cx="14" cy="14" r="9"/><path d="m4 7 3-4 5 1m11 17-3 4-5-1M14 8v6l4 3"/>',
};
icons.guider = '<path d="M14 3v5m0 12v5M3 14h5m12 0h5"/><circle cx="14" cy="14" r="7"/><circle cx="14" cy="14" r="2"/>';
const names: Record<string, string> = {
  camera: "Camera",
  mount: "Mount",
  filterWheel: "Filter wheel",
  focuser: "Focuser",
  rotator: "Rotator",
  guider: "Guider",
};
let sequenceScrollRestore:{frame:number;top:number}|undefined;
function cancelSequenceScrollRestore(){if(sequenceScrollRestore)cancelAnimationFrame(sequenceScrollRestore.frame);sequenceScrollRestore=undefined;}
let sequenceViewRefresh = () => {};
let jumpToCurrent = () => {};
let treeSignature = "",
  previewRevision = "",
  histogramValues: number[] = [];
const collapsed = new Set<string>();
let cachedSession = "";
let exposure: { end: number; duration: number } | null = null;
function readings(target: HTMLElement, values: Record<string, unknown>) {
  target.replaceChildren();
  for (const [label, value] of Object.entries(values)) {
    const dt = document.createElement("dt"),
      dd = document.createElement("dd");
    dt.textContent = label;
    dd.textContent = value == null ? "—" : String(value);
    target.append(dt, dd);
  }
}
export function mountDashboard() {
  const strip = node("equipment-strip");
  for (const key of Object.keys(names)) {
    const card = document.createElement("article");
    card.className = "equipment-card";
    card.id = `equipment-${key}`;
    card.innerHTML = `<div class="equipment-heading"><svg class="equipment-icon" viewBox="0 0 28 28" aria-hidden="true">${icons[key]}</svg><div class="equipment-title"><h2>${names[key]}</h2><span class="device-name">Not reported</span></div><button class="equipment-menu" data-control-device="${key}" aria-label="${names[key]} controls" title="${names[key]} controls">⋮</button></div><div class="device-status disconnected"><i class="status-dot offline"></i><span>Not reported</span></div><div class="equipment-values"></div><button class="device-toggle" id="${key}-action" disabled>Connect</button>`;
    strip.append(card);
  }
  const panel = document.querySelector<HTMLElement>(".sequence-panel")!;
  for(const event of ['wheel','touchstart','pointerdown','keydown'])node('sequence-tree').addEventListener(event,cancelSequenceScrollRestore,{passive:true});
  const expand = document.createElement("button");
  expand.id = "sequence-expand";
  expand.className = "sequence-expand";
  expand.type = "button";
  const hiddenSurfaces = [...document.querySelectorAll<HTMLElement>("#workspace,.app-header,body > footer")];
  function setExpanded(expanded: boolean) {
    const scroll = node("sequence-tree").scrollTop;
    document.dispatchEvent(new Event("sequence-layout-change"));
    panel.classList.toggle("is-expanded", expanded);
    sequenceViewRefresh();
    document.body.classList.toggle("sequence-expanded", expanded);
    expand.textContent = expanded ? "⤡" : "⤢";
    expand.title = expanded ? "Restore sequence panel" : "Expand sequence panel";
    expand.setAttribute("aria-label", expand.title);
    expand.setAttribute("aria-expanded", String(expanded));
    for (const surface of hiddenSurfaces) surface.inert = expanded;
    requestAnimationFrame(() => { node("sequence-tree").scrollTop = scroll; });
  }
  const jump = document.createElement("button");
  jump.id = "jump-current"; jump.type = "button"; jump.textContent = "Current step";
  jump.title = "Jump to the current instruction or waiting step";
  jump.onclick = () => jumpToCurrent();
  node("sequence-title-actions").append(jump, expand);
  expand.onclick = () => setExpanded(!panel.classList.contains("is-expanded"));
  document.addEventListener("keydown", e => {
    if (e.key === "Escape" && panel.classList.contains("is-expanded")
        && !e.defaultPrevented && !document.querySelector("dialog[open],.sequence-picker:popover-open")) {
      setExpanded(false); expand.focus();
    }
  });
  setExpanded(false);
  const image = node<HTMLImageElement>("latest-preview");
  image.onload = () => {
    const canvas = document.createElement("canvas");
    canvas.width = 160;
    canvas.height = 100;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;
    ctx.drawImage(image, 0, 0, 160, 100);
    const rgba = ctx.getImageData(0, 0, 160, 100).data;
    histogramValues = new Array<number>(64).fill(0);
    for (let i = 0; i < rgba.length; i += 4) {
      const lum =
        0.2126 * rgba[i] + 0.7152 * rgba[i + 1] + 0.0722 * rgba[i + 2];
      histogramValues[Math.min(63, Math.floor(lum / 4))]++;
    }
    renderHistogram();
  };
  image.onerror = () => {
    image.hidden = true;
    node("last-preview").hidden = true;
    node("image-empty").hidden = false;
    histogramValues = [];
    renderHistogram();
  };
  function zoom(mode: string) {
    image.classList.toggle("actual", mode === "actual");
    image.classList.toggle("zoom", mode === "zoom");
    node("image-stage").classList.toggle("zoomed", mode !== "fit");
    for (const id of ["fit", "actual", "zoom"])
      node("image-" + id).setAttribute("aria-pressed", String(mode === id));
  }
  node("image-fit").onclick = () => zoom("fit");
  node("image-actual").onclick = () => zoom("actual");
  node("image-zoom").onclick = () => zoom("zoom");
  node("image-fullscreen").onclick = () => {
    const stage = node("image-stage");
    if (document.fullscreenElement) void document.exitFullscreen();
    else
      void stage.requestFullscreen().catch(() => {
        text("image-caption", "Fullscreen is unavailable in this browser.");
      });
  };

}
function renderHistogram() {
  const svg = node("histogram");
  svg.setAttribute(
    "preserveAspectRatio",
    histogramValues.length ? "none" : "xMidYMid meet",
  );
  svg.replaceChildren();
  if (!histogramValues.length) {
    const t = svgElement("text", {
      x: "150",
      y: "52",
      "text-anchor": "middle",
      fill: "#61788d",
      "font-size": "10",
    });
    t.textContent = "Histogram available with preview";
    svg.append(t);
    return;
  }
  const max = Math.max(...histogramValues);
  const d = histogramValues
    .map(
      (v, i) =>
        `${i ? "L" : "M"}${(i * 300) / 63},${90 - (v / Math.max(1, max)) * 84}`,
    )
    .join(" ");
  svg.append(
    svgElement("path", {
      d: `M0,95 ${d.replace("M", "L")} L300,95 Z`,
      fill: "#7895a9",
      opacity: ".8",
    }),
  );
}
function svgElement(tag: string, attrs: Record<string, string>) {
  const element = document.createElementNS("http://www.w3.org/2000/svg", tag);
  for (const [k, v] of Object.entries(attrs)) element.setAttribute(k, v);
  return element;
}
function plot(id: string, series: number[][][], focus = false) {
  const svg = node(id);
  svg.replaceChildren();
  const points = series.flat().filter((p) => p.every(Number.isFinite));
  const minX = points.length ? Math.min(...points.map((p) => p[0])) : 0,
    maxX = points.length ? Math.max(...points.map((p) => p[0])) : 1;
  let minY = points.length ? Math.min(...points.map((p) => p[1])) : 0,
    maxY = points.length ? Math.max(...points.map((p) => p[1])) : 1;
  if (!focus) {
    const range = Math.max(0.5, Math.abs(minY), Math.abs(maxY)) * 1.15;
    minY = -range;
    maxY = range;
  } else {
    minY = Math.max(0, minY - 0.3);
    maxY += 0.3;
  }
  for (let i = 0; i <= 4; i++) {
    const y = 12 + i * 27;
    svg.append(
      svgElement("line", {
        x1: "35",
        x2: "452",
        y1: String(y),
        y2: String(y),
        stroke: "#293d4b",
        "stroke-width": ".8",
      }),
    );
    const t = svgElement("text", {
      x: "29",
      y: String(y + 3),
      "text-anchor": "end",
      fill: "#90a6b8",
      "font-size": "9",
    });
    t.textContent = format(maxY - (i * (maxY - minY)) / 4, 1);
    svg.append(t);
  }
  for (let i = 0; i <= 5; i++) {
    const x = 35 + i * 83;
    svg.append(
      svgElement("line", {
        x1: String(x),
        x2: String(x),
        y1: "12",
        y2: "120",
        stroke: "#293d4b",
        "stroke-width": ".6",
      }),
    );
    const t = svgElement("text", {
      x: String(x),
      y: "138",
      "text-anchor": "middle",
      fill: "#90a6b8",
      "font-size": "8",
    });
    t.textContent = points.length
      ? focus
        ? format(minX + (i * (maxX - minX)) / 5, 0)
        : String(Math.round(minX + (i * (maxX - minX)) / 5))
      : "—";
    svg.append(t);
  }
  if (!points.length) {
    const t = svgElement("text", {
      x: "244",
      y: "69",
      "text-anchor": "middle",
      fill: "#718b9f",
      "font-size": "11",
    });
    t.textContent = focus
      ? "No autofocus measurements received"
      : "No guide samples received";
    svg.append(t);
    return;
  }
  const xy = (p: number[]) => [
    35 + ((p[0] - minX) / Math.max(1, maxX - minX)) * 415,
    120 - ((p[1] - minY) / Math.max(0.01, maxY - minY)) * 108,
  ];
  for (const [i, list] of series.entries()) {
    const valid = list.filter((p) => p.every(Number.isFinite));
    const d = valid
      .map((p, j) => {
        const [x, y] = xy(p);
        return `${j ? "L" : "M"}${x},${y}`;
      })
      .join(" ");
    svg.append(
      svgElement("path", {
        d,
        stroke: focus ? "#39d9c5" : i ? "#ee839b" : "#5297ff",
        "stroke-width": "1.6",
        fill: "none",
      }),
    );
    if (focus)
      for (const p of valid) {
        const [x, y] = xy(p);
        svg.append(
          svgElement("circle", {
            cx: String(x),
            cy: String(y),
            r: "3",
            fill: "#39d9c5",
            stroke: "#10242a",
            "stroke-width": "1.5",
          }),
        );
      }
  }
}
function inlineReadings(
  target: HTMLElement,
  values: [string, string, string?][],
) {
  target.replaceChildren();
  for (const [label, value, cls] of values) {
    const div = document.createElement("div");
    div.className = "inline-reading";
    const span = document.createElement("span"),
      strong = document.createElement("strong");
    span.textContent = label;
    strong.textContent = value;
    if (cls) strong.className = cls;
    div.append(span, strong);
    target.append(div);
  }
}
function targetName(nodes: SequenceNode[]) {
  let item = nodes.find(
    (n) => n.kind === "instruction" && ["running", "waiting"].includes(n.state),
  );
  const seen = new Set<string>();
  while (item && !seen.has(item.id)) {
    seen.add(item.id);
    if (/DeepSkyObjectContainer|TargetContainer/.test(item.type))
      return item.name;
    item = nodes.find((n) => n.id === item!.parentId);
  }
  return "";
}
function sequenceLabel(item: SequenceNode) {
  return [sequenceName(item),sequenceDetail(item)].filter(Boolean).join(" · ");
}
function renderSequence(
  sequence: Record<string, unknown>,
  live: boolean,
  session: string,
  site?: Site,
  cooling: CoolingContext = {},
  profileId?:string,
  observedAt?:string,
) {
  const nodes = [...(Array.isArray(sequence.nodes) ? sequence.nodes : [])] as SequenceNode[];
  if (sequence.mode !== "simple") {
    for (const [type, name] of [["StartAreaContainer", "Start"], ["TargetAreaContainer", "Targets"], ["EndAreaContainer", "End"]]) {
      if (!nodes.some(n => n.type === type)) nodes.push({
        id: "display-only-" + type, parentId: null, type, name,
        kind: "container", sourceStatus: "", state: "unknown", details: {},
      });
    }
  }
  const root = nodes.find((n) => n.type === "SequenceRootContainer");
  const instructions = nodes.filter((n) => n.kind === "instruction");
  const completed = instructions.filter((n) => n.state === "completed").length;
  const active = currentSequenceNode(nodes, sequence.activeNodeIds);
  text("target-title", targetName(nodes));
  node("target-title").hidden = !targetName(nodes);
  text("preview-target", targetName(nodes) ? "— " + targetName(nodes) : "");
  text(
    "sequence-mode",
    sequence.available
      ? !instructions.length
        ? "Empty sequence"
        : root?.name && root.name !== "Advanced Sequence"
          ? root.name
          : sequence.mode === "simple"
            ? "Simple sequence"
            : "Loaded sequence"
      : (sequence.reason ?? "Waiting for sequence"),
  );
  text(
    "sequence-coverage",
    instructions.length
      ? `${completed} / ${instructions.length} steps complete`
      : "No instructions loaded",
  );
  const pct = instructions.length
    ? Math.round((completed / instructions.length) * 100)
    : 0;
  text("sequence-percent", instructions.length ? pct + "%" : "—");
  node("sequence-progress").style.width = pct + "%";
  node("sequence-progress").parentElement!.setAttribute(
    "aria-valuenow",
    String(pct),
  );
  node("sequence-progress").parentElement!.setAttribute("aria-valuemax", "100");
  text(
    "sequence-running",
    !live
      ? "Cached"
      : active
        ? active.state === "waiting"
          ? "Waiting"
          : "Executing"
        : instructions.length
          ? "Idle"
          : "",
  );
  node("sequence-warning").hidden = sequence.truncated !== true;
  text(
    "sequence-warning",
    "Partial sequence · projection limit reached; some steps are not shown.",
  );
  const groups = nodes.filter(n => n.kind === "container" && n.type !== "SequenceRootContainer");
  const allCollapsed = groups.length > 0 && groups.every(n => collapsed.has(n.id));
  const fold = node<HTMLButtonElement>("sequence-fold-all");
  fold.textContent = allCollapsed ? "Expand all" : "Collapse all";
  fold.disabled = !groups.length;
  fold.onclick = () => {
    collapsed.clear();
    if (!allCollapsed) for (const group of groups) collapsed.add(group.id);
    treeSignature = "";
    renderSequence(sequence, live, session, site, cooling, profileId, observedAt);
  };
  const index = nodes.findIndex((n) => n.id === active?.id);
  const upcoming = nodes
    .slice(index + 1)
    .find((n) => n.kind === "instruction" && n.state === "pending");
  text(
    "sequence-upcoming",
    active
      ? `Current · ${sequenceLabel(active)}`
      : upcoming
        ? `Upcoming · ${sequenceLabel(upcoming)}`
        : instructions.length
          ? "Loaded sequence"
          : "Load a sequence in N.I.N.A.",
  );
  text("sequence-freshness", live ? "" : "Offline");
  node<HTMLButtonElement>("jump-current").disabled = !active || !live;
  jumpToCurrent = () => {
    if (!active || !live) return;
    for (const id of sequenceAncestors(nodes, active.id)) collapsed.delete(id);
    treeSignature = "";
    sequenceViewRefresh();
    const row = [...node("sequence-tree").querySelectorAll<HTMLElement>("[data-node-id]")].find(row => row.dataset.nodeId === active.id);
    cancelSequenceScrollRestore();
    row?.scrollIntoView({block: "center", behavior: "smooth"});
  };
  sequenceViewRefresh = () => renderSequence(sequence, live, session, site, cooling, profileId, observedAt);
  const expanded = document.querySelector(".sequence-panel")!.classList.contains("is-expanded");
  const signature = JSON.stringify([session, live, nodes, site, cooling, profileId, expanded, [...collapsed]]);
  if (signature === treeSignature) return;
  treeSignature = signature;
  const tree = node("sequence-tree");
  const scroll = sequenceScrollRestore?.top ?? tree.scrollTop;
  cancelSequenceScrollRestore();
  renderSequenceView(tree, nodes, {
    collapsed, live, observedAt, site, profileId, expanded, cooling,
    toggle(id) {
      collapsed.has(id) ? collapsed.delete(id) : collapsed.add(id);
      treeSignature = "";
      renderSequence(sequence, live, session, site, cooling, profileId, observedAt);
    },
  });
  tree.scrollTop = scroll;
  // The editor adds insertion lines in its MutationObserver microtask. Restore
  // again before paint, after those lines have restored the full tree height.
  sequenceScrollRestore={top:scroll,frame:requestAnimationFrame(()=>{tree.scrollTop=scroll;sequenceScrollRestore=undefined;})};

}
export function updateExposureClock(live = true, now=Date.now()) {
  const sequenceProgress=document.getElementById("sequence-exposure-inline");
  if(sequenceProgress)sequenceProgress.hidden=!exposure||!live;
  if (!exposure) return;
  if (!live) {
    text("capture-progress-label", "Cached exposure · telemetry unavailable");
    return;
  }
  const remaining = Math.max(0, (exposure.end - now) / 1000);
  const elapsed = Math.max(
    0,
    Math.min(exposure.duration, exposure.duration - remaining),
  );
  const pct = exposure.duration ? (elapsed / exposure.duration) * 100 : 0;
  text(
    "capture-exposure",
    `${format(elapsed, 0)} / ${format(exposure.duration, 0)} s`,
  );
  text(
    "capture-progress-label",
    remaining
      ? `${format(remaining, 0)} s remaining`
      : "Waiting for camera update",
  );
  if(sequenceProgress){
    const bar=document.getElementById("sequence-exposure-bar");if(bar)bar.style.width=Math.min(100,pct)+"%";
    const reading=document.getElementById("sequence-exposure-reading");if(reading)reading.textContent=format(elapsed,0)+" / "+format(exposure.duration,0)+" s";
  }
  node("exposure-progress").style.width = Math.min(100, pct) + "%";
  node("exposure-track").setAttribute("aria-valuenow", String(Math.floor(pct)));
}
export function renderDashboard(
  states: Record<string, Topic>,
  live: boolean,
  session: string,
  now=Date.now(),
) {
  if (cachedSession !== session) {
    collapsed.clear();
    treeSignature = "";
    previewRevision = "";
    histogramValues = [];
  }
  cachedSession = session;
  const value = (topic: string) => states[topic]?.value ?? {};
  const choices = (value("devices").items ?? []) as {
    device: string;
    name: string;
    selected: boolean;
  }[];
  for (const key of Object.keys(names)) {
    const state = value(key),
      connected = state.connected === true,
      card = node("equipment-" + key);
    card.querySelector(".device-name")!.textContent = connected
      ? String(state.name ?? names[key])
      : (choices.find((x) => x.device === key && x.selected)?.name ??
        "No device selected");
    card
      .querySelector(".device-name")!
      .setAttribute(
        "title",
        String(card.querySelector(".device-name")!.textContent),
      );
    const status = card.querySelector(".device-status")!;
    status.classList.toggle("disconnected", !connected);
    status
      .querySelector(".status-dot")!
      .classList.toggle("offline", !connected || !live);
    status.querySelector("span")!.textContent =
      state.connected == null
        ? "Not reported"
        : `${connected ? "Connected" : "Disconnected"}${live ? "" : " · cached"}`;
    const vals = card.querySelector<HTMLElement>(".equipment-values")!;
    // The mount owns persistent GPU resources; telemetry must not detach its canvas.
    if(key==="mount"&&vals.querySelector(".mount-model"))continue;
    vals.replaceChildren();
    const s = connected ? state : {};
    if (key === "camera") {
      const primary = document.createElement("div");
      primary.className = "primary-reading";
      primary.textContent = format(s.temperature, 1, "°C");
      const small = document.createElement("small");
      small.textContent = `target ${format(s.temperatureSetPoint, 0, "°C")}`;
      primary.append(small);
      const dl = document.createElement("dl");
      dl.className = "compact-readings";
      readings(dl, {
        Cooler: format(s.coolerPower, 0, "%"),
        Gain: format(s.gain, 0),
        Bin: numeric(s.binX) && numeric(s.binY) ? `${s.binX}×${s.binY}` : "—",
      });
      vals.append(primary, dl);
    } else if (key === "mount") {
      const dl = document.createElement("dl");
      dl.className = "compact-readings mount-readings";
      readings(dl, {
        RA: format(s.rightAscension, 4, " h"),
        Dec: format(s.declination, 3, "°"),
        "Alt / Az": `${format(s.altitude, 1)} / ${format(s.azimuth, 1)}°`,
        Tracking:
          s.trackingEnabled == null ? "—" : s.trackingEnabled ? "On" : "Off",
      });
      vals.append(dl);
    } else if (key === "guider") {
      const dl = document.createElement("dl");
      dl.className = "compact-readings";
      const total = (s.rms as Record<string, Record<string, unknown>> | undefined)?.total;
      readings(dl, {
        State: connected ? (s.state ?? "Connected") : "—",
        "Total RMS": numeric(total?.arcseconds)
          ? format(total.arcseconds, 2, "″")
          : format(total?.pixel, 2, " px"),
      });
      vals.append(dl);
    } else {
      const p = document.createElement("div");
      p.className = "primary-reading";
      p.textContent =
        key === "filterWheel"
          ? format(s.filter)
          : format(s.position, 0, key === "rotator" ? "°" : "");
      const small = document.createElement("small");
      small.textContent =
        key === "focuser"
          ? `Temperature ${format(s.temperature, 1, "°C")}`
          : key === "rotator"
            ? `Mechanical ${format(s.mechanicalPosition, 1, "°")}`
            : "Current filter";
      p.append(small);
      vals.append(p);
    }
  }
  const site = value("administration").site as Site | undefined;
  const temperature = (name:string) => {
    const t=states[name],v=t?.value;
    return live && v?.connected===true && t && now-Date.parse(t.observedAt)<60000 && typeof v.temperature==='number' && Number.isFinite(v.temperature)?v.temperature:undefined;
  };
  renderSequence(value("sequence"), live, session, site?.timeZone ? site : undefined,{focuserTemperature:temperature('focuser'),weatherTemperature:temperature('weather')},(value('administration').profile as {id?:string}|undefined)?.id,states.sequence?.observedAt);
  for(const item of (value("sequence").nodes??[]) as SequenceNode[]){
    if(item.type!=="TimeCondition"&&item.type!=="WaitForTime")continue;
    const row=[...node("sequence-tree").querySelectorAll<HTMLElement>('[data-node-id]')].find(row=>row.dataset.nodeId===item.id);
    const detail=row?.querySelector<HTMLElement>('.sequence-detail');
    if(detail){const previous=detail.querySelector('.sequence-countdown');if(previous){if(previous.previousSibling?.textContent===' · ')previous.previousSibling.remove();previous.remove();}const clock=timedCountdownElement(item.details,states.sequence?.observedAt,site?.timeZone);if(clock)detail.append(document.createTextNode(' · '),clock);}
  }

  const preview = value("preview"),
    image = value("image");
  const img = node<HTMLImageElement>("latest-preview");
  const thumbnail = node<HTMLImageElement>("last-preview");
  const revision = `${session}:${preview.revision}`;
  if (typeof preview.jpeg === "string" && preview.jpeg) {
    if (revision !== previewRevision) {
      previewRevision = revision;
      img.src = "data:image/jpeg;base64," + preview.jpeg;
      thumbnail.src = img.src;
    }
    img.hidden = false;
    thumbnail.hidden = false;
    node("image-empty").hidden = true;
  } else {
    img.hidden = true;
    thumbnail.hidden = true;
    thumbnail.removeAttribute("src");
    img.removeAttribute("src");
    node("image-empty").hidden = false;
    histogramValues = [];
    renderHistogram();
  }
  for (const id of [
    "image-fit",
    "image-actual",
    "image-zoom",
    "image-fullscreen",
  ])
    node<HTMLButtonElement>(id).disabled = !preview.jpeg;
  text(
    "image-caption",
    image.filename
      ? `${image.filename}${live ? "" : " · cached"}`
      : "No image received in this plugin session",
  );
  text(
    "image-dimensions",
    numeric(image.width) && numeric(image.height)
      ? `${image.width} × ${image.height}`
      : "—",
  );
  const download = node<HTMLAnchorElement>("image-download");
  download.hidden = !live || typeof image.id !== "string";
  if (typeof image.id === "string") {
    download.href = (document.body.dataset.gatewayBase ?? "") + "/api/v1/images/" + encodeURIComponent(image.id);
    download.download =
      typeof image.filename === "string"
        ? image.filename
        : "starfront-image.fits";
  }
  readings(node("image-statistics"), {
    HFR: format(image.hfr, 2),
    Stars: format(image.stars, 0),
    Median: format(image.median, 0),
    Mean: format(image.mean, 0),
    Min: format(image.min, 0),
    Max: format(image.max, 0),
    SD: format(image.stDev, 0),
    Gain: format(image.gain, 0),
  });
  const camera = value("camera"),
    filter = value("filterWheel"),
    nodes = (value("sequence").nodes ?? []) as SequenceNode[],
    active = nodes.find(
      (n) =>
        n.kind === "instruction" &&
        n.state === "running" &&
        /Exposure|SmartExposure/.test(n.type),
    );
  const seconds = active?.details.exposureTime;
  const end =
    typeof camera.exposureEndTime === "string"
      ? Date.parse(camera.exposureEndTime)
      : NaN;
  exposure =
    live &&
    camera.isExposing === true &&
    numeric(seconds) &&
    seconds > 0 &&
    Number.isFinite(end)
      ? { end, duration: seconds }
      : null;
  text(
    "capture-state",
    !live
      ? "Cached state"
      : camera.isExposing === true
        ? "Exposing"
        : active
          ? "Running"
          : camera.connected === true
            ? "Idle"
            : "Camera disconnected",
  );
  text(
    "capture-filter",
    filter.connected === true ? filter.filter : image.filter,
  );
  text(
    "capture-frame",
    active &&
      numeric(active.details.progressExposureCount) &&
      numeric(active.details.exposureCount) &&
      active.details.exposureCount > 0
      ? `${active.details.progressExposureCount} / ${active.details.exposureCount}`
      : "—",
  );
  if (!exposure) {
    text(
      "capture-exposure",
      numeric(seconds)
        ? `${format(seconds, 0)} s`
        : image.exposure
          ? `${format(image.exposure, 0)} s`
          : "—",
    );
    text(
      "capture-progress-label",
      !live
        ? "Cached exposure · telemetry unavailable"
        : camera.isExposing === true
          ? "Exposing · elapsed time not reported"
          : image.exposure
            ? "Last completed exposure"
            : "No active exposure",
    );
    node("exposure-progress").style.width =
      camera.isExposing === true ? "100%" : "0";
    node("exposure-track").removeAttribute("aria-valuenow");
  } else updateExposureClock(live,now);
  const history = (value("imageHistory").items ?? []) as Record<
    string,
    unknown
  >[];
  const integration = history.reduce(
    (total, item) => total + (numeric(item.exposure) ? item.exposure : 0),
    0,
  );
  text(
    "integration",
    history.length
      ? `Recent: ${Math.floor(integration / 60)}m · ${history.length} frames`
      : "Recent integration —",
  );
  node("integration").title =
    "Integration in the retained image history; not the full observing night.";
  readings(node("last-frame-stats"), {
    Exposure: format(image.exposure, 0, " s"),
    Filter: format(image.filter),
    Stars: format(image.stars, 0),
    HFR: format(image.hfr, 2),
  });
  const guides = (value("guideHistory").points ?? []) as Record<
    string,
    unknown
  >[];
  const guider = value("guider");
  text(
    "guide-status",
    !live
      ? "Cached readings"
      : guider.connected === true
        ? guides.length
          ? "Guiding"
          : "Connected"
        : guides.length
          ? "Cached samples"
          : "Disconnected",
  );
  const points = (key: string) =>
    guides.flatMap((p, i) => (numeric(p[key]) ? [[i, p[key] as number]] : []));
  plot("guide-chart", [points("ra"), points("dec")]);
  const rms = (guider.rms ?? {}) as Record<string, Record<string, unknown>>;
  const rmsLabel = (key: string) =>
    guider.connected !== true
      ? "—"
      : numeric(rms[key]?.arcseconds)
        ? format(rms[key].arcseconds, 2, "″")
        : numeric(rms[key]?.pixel)
          ? format(rms[key].pixel, 2, " px")
          : "—";
  readings(node("guide-readings"), {
    "Total RMS": rmsLabel("total"),
    "RA RMS": rmsLabel("ra"),
    "Dec RMS": rmsLabel("dec"),
  });
  text("guide-units", "Raw distance · " + guides.length + " samples");
  const af = value("autofocus"),
    focus = (af.points ?? []) as Record<string, number>[];
  text(
    "autofocus-status",
    af.running
      ? "Running"
      : focus.length
        ? af.outcome === "cancelled"
          ? "Cancelled"
          : af.outcome === "failed"
            ? "Failed"
            : "Finished"
        : "No run received",
  );
  plot("focus-chart", [focus.map((p) => [p.position, p.hfr])], true);
  const result = (af.result ?? {}) as Record<string, unknown>;
  const valid = focus.filter((p) => numeric(p.hfr) && numeric(p.position));
  const best = valid.length
    ? valid.reduce((a, b) => (a.hfr < b.hfr ? a : b))
    : null;
  readings(node("focus-readings"), {
    "Best sample": format(best?.position, 0),
    HFR: format(best?.hfr, 2),
    Points: valid.length || "—",
  });
  text("focus-filter", result.filter ?? "Focus position");
}

export function renderRoofStatus(states: Record<string, Topic>, live: boolean) {
  let badge = document.getElementById("roof-status");
  if (!badge) {
    badge = document.createElement("button");
    (badge as HTMLButtonElement).type = "button";
    badge.id = "roof-status";
    badge.setAttribute("aria-live", "polite");
    node("connection").after(badge);
  }
  const safety = states.safety?.value;
  const known = live && safety?.connected === true && typeof safety.isSafe === "boolean";
  const state = known ? (safety!.isSafe ? "open" : "closed") : "unknown";
  const label = "Roof " + state;
  if (badge.textContent !== label) badge.textContent = label;
  badge.className = "roof-status roof-" + state;
}
