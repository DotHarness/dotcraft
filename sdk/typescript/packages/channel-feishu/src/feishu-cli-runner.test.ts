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
import { FeishuUserIdentityError } from "./feishu-user-identity.js";

type RecordedCall = Pick<FeishuCliProcessRequest, "args" | "cwd" | "env">;

async function createRunner(options?: {
  risk?: "read" | "write" | "high-risk-write";
  shortcutRisk?: "read" | "write" | "high-risk-write";
  result?: { exitCode: number; stdout: string; stderr: string };
  tokenError?: Error;
  userToken?: string | Error;
}) {
  const userToken = options?.userToken ?? new FeishuUserIdentityError("not_authorized", "no binding");
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
  let tokenRequests = 0;
  const runner = new FeishuCliRunner({
    executable: "pinned-lark-cli",
    shortcutCatalog: {
      version: "1.0.87",
      commands: { "docs +fetch": options?.shortcutRisk ?? "read" },
    },
    workspaceRoot,
    appId: "app-id",
    brand: "feishu",
    version: "1.0.87",
    getTenantAccessToken: async () => {
      tokenRequests += 1;
      if (options?.tokenError) throw options.tokenError;
      return "tenant-token";
    },
    getUserAccessToken: async () => {
      if (userToken instanceof Error) throw userToken;
      return userToken;
    },
    execute,
  });
  return { runner, calls, workspaceRoot, get tokenRequests() { return tokenRequests; } };
}

test("passes structured argv directly and injects only adapter-managed Bot credentials", async () => {
  process.env.LARKSUITE_CLI_USER_ACCESS_TOKEN = "inherited-user-token";
  process.env.LARKSUITE_CLI_PROFILE = "host-profile";
  try {
    const fixture = await createRunner({ risk: "write" });
    const { runner, calls, workspaceRoot } = fixture;
    const result = await runner.run("calendar", ["events", "create", "--summary", "hello; echo nope"]);

    assert.equal(result.risk, "write");
    assert.equal(fixture.tokenRequests, 1);
    assert.deepEqual(calls.map((call) => call.args), [
      ["schema", "calendar.events.create", "--format", "json"],
      ["calendar", "events", "create", "--summary", "hello; echo nope"],
    ]);
    assert.equal(calls[1]?.cwd, workspaceRoot);
    assert.equal(calls[1]?.env.LARKSUITE_CLI_APP_ID, "app-id");
    assert.equal(calls[1]?.env.LARKSUITE_CLI_TENANT_ACCESS_TOKEN, "tenant-token");
    assert.equal(calls[1]?.env.LARKSUITE_CLI_APP_SECRET, undefined);
    assert.equal(calls[1]?.env.LARKSUITE_CLI_DEFAULT_AS, "bot");
    assert.equal(calls[1]?.env.LARKSUITE_CLI_STRICT_MODE, "bot");
    assert.equal(calls[1]?.env.LARKSUITE_CLI_USER_ACCESS_TOKEN, undefined);
    assert.equal(calls[1]?.env.LARKSUITE_CLI_PROFILE, undefined);
    assert.equal(calls[0]?.env.LARKSUITE_CLI_TENANT_ACCESS_TOKEN, undefined);
  } finally {
    delete process.env.LARKSUITE_CLI_USER_ACCESS_TOKEN;
    delete process.env.LARKSUITE_CLI_PROFILE;
  }
});

test("runs a user-identity command with only the user token and a locked user strict mode", async () => {
  const fixture = await createRunner({ userToken: "user-token" });
  await fixture.runner.run("calendar", ["events", "list"], { identity: "user" });

  assert.equal(fixture.tokenRequests, 0);
  const invocation = fixture.calls[1];
  assert.equal(invocation?.env.LARKSUITE_CLI_USER_ACCESS_TOKEN, "user-token");
  assert.equal(invocation?.env.LARKSUITE_CLI_TENANT_ACCESS_TOKEN, undefined);
  assert.equal(invocation?.env.LARKSUITE_CLI_APP_SECRET, undefined);
  assert.equal(invocation?.env.LARKSUITE_CLI_DEFAULT_AS, "user");
  assert.equal(invocation?.env.LARKSUITE_CLI_STRICT_MODE, "user");
  assert.equal(fixture.calls[0]?.env.LARKSUITE_CLI_STRICT_MODE, "bot");
});

test("keeps user identity read-only and reports when no account is authorized", async () => {
  const write = await createRunner({ risk: "write", userToken: "user-token" });
  await assert.rejects(
    () => write.runner.run("calendar", ["events", "create"], { identity: "user" }),
    errorCode("FeishuCliUserWriteRejected"),
  );
  assert.equal(write.calls.length, 1);

  const unauthorized = await createRunner({
    userToken: new FeishuUserIdentityError("not_authorized", "no binding"),
  });
  await assert.rejects(
    () => unauthorized.runner.run("calendar", ["events", "list"], { identity: "user" }),
    errorCode("FeishuCliUserAuthorizationRequired"),
  );

  const unconfigured = await createRunner({
    userToken: new FeishuUserIdentityError("not_configured", "scopes missing"),
  });
  await assert.rejects(
    () => unconfigured.runner.run("calendar", ["events", "list"], { identity: "user" }),
    errorCode("FeishuCliUserIdentityUnavailable"),
  );
});

test("uses the pinned shortcut catalog and appends --yes only for high-risk commands", async () => {
  const { runner, calls } = await createRunner({ shortcutRisk: "high-risk-write" });
  await runner.run("docs", ["+fetch", "--document-id", "doc-token"]);
  assert.deepEqual(calls.map((call) => call.args), [
    ["docs", "+fetch", "--document-id", "doc-token", "--yes"],
  ]);
});

test("reads a referenced Skill file with the caller's structured argv", async () => {
  const fixture = await createRunner({
    result: { exitCode: 0, stdout: "# docs +fetch\nUse --doc.", stderr: "ignored tip" },
  });
  const result = await fixture.runner.run("skills", [
    "read",
    "lark-doc",
    "references/lark-doc-fetch.md",
  ]);

  assert.equal(fixture.tokenRequests, 0);
  assert.deepEqual(fixture.calls[0]?.args, [
    "skills",
    "read",
    "lark-doc",
    "references/lark-doc-fetch.md",
  ]);
  assert.deepEqual(result.contentItems, [{ type: "text", text: "# docs +fetch\nUse --doc." }]);
});

test("returns plain-text help without requesting credentials or confirmation", async () => {
  const fixture = await createRunner({
    shortcutRisk: "high-risk-write",
    result: { exitCode: 0, stdout: "", stderr: "Usage: lark-cli docs +fetch --doc <value>" },
  });
  const result = await fixture.runner.run("docs", ["+fetch", "--help"]);

  assert.equal(result.risk, "read");
  assert.equal(result.structuredResult, undefined);
  assert.equal(fixture.tokenRequests, 0);
  assert.deepEqual(fixture.calls[0]?.args, ["docs", "+fetch", "--help"]);
  assert.deepEqual(result.contentItems, [{
    type: "text",
    text: "Usage: lark-cli docs +fetch --doc <value>",
  }]);
});

test("rejects raw API, managed commands, profiles, caller confirmation, and unknown shortcuts", async () => {
  const { runner } = await createRunner();
  await assert.rejects(() => runner.run("api", ["--method", "DELETE"]), errorCode("FeishuCliCommandRejected"));
  await assert.rejects(() => runner.run("auth", ["status"]), errorCode("FeishuCliCommandRejected"));
  await assert.rejects(() => runner.run("config", ["show"]), errorCode("FeishuCliCommandRejected"));
  await assert.rejects(() => runner.run("profile", ["list"]), errorCode("FeishuCliCommandRejected"));
  await assert.rejects(() => runner.run("docs", ["+fetch", "--profile", "host"]), errorCode("FeishuCliCommandRejected"));
  await assert.rejects(() => runner.run("docs", ["+fetch", "--yes"]), errorCode("FeishuCliCommandRejected"));
  await assert.rejects(
    () => runner.run("docs", ["+unknown"]),
    (error) => error instanceof FeishuCliRunnerError
      && error.code === "FeishuCliCommandRejected"
      && error.message.includes("official Skill"),
  );
});

test("drops a redundant identity flag and rejects one that disagrees with the chosen identity", async () => {
  const matching = await createRunner({ userToken: "user-token" });
  await matching.runner.run("docs", ["+fetch", "--as", "bot", "--doc", "doc-token"]);
  await matching.runner.run("docs", ["+fetch", "--as=user"], { identity: "user" });
  assert.deepEqual(matching.calls.map((call) => call.args), [
    ["docs", "+fetch", "--doc", "doc-token"],
    ["docs", "+fetch"],
  ]);

  const mismatched = await createRunner({ userToken: "user-token" });
  await assert.rejects(
    () => mismatched.runner.run("docs", ["+fetch", "--as", "user"]),
    errorCode("FeishuCliCommandRejected"),
  );
});

test("rejects a shortcut supplied as the command", async () => {
  const { runner, calls } = await createRunner();
  await assert.rejects(
    () => runner.run("+fetch", ["--doc", "doc-token"]),
    errorCode("FeishuCliCommandRejected"),
  );
  assert.equal(calls.length, 0);
});

test("logs only the classified operation and never positional resource values", async () => {
  const { runner } = await createRunner();
  const messages: string[] = [];
  const originalLog = console.log;
  console.log = (...values: unknown[]) => messages.push(values.map(String).join(" "));
  try {
    await runner.run("docs", ["+fetch", "https://example.invalid/wiki/resource-token"]);
  } finally {
    console.log = originalLog;
  }
  assert.ok(messages.some((message) => message.includes("command=docs.+fetch")));
  assert.ok(messages.every((message) => !message.includes("resource-token")));
});

test("runs whoami as a read-only diagnostic with the managed Bot token", async () => {
  const fixture = await createRunner();
  await fixture.runner.run("whoami", []);
  assert.equal(fixture.tokenRequests, 1);
  assert.deepEqual(fixture.calls[0]?.args, ["whoami"]);
  assert.equal(fixture.calls[0]?.env.LARKSUITE_CLI_TENANT_ACCESS_TOKEN, "tenant-token");
});

test("preserves safe official strict-mode and authentication error details", async () => {
  const strict = await createRunner({
    result: {
      exitCode: 2,
      stdout: JSON.stringify({
        ok: false,
        identity: "user",
        error: {
          type: "validation",
          subtype: "invalid_argument",
          message: "strict mode is bot",
          hint: "use bot identity",
          ignored: "not exposed",
        },
        ignored: "not exposed",
      }),
      stderr: "",
    },
  });
  await assert.rejects(
    () => strict.runner.run("docs", ["+fetch"]),
    (error) => {
      assert.ok(error instanceof FeishuCliRunnerError);
      assert.equal(error.code, "FeishuCliValidationFailed");
      assert.deepEqual(error.structuredResult, {
        type: "validation",
        subtype: "invalid_argument",
        message: "strict mode is bot",
        hint: "use bot identity",
        identity: "user",
      });
      return true;
    },
  );

  const missingToken = await createRunner({
    result: {
      exitCode: 3,
      stdout: "",
      stderr: JSON.stringify({
        ok: false,
        identity: "bot",
        error: { type: "authentication", subtype: "token_missing", message: "no token" },
      }),
    },
  });
  await assert.rejects(
    () => missingToken.runner.run("docs", ["+fetch"]),
    errorCode("FeishuCliAuthenticationFailed"),
  );
});

test("maps adapter token acquisition failures without exposing the source error", async () => {
  const { runner, calls } = await createRunner({ tokenError: new Error("secret upstream detail") });
  await assert.rejects(
    () => runner.run("docs", ["+fetch"]),
    (error) => error instanceof FeishuCliRunnerError
      && error.code === "FeishuCliAuthenticationFailed"
      && !error.message.includes("secret upstream detail"),
  );
  assert.equal(calls.length, 0);
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
      brand: "lark",
      version: "1.0.87",
      getTenantAccessToken: async () => "tenant-token",
      getUserAccessToken: async () => "user-token",
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
