import { test, expect } from "bun:test";
import { ThermalHistory, thermalPaths } from "../../web/camera-thermal";
test("thermal history is bounded, throttled and scoped to host session and camera", () => {
  const h = new ThermalHistory(),
    now = Date.now();
  for (let i = 0; i < 10000; i++)
    h.receive("a", now + i * 1000, true, -10, 65, now + i * 1000);
  expect(h.samples.length).toBeLessThanOrEqual(720);
  expect(h.samples.at(-1)!.at - h.samples[0].at).toBeLessThanOrEqual(3600000);
  h.receive("b", now + 10000000, true, 5, 20, now + 10000000);
  expect(h.samples).toHaveLength(1);
  expect(h.samples[0].temperature).toBe(5);
});
test("offline or invalid readings make gaps, not fabricated zeros; stale samples rejected", () => {
  const h = new ThermalHistory(),
    n = Date.now();
  h.receive("a", n, true, -10, 50, n);
  h.receive("a", n + 5000, false, -11, 60, n + 5000);
  h.receive("a", n + 10000, true, NaN, 101, n + 10000);
  h.receive("a", n - 100000, true, 9, 9, n);
  expect(h.samples).toHaveLength(3);
  expect(h.samples[1].temperature).toBeNull();
  expect(h.samples[2].temperature).toBeNull();
  expect(h.samples[2].power).toBeNull();
});
test("SVG paths do not join disconnected or long telemetry gaps", () => {
  const samples = [
    { at: 1, temperature: 1, power: 1 },
    { at: 5001, temperature: 2, power: 2 },
    { at: 10001, temperature: null, power: null },
    { at: 15001, temperature: 3, power: 3 },
    { at: 40001, temperature: 4, power: 4 },
  ];
  const p = thermalPaths(
    samples,
    "temperature",
    (n) => n,
    (n) => n,
  );
  expect(p.match(/M/g)).toHaveLength(3);
  expect(p.match(/L/g)).toHaveLength(1);
  expect(p).not.toContain("NaN");
});
