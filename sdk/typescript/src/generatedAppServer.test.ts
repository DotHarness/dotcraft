import assert from "node:assert/strict";
import test from "node:test";

import { DotCraftWireClient } from "./client.js";
import {
  APP_SERVER_CONTRACT_HASH,
  APP_SERVER_METHOD_GROUPS,
  type ClientRequestMethods,
  type RuntimeDynamicToolDeclaration,
  type TokenUsageInfo,
} from "./generated/appserver/index.js";
import { type Transport, TransportClosed } from "./transport.js";

class GeneratedBindingTransport implements Transport {
  readonly written: Record<string, unknown>[] = [];
  private readonly queued: Record<string, unknown>[] = [];
  private readonly readers: Array<(message: Record<string, unknown>) => void> = [];
  private closed = false;

  push(message: Record<string, unknown>): void {
    const reader = this.readers.shift();
    if (reader) reader(message);
    else this.queued.push(message);
  }

  async readMessage(): Promise<Record<string, unknown>> {
    if (this.closed) throw new TransportClosed();
    const message = this.queued.shift();
    return message ?? await new Promise((resolve) => this.readers.push(resolve));
  }

  async writeMessage(message: Record<string, unknown>): Promise<void> {
    this.written.push(message);
  }

  async close(): Promise<void> {
    this.closed = true;
  }
}

test("generated method maps type and execute a low-level request", async () => {
  const transport = new GeneratedBindingTransport();
  const client = new DotCraftWireClient(transport);
  await client.start();

  const params: ClientRequestMethods["initialize"]["params"] = {
    clientInfo: { name: "fixture-client", version: "1.0" },
    capabilities: { threadSubscriptions: true },
  };
  const pending = client.request("initialize", params);
  const request = transport.written[0];
  assert.equal(request.method, "initialize");
  transport.push({
    jsonrpc: "2.0",
    id: request.id,
    result: {
      serverInfo: { name: "fixture-server", version: "1.0", protocolVersion: "1" },
      capabilities: { threadManagement: true, threadSubscriptions: true },
    },
  });

  const result = await pending;
  assert.equal(result.serverInfo.protocolVersion, "1");
  assert.ok(APP_SERVER_METHOD_GROUPS.clientRequests.includes("initialize"));
  assert.match(APP_SERVER_CONTRACT_HASH, /^[0-9a-f]{64}$/);
  await client.stop();
});

test("generated unions discriminate while opaque JSON remains open", () => {
  const declaration: RuntimeDynamicToolDeclaration = {
    type: "function",
    name: "lookup",
    description: "Lookup data",
    inputSchema: { type: "object", futureKeyword: [1, true, null] },
  };

  if (declaration.type === "function") {
    assert.equal(declaration.name, "lookup");
    const schema = declaration.inputSchema as { [key: string]: unknown };
    assert.deepEqual(schema.futureKeyword, [1, true, null]);
  } else {
    assert.fail("expected function declaration");
  }
});

test("generated safe integers use the TypeScript number wire type", () => {
  const usage: TokenUsageInfo = {
    inputTokens: Number.MAX_SAFE_INTEGER,
    totalTokens: 42,
  };

  assert.equal(usage.inputTokens, Number.MAX_SAFE_INTEGER);
  assert.equal(usage.totalTokens, 42);
});
