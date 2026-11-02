import { spawn } from "node:child_process";
import { resolve } from "node:path";

import type { HubClientOptions, HubLockInfo, HubStatusResponse } from "./hubClient.js";

const DEFAULT_STARTUP_TIMEOUT_MS = 15_000;
const DEFAULT_SHUTDOWN_TIMEOUT_MS = 30_000;
const PROBE_TIMEOUT_MS = 2_000;
const POLL_MS = 200;

type BinaryMismatch = { expectedExecutable: string; actualExecutable: string | null };
type Failure = { name?: string; code?: string };
type ProbeResult = {
  hub: HubLockInfo | null;
  httpStatus?: number;
  error?: Failure;
};
type SpawnState = {
  pid?: number;
  spawnError?: Failure;
  exitCode?: number | null;
  signal?: NodeJS.Signals | null;
  dispose(): void;
};

interface BootstrapDependencies {
  lockPath: string;
  readLock(): HubLockInfo | null;
  validateBaseUrl(value: string): unknown;
  shutdown(hub: HubLockInfo, signal: AbortSignal): Promise<unknown>;
  error(code: string, message: string, details: unknown): Error;
}

export function isProcessAlive(pid: number): boolean {
  if (!Number.isInteger(pid) || pid <= 0) return false;
  try {
    process.kill(pid, 0);
    return true;
  } catch (error) {
    return (error as NodeJS.ErrnoException).code === "EPERM";
  }
}

export async function probeLocalHub(
  info: HubLockInfo,
  validateBaseUrl: (value: string) => unknown,
  timeoutMs = PROBE_TIMEOUT_MS,
): Promise<ProbeResult> {
  try {
    validateBaseUrl(info.apiBaseUrl);
    const signal = AbortSignal.timeout(Math.max(1, Math.floor(timeoutMs)));
    const response = await fetch(`${info.apiBaseUrl}/v1/status`, { signal });
    if (!response.ok) return { hub: null, httpStatus: response.status };
    let status: Partial<HubStatusResponse> | null = null;
    try {
      status = await response.json() as Partial<HubStatusResponse>;
    } catch (error) {
      if (signal.aborted) throw error;
      // A status response can be empty during startup races.
    }
    return {
      hub: {
        ...info,
        version: typeof status?.hubVersion === "string" ? status.hubVersion : info.version,
        binaryPath: typeof status?.binaryPath === "string" ? status.binaryPath : info.binaryPath,
      },
      httpStatus: response.status,
    };
  } catch (error) {
    return { hub: null, error: safeFailure(error) };
  }
}

export async function ensureLocalHub(
  options: HubClientOptions,
  deps: BootstrapDependencies,
): Promise<HubLockInfo> {
  const startedAt = Date.now();
  const shutdownBudgetMs = options.hubShutdownTimeoutMs ?? DEFAULT_SHUTDOWN_TIMEOUT_MS;
  const startupBudgetMs = options.hubStartupTimeoutMs ?? DEFAULT_STARTUP_TIMEOUT_MS;
  const shutdownDeadline = startedAt + shutdownBudgetMs;
  let stopping: { pid: number; mismatch: BinaryMismatch } | null = null;
  let startupStartedAt: number | null = null;
  let child: SpawnState | null = null;
  let lastOwner: HubLockInfo | null = null;
  let lastProbe: Omit<ProbeResult, "hub"> | undefined;

  const details = (
    stage: "owner-wait" | "mismatch-shutdown" | "spawn" | "ready",
    mismatch?: BinaryMismatch,
  ) => ({
    stage,
    elapsedMs: Date.now() - (startupStartedAt ?? startedAt),
    budgetMs: startupStartedAt === null ? shutdownBudgetMs : startupBudgetMs,
    lockPath: deps.lockPath,
    ...(lastOwner ? ownerDetails(lastOwner) : {}),
    ...(options.executable ? { executable: options.executable } : {}),
    ...(options.expectedExecutable ? { expectedExecutable: options.expectedExecutable } : {}),
    ...mismatch,
    ...(lastProbe ? { lastProbe } : {}),
    ...(child?.pid !== undefined ? { spawnedPid: child.pid } : {}),
    ...(child?.spawnError ? { spawnError: child.spawnError } : {}),
    ...(child?.exitCode !== undefined ? { exitCode: child.exitCode, signal: child.signal } : {}),
  });

  try {
    for (;;) {
      const lock = deps.readLock();
      const owner = lock && isProcessAlive(lock.pid) ? lock : null;
      if (owner && owner.pid === stopping?.pid) {
        if (Date.now() >= shutdownDeadline) {
          throw deps.error(
            "hubMismatchShutdownTimeout",
            "Hub uses a different executable and did not stop after shutdown.",
            details("owner-wait", stopping.mismatch),
          );
        }
      } else if (owner) {
        lastOwner = owner;
        const deadline = startupStartedAt === null ? shutdownDeadline : startupStartedAt + startupBudgetMs;
        const probe = await probeLocalHub(owner, deps.validateBaseUrl, Math.min(PROBE_TIMEOUT_MS, deadline - Date.now()));
        const { hub: live, ...diagnostic } = probe;
        lastProbe = diagnostic;
        if (live) lastOwner = live;
        if (live && startupStartedAt !== null) return live;
        if (live) {
          const mismatch = binaryMismatch(options, live);
          if (!mismatch || options.binaryMatchPolicy === "ignore" || !options.binaryMatchPolicy) return live;
          if (options.binaryMatchPolicy === "errorIfMismatch") {
            throw deps.error("hubBinaryMismatch", "Hub is running from a different executable.", mismatch);
          }
          const remainingMs = shutdownDeadline - Date.now();
          if (remainingMs <= 0) {
            throw deps.error(
              "hubMismatchShutdownTimeout",
              "Hub uses a different executable and could not be stopped before the shutdown deadline.",
              details("mismatch-shutdown", mismatch),
            );
          }
          const signal = AbortSignal.timeout(Math.max(1, Math.floor(remainingMs)));
          try {
            await deps.shutdown(live, signal);
          } catch (error) {
            const timedOut = signal.aborted;
            throw deps.error(
              timedOut ? "hubMismatchShutdownTimeout" : "hubMismatchShutdownFailed",
              timedOut
                ? "Hub uses a different executable and its shutdown request timed out."
                : "Hub uses a different executable and could not be stopped.",
              { ...details("mismatch-shutdown", mismatch), shutdownError: safeFailure(error) },
            );
          }
          stopping = { pid: live.pid, mismatch };
        } else if (startupStartedAt === null && Date.now() >= shutdownDeadline) {
          throw deps.error(
            "hubUnavailable",
            "The previous DotCraft Hub is still shutting down.",
            details("owner-wait"),
          );
        }
      } else if (startupStartedAt === null) {
        startupStartedAt = Date.now();
        child = startHub(options.executable?.trim() || "dotcraft");
      }

      if (child?.spawnError) {
        throw deps.error("hubUnavailable", "DotCraft Hub could not be started.", details("spawn"));
      }
      if (startupStartedAt !== null && Date.now() >= startupStartedAt + startupBudgetMs) {
        throw deps.error("hubUnavailable", "DotCraft Hub could not be started.", details("ready"));
      }
      const deadline = startupStartedAt === null ? shutdownDeadline : startupStartedAt + startupBudgetMs;
      await new Promise((resolve) => setTimeout(resolve, Math.max(1, Math.min(POLL_MS, deadline - Date.now()))));
    }
  } finally {
    child?.dispose();
  }
}

function startHub(executable: string): SpawnState {
  const state: SpawnState = { dispose() {} };
  const isDll = executable.toLowerCase().endsWith(".dll");
  try {
    const child = spawn(isDll ? "dotnet" : executable, isDll ? [executable, "hub"] : ["hub"], {
      detached: true,
      stdio: "ignore",
      windowsHide: true,
    });
    state.pid = child.pid;
    const onError = (error: Error) => { state.spawnError = safeFailure(error); };
    const onExit = (code: number | null, signal: NodeJS.Signals | null) => {
      state.exitCode = code;
      state.signal = signal;
    };
    child.on("error", onError);
    child.on("exit", onExit);
    child.unref();
    state.dispose = () => {
      child.off("error", onError);
      child.off("exit", onExit);
      child.on("error", () => {});
    };
  } catch (error) {
    state.spawnError = safeFailure(error);
  }
  return state;
}

function ownerDetails(owner: HubLockInfo) {
  return {
    pid: owner.pid,
    ...(typeof owner.startedAt === "string" ? { startedAt: owner.startedAt } : {}),
    ...(typeof owner.version === "string" ? { version: owner.version } : {}),
    ...(typeof owner.binaryPath === "string" ? { binaryPath: owner.binaryPath } : {}),
  };
}

function safeFailure(error: unknown): Failure {
  if (!error || typeof error !== "object") return {};
  const failure = error as { name?: unknown; code?: unknown; cause?: { code?: unknown } };
  const name = identifier(failure.name);
  const code = identifier(failure.code) ?? identifier(failure.cause?.code);
  return { ...(name ? { name } : {}), ...(code ? { code } : {}) };
}

function identifier(value: unknown): string | undefined {
  return typeof value === "string" && /^[a-zA-Z][a-zA-Z0-9_]{0,63}$/.test(value) ? value : undefined;
}

function binaryMismatch(options: HubClientOptions, hub: HubLockInfo): BinaryMismatch | null {
  const expected = options.expectedExecutable?.trim();
  if (!expected) return null;
  const actual = hub.binaryPath?.trim() || null;
  const normalize = (path: string) => process.platform === "win32" ? resolve(path).toLowerCase() : resolve(path);
  if (actual && normalize(actual) === normalize(expected)) return null;
  return { expectedExecutable: expected, actualExecutable: actual };
}
