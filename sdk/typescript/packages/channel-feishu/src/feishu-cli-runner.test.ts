import assert from "node:assert/strict";
import { mkdtemp } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

import {
  executeFeishuCliProcess,
  FeishuCliRunner,
  FeishuCliRunnerError,
  type FeishuCliProcessExecutor,
  type FeishuCliProcessRequest,
} from "./feishu-cli-runner.js";

type RecordedCall = Pick<FeishuCliProcessRequest, "args" | "cwd" | "env">;

async function createRunner(options?: {
  risk?: "read" | "write" | "high-risk-write";
  shortcutRisk?: "read" | "write" | "high-risk-write";
  result?: { exitCode: number; stdout: string; stderr: string };
}) {
  const workspaceRoot = await mkdtemp(join(tmpdir(), "dotcraft-feishu-cli-"));
  const calls: RecordedCall[] = [];
  const execute: FeishuCliProcessExecutor = async (request) => {
    calls.push({ args: request.args, cwd: request.cwd, env: request.env });
    if (request.args[0] === "schema") {
      return {
        exitCode: 0,
        stdout: JSON.stringify({ _meta: { risk: options?.risk ?? "read" } }),
        stderr: "",
      };
    }
    return options?.result ?? {
      exitCode: 0,
      stdout: JSON.stringify({ code: 0, data: { ok: true } }),
      stderr: "",
    };
  };
  const runner = new FeishuCliRunner({
    executable: "pinned-lark-cli",
    shortcutCatalog: {
      version: "1.0.87",
      commands: { "docs +fetch": options?.shortcutRisk ?? "read" },
    },
    workspaceRoot,
    appId: "app-id",
    appSecret: "app-secret",
    brand: "feishu",
    version: "1.0.87",
    execute,
  });
  return { runner, calls, workspaceRoot };
}

test("passes structured argv directly and injects Bot credentials only in the child environment", async () => {
  process.env.LARKSUITE_CLI_USER_ACCESS_TOKEN = "inherited-user-token";
  try {
    const { runner, calls, workspaceRoot } = await createRunner({ risk: "write" });
    const result = await runner.run("calendar", ["events", "create", "--summary", "hello; echo nope"]);

    assert.equal(result.risk, "write");
    assert.deepEqual(calls.map((call) => call.args), [
      ["schema", "calendar.events.create", "--format", "json"],
      ["calendar", "events", "create", "--summary", "hello; echo nope"],
    ]);
    assert.equal(calls[1]?.cwd, workspaceRoot);
    assert.equal(calls[1]?.env.LARKSUITE_CLI_APP_ID, "app-id");
    assert.equal(calls[1]?.env.LARKSUITE_CLI_APP_SECRET, "app-secret");
    assert.equal(calls[1]?.env.LARKSUITE_CLI_DEFAULT_AS, "bot");
    assert.equal(calls[1]?.env.LARKSUITE_CLI_STRICT_MODE, "bot");
    assert.equal(calls[1]?.env.LARKSUITE_CLI_USER_ACCESS_TOKEN, undefined);
  } finally {
    delete process.env.LARKSUITE_CLI_USER_ACCESS_TOKEN;
  }
});

test("uses the pinned shortcut catalog and appends --yes only for high-risk commands", async () => {
  const { runner, calls } = await createRunner({ shortcutRisk: "high-risk-write" });
  await runner.run("docs", ["+fetch", "--document-id", "doc-token"]);
  assert.deepEqual(calls.map((call) => call.args), [
    ["docs", "+fetch", "--document-id", "doc-token", "--yes"],
  ]);
});

test("supports embedded Skill reads without copying the Skill tree", async () => {
  const { runner, calls } = await createRunner({
    result: { exitCode: 0, stdout: "# Official Lark Skill\n", stderr: "" },
  });
  const result = await runner.run("skills", ["read", "lark-doc"]);
  assert.equal(result.contentItems[0]?.text, "# Official Lark Skill");
  assert.deepEqual(calls[0]?.args, ["skills", "read", "lark-doc"]);
});

test("rejects raw API, caller confirmation, identity overrides, and unknown shortcuts", async () => {
  const { runner } = await createRunner();
  await assert.rejects(() => runner.run("api", ["--method", "DELETE"]), errorCode("FeishuCliCommandRejected"));
  await assert.rejects(() => runner.run("docs", ["+fetch", "--yes"]), errorCode("FeishuCliCommandRejected"));
  await assert.rejects(() => runner.run("docs", ["+fetch", "--as=user"]), errorCode("FeishuCliCommandRejected"));
  await assert.rejects(() => runner.run("docs", ["+unknown"]), errorCode("FeishuCliCommandRejected"));
});

test("rejects file arguments outside the workspace", async () => {
  const { runner } = await createRunner();
  await assert.rejects(
    () => runner.run("docs", ["+fetch", "--output", join("..", "outside.md")]),
    errorCode("FeishuCliPathRejected"),
  );
});

test("maps timeout, cancellation, output overflow, and invalid output to stable errors", async () => {
  for (const [result, code] of [
    [{ exitCode: null, stdout: "", stderr: "", timedOut: true }, "FeishuCliTimeout"],
    [{ exitCode: null, stdout: "", stderr: "", cancelled: true }, "FeishuCliCancelled"],
    [{ exitCode: null, stdout: "", stderr: "", outputExceeded: true }, "FeishuCliOutputLimitExceeded"],
  ] as const) {
    const runner = new FeishuCliRunner({
      executable: "pinned-lark-cli",
      shortcutCatalog: { version: "1.0.87", commands: { "docs +fetch": "read" } },
      workspaceRoot: tmpdir(),
      appId: "id",
      appSecret: "secret",
      brand: "lark",
      version: "1.0.87",
      execute: async () => result,
    });
    await assert.rejects(() => runner.run("docs", ["+fetch"]), errorCode(code));
  }

  const { runner } = await createRunner({
    result: { exitCode: 0, stdout: "not-json", stderr: "" },
  });
  await assert.rejects(() => runner.run("docs", ["+fetch"]), errorCode("FeishuCliInvalidOutput"));
});

test("the process executor terminates an aborted child", async () => {
  const abort = new AbortController();
  const running = executeFeishuCliProcess({
    executable: process.execPath,
    args: ["-e", "setInterval(() => {}, 1000)"],
    cwd: tmpdir(),
    env: process.env,
    timeoutMs: 5_000,
    outputLimit: 1024,
    signal: abort.signal,
  });
  abort.abort();
  const result = await running;
  assert.equal(result.cancelled, true);
});

function errorCode(code: string): (error: unknown) => boolean {
  return (error) => error instanceof FeishuCliRunnerError && error.code === code;
}
