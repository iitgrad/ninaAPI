import {mountReplay,type ReplayFrame} from "./replay";
import {sequenceCommandIdentity,sequenceStopState} from './sequence-command-state';
import {updateSequenceClocks} from './sequence-clock';
import { mountSequenceLibrary } from "./sequence-library";
import { mountSequenceEditor } from "./sequence-editor";
import { mountDevicePanels } from "./device-panels";
import {
  mountDashboard,
  renderDashboard,
  renderRoofStatus,
  updateExposureClock,
  numeric,
  format,
  type Topic,
  type SequenceNode,
} from "./dashboard";
type Choice = {
  device: string;
  id: string;
  name: string;
  allowed: boolean;
  selected: boolean;
};
type Operation = {
  requestId: string;
  device: string;
  action: string;
  status: string;
  acceptedAt: string;
  completedAt?: string;
  error?: string;
};
type Envelope = {
  bridge: { connected: boolean; lastReceivedAt?: string };
  snapshot: {
    sessionId: string;
    revision: number;
    states: Record<string, Topic>;
  } | null;
};
const el = <T extends HTMLElement = HTMLElement>(id: string): T =>
  document.getElementById(id)! as T;
class RequestError extends Error {
  constructor(
    message: string,
    readonly status: number,
  ) {
    super(message);
  }
}
const preview = new URLSearchParams(location.search).get("preview") === "1";
const gatewayBase = document.body.dataset.gatewayBase ?? "";
document
  .getElementById("dashboard-lock")
  ?.addEventListener("click", async () => {
    await request("/auth/dashboard-logout", {});
    location.replace("/dashboard/");
  });
let liveCurrent:Envelope|null=null;
let replayFrame:ReplayFrame|null=null;
let replayMode=false;
let current: Envelope | null = null,
  websocket: WebSocket | null = null,
  retry = 1000,
  currentSession = "",
  lastMetric = "";
let socketReady = false;
const pending = new Map<string, string>();
const pendingActions = new Map<string,string>();
const samples: { at: string; bytes: number }[] = [];
const write = (id: string, value: unknown) => {
  const text = value == null ? "—" : String(value);
  if (el(id).textContent !== text) el(id).textContent = text;
};
const clockFormat = new Intl.DateTimeFormat([], {
  hour: "2-digit",
  minute: "2-digit",
  second: "2-digit",
});
const time = (value?: string) =>
  value && Number.isFinite(Date.parse(value))
    ? clockFormat.format(new Date(value))
    : "—";
const notice = (message: string) => {
  el("notice").hidden = false;
  write("notice", message);
};
function topic(name: string): Record<string, unknown> {
  return current?.snapshot?.states[name]?.value ?? {};
}
function fresh() {
  if(replayMode)return false;
  const stamp =
    current?.bridge.lastReceivedAt ??
    current?.snapshot?.states.diagnostics?.observedAt;
  return (
    socketReady &&
    current?.bridge.connected === true &&
    !!stamp &&
    Date.now() - Date.parse(stamp) < 30_000 &&
    Date.now() - Date.parse(stamp) >= -5000
  );
}
async function request(
  path: string,
  body?: unknown,
  signal?:AbortSignal,
): Promise<Record<string, unknown>> {
  if(replayMode&&body!=null&&path.startsWith("/api/"))throw new Error("Replay is read-only. Return to Live to control equipment.");
  if (preview && body != null)
    throw new Error("Commands are disabled in design preview.");
  const response = await fetch(gatewayBase + path, {
    signal,
    method: body == null ? "GET" : "POST",
    headers: body == null ? {} : { "Content-Type": "application/json" },
    body: body == null ? undefined : JSON.stringify(body),
  });
  const data = (await response.json()) as Record<string, unknown>;
  if (!response.ok)
    if (gatewayBase && response.status === 401) location.reload();
  if (!response.ok)
    throw new RequestError(
      String(data.error ?? `Request returned ${response.status}`),
      response.status,
    );
  return data;
}
mountDashboard();
const tools = el<HTMLDialogElement>("tools-dialog");
el("open-tools").onclick = () => tools.showModal();
el("close-tools").onclick = () => tools.close();
function operations(): Operation[] {
  return (topic("operations").items ?? []) as Operation[];
}
function commandReason(device: string, action: string): string {
  if(replayMode)return "Replay is read-only. Return to Live to control equipment.";
  if (preview) return "Commands are disabled in design preview.";
  if (!fresh()) return "Waiting for fresh, connected telemetry.";
  if(!((topic("commands").items??[]) as {device:string;action:string}[]).some(c=>c.device===device&&c.action===action))return "This plugin does not support this control yet.";
  const ops = operations();
  if(device==="safetyMonitor" && action==="disconnect")return "Roof monitoring stays connected.";
  if(device==="safetyMonitor" && action==="ensureConnected")return "";
  const ownedStart = ops.find(
    (x) => x.device === "sequence" && x.action === "start" && !x.completedAt,
  );
  if (device === "sequence" && action === "stop") {
    if (ops.some(x => x.device === device && x.action === "stop" && !x.completedAt)) return "Stop is already pending.";
    if (pending.has(device) && pending.get(device) !== ownedStart?.requestId) return "Waiting for the previous sequence command result.";
    const external = ((topic("commands").items ?? []) as {device: string; action: string; externalSequenceStop?: boolean}[])
      .some(x => x.device === "sequence" && x.action === "stop" && x.externalSequenceStop === true);
    return sequenceStopState(topic("application").sequenceRunning,!!ownedStart,external);
  }
  if(device==="mount"&&action==="stop"){
    const choice=((topic("devices").items??[]) as Choice[]).find(c=>c.device==="mount"&&c.selected);
    if(!choice?.allowed||topic("mount").connected!==true)return "Connected mount controls required.";
    if(pendingActions.get("mount")==="stop"||ops.some(o=>o.device==="mount"&&o.action==="stop"&&!o.completedAt))return "Stop is already pending.";
    const external=((topic("commands").items??[]) as {device:string;action:string;externalMountStop?:boolean}[]).some(c=>c.device==="mount"&&c.action==="stop"&&c.externalMountStop===true);
    const owned=ops.some(o=>o.device==="mount"&&["slew","home","park","unpark","move","flip"].includes(o.action)&&!o.completedAt);
    if(external&&(topic("mount").slewing===true||owned))return "";
    if(owned)return "";
    return external?"Mount is not slewing.":"Update the plugin to stop slews started in N.I.N.A.";
  }
  if (device !== "sequence" && ["stop", "abort"].includes(action)) {
    const choice = ((topic("devices").items ?? []) as Choice[]).find(
      (c) => c.device === device && c.selected,
    );
    if (!choice?.allowed) return "Device controls are blocked by the host.";
    if (ops.some((o) => o.device === device && !o.completedAt)) return "";
    if (device !== "guider") return "No active Starfront operation to stop.";
  }
  if (pending.has(device))
    return "Waiting for the previous command result. Check Operations before retrying.";
  if (
    ops.some((x) => !x.completedAt) ||
    topic("application").sequenceRunning === true ||
    topic("camera").isExposing === true
  )
    return "Acquisition or another command is active.";
  if (device === "application" && action === "refresh") return "";
  if (device === "sequence") {
    if(["insertDraft","save"].includes(action)&&!(topic("sequence").editing as {canEdit?:boolean}|undefined)?.canEdit)return "Stop acquisition before editing or saving.";
    if (topic("application").sequenceRunning !== false)
      return "Waiting for N.I.N.A. acquisition state.";
    const nodes = (topic("sequence").nodes ?? []) as SequenceNode[];
    if (action === "start" && !nodes.some((n) => n.kind === "instruction"))
      return "Load a sequence in N.I.N.A. first.";
    if (
      action === "start" &&
      !((topic("devices").items ?? []) as Choice[]).some(
        (x) => x.selected && x.allowed,
      )
    )
      return "The host has not enabled controls for the selected devices.";
  } else {
    if (topic(device === "safetyMonitor" ? "safety" : device).connected == null)
      return "Waiting for device state.";
    const choice = ((topic("devices").items ?? []) as Choice[]).find(
      (x) => x.device === device && x.selected,
    );
    if (!choice || !choice.allowed)
      return "The active profile device is unavailable or blocked by the host control guard.";
  }
  if (
    device !== "sequence" &&
    action !== "connect" &&
    topic(device === "safetyMonitor" ? "safety" : device).connected !== true
  )
    return "Connect the device first.";
  return "";
}
let confirmation: (() => Promise<void>) | null = null;
const confirm = el<HTMLDialogElement>("confirm-dialog");
confirm.classList.add("parameter-dialog");
const setupOption = document.createElement("label");
setupOption.className = "sequence-setup-option";
setupOption.hidden = true;
const setupCopy = document.createElement("span");
setupCopy.className = "sequence-setup-copy";
const setupTitle = document.createElement("strong");
setupTitle.textContent = "Use sequence setup";
const setupSubtitle = document.createElement("small");
setupSubtitle.textContent = "Skip all startup checks";
setupCopy.append(setupTitle, setupSubtitle);
const setupCheckbox = document.createElement("input");
setupCheckbox.type = "checkbox";
setupCheckbox.id = "sequence-use-setup";
setupCheckbox.setAttribute("role", "switch");
setupCheckbox.setAttribute("aria-label", "Use sequence setup");
setupCheckbox.setAttribute("aria-describedby", "sequence-setup-hint");
const setupSwitch = document.createElement("span");
setupSwitch.className = "sequence-setup-switch";
setupSwitch.setAttribute("aria-hidden", "true");
setupOption.append(setupCopy, setupCheckbox, setupSwitch);
const setupHint = document.createElement("p");
setupHint.id = "sequence-setup-hint";
setupHint.className = "sequence-setup-hint";
setupHint.textContent = "Sequence must connect equipment.";
setupHint.hidden = true;
el("confirm-description").after(setupOption, setupHint);
el("confirm-cancel").onclick = () => confirm.close();
confirm.addEventListener("close", () => {
  confirmation = null;
});
el("confirm-send").onclick = () => {
  const callback = confirmation;
  confirm.close();
  if (callback) void callback();
};
async function send(
  device: string,
  action: string,
  deviceId?: string,
  parameters?: Record<string, unknown>,
  rejectKnown = false,
) {
  const reason = commandReason(device, action);
  if (reason) {
    notice(reason);
    return;
  }
  const requestId = crypto.randomUUID();
  pending.set(device, requestId);
  pendingActions.set(device,action);
  render();
  el("notice").hidden = true;
  try {
    await request("/api/v1/commands", {
      requestId,
      device,
      action,
      deviceId,
      parameters,
    });
  } catch (error) {
    if (
      error instanceof RequestError &&
      [400, 401, 403, 404, 409, 413, 422, 429].includes(error.status)
    ) { pending.delete(device);pendingActions.delete(device); }
    notice(`${error instanceof Error ? error.message : "Command outcome unknown."} Request ${requestId}. Check Operations before retrying.`);
    if(rejectKnown&&error instanceof RequestError&&[400,401,403,404,409,413,422,429].includes(error.status))throw error;
  }
  // A lost response does not imply failure. Only an observed operation completion releases the pending state.
  render();
  return requestId;
}
function control(
  device: string,
  action: string,
  parameters?: Record<string, unknown>,
) {
  const reason = commandReason(device, action);
  if (reason) {
    notice(reason);
    return;
  }
  const selected = ((topic("devices").items ?? []) as Choice[]).find(
    (x) => x.device === device && x.selected,
  );
  const session = current?.snapshot?.sessionId, profileId=(topic("administration").profile as {id?:string}|undefined)?.id,
    sequence = sequenceCommandIdentity(topic("sequence"));
  const execute = async () => {
    const now = ((topic("devices").items ?? []) as Choice[]).find(
      (x) => x.device === device && x.selected,
    );
    if (
      session !== current?.snapshot?.sessionId || profileId !== (topic("administration").profile as {id?:string}|undefined)?.id ||
      (device !== "sequence" && selected?.id !== now?.id) ||
      (device === "sequence" &&
        ["start", "reset", "load"].includes(action) &&
        sequence !== sequenceCommandIdentity(topic("sequence")))
    ) {
      notice(
        "The profile or sequence changed. Review it before sending the command.",
      );
      return;
    }
    const commandParameters = device === "sequence" && action === "start" && !setupOption.hidden && setupCheckbox.checked
      ? { ...parameters, skipValidation: true } : parameters;
    await send(device, action, selected?.id, commandParameters);
  };
  if ((device === "mount" && action === "stop") || action === "connect" || (device === "safetyMonitor" && action === "disconnect") || (device === "application" && action === "refresh")) {
    void execute();
    return;
  }
  write(
    "confirm-title",
    `${action === "disconnect" ? "Disconnect " + (selected?.name ?? device) : device === "sequence" && action === "start" ? "Start sequence" : device === "sequence" && action === "load" ? "Load sequence" : device === "sequence" && action === "reset" ? "Reset sequence" : device === "sequence" ? "Stop the sequence" : action.charAt(0).toUpperCase() + action.slice(1) + " " + (selected?.name ?? device)}?`,
  );
  write(
    "confirm-description",
    device === "sequence" && action === "start"
      ? ""
      : device === "sequence" && action === "load"
        ? `${parameters?.fileName ?? "Selected file"} · Replaces the current sequence and stays stopped.`
      : device === "sequence" && action === "stop"
        ? "Stop the sequence currently running in N.I.N.A., including its current wait."
        : device === "sequence" && action === "reset"
          ? "Clear sequence progress and return steps to their initial state. The sequence stays stopped."
        : action === "disconnect"
          ? "This disconnects the selected device in N.I.N.A."
          : Object.entries(parameters ?? {})
              .map(([k, v]) => `${k}: ${v}`)
              .join(" · "),
  );
  setupCheckbox.checked = false;
  setupOption.hidden = !(device === "sequence" && action === "start" &&
    ((topic("commands").items ?? []) as { device: string; action: string; parameters?: { name: string; type: string }[] }[])
      .some(x => x.device === "sequence" && x.action === "start" && x.parameters?.some(p => p.name === "skipValidation" && p.type === "boolean")));
  const isStart = device === "sequence" && action === "start";
  confirm.classList.toggle("sequence-start-dialog", isStart);
  el("confirm-description").hidden = isStart;
  setupHint.hidden = setupOption.hidden;
  write("confirm-send", isStart ? "▶ Start" : "Confirm");
  el("confirm-send").classList.toggle("danger", !isStart);
  confirmation = execute;
  confirm.showModal();
}
for (const device of ["camera", "mount", "filterWheel", "focuser", "rotator", "guider"])
  el(`${device}-action`).onclick = () =>
    control(device, topic(device).connected ? "disconnect" : "connect");
el("sequence-start").onclick = () => control("sequence", "start");
el("sequence-stop").onclick = () => control("sequence", "stop");
el("sequence-reset").onclick = () => control("sequence", "reset");
let ensuredSafetyContext="";
function renderActions() {
  const ensureContext=fresh()&&!preview?String(current?.snapshot?.sessionId)+":"+String((topic("administration").profile as {id?:string}|undefined)?.id):"";
  if(!ensureContext)ensuredSafetyContext="";
  if(ensureContext&&ensureContext!==ensuredSafetyContext&&((topic("commands").items??[]) as {device:string;action:string}[]).some(x=>x.device==="safetyMonitor"&&x.action==="ensureConnected")){
    ensuredSafetyContext=ensureContext;void send("safetyMonitor","ensureConnected");
  }
  for (const device of [
    "camera",
    "mount",
    "filterWheel",
    "focuser",
    "rotator",
    "guider",
  ]) {
    const action = topic(device).connected ? "disconnect" : "connect",
      reason = commandReason(device, action),
      button = el<HTMLButtonElement>(device + "-action");
    button.textContent = pending.has(device)
      ? "Working…"
      : action === "disconnect"
        ? "Disconnect"
        : "Connect";
    button.disabled = !!reason;
    button.title =
      reason || `${button.textContent} the active profile ${device}`;
  }
  for (const action of ["start", "stop", "reset"]) {
    const button = el<HTMLButtonElement>("sequence-" + action),
      reason = commandReason("sequence", action);
    button.disabled = !!reason;
    button.title = reason;
  }
  const startReason = commandReason("sequence", "start");
  write(
    "sequence-action-hint",
    replayMode ? "Replay · controls disabled" : preview
      ? "Preview · controls disabled"
      : topic("application").sequenceRunning
        ? "Sequence running"
        : startReason === "Acquisition or another command is active." ? "" : startReason,
  );
}
const updateDevicePanels = mountDevicePanels({
  states: () => current?.snapshot?.states ?? {},
  session: () => current?.snapshot?.sessionId ?? "",
  live: () => !preview && fresh(),
  reason: commandReason,
  run: control,
});
const updateSequenceLibrary = mountSequenceLibrary({states:()=>current?.snapshot?.states??{},session:()=>current?.snapshot?.sessionId??"",reason:commandReason,run:control,save:parameters=>send("sequence","save",undefined,parameters,true)});
const updateSequenceEditor = mountSequenceEditor({
  states: () => current?.snapshot?.states ?? {},
  session: () => current?.snapshot?.sessionId ?? "",
  live: () => !preview && fresh(),
  reason: commandReason,
  run: (device, action, parameters) => { void send(device, action, undefined, parameters); },
  schema: type => request("/api/v1/sequence/insertion-schema?type="+encodeURIComponent(type)),
  submit: parameters => send("sequence","insertDraft",undefined,parameters,true),
});
function updateClock() {
  write("local-clock", clockFormat.format(new Date(replayMode&&replayFrame?replayFrame.at:Date.now())));
  const observed = current?.snapshot?.states.camera?.observedAt;
  const age = observed ? (Date.now() - Date.parse(observed)) / 1000 : NaN;
  write(
    "observation-age",
    preview
      ? "Illustrative data"
      : !Number.isFinite(age)
        ? "Waiting for telemetry"
        : age < -5
          ? "Device clock differs"
          : `Observed ${Math.max(0, Math.floor(age))}s ago${fresh() ? "" : " · stale"}`,
  );
  if (!preview) {
    write(
      "connection",
      fresh()
        ? "Live"
        : socketReady && current?.bridge.connected
          ? "Stale"
          : "Offline",
    );
    el("connection").classList.toggle("offline", !fresh());
    document.body.dataset.transport = fresh() ? "live" : "offline";
  }
  if(replayMode){
    write("connection","REPLAY");el("connection").classList.remove("offline");document.body.dataset.transport="replay";
    write("observation-age",replayFrame?.gap?"No recorded telemetry":"Recorded · "+(replayFrame?time(new Date(replayFrame.lastTelemetryAt).toISOString()):"Loading"));
  }
  const readingsAvailable=replayMode?!!replayFrame&&!replayFrame.gap:preview||fresh();
  renderRoofStatus(current?.snapshot?.states ?? {},readingsAvailable);
  const roof = el<HTMLButtonElement>("roof-status");
  roof.disabled = true;
  roof.onclick = null;
  roof.title = String(topic("safetyConnection").error ?? "Roof monitoring stays connected");
  if(topic("safetyConnection").error && topic("safety").connected!==true)roof.textContent="Roof · retrying";

  updateExposureClock(readingsAvailable,replayMode?replayFrame?.at:undefined);
  updateSequenceClocks(readingsAvailable,replayMode?replayFrame?.at:undefined);
  renderActions();
  updateDevicePanels();
  updateSequenceEditor();
  updateSequenceLibrary();
}
// Local animation only; no polling, driver reads, or complete dashboard renders.
setInterval(updateClock, 250);
document.addEventListener("visibilitychange", updateClock);
function render() {
  const states = current?.snapshot?.states ?? {},
    live = replayMode?!!replayFrame&&!replayFrame.gap:preview||fresh();
  renderDashboard(states, live, current?.snapshot?.sessionId ?? "",replayMode?replayFrame?.at:undefined);
  const empty=el("image-empty");
  empty.querySelector("strong")!.textContent=replayMode?"No recorded image":"Waiting for an image";
  empty.querySelector("span")!.textContent=replayMode?"No image preview was recorded at this time.":"The next image preview will appear here.";
  el<HTMLImageElement>("latest-preview").alt=replayMode?"Recorded image preview":"Latest image from N.I.N.A.";
  const app = topic("application"),
    diagnostic = topic("diagnostics");
  write(
    "environment",
    preview
      ? "Illustrative preview"
      : app.environment === "simulation"
        ? "Simulation"
        : app.environment === "equipment"
          ? "Equipment"
          : "Environment unknown",
  );
  write("api-version", "Starfront Control");
  for (const [id, key] of [
    ["memory", "processPrivateBytes"],
    ["heap", "managedLiveEstimateBytes"],
    ["logs", "ownLogBytes"],
  ])
    write(
      id,
      numeric(diagnostic[key])
        ? format((diagnostic[key] as number) / 1e6, 1, " MB")
        : "—",
    );
  write("queued", diagnostic.pendingTopics);
  write("clients", diagnostic.clients);
  write("diagnostic-memory", numeric(diagnostic.processPrivateBytes) ? format(diagnostic.processPrivateBytes / 1e6, 1, " MB") : "—");
  write("diagnostic-heap", numeric(diagnostic.managedLiveEstimateBytes) ? format(diagnostic.managedLiveEstimateBytes / 1e6, 1, " MB") : "—");
  if (
    typeof diagnostic.observedAt === "string" &&
    diagnostic.observedAt !== lastMetric &&
    numeric(diagnostic.processPrivateBytes)
  ) {
    lastMetric = diagnostic.observedAt;
    samples.push({ at: lastMetric, bytes: diagnostic.processPrivateBytes });
    if (samples.length > 720) samples.shift();
    renderPlot();
  }
  const body = el<HTMLTableSectionElement>("operations");
  body.replaceChildren();
  const ops = operations();
  for (const op of ops)
    if (op.completedAt && pending.get(op.device) === op.requestId) {
      pending.delete(op.device);pendingActions.delete(op.device);
    }
  if (!ops.length) {
    const cell = body.insertRow().insertCell();
    cell.colSpan = 4;
    cell.textContent = "No operations reported";
  }
  for (const op of ops.slice(0, 12)) {
    const row = body.insertRow();
    row.title = op.requestId + (op.error ? ` · ${op.error}` : "");
    for (const value of [time(op.acceptedAt), op.device, op.action, op.status])
      row.insertCell().textContent = value;
  }
  updateClock();
}
function renderPlot() {
  const plot = el("memory-plot"),
    width = Math.max(350, plot.clientWidth);
  plot.setAttribute("viewBox", `0 0 ${width} 180`);
  el("plot-empty").setAttribute("x", String(width / 2));
  el("plot-empty").setAttribute(
    "visibility",
    samples.length ? "hidden" : "visible",
  );
  const max = Math.max(100e6, ...samples.map((x) => x.bytes)) * 1.15,
    grid = el("plot-grid");
  grid.replaceChildren();
  for (let i = 0; i <= 4; i++) {
    const y = 160 - i * 38;
    const line = document.createElementNS("http://www.w3.org/2000/svg", "line");
    for (const [key, value] of Object.entries({
      x1: "40",
      x2: String(width - 10),
      y1: String(y),
      y2: String(y),
    }))
      line.setAttribute(key, value);
    const label = document.createElementNS(
      "http://www.w3.org/2000/svg",
      "text",
    );
    label.setAttribute("x", "4");
    label.setAttribute("y", String(y + 4));
    label.textContent = String(Math.round((max * i) / 4 / 1e6));
    grid.append(line, label);
  }
  const first = samples[0]?.at,
    last = samples.at(-1)?.at;
  write("plot-start", time(first));
  write("plot-end", time(last));
  el("memory-line").setAttribute(
    "d",
    samples
      .map(
        (s, i) =>
          `${i ? "L" : "M"}${40 + ((Date.parse(s.at) - Date.parse(first!)) / Math.max(1, Date.parse(last!) - Date.parse(first!))) * (width - 50)},${160 - (s.bytes / max) * 152}`,
      )
      .join(" "),
  );
}
function connect() {
  const socket = new WebSocket(
    `${location.protocol === "https:" ? "wss:" : "ws:"}//${location.host}${gatewayBase}/socket`,
  );
  websocket = socket;
  socket.onopen = () => {
    socketReady = true;
    retry = 1000;
  };
  socket.onmessage = (event) => {
    if (socket !== websocket) return;
    try {
      current = JSON.parse(String(event.data)) as Envelope;
    } catch {
      notice("An invalid telemetry message was ignored.");
      return;
    }
    liveCurrent=current;
    if(replayMode){current=replayFrame?{bridge:{connected:false},snapshot:replayFrame.snapshot}:null;return;}
    if (current.snapshot && current.snapshot.sessionId !== currentSession) {
      if (currentSession && pending.size)
        notice(
          "N.I.N.A. restarted. Check Operations and equipment state before issuing another command.",
        );
      pending.clear();pendingActions.clear();
      currentSession = current.snapshot.sessionId;
      samples.length = 0;
      lastMetric = "";
    }
    render();
  };
  socket.onclose = () => {
    if (socket !== websocket) return;
    socketReady = false;
    if(liveCurrent)liveCurrent.bridge.connected=false;
    if (!replayMode&&current) current.bridge.connected = false;
    render();
    setTimeout(connect, retry);
    if (gatewayBase)
      void request("/auth/dashboard-session", {}).catch(() => {});
    retry = Math.min(10_000, retry * 2);
  };
}
async function start() {
  try {
    if (preview) {
      current = (await request("/preview.json")) as unknown as Envelope;
      const now = new Date().toISOString();
      for (const state of Object.values(current.snapshot!.states))
        state.observedAt = now;
      topic("camera").exposureEndTime = new Date(
        Date.now() + 117_000,
      ).toISOString();
      topic("diagnostics").observedAt = now;
      write("connection", "Preview");
      el("connection").classList.remove("offline");
      el("preview-banner").hidden = false;
      el("preview-link").hidden = true;
    } else {
      const ticket = new URLSearchParams(location.hash.slice(1)).get("session");
      if (ticket) {
        history.replaceState(null, "", location.pathname + location.search);
        await request("/auth/session", { ticket });
      }
      await request(
        gatewayBase ? "/auth/dashboard-session" : "/auth/local-session",
        {},
      );
      current = (await request("/api/v1/status")) as unknown as Envelope;
    }
    liveCurrent=current;
    el("notice").hidden = true;
    el("monitor").hidden = false;
    render();
    if (!preview) connect();
  } catch {
    notice("Connecting to the local dashboard…");
    setTimeout(() => void start(), 5000);
  }
}
mountReplay({
  read:(path,signal)=>request(path,undefined,signal),
  mode(active){replayMode=active;replayFrame=null;current=active?null:liveCurrent;samples.length=0;lastMetric="";confirm.close();document.querySelectorAll<HTMLDialogElement>("dialog[open]").forEach(d=>d.close());render();},
  frame(frame){if(replayFrame&&frame.at<replayFrame.at){samples.length=0;lastMetric="";}replayFrame=frame;current={bridge:{connected:false},snapshot:frame.snapshot};render();}
});
new ResizeObserver(renderPlot).observe(el("memory-plot"));
void start();

window.addEventListener("focus",()=>{ensuredSafetyContext="";renderActions();});
