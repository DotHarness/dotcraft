import assert from "node:assert/strict";
import { mkdtemp, readFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

import {
  FeishuUserIdentity,
  FeishuUserIdentityError,
  type FeishuUserIdentityRecord,
} from "./feishu-user-identity.js";

type Reply = Record<string, unknown>;

function createFetch(replies: Reply[]): { fetchImpl: typeof fetch; urls: string[]; bodies: string[] } {
  const urls: string[] = [];
  const bodies: string[] = [];
  const queue = [...replies];
  const fetchImpl = (async (input: unknown, init?: { body?: string }) => {
    urls.push(String(input));
    bodies.push(init?.body ?? "");
    const payload = queue.shift() ?? {};
    return { json: async () => payload } as Response;
  }) as unknown as typeof fetch;
  return { fetchImpl, urls, bodies };
}

const DEVICE_REPLY: Reply = {
  device_code: "device-code",
  user_code: "ABCD-1234",
  verification_uri: "https://example.invalid/device",
  verification_uri_complete: "https://example.invalid/device?code=ABCD-1234",
  interval: 1,
  expires_in: 300,
};

const TOKEN_REPLY: Reply = {
  access_token: "user-token",
  refresh_token: "refresh-token",
  expires_in: 7200,
  refresh_token_expires_in: 2592000,
  scope: "calendar:calendar:readonly offline_access",
};

const PROFILE_REPLY: Reply = { data: { open_id: "ou_operator", name: "Operator" } };

async function createIdentity(replies: Reply[], scopes = ["calendar:calendar:readonly"]) {
  const stateDir = await mkdtemp(join(tmpdir(), "dotcraft-feishu-identity-"));
  const { fetchImpl, urls, bodies } = createFetch(replies);
  const identity = new FeishuUserIdentity({
    appId: "app-id",
    appSecret: "app-secret",
    brand: "feishu",
    scopes,
    stateDir,
    fetchImpl,
    sleep: async () => {},
  });
  return { identity, urls, bodies, stateDir };
}

test("stays disabled until scopes are configured", async () => {
  const { identity } = await createIdentity([], []);
  assert.equal(identity.isConfigured(), false);
  await assert.rejects(
    () => identity.getAccessToken(),
    (error) => error instanceof FeishuUserIdentityError && error.code === "not_configured",
  );
  await assert.rejects(
    () => identity.requestAuthorization(),
    (error) => error instanceof FeishuUserIdentityError && error.code === "not_configured",
  );
});

test("reports that no account is authorized before the first device flow", async () => {
  const { identity } = await createIdentity([]);
  assert.equal(identity.getBinding(), null);
  await assert.rejects(
    () => identity.getAccessToken(),
    (error) => error instanceof FeishuUserIdentityError && error.code === "not_authorized",
  );
});

test("requests a device code with offline access and stores the granted binding", async () => {
  const { identity, urls, bodies, stateDir } = await createIdentity([
    DEVICE_REPLY,
    { error: "authorization_pending" },
    { error: "slow_down" },
    TOKEN_REPLY,
    PROFILE_REPLY,
  ]);

  const authorization = await identity.requestAuthorization();
  assert.equal(authorization.userCode, "ABCD-1234");
  assert.equal(authorization.verificationUriComplete, "https://example.invalid/device?code=ABCD-1234");
  assert.ok(urls[0]?.includes("/oauth/v1/device_authorization"));
  assert.ok(decodeURIComponent(bodies[0] ?? "").includes("offline_access"));

  const record = await identity.waitForAuthorization(authorization);
  assert.equal(record.name, "Operator");
  assert.equal(record.openId, "ou_operator");
  assert.equal(await identity.getAccessToken(), "user-token");
  assert.deepEqual(record.scopes, ["calendar:calendar:readonly", "offline_access"]);

  const stored = JSON.parse(
    await readFile(join(stateDir, "user-identity.json"), "utf8"),
  ) as FeishuUserIdentityRecord;
  assert.equal(stored.refreshToken, "refresh-token");
  assert.equal(new FeishuUserIdentity({
    appId: "app-id",
    appSecret: "app-secret",
    brand: "feishu",
    scopes: ["calendar:calendar:readonly"],
    stateDir,
  }).getBinding()?.openId, "ou_operator");
});

test("surfaces a declined or expired device authorization", async () => {
  const denied = await createIdentity([DEVICE_REPLY, { error: "access_denied" }]);
  await assert.rejects(
    async () => denied.identity.waitForAuthorization(await denied.identity.requestAuthorization()),
    (error) => error instanceof FeishuUserIdentityError && error.code === "authorization_denied",
  );

  const expired = await createIdentity([DEVICE_REPLY, { error: "expired_token" }]);
  await assert.rejects(
    async () => expired.identity.waitForAuthorization(await expired.identity.requestAuthorization()),
    (error) => error instanceof FeishuUserIdentityError && error.code === "authorization_expired",
  );
});

test("refreshes an expiring token and clears the binding when the refresh token is rejected", async () => {
  const refreshed = await createIdentity([
    DEVICE_REPLY,
    { ...TOKEN_REPLY, expires_in: 60 },
    PROFILE_REPLY,
    { ...TOKEN_REPLY, access_token: "second-token" },
  ]);
  const record = await refreshed.identity.waitForAuthorization(
    await refreshed.identity.requestAuthorization(),
  );
  assert.equal(record.accessToken, "user-token");
  assert.equal(await refreshed.identity.getAccessToken(), "second-token");
  assert.ok(refreshed.bodies.some((body) => body.includes("grant_type=refresh_token")));

  const rejected = await createIdentity([
    DEVICE_REPLY,
    { ...TOKEN_REPLY, expires_in: 60 },
    PROFILE_REPLY,
    { error: "invalid_grant" },
  ]);
  await rejected.identity.waitForAuthorization(await rejected.identity.requestAuthorization());
  await assert.rejects(
    () => rejected.identity.getAccessToken(),
    (error) => error instanceof FeishuUserIdentityError && error.code === "authorization_expired",
  );
  assert.equal(rejected.identity.getBinding(), null);
});

test("clearing the binding removes the stored record", async () => {
  const { identity, stateDir } = await createIdentity([DEVICE_REPLY, TOKEN_REPLY, PROFILE_REPLY]);
  await identity.waitForAuthorization(await identity.requestAuthorization());
  identity.clearBinding();

  assert.equal(identity.getBinding(), null);
  await assert.rejects(
    () => readFile(join(stateDir, "user-identity.json"), "utf8"),
    (error) => (error as NodeJS.ErrnoException).code === "ENOENT",
  );
});
