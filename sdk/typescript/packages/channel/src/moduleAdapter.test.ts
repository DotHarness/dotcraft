import assert from "node:assert/strict";
import { mkdtemp, mkdir, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

import { ChannelAdapter } from "./adapter.js";
import type { ChannelChatDescriptor, ChannelDeliveryCapabilities } from "./capability.js";
import { ConfigValidationError, ModuleChannelAdapter, resolveConfigPath, resolveModuleStatePath, resolveModuleTempPath } from "./moduleAdapter.js";
import type { WorkspaceContext } from "./module.js";
import { TransportClosed, type Transport } from "@dotcraft/sdk/wire";

class MockTransport implements Transport {
  readonly written: Record<string, unknown>[] = [];
  private closed = false;
  private readonly queue: Record<string, unknown>[] = [];
  private readonly waiting: Array<{
    resolve: (msg: Record<string, unknown>) => void;
    reject: (error: unknown) => void;
  }> = [];

  async readMessage(): Promise<Record<string, unknown>> {
    if (this.closed) throw new TransportClosed();
    if (this.queue.length > 0) return this.queue.shift()!;
    return await new Promise<Record<string, unknown>>((resolve, reject) => {
      this.waiting.push({ resolve, reject });
    });
  }

  async writeMessage(msg: Record<string, unknown>): Promise<void> {
    if (this.closed) throw new TransportClosed();
    this.written.push(msg);
    const method = String(msg.method ?? "");
    if (method === "initialize") {
      this.push({
        jsonrpc: "2.0",
        id: msg.id as string | number,
        result: {
          serverInfo: {
            name: "dotcraft",
            version: "test",
            protocolVersion: "1.0",
          },
          capabilities: {},
        },
      });
    }
  }

  async close(): Promise<void> {
    this.closed = true;
    const error = new TransportClosed();
    for (const waiter of this.waiting.splice(0)) {
      waiter.reject(error);
    }
  }

  private push(msg: Record<string, unknown>): void {
    const waiter = this.waiting.shift();
    if (waiter) {
      waiter.resolve(msg);
      return;
    }
    this.queue.push(msg);
  }
}

class LifecycleAdapter extends ChannelAdapter {
  constructor(transport: Transport = new MockTransport()) {
    super(transport, "test-channel", "test-client", "0.0.0");
  }

  async onDeliver(_target: string, _content: string, _metadata: Record<string, unknown>): Promise<boolean> {
    return true;
  }

  async onApprovalRequest(_request: Record<string, unknown>): Promise<string> {
    return "cancel";
  }

  protected getDisplayName(): string {
    return "Test";
  }

  protected getDeliveryCapabilities(): ChannelDeliveryCapabilities {
    return { text: { format: "plain" }, targets: { group: false, direct: "none" } };
  }

  protected getChatDescriptor(): ChannelChatDescriptor {
    return { kind: "direct", membership: "shared" };
  }
}

class TestModuleAdapter extends ModuleChannelAdapter<{ wsUrl: string }> {
  buildTransportCalls = 0;

  constructor() {
    super("module-channel", "module-client", "0.0.0");
  }

  protected override getConfigFileName(_context: WorkspaceContext): string {
    return "module.json";
  }

  protected override validateConfig(rawConfig: unknown): asserts rawConfig is { wsUrl: string } {
    if (typeof rawConfig !== "object" || rawConfig === null) {
      throw new ConfigValidationError("Config must be an object.");
    }
    if (!("wsUrl" in rawConfig) || typeof (rawConfig as { wsUrl?: unknown }).wsUrl !== "string") {
      throw new ConfigValidationError("Missing wsUrl.", ["wsUrl"]);
    }
  }

  protected override buildTransportFromConfig(_config: { wsUrl: string }): Transport {
    this.buildTransportCalls += 1;
    return new MockTransport();
  }

  async onDeliver(_target: string, _content: string, _metadata: Record<string, unknown>): Promise<boolean> {
    return true;
  }

  async onApprovalRequest(_request: Record<string, unknown>): Promise<string> {
    return "cancel";
  }

  protected getDisplayName(): string {
    return "Test";
  }

  protected getDeliveryCapabilities(): ChannelDeliveryCapabilities {
    return { text: { format: "plain" }, targets: { group: false, direct: "none" } };
  }

  protected getChatDescriptor(): ChannelChatDescriptor {
    return { kind: "direct", membership: "shared" };
  }

  cacheThreadForTest(identityKey: string, threadId: string): void {
    this.threadResolver.setCachedThread(identityKey, threadId);
  }

  hasCachedThreadForTest(identityKey: string): boolean {
    return this.threadResolver.getCachedThreadId(identityKey) !== undefined;
  }

  protected override getRuntimeAdditionalContext() {
    return {
      "test.runtime": {
        kind: "application" as const,
        value: "connection-owned test context",
      },
    };
  }
}

test("ChannelAdapter start and stop emit lifecycle transitions and declare channel capabilities", async () => {
  const transport = new MockTransport();
  const adapter = new LifecycleAdapter(transport);
  const transitions: string[] = [];
  adapter.onStatusChange((status) => transitions.push(status));

  await adapter.start();
  assert.equal(adapter.getStatus(), "ready");
  await adapter.stop();
  assert.equal(adapter.getStatus(), "stopped");
  assert.deepEqual(transitions, ["starting", "ready", "stopped"]);

  const initialize = transport.written.find((msg) => msg.method === "initialize");
  const capabilities = (initialize?.params as { capabilities?: Record<string, unknown> } | undefined)?.capabilities;
  assert.deepEqual(capabilities?.channelAdapter, {
    channelName: "test-channel",
    displayName: "Test",
    deliveryCapabilities: { text: { format: "plain" }, targets: { group: false, direct: "none" } },
  });
});

test("module paths honor the config override and scope state and temp by module", () => {
  const context: WorkspaceContext = {
    workspaceRoot: "/ws",
    craftPath: "/ws/.craft",
    channelName: "demo",
    moduleId: "demo-module",
    configOverridePath: "/tmp/override.json",
  };

  assert.equal(resolveConfigPath(context, "demo.json"), "/tmp/override.json");
  assert.equal(resolveConfigPath({ ...context, configOverridePath: undefined }, "demo.json"), join("/ws/.craft", "demo.json"));
  assert.equal(resolveModuleStatePath(context), join("/ws/.craft", "state", "demo-module"));
  assert.equal(resolveModuleTempPath(context), join("/ws/.craft", "tmp", "demo-module"));
});

test("startWithContext reports configMissing and configInvalid without building a transport", async () => {
  const baseDir = await mkdtemp(join(tmpdir(), "dotcraft-sdk-m2-missing-"));
  try {
    const craftPath = join(baseDir, ".craft");
    await mkdir(craftPath, { recursive: true });

    const adapter = new TestModuleAdapter();
    const transitions: string[] = [];
    adapter.onStatusChange((status) => transitions.push(status));

    await adapter.startWithContext({
      workspaceRoot: baseDir,
      craftPath,
      channelName: "demo",
      moduleId: "demo-module",
    });

    assert.equal(adapter.getStatus(), "configMissing");
    assert.equal(adapter.getError()?.code, "configMissing");
    assert.deepEqual(transitions, ["starting", "configMissing"]);

    await writeFile(join(craftPath, "module.json"), JSON.stringify({ invalid: true }), "utf-8");
    await adapter.startWithContext({
      workspaceRoot: baseDir,
      craftPath,
      channelName: "demo",
      moduleId: "demo-module",
    });
    assert.equal(adapter.getStatus(), "configInvalid");
    assert.equal(adapter.getError()?.code, "configInvalid");
    assert.equal(adapter.buildTransportCalls, 0);
  } finally {
    await rm(baseDir, { recursive: true, force: true });
  }
});

test("startWithContext transitions to ready for valid config and successful startup", async () => {
  const baseDir = await mkdtemp(join(tmpdir(), "dotcraft-sdk-m2-ready-"));
  try {
    const craftPath = join(baseDir, ".craft");
    await mkdir(craftPath, { recursive: true });
    await writeFile(join(craftPath, "module.json"), JSON.stringify({ wsUrl: "ws://localhost" }), "utf-8");

    const adapter = new TestModuleAdapter();
    const transitions: string[] = [];
    adapter.onStatusChange((status) => transitions.push(status));

    await adapter.startWithContext({
      workspaceRoot: baseDir,
      craftPath,
      channelName: "demo",
      moduleId: "demo-module",
    });

    assert.equal(adapter.getStatus(), "ready");
    assert.equal(adapter.getError(), undefined);
    assert.equal(adapter.buildTransportCalls, 1);
    assert.deepEqual(transitions, ["starting", "ready"]);

    await adapter.stop();
  } finally {
    await rm(baseDir, { recursive: true, force: true });
  }
});

test("startWithContext invalidates connection-bound thread cache after module restart", async () => {
  const baseDir = await mkdtemp(join(tmpdir(), "dotcraft-sdk-m2-restart-"));
  try {
    const craftPath = join(baseDir, ".craft");
    await mkdir(craftPath, { recursive: true });
    await writeFile(join(craftPath, "module.json"), JSON.stringify({ wsUrl: "ws://localhost" }), "utf-8");
    const context: WorkspaceContext = {
      workspaceRoot: baseDir,
      craftPath,
      channelName: "demo",
      moduleId: "demo-module",
    };
    const adapter = new TestModuleAdapter();

    await adapter.startWithContext(context);
    adapter.cacheThreadForTest("user:conversation", "thread-active");
    await adapter.stop();
    assert.equal(adapter.hasCachedThreadForTest("user:conversation"), true);

    await adapter.startWithContext(context);

    assert.equal(adapter.getStatus(), "ready");
    assert.equal(adapter.hasCachedThreadForTest("user:conversation"), false);
    assert.equal(adapter.buildTransportCalls, 2);
    await adapter.stop();
  } finally {
    await rm(baseDir, { recursive: true, force: true });
  }
});
