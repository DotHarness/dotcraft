import assert from "node:assert/strict";
import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

import {
  HubClientError,
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
