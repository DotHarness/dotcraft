import { resolve } from "node:path";

import { safeStartupFailure, startHubProcess, type HubStartupProcess, type StartupFailure } from "./hubStartupProcess.js";

import type { HubClientOptions, HubLockInfo, HubStatusResponse } from "./hubTypes.js";

const DEFAULT_STARTUP_TIMEOUT_MS = 15_000;
const DEFAULT_SHUTDOWN_TIMEOUT_MS = 30_000;
const PROBE_TIMEOUT_MS = 2_000;
const POLL_MS = 200;

type BinaryMismatch = { expectedExecutable: string; actualExecutable: string | null };
export interface HubConnection {
  hub: HubLockInfo;
  status: HubStatusResponse;
}

type ProbeResult = (HubConnection | { hub: null; status?: never }) & {
  httpStatus?: number;
  error?: StartupFailure;
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
  cancellation?: AbortSignal,
): Promise<ProbeResult> {
  cancellation?.throwIfAborted();
  try {
    validateBaseUrl(info.apiBaseUrl);
    const timeout = AbortSignal.timeout(Math.max(1, Math.floor(timeoutMs)));
    const signal = cancellation ? AbortSignal.any([cancellation, timeout]) : timeout;
    const response = await fetch(`${info.apiBaseUrl}/v1/status`, { signal });
    if (!response.ok) return { hub: null, httpStatus: response.status };
    const status = await response.json() as HubStatusResponse | null;
    if (!status) return { hub: null, httpStatus: response.status };
    return {
      hub: {
        ...info,
        version: typeof status?.hubVersion === "string" ? status.hubVersion : info.version,
        binaryPath: typeof status?.binaryPath === "string" ? status.binaryPath : info.binaryPath,
      },
      status,
      httpStatus: response.status,
    };
  } catch (error) {
    cancellation?.throwIfAborted();
    return { hub: null, error: safeStartupFailure(error) };
  }
}

export async function ensureLocalHub(
  options: HubClientOptions,
  deps: BootstrapDependencies,
  signal: AbortSignal,
): Promise<HubConnection> {
  signal.throwIfAborted();
  const startedAt = Date.now();
  const shutdownBudgetMs = options.hubShutdownTimeoutMs ?? DEFAULT_SHUTDOWN_TIMEOUT_MS;
  const startupBudgetMs = options.hubStartupTimeoutMs ?? DEFAULT_STARTUP_TIMEOUT_MS;
  const shutdownDeadline = startedAt + shutdownBudgetMs;
  let stopping: { pid: number; mismatch: BinaryMismatch } | null = null;
  let startupStartedAt: number | null = null;
  let child: HubStartupProcess | null = null;
  let lastOwner: HubLockInfo | null = null;
  let lastToken: string | undefined;
  let lastProbe: Omit<ProbeResult, "hub" | "status"> | undefined;

  const details = (
    stage: "owner-wait" | "mismatch-shutdown" | "spawn" | "ready",
    mismatch?: BinaryMismatch,
  ) => {
    const stderr = child?.recentStderr();
    const recentStderr = lastToken ? stderr?.replaceAll(lastToken, "[redacted]") : stderr;
    return {
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
      ...(recentStderr ? { recentStderr } : {}),
    };
  };

  try {
    for (;;) {
      signal.throwIfAborted();
      const lock = deps.readLock();
      if (lock?.token) lastToken = lock.token;
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
        const probe = await probeLocalHub(owner, deps.validateBaseUrl, Math.min(PROBE_TIMEOUT_MS, deadline - Date.now()), signal);
        const { hub: live, status, ...diagnostic } = probe;
        lastProbe = diagnostic;
        if (live) lastOwner = live;
        if (live && startupStartedAt !== null) return { hub: live, status };
        if (live) {
          const mismatch = binaryMismatch(options, live);
          if (!mismatch || options.binaryMatchPolicy === "ignore" || !options.binaryMatchPolicy) return { hub: live, status };
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
          const timeout = AbortSignal.timeout(Math.max(1, Math.floor(remainingMs)));
          try {
            await deps.shutdown(live, AbortSignal.any([signal, timeout]));
          } catch (error) {
            signal.throwIfAborted();
            const timedOut = timeout.aborted;
            throw deps.error(
              timedOut ? "hubMismatchShutdownTimeout" : "hubMismatchShutdownFailed",
              timedOut
                ? "Hub uses a different executable and its shutdown request timed out."
                : "Hub uses a different executable and could not be stopped.",
              { ...details("mismatch-shutdown", mismatch), shutdownError: safeStartupFailure(error) },
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
        child = startHubProcess(options.executable?.trim() || "dotcraft", deps.lockPath, signal);
      }

      if (child?.spawnError) {
        throw deps.error("hubUnavailable", "DotCraft Hub could not be started.", details("spawn"));
      }
      if (child?.exitCode !== undefined && (!owner || owner.pid === child.pid)) {
        throw deps.error("hubUnavailable", "DotCraft Hub exited before becoming ready.", {
          ...details("ready"), failureKind: "processExited",
        });
      }
      if (startupStartedAt !== null && Date.now() >= startupStartedAt + startupBudgetMs) {
        throw deps.error("hubUnavailable", "DotCraft Hub could not be started.", {
          ...details("ready"), failureKind: "timeout",
        });
      }
      const deadline = startupStartedAt === null ? shutdownDeadline : startupStartedAt + startupBudgetMs;
      await waitForNextProbe(
        Math.max(1, Math.min(POLL_MS, deadline - Date.now())), signal,
        child?.exitCode === undefined ? child?.changed : undefined,
      );
    }
  } finally {
    child?.dispose();
  }
}

async function waitForNextProbe(ms: number, signal: AbortSignal, changed?: Promise<void>): Promise<void> {
  signal.throwIfAborted();
  await new Promise<void>((resolve, reject) => {
    const finish = (): void => { cleanup(); resolve(); };
    const abort = (): void => { cleanup(); reject(signal.reason); };
    const timer = setTimeout(finish, ms);
    const cleanup = (): void => {
      clearTimeout(timer);
      signal.removeEventListener("abort", abort);
    };
    signal.addEventListener("abort", abort, { once: true });
    void changed?.then(finish);
  });
  signal.throwIfAborted();
}

function ownerDetails(owner: HubLockInfo) {
  return {
    pid: owner.pid,
    ...(typeof owner.startedAt === "string" ? { startedAt: owner.startedAt } : {}),
    ...(typeof owner.version === "string" ? { version: owner.version } : {}),
    ...(typeof owner.binaryPath === "string" ? { binaryPath: owner.binaryPath } : {}),
  };
}

function binaryMismatch(options: HubClientOptions, hub: HubLockInfo): BinaryMismatch | null {
  const expected = options.expectedExecutable?.trim();
  if (!expected) return null;
  const actual = hub.binaryPath?.trim() || null;
  const normalize = (path: string) => process.platform === "win32" ? resolve(path).toLowerCase() : resolve(path);
  if (actual && normalize(actual) === normalize(expected)) return null;
  return { expectedExecutable: expected, actualExecutable: actual };
}
