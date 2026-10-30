import assert from "node:assert/strict";
import { spawn, type ChildProcess } from "node:child_process";
import { mkdir, mkdtemp, readFile, rm, stat, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

import {
  HubClient,
  HubClientError,
  defaultChatWorkspacePath,
  ensureDefaultChatWorkspace,
  findSseBoundary,
  isLoopbackHost,
  parseHubBaseUrl,
  readHubLockFromPath,
} from "./hubClient.js";

test("Hub URL validation accepts only loopback HTTP base URLs with explicit port", () => {
  assert.equal(parseHubBaseUrl("http://127.0.0.1:4231/").origin, "http://127.0.0.1:4231");
  assert.equal(parseHubBaseUrl("http://[::1]:4231/").hostname, "[::1]");
  assert.equal(isLoopbackHost("localhost"), true);
  assert.equal(isLoopbackHost("127.12.1.9"), true);
  for (const url of [
    "https://127.0.0.1:4231/",
    "http://0.0.0.0:4231/",
    "http://192.168.1.10:4231/",
    "http://127.0.0.1:4231/v1/status",
    "http://127.0.0.1/",
  ]) {
    assert.throws(() => parseHubBaseUrl(url), HubClientError);
  }
});

test("Hub lock parser ignores missing, malformed, and incomplete locks", async () => {
  const dir = await mkdtemp(join(tmpdir(), "dotcraft-sdk-hub-lock-"));
  try {
    const lockPath = join(dir, "hub.lock");
    assert.equal(readHubLockFromPath(lockPath), null);

    await writeFile(lockPath, "{", "utf8");
    assert.equal(readHubLockFromPath(lockPath), null);

    await writeFile(lockPath, JSON.stringify({ pid: 1, apiBaseUrl: "http://127.0.0.1:1/" }), "utf8");
    assert.equal(readHubLockFromPath(lockPath), null);

    await writeFile(
      lockPath,
      JSON.stringify({ pid: 123, apiBaseUrl: "http://127.0.0.1:1/", token: "token", startedAt: "now" }),
      "utf8",
    );
    assert.deepEqual(readHubLockFromPath(lockPath), {
      pid: 123,
      apiBaseUrl: "http://127.0.0.1:1/",
      token: "token",
      startedAt: "now",
      version: "",
      binaryPath: null,
    });

    await writeFile(
      lockPath,
      JSON.stringify({
        pid: 456,
        apiBaseUrl: "http://127.0.0.1:2/",
        token: "token",
        startedAt: "now",
        version: "0.1.0",
        binaryPath: "/usr/local/bin/dotcraft",
      }),
      "utf8",
    );
    assert.deepEqual(readHubLockFromPath(lockPath), {
      pid: 456,
      apiBaseUrl: "http://127.0.0.1:2/",
      token: "token",
      startedAt: "now",
      version: "0.1.0",
      binaryPath: "/usr/local/bin/dotcraft",
    });
  } finally {
    await rm(dir, { recursive: true, force: true });
  }
});

test("SSE boundary finder handles LF and CRLF frames", () => {
  assert.deepEqual(findSseBoundary("data: one\n\nrest"), { index: 9, sequence: "\n\n" });
  assert.deepEqual(findSseBoundary("data: one\r\n\r\nrest"), { index: 9, sequence: "\r\n\r\n" });
  assert.equal(findSseBoundary("data: one\nstill-open"), null);
});

test("default Chat workspace helper creates skeleton without overwriting config", async () => {
  const dir = await mkdtemp(join(tmpdir(), "dotcraft-sdk-chat-workspace-"));
  try {
    const workspace = ensureDefaultChatWorkspace(dir);
    const craft = join(workspace, ".craft");
    const config = join(craft, "config.json");

    assert.equal(workspace, defaultChatWorkspacePath(dir));
    assert.equal(await readFile(config, "utf8"), "{}\n");

    await writeFile(config, "{\"keep\":true}\n", "utf8");
    ensureDefaultChatWorkspace(dir);

    assert.equal(await readFile(config, "utf8"), "{\"keep\":true}\n");
    assert.equal((await stat(join(craft, "memory"))).isDirectory(), true);
    assert.equal((await stat(join(craft, "skills"))).isDirectory(), true);
    assert.equal((await stat(join(craft, "security"))).isDirectory(), true);
  } finally {
    await rm(dir, { recursive: true, force: true });
  }
});

test("ensureDefaultChatAppServer posts concrete default workspace path", async () => {
  const dir = await mkdtemp(join(tmpdir(), "dotcraft-sdk-chat-ensure-"));
  const originalFetch = globalThis.fetch;
  try {
    await mkdir(join(dir, ".craft", "hub"), { recursive: true });
    await writeFile(
      join(dir, ".craft", "hub", "hub.lock"),
      JSON.stringify({
        pid: process.pid,
        apiBaseUrl: "http://127.0.0.1:49125",
        token: "hub-token",
        startedAt: "now",
      }),
      "utf8",
    );

    let capturedWorkspace: string | null = null;
    globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url === "http://127.0.0.1:49125/v1/status") {
        return new Response("{}", { status: 200 });
      }

      assert.equal(url, "http://127.0.0.1:49125/v1/appservers/ensure");
      assert.equal((init?.headers as Record<string, string>).Authorization, "Bearer hub-token");
      const body = JSON.parse(String(init?.body ?? "{}")) as { workspacePath?: string };
      capturedWorkspace = body.workspacePath ?? null;
      return new Response(
        JSON.stringify({
          workspacePath: capturedWorkspace,
          canonicalWorkspacePath: capturedWorkspace,
          state: "running",
          pid: 123,
          endpoints: { appServerWebSocket: "ws://127.0.0.1:5000/ws?token=x" },
          serviceStatus: {},
          serverVersion: "0.1",
          startedByHub: true,
        }),
        { status: 200, headers: { "content-type": "application/json" } },
      );
    }) as typeof fetch;

    const hub = new HubClient({ homeDir: dir });
    const response = await hub.ensureDefaultChatAppServer();
    const expectedWorkspace = defaultChatWorkspacePath(dir);

    assert.equal(capturedWorkspace, expectedWorkspace);
    assert.equal(response.workspacePath, expectedWorkspace);
    assert.equal(await readFile(join(expectedWorkspace, ".craft", "config.json"), "utf8"), "{}\n");
  } finally {
    globalThis.fetch = originalFetch;
    await rm(dir, { recursive: true, force: true });
  }
});

test("Hub management methods share structured models and authorization", async () => {
  const dir = await mkdtemp(join(tmpdir(), "dotcraft-sdk-hub-management-"));
  const originalFetch = globalThis.fetch;
  try {
    await mkdir(join(dir, ".craft", "hub"), { recursive: true });
    await writeFile(join(dir, ".craft", "hub", "hub.lock"), JSON.stringify({
      pid: process.pid,
      apiBaseUrl: "http://127.0.0.1:49127",
      token: "hub-token",
      binaryPath: "/opt/dotcraft",
    }), "utf8");
    const calls: Array<{ path: string; authorization?: string; body?: string }> = [];
    globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
      const url = new URL(input instanceof Request ? input.url : String(input));
      const headers = (init?.headers ?? {}) as Record<string, string>;
      calls.push({ path: url.pathname, authorization: headers.Authorization, body: typeof init?.body === "string" ? init.body : undefined });
      if (url.pathname === "/v1/status") {
        return new Response(JSON.stringify({
          hubVersion: "1",
          pid: process.pid,
          startedAt: "2026-01-01T00:00:00Z",
          statePath: "/tmp/state",
          apiBaseUrl: "http://127.0.0.1:49127",
          binaryPath: "/opt/dotcraft",
          capabilities: { appServerManagement: true, portManagement: true, events: true, notifications: true, tray: true },
        }), { status: 200 });
      }
      if (url.pathname === "/v1/appservers/by-workspace") return new Response("", { status: 404 });
      if (url.pathname === "/v1/appservers") return new Response("[]", { status: 200 });
      if (url.pathname.startsWith("/v1/services")) return new Response(JSON.stringify({
        serviceId: "oratorio", state: "running", pid: 42,
        endpoint: "http://127.0.0.1:5001", accessToken: "service-token",
      }), { status: 200 });
      return new Response(JSON.stringify({
        workspacePath: "/repo",
        canonicalWorkspacePath: "/repo",
        state: "running",
        endpoints: {},
        serviceStatus: {},
        startedByHub: true,
      }), { status: 200 });
    }) as typeof fetch;

    const client = new HubClient({ homeDir: dir });
    assert.equal(await client.getAppServerByWorkspace("/missing"), null);
    assert.deepEqual(await client.listAppServers(), []);
    assert.equal((await client.restartAppServer("/repo")).state, "running");
    assert.equal((await client.stopAppServer("/repo")).state, "running");
    assert.equal((await client.getStatus()).capabilities.appServerManagement, true);
    assert.equal((await client.ensureManagedService("oratorio")).serviceId, "oratorio");
    assert.equal((await client.ensureManagedService("oratorio", { executable: "/opt/dotcraft" })).serviceId, "oratorio");
    assert.equal((await client.getManagedService("oratorio")).state, "running");
    assert.equal((await client.restartManagedService("oratorio")).pid, 42);
    assert.equal((await client.restartManagedService("oratorio", "/opt/dotcraft")).pid, 42);
    assert.equal((await client.stopManagedService("oratorio")).state, "running");
    assert.ok(calls.filter((call) => call.path.startsWith("/v1/appservers")).every((call) => call.authorization === "Bearer hub-token"));
    assert.ok(calls.filter((call) => call.path.startsWith("/v1/services")).every((call) => call.authorization === "Bearer hub-token"));
    assert.deepEqual(calls.filter((call) => call.path === "/v1/services/ensure").map((call) => JSON.parse(call.body!)), [
      { serviceId: "oratorio", startIfMissing: true },
      { serviceId: "oratorio", executable: "/opt/dotcraft", startIfMissing: true },
    ]);
    assert.deepEqual(calls.filter((call) => call.path === "/v1/services/restart").map((call) => JSON.parse(call.body!)), [
      { serviceId: "oratorio" },
      { serviceId: "oratorio", executable: "/opt/dotcraft" },
    ]);
  } finally {
    globalThis.fetch = originalFetch;
    await rm(dir, { recursive: true, force: true });
  }
});

test("Hub binary mismatch policy returns structured details", async () => {
  const dir = await mkdtemp(join(tmpdir(), "dotcraft-sdk-hub-mismatch-"));
  const originalFetch = globalThis.fetch;
  try {
    await mkdir(join(dir, ".craft", "hub"), { recursive: true });
    await writeFile(join(dir, ".craft", "hub", "hub.lock"), JSON.stringify({
      pid: process.pid,
      apiBaseUrl: "http://127.0.0.1:49128",
      token: "hub-token",
      binaryPath: "/old/dotcraft",
    }), "utf8");
    globalThis.fetch = (async () => new Response(JSON.stringify({ binaryPath: "/old/dotcraft" }), { status: 200 })) as typeof fetch;

    await assert.rejects(
      new HubClient({
        homeDir: dir,
        expectedExecutable: "/new/dotcraft",
        binaryMatchPolicy: "errorIfMismatch",
      }).ensureHub(),
      (error: unknown) => error instanceof HubClientError
        && error.code === "hubBinaryMismatch"
        && (error.details as { actualExecutable?: string }).actualExecutable === "/old/dotcraft",
    );
  } finally {
    globalThis.fetch = originalFetch;
    await rm(dir, { recursive: true, force: true });
  }
});

function startIdleProcess(): ChildProcess {
  return spawn(process.execPath, ["-e", "setInterval(() => {}, 1000)"], { stdio: "ignore" });
}

async function writeHubLock(dir: string, lock: { pid: number; apiBaseUrl: string; binaryPath?: string }): Promise<void> {
  await mkdir(join(dir, ".craft", "hub"), { recursive: true });
  await writeFile(join(dir, ".craft", "hub", "hub.lock"), JSON.stringify({ ...lock, token: "hub-token" }), "utf8");
}

async function stopProcess(child: ChildProcess): Promise<void> {
  if (child.exitCode !== null || child.signalCode !== null) return;
  await new Promise((resolve) => {
    child.once("exit", resolve);
    child.kill();
  });
}

// Exits while the lock owner is alive, so only a client that waits for the owner to exit gets a Hub.
const FAKE_HUB_SCRIPT = `
const fs = require("node:fs");
const lockPath = process.env.DOTCRAFT_TEST_HUB_LOCK;
let owner = null;
try { owner = JSON.parse(fs.readFileSync(lockPath, "utf8")); } catch {}
const alive = (pid) => { try { process.kill(pid, 0); return true; } catch (error) { return error.code === "EPERM"; } };
if (owner && alive(owner.pid)) process.exit(0);
fs.writeFileSync(lockPath, JSON.stringify({ pid: process.pid, apiBaseUrl: "http://127.0.0.1:49141", token: "hub-token" }));
setInterval(() => {}, 1000);
`;

test("ensureHub starts a replacement only after a Hub that stopped answering exits", async () => {
  const dir = await mkdtemp(join(tmpdir(), "dotcraft-sdk-hub-handoff-"));
  const originalFetch = globalThis.fetch;
  const originalCwd = process.cwd();
  const lockPath = join(dir, ".craft", "hub", "hub.lock");
  const previous = startIdleProcess();
  try {
    await writeHubLock(dir, { pid: previous.pid!, apiBaseUrl: "http://127.0.0.1:49140" });
    // HubClient runs `<executable> hub`; with node as the executable that runs ./hub from the cwd.
    await writeFile(join(dir, "hub"), FAKE_HUB_SCRIPT, "utf8");
    process.chdir(dir);
    process.env.DOTCRAFT_TEST_HUB_LOCK = lockPath;
    globalThis.fetch = (async (input: string | URL | Request) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.startsWith("http://127.0.0.1:49141/")) return new Response("{}", { status: 200 });
      throw new TypeError("fetch failed");
    }) as typeof fetch;

    const options = { homeDir: dir, executable: process.execPath, hubStartupTimeoutMs: 5_000 };
    const first = new HubClient(options).ensureHub();
    const second = new HubClient(options).ensureHub();
    let settled = false;
    void first.then(() => { settled = true; }, () => { settled = true; });

    await new Promise((resolve) => setTimeout(resolve, 600));
    assert.equal(settled, false);
    await stopProcess(previous);

    const hub = await first;
    assert.equal(hub.apiBaseUrl, "http://127.0.0.1:49141");
    assert.equal(await second, hub);
  } finally {
    globalThis.fetch = originalFetch;
    process.chdir(originalCwd);
    delete process.env.DOTCRAFT_TEST_HUB_LOCK;
    await stopProcess(previous);
    const replacement = readHubLockFromPath(lockPath);
    if (replacement && replacement.pid !== previous.pid) {
      try { process.kill(replacement.pid); } catch { /* already gone */ }
    }
    await rm(dir, { recursive: true, force: true, maxRetries: 20, retryDelay: 100 });
  }
});

test("ensureHub reports a previous Hub that never finishes shutting down", async () => {
  const dir = await mkdtemp(join(tmpdir(), "dotcraft-sdk-hub-stalled-"));
  const originalFetch = globalThis.fetch;
  const previous = startIdleProcess();
  try {
    await writeHubLock(dir, { pid: previous.pid!, apiBaseUrl: "http://127.0.0.1:49142" });
    globalThis.fetch = (async () => {
      throw new TypeError("fetch failed");
    }) as typeof fetch;

    await assert.rejects(
      new HubClient({ homeDir: dir, hubShutdownTimeoutMs: 300 }).ensureHub(),
      (error: unknown) => error instanceof HubClientError
        && error.code === "hubUnavailable"
        && (error.details as { pid?: number }).pid === previous.pid,
    );
  } finally {
    globalThis.fetch = originalFetch;
    await stopProcess(previous);
    await rm(dir, { recursive: true, force: true });
  }
});

test("ensureHub never reuses a mismatched Hub it asked to stop", async () => {
  const dir = await mkdtemp(join(tmpdir(), "dotcraft-sdk-hub-restart-"));
  const originalFetch = globalThis.fetch;
  const previous = startIdleProcess();
  try {
    await writeHubLock(dir, { pid: previous.pid!, apiBaseUrl: "http://127.0.0.1:49143", binaryPath: "/old/dotcraft" });
    let shutdownRequested = false;
    globalThis.fetch = (async (input: string | URL | Request) => {
      const url = new URL(input instanceof Request ? input.url : String(input));
      if (url.port === "49143" && url.pathname === "/v1/shutdown") {
        shutdownRequested = true;
        return new Response(JSON.stringify({ ok: true }), { status: 200 });
      }
      const binaryPath = url.port === "49143" ? "/old/dotcraft" : "/new/dotcraft";
      return new Response(JSON.stringify({ binaryPath }), { status: 200 });
    }) as typeof fetch;

    const ensured = new HubClient({
      homeDir: dir,
      executable: join(dir, "missing-dotcraft"),
      expectedExecutable: "/new/dotcraft",
      binaryMatchPolicy: "restartIfMismatch",
    }).ensureHub();
    await new Promise((resolve) => setTimeout(resolve, 600));
    assert.equal(shutdownRequested, true);

    await stopProcess(previous);
    await writeHubLock(dir, { pid: process.pid, apiBaseUrl: "http://127.0.0.1:49144", binaryPath: "/new/dotcraft" });

    assert.equal((await ensured).apiBaseUrl, "http://127.0.0.1:49144");
  } finally {
    globalThis.fetch = originalFetch;
    await stopProcess(previous);
    await rm(dir, { recursive: true, force: true });
  }
});

test("Mobile methods read, toggle, pair, and revoke through the authorized Hub API", async () => {
  const dir = await mkdtemp(join(tmpdir(), "dotcraft-sdk-hub-mobile-"));
  const originalFetch = globalThis.fetch;
  try {
    await mkdir(join(dir, ".craft", "hub"), { recursive: true });
    await writeFile(join(dir, ".craft", "hub", "hub.lock"), JSON.stringify({
      pid: process.pid,
      apiBaseUrl: "http://127.0.0.1:49130",
      token: "hub-token",
    }), "utf8");

    const state = {
      state: "on",
      port: 47610,
      addresses: ["192.168.1.20"],
      devices: [{
        deviceId: "dev_001",
        displayName: "iPhone 16",
        platform: "ios",
        osVersion: "18.6",
        appVersion: "0.8.1",
        pairedAt: "2026-10-03T08:00:00Z",
        lastSeenAt: null,
        connected: false,
      }],
    };
    const calls: Array<{ method?: string; path: string; authorization?: string }> = [];
    globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
      const url = new URL(input instanceof Request ? input.url : String(input));
      const headers = (init?.headers ?? {}) as Record<string, string>;
      calls.push({ method: init?.method, path: url.pathname, authorization: headers.Authorization });
      if (url.pathname === "/v1/status") {
        return new Response(JSON.stringify({ capabilities: {} }), { status: 200 });
      }
      if (url.pathname === "/v1/mobile/pairings") {
        return new Response(JSON.stringify({
          error: { code: "gatewayOff", message: "Phone access is off.", details: {} },
        }), { status: 409 });
      }
      if (url.pathname.startsWith("/v1/mobile/devices/")) {
        return new Response(null, { status: 204 });
      }
      return new Response(JSON.stringify(state), { status: 200 });
    }) as typeof fetch;

    const client = new HubClient({ homeDir: dir });
    assert.equal((await client.getMobile()).devices[0].deviceId, "dev_001");
    assert.equal((await client.enableMobile()).state, "on");
    await client.disableMobile();
    await assert.rejects(
      client.createMobilePairing(),
      (error: unknown) => error instanceof HubClientError && error.code === "gatewayOff",
    );
    assert.equal(await client.revokeMobileDevice("dev/001"), undefined);

    const mobileCalls = calls.filter((call) => call.path.startsWith("/v1/mobile"));
    assert.ok(mobileCalls.every((call) => call.authorization === "Bearer hub-token"));
    assert.deepEqual(
      mobileCalls.map((call) => [call.method, call.path]),
      [
        ["GET", "/v1/mobile"],
        ["POST", "/v1/mobile/enable"],
        ["POST", "/v1/mobile/disable"],
        ["POST", "/v1/mobile/pairings"],
        ["DELETE", "/v1/mobile/devices/dev%2F001"],
      ],
    );
  } finally {
    globalThis.fetch = originalFetch;
    await rm(dir, { recursive: true, force: true });
  }
});

test("Mobile relay methods set and clear the relay through the authorized Hub API", async () => {
  const dir = await mkdtemp(join(tmpdir(), "dotcraft-sdk-hub-mobile-relay-"));
  const originalFetch = globalThis.fetch;
  try {
    await mkdir(join(dir, ".craft", "hub"), { recursive: true });
    await writeFile(join(dir, ".craft", "hub", "hub.lock"), JSON.stringify({
      pid: process.pid,
      apiBaseUrl: "http://127.0.0.1:49131",
      token: "hub-token",
    }), "utf8");

    const calls: Array<{ method?: string; path: string; authorization?: string; body?: string }> = [];
    globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
      const url = new URL(input instanceof Request ? input.url : String(input));
      const headers = (init?.headers ?? {}) as Record<string, string>;
      calls.push({
        method: init?.method,
        path: url.pathname,
        authorization: headers.Authorization,
        body: typeof init?.body === "string" ? init.body : undefined,
      });
      const relay = init?.method === "PUT" ? { url: "wss://relay.example.com", state: "connecting" } : null;
      return new Response(JSON.stringify({ state: "on", port: 47610, addresses: [], devices: [], relay }), { status: 200 });
    }) as typeof fetch;

    const client = new HubClient({ homeDir: dir });
    const set = await client.setMobileRelay("wss://relay.example.com", "relay-secret");
    assert.deepEqual(set.relay, { url: "wss://relay.example.com", state: "connecting" });
    assert.equal((await client.clearMobileRelay()).relay, null);

    const relayCalls = calls.filter((call) => call.path === "/v1/mobile/relay");
    assert.ok(relayCalls.every((call) => call.authorization === "Bearer hub-token"));
    assert.deepEqual(relayCalls.map((call) => call.method), ["PUT", "DELETE"]);
    assert.deepEqual(JSON.parse(relayCalls[0]?.body ?? "{}"), {
      url: "wss://relay.example.com",
      token: "relay-secret",
    });
  } finally {
    globalThis.fetch = originalFetch;
    await rm(dir, { recursive: true, force: true });
  }
});
