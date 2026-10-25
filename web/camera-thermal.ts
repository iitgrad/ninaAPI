export type ThermalSample = {
  at: number;
  temperature: number | null;
  power: number | null;
};
type HistoryStorage = Pick<Storage, "getItem" | "setItem">;
const storageKey = "starfront.camera-thermal.v1";
const maxAge = 3600000, maxSamples = 720;
export class ThermalHistory {
  samples: ThermalSample[] = [];
  private context = "";
  constructor(private storage?: HistoryStorage) {}
  private restore(context: string, now: number): ThermalSample[] {
    try {
      const raw = this.storage?.getItem(storageKey);
      if (!raw || raw.length > 100000) return [];
      const saved = JSON.parse(raw);
      if (saved.context !== context || !Array.isArray(saved.samples)) return [];
      let previous = -Infinity;
      return saved.samples.slice(-maxSamples).filter((s: ThermalSample) => {
        const valid = s && Number.isFinite(s.at) && s.at > previous && now-s.at <= maxAge && s.at-now <= 5000 &&
          (s.temperature === null || typeof s.temperature === "number" && Number.isFinite(s.temperature)) &&
          (s.power === null || typeof s.power === "number" && Number.isFinite(s.power) && s.power >= 0 && s.power <= 100);
        if (valid) previous = s.at;
        return valid;
      }).map((s: ThermalSample) => ({at:s.at,temperature:s.temperature,power:s.power}));
    } catch { return []; }
  }
  private save() {
    try { this.storage?.setItem(storageKey, JSON.stringify({context:this.context,samples:this.samples})); }
    catch { /* Storage may be unavailable or full; live telemetry still works. */ }
  }
  receive(
    context: string,
    at: number,
    live: boolean,
    temperature: unknown,
    power: unknown,
    now = Date.now(),
  ) {
    if (context !== this.context) {
      this.samples = this.restore(context, now);
      this.context = context;
    }
    const count = this.samples.length;
    this.samples = this.samples.filter(s => now-s.at <= maxAge && s.at-now <= 5000).slice(-maxSamples);
    if (count !== this.samples.length) this.save();
    if (
      !Number.isFinite(at) ||
      now - at > 15000 ||
      at - now > 5000 ||
      (this.samples.length && at - this.samples.at(-1)!.at < 5000)
    )
      return;
    const number = (v: unknown) =>
      typeof v === "number" && Number.isFinite(v) ? v : null;
    this.samples.push({
      at,
      temperature: live ? number(temperature) : null,
      power:
        live &&
        number(power) !== null &&
        Number(power) >= 0 &&
        Number(power) <= 100
          ? Number(power)
          : null,
    });
    this.samples = this.samples.slice(-maxSamples);
    this.save();
  }
}
export function thermalPaths(
  samples: ThermalSample[],
  key: "temperature" | "power",
  x: (n: number) => number,
  y: (n: number) => number,
) {
  let last = 0;
  return samples
    .map((s) => {
      const v = s[key];
      if (v === null) {
        last = 0;
        return "";
      }
      const result = `${last && s.at - last <= 15000 ? "L" : "M"}${x(s.at).toFixed(1)},${y(v).toFixed(1)}`;
      last = s.at;
      return result;
    })
    .join(" ");
}
