import assert from "node:assert/strict";
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

test("Hub URL validation accepts loopback HTTP with explicit port", () => {
  assert.equal(parseHubBaseUrl("http://127.0.0.1:4231/").origin, "http://127.0.0.1:4231");
  assert.equal(parseHubBaseUrl("http://[::1]:4231/").hostname, "[::1]");
  assert.equal(isLoopbackHost("localhost"), true);
  assert.equal(isLoopbackHost("127.12.1.9"), true);
});

test("Hub URL validation rejects non-loopback or non-base URLs", () => {
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
    assert.equal((await client.ensureManagedService("oratorio", { executable: "/opt/oratorio" })).serviceId, "oratorio");
    assert.equal((await client.getManagedService("oratorio")).state, "running");
    assert.equal((await client.restartManagedService("oratorio", "/opt/oratorio")).pid, 42);
    assert.equal((await client.stopManagedService("oratorio")).state, "running");
    assert.ok(calls.filter((call) => call.path.startsWith("/v1/appservers")).every((call) => call.authorization === "Bearer hub-token"));
    assert.ok(calls.filter((call) => call.path.startsWith("/v1/services")).every((call) => call.authorization === "Bearer hub-token"));
    assert.match(calls.find((call) => call.path === "/v1/services/ensure")?.body ?? "", /"executable":"\/opt\/oratorio"/);
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

test("Satellite methods list, invite, and revoke through the authorized Hub API", async () => {
  const dir = await mkdtemp(join(tmpdir(), "dotcraft-sdk-hub-satellites-"));
  const originalFetch = globalThis.fetch;
  try {
    await mkdir(join(dir, ".craft", "hub"), { recursive: true });
    await writeFile(join(dir, ".craft", "hub", "hub.lock"), JSON.stringify({
      pid: process.pid,
      apiBaseUrl: "http://127.0.0.1:49129",
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
      if (url.pathname === "/v1/status") {
        return new Response(JSON.stringify({
          hubVersion: "1",
          pid: process.pid,
          startedAt: "2026-01-01T00:00:00Z",
          statePath: "/tmp/state",
          apiBaseUrl: "http://127.0.0.1:49129",
          capabilities: {
            appServerManagement: true,
            portManagement: true,
            events: true,
            notifications: true,
            tray: true,
            satellites: true,
          },
        }), { status: 200 });
      }
      if (url.pathname === "/v1/satellites") {
        return new Response(JSON.stringify([{
          peerId: "peer_001",
          displayName: "Studio PC",
          online: true,
          machineName: "STUDIO-PC",
          operatingSystem: "Windows",
          userName: "designer",
          buildVersion: "0.6.2",
          workspaces: [{
            workspaceId: "workspace_001",
            path: "D:/example/game-client",
            busy: true,
            busyOwner: "other",
            leaseExpiresAt: "2026-01-01T00:00:00+00:00",
          }],
          pairedAt: "2026-01-01T00:00:00+00:00",
          lastSeenAt: null,
        }]), { status: 200 });
      }
      if (url.pathname === "/v1/satellites/invites") {
        return new Response(JSON.stringify({
          inviteId: "invite_001",
          url: "http://studio-pc:47600/satellite/join/invite_001",
          expiresAt: "2026-01-02T00:00:00+00:00",
        }), { status: 200 });
      }
      return new Response(JSON.stringify({ revoked: true }), { status: 200 });
    }) as typeof fetch;

    const client = new HubClient({ homeDir: dir });
    assert.equal((await client.getStatus()).capabilities.satellites, true);

    const satellites = await client.listSatellites();
    assert.equal(satellites.length, 1);
    assert.equal(satellites[0].peerId, "peer_001");
    assert.equal(satellites[0].workspaces[0].busyOwner, "other");

    const invite = await client.createSatelliteInvite({
      name: "Studio PC",
      ttlHours: 24,
      purpose: "art review",
    });
    assert.equal(invite.inviteId, "invite_001");

    await client.createSatelliteInvite({ name: "Studio PC" });
    await client.revokeSatellite("peer/001");

    const satelliteCalls = calls.filter((call) => call.path.startsWith("/v1/satellites"));
    assert.ok(satelliteCalls.every((call) => call.authorization === "Bearer hub-token"));
    const inviteCalls = satelliteCalls.filter((call) => call.path === "/v1/satellites/invites");
    assert.deepEqual(
      JSON.parse(inviteCalls[0]?.body ?? "{}"),
      { name: "Studio PC", ttlHours: 24, purpose: "art review" },
    );
    assert.deepEqual(JSON.parse(inviteCalls[1]?.body ?? "{}"), { name: "Studio PC" });
    const revoke = satelliteCalls.find((call) => call.method === "DELETE");
    assert.equal(revoke?.path, "/v1/satellites/peer%2F001");
  } finally {
    globalThis.fetch = originalFetch;
    await rm(dir, { recursive: true, force: true });
  }
});
