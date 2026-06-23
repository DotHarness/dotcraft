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
