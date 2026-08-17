import assert from "node:assert/strict";
import { mkdtemp, mkdir, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

import { ChannelAdapter } from "./adapter.js";
import { ConfigValidationError, loadJsonConfig, ModuleChannelAdapter, resolveConfigPath, resolveModuleStatePath, resolveModuleTempPath } from "./moduleAdapter.js";
import type { ModuleError } from "./lifecycle.js";
import type { WorkspaceContext } from "./module.js";
import { TransportClosed, type Transport } from "@dotcraft/sdk/wire";

class MockTransport implements Transport {
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
  constructor() {
    super(new MockTransport(), "test-channel", "test-client", "0.0.0");
  }

  async onDeliver(_target: string, _content: string, _metadata: Record<string, unknown>): Promise<boolean> {
    return true;
  }

  async onApprovalRequest(_request: Record<string, unknown>): Promise<string> {
    return "cancel";
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

  triggerAuthRequired(error?: Partial<ModuleError>): void {
    this.signalAuthRequired(error);
  }

  triggerAuthExpired(error?: Partial<ModuleError>): void {
    this.signalAuthExpired(error);
  }
}

test("ChannelAdapter getStatus returns stopped before start", () => {
  const adapter = new LifecycleAdapter();
  assert.equal(adapter.getStatus(), "stopped");
  assert.equal(adapter.getError(), undefined);
});

test("ChannelAdapter start and stop emit lifecycle transitions", async () => {
  const adapter = new LifecycleAdapter();
  const transitions: string[] = [];
  adapter.onStatusChange((status) => transitions.push(status));

  await adapter.start();
  assert.equal(adapter.getStatus(), "ready");
  assert.equal(adapter.getError(), undefined);

  await adapter.stop();
  assert.equal(adapter.getStatus(), "stopped");
  assert.deepEqual(transitions, ["starting", "ready", "stopped"]);
});

test("ChannelAdapter notifies multiple status handlers", async () => {
  const adapter = new LifecycleAdapter();
  const first: string[] = [];
  const second: string[] = [];

  adapter.onStatusChange((status) => first.push(status));
  adapter.onStatusChange((status) => second.push(status));

  await adapter.start();
  await adapter.stop();

  assert.deepEqual(first, ["starting", "ready", "stopped"]);
  assert.deepEqual(second, ["starting", "ready", "stopped"]);
});

test("resolveConfigPath prefers override path", () => {
  const context: WorkspaceContext = {
    workspaceRoot: "/ws",
    craftPath: "/ws/.craft",
    channelName: "demo",
    moduleId: "demo-module",
    configOverridePath: "/tmp/override.json",
  };

  assert.equal(resolveConfigPath(context, "demo.json"), "/tmp/override.json");
});

test("resolveConfigPath defaults to craft config file", () => {
  const context: WorkspaceContext = {
    workspaceRoot: "/ws",
    craftPath: "/ws/.craft",
    channelName: "demo",
    moduleId: "demo-module",
  };

  assert.equal(resolveConfigPath(context, "demo.json"), join("/ws/.craft", "demo.json"));
});

test("resolveModuleStatePath and resolveModuleTempPath compute module scoped paths", () => {
  const context: WorkspaceContext = {
    workspaceRoot: "/ws",
    craftPath: "/ws/.craft",
    channelName: "demo",
    moduleId: "demo-module",
  };

  assert.equal(resolveModuleStatePath(context), join("/ws/.craft", "state", "demo-module"));
  assert.equal(resolveModuleTempPath(context), join("/ws/.craft", "tmp", "demo-module"));
});

test("loadJsonConfig returns found false when file does not exist", async () => {
  const result = await loadJsonConfig(join(tmpdir(), `missing-${Date.now()}.json`));
  assert.deepEqual(result, { found: false });
});

test("loadJsonConfig returns parsed data for valid JSON", async () => {
  const baseDir = await mkdtemp(join(tmpdir(), "dotcraft-sdk-m2-json-"));
  try {
    const configPath = join(baseDir, "config.json");
    await writeFile(configPath, JSON.stringify({ ok: true, nested: { a: 1 } }), "utf-8");
    const result = await loadJsonConfig(configPath);
    assert.equal(result.found, true);
    if (result.found) {
      assert.deepEqual(result.data, { ok: true, nested: { a: 1 } });
    }
  } finally {
    await rm(baseDir, { recursive: true, force: true });
  }
});

test("startWithContext transitions to configMissing when config is absent", async () => {
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
    assert.equal(adapter.buildTransportCalls, 0);
  } finally {
    await rm(baseDir, { recursive: true, force: true });
  }
});

test("startWithContext transitions to configInvalid when validateConfig fails", async () => {
  const baseDir = await mkdtemp(join(tmpdir(), "dotcraft-sdk-m2-invalid-"));
  try {
    const craftPath = join(baseDir, ".craft");
    await mkdir(craftPath, { recursive: true });
    await writeFile(join(craftPath, "module.json"), JSON.stringify({ invalid: true }), "utf-8");

    const adapter = new TestModuleAdapter();
    const transitions: string[] = [];
    adapter.onStatusChange((status) => transitions.push(status));

    await adapter.startWithContext({
      workspaceRoot: baseDir,
      craftPath,
      channelName: "demo",
      moduleId: "demo-module",
    });

    assert.equal(adapter.getStatus(), "configInvalid");
    assert.equal(adapter.getError()?.code, "configInvalid");
    assert.deepEqual(transitions, ["starting", "configInvalid"]);
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

test("signalAuthRequired and signalAuthExpired update status and notify handlers", () => {
  const adapter = new TestModuleAdapter();
  const transitions: Array<{ status: string; code: string | undefined }> = [];
  adapter.onStatusChange((status, error) => {
    transitions.push({ status, code: error?.code });
  });

  adapter.triggerAuthRequired();
  assert.equal(adapter.getStatus(), "authRequired");
  assert.equal(adapter.getError()?.code, "authRequired");

  adapter.triggerAuthExpired({ message: "Session expired." });
  assert.equal(adapter.getStatus(), "authExpired");
  assert.equal(adapter.getError()?.code, "authExpired");

  assert.deepEqual(transitions, [
    { status: "authRequired", code: "authRequired" },
    { status: "authExpired", code: "authExpired" },
  ]);
});
