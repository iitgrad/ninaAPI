type Field = { name: string; type: 'number' | 'integer' | 'boolean' | 'string'; required: boolean; minimum?: number; maximum?: number };
type Definition = { device: string; action: string; interrupt: boolean; parameters: Field[] };
type Choice = { device: string; id: string; name: string; selected: boolean; allowed: boolean };
export function mountControls(send: (body: unknown, dryRun: boolean) => Promise<unknown>) {
  const panel = document.createElement('section'); panel.className = 'panel system-controls';
  panel.innerHTML = '<h2>System controls</h2><p>Commands use the active N.I.N.A. profile. Results appear in Operations.</p><form><label>Device <select name="device"></select></label> <label>Action <select name="action"></select></label><div class="control-fields"></div><label class="control-device">Explicit device <select name="deviceId"></select></label><p><button type="button" class="validate">Validate</button> <button type="submit">Run command</button></p><output aria-live="polite"></output></form>';
  (document.getElementById('tools-controls') ?? document.querySelector('main'))!.append(panel);
  const details = document.createElement('details'); details.className = 'command-details'; details.innerHTML = '<summary>Command data and device status</summary><pre style="white-space:pre-wrap;overflow-wrap:anywhere;max-height:32rem;overflow:auto"></pre><img alt="Requested N.I.N.A. screenshot" style="max-width:100%" hidden>';
  panel.append(details);
  let lastDetails = '', lastScreenshot = '';
  const download = document.createElement('a'); download.textContent = 'Download latest saved image'; download.hidden = true; panel.append(download);
  const form = panel.querySelector('form')!;
  const device = form.elements.namedItem('device') as HTMLSelectElement;
  const action = form.elements.namedItem('action') as HTMLSelectElement;
  const deviceId = form.elements.namedItem('deviceId') as HTMLSelectElement;
  const fields = panel.querySelector('.control-fields')!;
  const output = panel.querySelector('output')!;
  let definitions: Definition[] = [], choices: Choice[] = [], signature = '', live = false, submitting = false;
  const definition = () => definitions.find(x => x.device === device.value && x.action === action.value);
  function rebuildFields() {
    fields.replaceChildren();
    for (const field of definition()?.parameters ?? []) {
      const label = document.createElement('label'); label.textContent = `${field.name}${field.required ? '' : ' (optional)'} `;
      const input = document.createElement(field.type === 'boolean' ? 'select' : 'input') as HTMLInputElement | HTMLSelectElement;
      input.name = `param-${field.name}`;
      if (input instanceof HTMLSelectElement) { input.add(new Option('Choose…', '')); input.add(new Option('Yes', 'true')); input.add(new Option('No', 'false')); }
      else { input.type = (field.type === 'number' || field.type === 'integer') ? 'number' : 'text'; input.maxLength = 128; if ((field.type === 'number' || field.type === 'integer')) { input.step = field.type === 'integer' ? '1' : 'any'; if (field.minimum != null) input.min = String(field.minimum); if (field.maximum != null) input.max = String(field.maximum); } }
      input.required = field.required; label.append(input); fields.append(label);
    }
    panel.querySelector<HTMLElement>('.control-device')!.hidden = action.value !== 'connect';
    deviceId.replaceChildren(new Option('Choose explicit device…', ''));
    for (const choice of choices.filter(x => x.device === device.value)) { const option = new Option(choice.name, choice.id); option.disabled = !choice.allowed; deviceId.add(option); }
    deviceId.value = choices.find(x => x.device === device.value && x.selected && x.allowed)?.id ?? '';
    deviceId.required = action.value === 'connect';
  }
  function rebuildActions() { action.replaceChildren(...definitions.filter(x => x.device === device.value).map(x => new Option(x.action, x.action))); rebuildFields(); }
  device.addEventListener('change', rebuildActions); action.addEventListener('change', rebuildFields);
  async function execute(dryRun: boolean) {
    if (!live || submitting || !form.reportValidity()) return;
    const parameters: Record<string, unknown> = {};
    for (const field of definition()?.parameters ?? []) { const value = (form.elements.namedItem(`param-${field.name}`) as HTMLInputElement).value; if (value !== '') parameters[field.name] = (field.type === 'number' || field.type === 'integer') ? Number(value) : field.type === 'boolean' ? value === 'true' : value; }
    submitting = true; output.textContent = dryRun ? 'Validating…' : 'Submitting…';
    try { const result = await send({ requestId: crypto.randomUUID(), device: device.value, action: action.value, deviceId: action.value === 'connect' ? deviceId.value : undefined, parameters }, dryRun) as {requestId?: string}; output.textContent = dryRun ? 'Valid. No command executed.' : `Accepted. Request ${result.requestId}. Watch Operations for the result.`; }
    catch (e) { output.textContent = e instanceof Error ? e.message : 'Command failed. Check Operations before retrying.'; }
    finally { submitting = false; }
  }
  form.addEventListener('submit', e => { e.preventDefault(); void execute(false); });
  panel.querySelector('.validate')!.addEventListener('click', () => { void execute(true); });
  return (states: Record<string, { value: Record<string, unknown> }>, connected: boolean) => {
    live = connected;
    const data = JSON.stringify(Object.fromEntries(['administration','dome','flatDevice','switch','solve','logTail'].filter(key => states[key]).map(key => [key,states[key].value])),null,2);
    if (data !== lastDetails) { details.querySelector('pre')!.textContent = data; lastDetails = data; }
    const jpeg = states.screenshot?.value.jpeg;
    if (typeof jpeg === 'string' && jpeg !== lastScreenshot) { const image = details.querySelector('img')!; image.src = 'data:image/jpeg;base64,' + jpeg; image.hidden = false; lastScreenshot = jpeg; }
    const imageId = states.image?.value.id;
    download.hidden = !connected || typeof imageId !== 'string';
    if (typeof imageId === 'string') { download.href = '/api/v1/images/' + encodeURIComponent(imageId); download.download = 'starfront-image.fits'; }

    const nextDefinitions = (states.commands?.value.items ?? []) as Definition[];
    choices = (states.devices?.value.items ?? []) as Choice[];
    const next = JSON.stringify([nextDefinitions, choices]);
    if (signature !== next) {
      const oldDevice = device.value, oldAction = action.value;
      const catalogChanged = JSON.stringify(definitions) !== JSON.stringify(nextDefinitions);
      definitions = nextDefinitions; signature = next;
      if (catalogChanged) { device.replaceChildren(...[...new Set(definitions.map(x => x.device))].map(x => new Option(x, x))); if (definitions.some(x => x.device === oldDevice)) device.value = oldDevice; rebuildActions(); if (definitions.some(x => x.device === device.value && x.action === oldAction)) { action.value = oldAction; rebuildFields(); } }
      else if (action.value === 'connect') { const previous = deviceId.value; rebuildFields(); deviceId.value = previous; }
    }
    for (const button of panel.querySelectorAll<HTMLButtonElement>('button')) button.disabled = !live || submitting;
  };
}
