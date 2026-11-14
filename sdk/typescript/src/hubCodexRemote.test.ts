import assert from "node:assert/strict";
import { createServer } from "node:http";
import { mkdir, mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { HubClient, HubClientError, type HubCodexRemoteStatus, type HubEvent } from "./hub.js";

test("Codex remote controls and events use the authenticated Hub contract", async () => {
  const homeDir = await mkdtemp(join(tmpdir(), "dotcraft-codex-hub-"));
  const calls: Array<{ method: string; path: string; authorization: string | undefined }> = [];
  let signedIn = false;
  let state: HubCodexRemoteStatus["state"] = "off";
  const status = (): HubCodexRemoteStatus => ({ state, signedIn, hostName: "Test host" });
  const pairing = { pairingUrl: "https://example.test/pair", manualCode: null, expiresAt: "2026-12-01T00:00:00Z" };
  const server = createServer((request, response) => {
    const path = request.url!;
    calls.push({ method: request.method!, path, authorization: request.headers.authorization });
    response.setHeader("Content-Type", "application/json");
    if (path === "/v1/status") {
      response.end(JSON.stringify({ capabilities: { codexRemote: true } }));
    } else if (path === "/v1/codex-remote/enable" && !signedIn) {
      response.writeHead(409);
      response.end(JSON.stringify({ error: { code: "codexRemoteSignInRequired", message: "Sign in to ChatGPT." } }));
    } else if (path === "/v1/codex-remote/enable") {
      state = "connecting";
      response.end(JSON.stringify(status()));
    } else if (path === "/v1/codex-remote/disable") {
      state = "off";
      response.end(JSON.stringify(status()));
    } else if (path === "/v1/codex-remote/pairings") {
      response.end(JSON.stringify(pairing));
    } else if (path === "/v1/events") {
      state = "on";
      response.writeHead(200, { "Content-Type": "text/event-stream" });
      response.end([
        { kind: "codexRemote.stateChanged", at: "now", data: status() },
        { kind: "codexRemote.paired", at: "now", data: {} },
      ].map((event) => `data: ${JSON.stringify(event)}\n\n`).join(""));
    } else {
      response.end(JSON.stringify(status()));
    }
  });
  try {
    await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
    const address = server.address() as { port: number };
    await mkdir(join(homeDir, ".craft", "hub"), { recursive: true });
    await writeFile(join(homeDir, ".craft", "hub", "hub.lock"), JSON.stringify({
      pid: process.pid, apiBaseUrl: `http://127.0.0.1:${address.port}`, token: "test-hub-bearer",
    }));
    const client = new HubClient({ homeDir });
    assert.equal((await client.getCodexRemote()).signedIn, false);
    await assert.rejects(client.enableCodexRemote(), (error: unknown) =>
      error instanceof HubClientError && error.code === "codexRemoteSignInRequired");
    signedIn = true;
    assert.equal((await client.enableCodexRemote()).state, "connecting");
    await client.createCodexRemotePairing();
    const events: HubEvent[] = [];
    let connected = false;
    await client.subscribeEvents((event) => {
      assert.equal(connected, true);
      events.push(event);
    }, new AbortController().signal, () => { connected = true; });
    assert.deepEqual(events.map((event) => event.kind), ["codexRemote.stateChanged", "codexRemote.paired"]);
    assert.equal((await client.disableCodexRemote()).state, "off");
    assert.ok(calls.filter((call) => call.path !== "/v1/status").every((call) => call.authorization === "Bearer test-hub-bearer"));
    assert.deepEqual(calls.filter((call) => call.path.startsWith("/v1/codex-remote")).map(({ method, path }) => [method, path]), [
      ["GET", "/v1/codex-remote"], ["POST", "/v1/codex-remote/enable"],
      ["POST", "/v1/codex-remote/enable"], ["POST", "/v1/codex-remote/pairings"],
      ["POST", "/v1/codex-remote/disable"],
    ]);
  } finally {
    server.closeAllConnections();
    await new Promise<void>((resolve) => server.close(() => resolve()));
    await rm(homeDir, { recursive: true, force: true });
  }
});
