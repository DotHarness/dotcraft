import { resolve } from "node:path";

import type { HubClientOptions } from "./hubTypes.js";
import type { HubConnection } from "./hubBootstrap.js";

interface BootstrapFlight {
  controller: AbortController;
  pending: Promise<HubConnection>;
  waiters: number;
}

const flights = new Map<string, BootstrapFlight>();

export function shareHubBootstrap(
  options: HubClientOptions,
  lockPath: string,
  start: (signal: AbortSignal) => Promise<HubConnection>,
  signal?: AbortSignal,
): Promise<HubConnection> {
  signal?.throwIfAborted();
  const normalize = (path: string) => process.platform === "win32" ? resolve(path).toLowerCase() : resolve(path);
  const runtime = (value: string | undefined) => {
    const path = value?.trim();
    return path && /[/\\]/.test(path) ? normalize(path) : process.platform === "win32" ? path?.toLowerCase() : path;
  };
  const key = JSON.stringify([
    normalize(lockPath), runtime(options.executable) || "dotcraft", runtime(options.expectedExecutable),
    options.binaryMatchPolicy ?? "ignore", options.hubStartupTimeoutMs, options.hubShutdownTimeoutMs,
  ]);
  let flight = flights.get(key);
  if (!flight) {
    const controller = new AbortController();
    const created: BootstrapFlight = {
      controller,
      waiters: 0,
      pending: Promise.resolve().then(() => start(controller.signal)).finally(() => {
        if (flights.get(key) === created) flights.delete(key);
      }),
    };
    flights.set(key, created);
    flight = created;
  }

  const shared = flight;
  shared.waiters += 1;
  return new Promise<HubConnection>((resolve, reject) => {
    let done = false;
    const release = (): boolean => {
      if (done) return false;
      done = true;
      signal?.removeEventListener("abort", abort);
      shared.waiters -= 1;
      return true;
    };
    const abort = (): void => {
      if (!release()) return;
      if (shared.waiters === 0) {
        if (flights.get(key) === shared) flights.delete(key);
        shared.controller.abort(signal?.reason);
      }
      reject(signal?.reason);
    };
    signal?.addEventListener("abort", abort, { once: true });
    shared.pending.then(
      (result) => { if (release()) resolve(result); },
      (error: unknown) => { if (release()) reject(error); },
    );
  });
}
