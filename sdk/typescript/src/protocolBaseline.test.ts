import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

import { DotCraftWireClient } from "./client.js";
import { type Transport, TransportClosed } from "./transport.js";

interface FixtureCase {
  name: string;
  messages: Record<string, unknown>[];
}

interface FixtureDocument {
  version: number;
  cases: FixtureCase[];
}

class FixtureTransport implements Transport {
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
    if (message) return message;
    return await new Promise((resolve) => this.readers.push(resolve));
  }

  async writeMessage(_message: Record<string, unknown>): Promise<void> {}

  async close(): Promise<void> {
    this.closed = true;
  }
}

const fixtures = JSON.parse(
  readFileSync(
    new URL("../../../specs/protocols/fixtures/appserver-v1/messages.json", import.meta.url),
    "utf8",
  ),
) as FixtureDocument;

function fixtureCase(name: string): FixtureCase {
  const value = fixtures.cases.find((item) => item.name === name);
  assert.ok(value, `missing fixture case: ${name}`);
  return value;
}

test("shared AppServer fixtures expose portable lifecycle and callback cases", () => {
  assert.equal(fixtures.version, 1);
  for (const name of [
    "initialize",
    "thread-start-response-before-notification",
    "turn-start-and-complete",
    "approval-callback",
    "user-input-callback",
    "dynamic-tool-callback",
    "structured-error",
    "opaque-mcp-result",
    "core-domain-catalog",
    "mcp-elicitation-callback",
    "app-binding",
    "automation",
    "teams",
    "acp-callbacks",
    "node-repl-callback",
    "external-channel",
  ]) {
    assert.ok(fixtureCase(name).messages.length > 0);
  }
});

test("wire client preserves the shared unknown-notification fixture", async () => {
  const transport = new FixtureTransport();
  const client = new DotCraftWireClient(transport);
  await client.start();

  const received = new Promise<Record<string, unknown>>((resolve) => {
    client.onRaw("fixture/unknownNotification", resolve);
  });
  transport.push(fixtureCase("unknown-notification").messages[0]);

  const params = await received;
  assert.equal(params.preserveMe, true);
  assert.deepEqual(params.future, { nested: [1, "two", null] });
  await client.stop();
});

test("raw TypeScript consumers retain opaque MCP fields", () => {
  const result = fixtureCase("opaque-mcp-result").messages[1].result as Record<string, unknown>;
  assert.deepEqual(result.futureResultField, { kept: true });
  assert.deepEqual(result.structuredContent, { count: 1, futureShape: ["kept"] });
});
