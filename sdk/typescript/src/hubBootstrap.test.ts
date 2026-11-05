import assert from "node:assert/strict";
import { existsSync, unlinkSync } from "node:fs";
import { mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { createServer } from "node:http";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test, { type TestContext } from "node:test";

import { HubClient, HubClientError, hubLockPath, type HubClientOptions } from "./hubClient.js";

function deferred<T = void>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((done) => { resolve = done; });
  return { promise, resolve };
}

async function fixture(t: TestContext) {
  const homeDir = await mkdtemp(join(tmpdir(), "dotcraft-bootstrap-"));
  const originalCwd = process.cwd();
  const originalFetch = globalThis.fetch;
  const lockPath = hubLockPath(homeDir);
  await mkdir(join(homeDir, ".craft", "hub"), { recursive: true });
  const cleanups: Array<() => Promise<void>> = [];
  t.after(async () => {
    for (const cleanup of cleanups.reverse()) await cleanup();
    globalThis.fetch = originalFetch;
    process.chdir(originalCwd);
    await rm(homeDir, { recursive: true, force: true, maxRetries: 20, retryDelay: 100 });
  });
  const writeOwner = async (apiBaseUrl = "http://127.0.0.1:49170") => {
    await writeFile(lockPath, JSON.stringify({ pid: process.pid, apiBaseUrl, token: "fixture-token" }));
  };
  return { homeDir, lockPath, writeOwner, cleanups };
}

async function startingHub(t: TestContext) {
  const base = await fixture(t);
  const started = deferred();
  const probing = deferred();
  const ready = deferred();
  const pids: number[] = [];
  const server = createServer((request, response) => {
    if (request.url?.startsWith("/started?")) {
      pids.push(Number(new URL(request.url, "http://localhost").searchParams.get("pid")));
      response.end();
      started.resolve();
    } else {
      probing.resolve();
      void ready.promise.then(() => response.end(JSON.stringify({
        hubVersion: "fixture", pid: pids[0], capabilities: {},
      })));
    }
  });
  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  const address = server.address();
  assert.ok(address && typeof address === "object");
  const apiBaseUrl = `http://127.0.0.1:${address.port}`;
  const spawnsPath = join(base.homeDir, "spawns");
  await writeFile(join(base.homeDir, "hub"), `
    const fs = require("node:fs");
    fs.appendFileSync(${JSON.stringify(spawnsPath)}, process.pid + "\\n");
    fs.writeFileSync(${JSON.stringify(base.lockPath)}, JSON.stringify({
      pid: process.pid, apiBaseUrl: ${JSON.stringify(apiBaseUrl)}, token: "fixture-token"
    }));
    require("node:http").get(${JSON.stringify(apiBaseUrl)} + "/started?pid=" + process.pid, (response) => response.resume());
    setInterval(() => {}, 1000);
  `);
  process.chdir(base.homeDir);
  base.cleanups.push(async () => {
    ready.resolve();
    for (const pid of pids) { try { process.kill(pid); } catch {} }
    server.closeAllConnections();
    await new Promise<void>((resolve) => server.close(() => resolve()));
  });
  const options: HubClientOptions = {
    homeDir: base.homeDir, executable: process.execPath, hubStartupTimeoutMs: 5_000,
  };
  return { ...base, started: started.promise, probing: probing.promise, ready, options, spawnsPath };
}

test("concurrent clients for one runtime share startup and its readiness probe", { timeout: 10_000 }, async (t) => {
  const hub = await startingHub(t);
  const pending = [
    new HubClient(hub.options).ensureHub(),
    new HubClient(hub.options).ensureHub(),
    new HubClient(hub.options).getStatus(),
  ];
  await hub.started;
  hub.ready.resolve();
  const results = await Promise.all(pending);
  assert.equal(results[0].pid, results[1].pid);
  assert.equal(results[0].pid, results[2].pid);
  assert.equal((await readFile(hub.spawnsPath, "utf8")).trim().split("\n").length, 1);
});

test("cancelling one client's lifetime does not cancel another caller's shared startup", { timeout: 10_000 }, async (t) => {
  const hub = await startingHub(t);
  const cancelled = new AbortController();
  const reason = new Error("window closed");
  const first = new HubClient({ ...hub.options, signal: cancelled.signal }).ensureHub();
  const rejected = assert.rejects(first, (error) => error === reason);
  const second = new HubClient(hub.options).ensureHub();
  await hub.probing;
  cancelled.abort(reason);
  await rejected;
  hub.ready.resolve();
  assert.equal((await second).version, "fixture");
  assert.equal((await readFile(hub.spawnsPath, "utf8")).trim().split("\n").length, 1);
});

test("an application abort before shared startup begins cannot spawn a Hub", async (t) => {
  const hub = await fixture(t);
  const lifetime = new AbortController();
  const reason = new Error("updating");
  const client = new HubClient({ homeDir: hub.homeDir, executable: process.execPath, signal: lifetime.signal });
  const calls = [client.ensureHub(), client.listProjects()];
  const rejected = calls.map((call) => assert.rejects(call, (error) => error === reason));
  lifetime.abort(reason);
  await Promise.all(rejected);
  assert.equal(existsSync(join(hub.homeDir, ".craft", "hub", "hub-startup.stderr.log")), false);
});

test("cancellation during a failed owner probe prevents a later replacement spawn", async (t) => {
  const hub = await fixture(t);
  await hub.writeOwner();
  const controller = new AbortController();
  const reason = new Error("update started");
  globalThis.fetch = (async () => {
    unlinkSync(hub.lockPath);
    controller.abort(reason);
    return new Response("", { status: 503 });
  }) as typeof fetch;
  await assert.rejects(
    new HubClient({ homeDir: hub.homeDir, executable: process.execPath }).ensureHub(controller.signal),
    (error) => error === reason,
  );
  assert.equal(existsSync(join(hub.homeDir, ".craft", "hub", "hub-startup.stderr.log")), false);
});

test("a cancelled flight cannot evict a replacement shared by new callers", async (t) => {
  const hub = await fixture(t);
  await hub.writeOwner();
  const firstProbe = deferred();
  const secondProbe = deferred();
  const ready = deferred<Response>();
  const controller = new AbortController();
  let probes = 0;
  globalThis.fetch = (async (_input: string | URL | Request, init?: RequestInit) => {
    if (++probes === 1) {
      firstProbe.resolve();
      return await new Promise<Response>((_resolve, reject) => {
        init!.signal!.addEventListener("abort", () => reject(init!.signal!.reason), { once: true });
      });
    }
    secondProbe.resolve();
    return await ready.promise;
  }) as typeof fetch;
  const client = new HubClient({ homeDir: hub.homeDir });
  const cancelled = client.ensureHub(controller.signal);
  const rejected = assert.rejects(cancelled, { name: "AbortError" });
  await firstProbe.promise;
  controller.abort();
  const replacement = client.ensureHub();
  await secondProbe.promise;
  const joined = new HubClient({ homeDir: hub.homeDir }).ensureHub();
  ready.resolve(new Response("{}"));
  await Promise.all([rejected, replacement, joined]);
  assert.equal(probes, 2);
});

test("ensureAppServer carries caller cancellation through the managed ensure request", async (t) => {
  const hub = await fixture(t);
  await hub.writeOwner();
  const requested = deferred();
  const controller = new AbortController();
  const reason = new Error("cancel ensure");
  globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
    if (String(input).endsWith("/v1/status")) return new Response("{}");
    requested.resolve();
    return await new Promise<Response>((_resolve, reject) => {
      init!.signal!.addEventListener("abort", () => reject(init!.signal!.reason), { once: true });
    });
  }) as typeof fetch;
  const pending = new HubClient({ homeDir: hub.homeDir }).ensureAppServer("/workspace", {}, controller.signal);
  await requested.promise;
  controller.abort(reason);
  await assert.rejects(pending, (error) => error === reason);
});

test("SSE cancellation also cancels its preceding Hub bootstrap", async (t) => {
  const hub = await fixture(t);
  await hub.writeOwner();
  const probing = deferred();
  const controller = new AbortController();
  let eventRequests = 0;
  globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
    if (String(input).endsWith("/v1/events")) eventRequests += 1;
    probing.resolve();
    return await new Promise<Response>((_resolve, reject) => {
      init!.signal!.addEventListener("abort", () => reject(init!.signal!.reason), { once: true });
    });
  }) as typeof fetch;
  const pending = new HubClient({ homeDir: hub.homeDir }).subscribeEvents(() => {}, controller.signal);
  await probing.promise;
  controller.abort();
  await assert.rejects(pending, { name: "AbortError" });
  assert.equal(eventRequests, 0);
});

test("the client lifetime cancels SSE delivery even with an independent subscription signal", async (t) => {
  const hub = await fixture(t);
  await hub.writeOwner();
  const lifetime = new AbortController();
  const reason = new Error("application stopping");
  const received: string[] = [];
  globalThis.fetch = (async (input: string | URL | Request) => {
    if (String(input).endsWith("/v1/status")) return new Response("{}");
    return new Response('data: {"kind":"first"}\n\ndata: {"kind":"second"}\n\n');
  }) as typeof fetch;
  await assert.rejects(new HubClient({ homeDir: hub.homeDir, signal: lifetime.signal }).subscribeEvents((event) => {
    received.push(event.kind);
    lifetime.abort(reason);
  }, new AbortController().signal), (error) => error === reason);
  assert.deepEqual(received, ["first"]);
});

test("getStatus reuses its readiness response without retaining stale health", async (t) => {
  const hub = await fixture(t);
  await hub.writeOwner();
  let probes = 0;
  globalThis.fetch = (async () => new Response(JSON.stringify({ hubVersion: String(++probes) }))) as typeof fetch;
  const client = new HubClient({ homeDir: hub.homeDir });
  assert.equal((await client.getStatus()).hubVersion, "1");
  assert.equal((await client.getStatus()).hubVersion, "2");
  assert.equal(probes, 2);
});

test("explicit binary policies do not share an incompatible bootstrap flight", async (t) => {
  const hub = await fixture(t);
  await hub.writeOwner();
  globalThis.fetch = (async () => new Response(JSON.stringify({ binaryPath: "/old/dotcraft" }))) as typeof fetch;
  const options = { homeDir: hub.homeDir, expectedExecutable: "/new/dotcraft" };
  const ignored = new HubClient({ ...options, binaryMatchPolicy: "ignore" }).ensureHub();
  await assert.rejects(new HubClient({ ...options, binaryMatchPolicy: "errorIfMismatch" }).ensureHub(),
    (error) => error instanceof HubClientError && error.code === "hubBinaryMismatch");
  assert.equal((await ignored).binaryPath, "/old/dotcraft");
});

test("early Hub exit retains a bounded redacted stderr tail instead of waiting for timeout", async (t) => {
  const hub = await fixture(t);
  await writeFile(join(hub.homeDir, "hub"), `
    process.stderr.write("x".repeat(6000) + "\\nStartup failed: Bearer bearer-secret https://name:password@localhost:1234/status?token=query-secret token=field-secret\\n");
    process.exit(23);
  `);
  process.chdir(hub.homeDir);
  await assert.rejects(new HubClient({
    homeDir: hub.homeDir, executable: process.execPath, hubStartupTimeoutMs: 5_000,
  }).ensureHub(), (error) => {
    assert.ok(error instanceof HubClientError);
    const details = error.details as Record<string, unknown>;
    assert.equal(error.code, "hubUnavailable");
    assert.equal(details.stage, "ready");
    assert.equal(details.failureKind, "processExited");
    assert.equal(details.exitCode, 23);
    assert.equal(typeof details.spawnedPid, "number");
    assert.ok(Number(details.elapsedMs) < 5_000);
    const stderr = String(details.recentStderr);
    assert.ok(stderr.includes("Startup failed:"));
    assert.ok(stderr.length <= 4096);
    for (const secret of ["bearer-secret", "password", "query-secret", "field-secret"]) assert.ok(!stderr.includes(secret));
    return true;
  });
});

test("a competing start's normal exit does not hide another published live Hub", async (t) => {
  const hub = await fixture(t);
  await writeFile(join(hub.homeDir, "hub"), `
    require("node:fs").writeFileSync(${JSON.stringify(hub.lockPath)}, JSON.stringify({
      pid: ${process.pid}, apiBaseUrl: "http://127.0.0.1:49170", token: "fixture-token"
    }));
    process.exit(0);
  `);
  process.chdir(hub.homeDir);
  globalThis.fetch = (async () => new Response("{}")) as typeof fetch;
  assert.equal((await new HubClient({ homeDir: hub.homeDir, executable: process.execPath }).ensureHub()).pid, process.pid);
});
