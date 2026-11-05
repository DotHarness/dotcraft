import { spawn } from "node:child_process";
import { closeSync, fstatSync, mkdirSync, openSync, readSync } from "node:fs";
import { dirname, join } from "node:path";

const STDERR_LIMIT_BYTES = 4096;

export type StartupFailure = { name?: string; code?: string };

export interface HubStartupProcess {
  pid?: number;
  spawnError?: StartupFailure;
  exitCode?: number | null;
  signal?: NodeJS.Signals | null;
  changed: Promise<void>;
  recentStderr(): string | undefined;
  dispose(): void;
}

export function startHubProcess(executable: string, lockPath: string, signal: AbortSignal): HubStartupProcess {
  signal.throwIfAborted();
  let changed!: () => void;
  const state: HubStartupProcess = {
    changed: new Promise<void>((resolve) => { changed = resolve; }),
    recentStderr: () => undefined,
    dispose() {},
  };
  let stderrFd: number | undefined;
  try {
    mkdirSync(dirname(lockPath), { recursive: true });
    const stderrPath = join(dirname(lockPath), "hub-startup.stderr.log");
    // Append preserves diagnostics when another caller or a previous Hub still owns a writer.
    stderrFd = openSync(stderrPath, "a", 0o600);
    const stderrStart = fstatSync(stderrFd).size;
    state.recentStderr = () => readStderrTail(stderrPath, stderrStart);
    signal.throwIfAborted();
    const isDll = executable.toLowerCase().endsWith(".dll");
    const child = spawn(isDll ? "dotnet" : executable, isDll ? [executable, "hub"] : ["hub"], {
      detached: true,
      stdio: ["ignore", "ignore", stderrFd],
      windowsHide: true,
    });
    state.pid = child.pid;
    const onError = (error: Error): void => { state.spawnError = safeStartupFailure(error); changed(); };
    const onExit = (code: number | null, signal: NodeJS.Signals | null): void => {
      state.exitCode = code;
      state.signal = signal;
      changed();
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
    signal.throwIfAborted();
    state.spawnError = safeStartupFailure(error);
    changed();
  } finally {
    if (stderrFd !== undefined) closeSync(stderrFd);
  }
  return state;
}

function readStderrTail(path: string, start: number): string | undefined {
  let fd: number | undefined;
  try {
    fd = openSync(path, "r");
    const end = fstatSync(fd).size;
    const from = Math.max(start, end - STDERR_LIMIT_BYTES);
    if (from >= end) return undefined;
    const bytes = Buffer.alloc(end - from);
    const length = readSync(fd, bytes, 0, bytes.length, from);
    return redactStartupStderr(bytes.subarray(0, length).toString("utf8")).slice(-STDERR_LIMIT_BYTES).trim() || undefined;
  } catch {
    return undefined;
  } finally {
    if (fd !== undefined) closeSync(fd);
  }
}

function redactStartupStderr(value: string): string {
  return value.replace(/Bearer\s+\S+/giu, "Bearer [redacted]")
    .replace(/(?:https?|wss?):\/\/[^\s"<>]+/giu, (value) => {
      try {
        const url = new URL(value);
        url.username = "";
        url.password = "";
        url.search = "";
        url.hash = "";
        return url.toString();
      } catch { return "[invalid URL]"; }
    })
    .replace(/((?:token|api[_-]?key|authorization|secret|password)["']?\s*[:=]\s*["']?)[^\s"',;]+/giu, "$1[redacted]");
}

export function safeStartupFailure(error: unknown): StartupFailure {
  if (!error || typeof error !== "object") return {};
  const failure = error as { name?: unknown; code?: unknown; cause?: { code?: unknown } };
  const name = identifier(failure.name);
  const code = identifier(failure.code) ?? identifier(failure.cause?.code);
  return { ...(name ? { name } : {}), ...(code ? { code } : {}) };
}

function identifier(value: unknown): string | undefined {
  return typeof value === "string" && /^[a-zA-Z][a-zA-Z0-9_]{0,63}$/.test(value) ? value : undefined;
}
