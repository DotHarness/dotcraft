import { chmodSync, existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { join } from "node:path";

import { logInfo, logWarn } from "./logging.js";

const RECORD_FILE_NAME = "user-identity.json";
const REFRESH_LEAD_MS = 5 * 60 * 1000;
const DEFAULT_POLL_INTERVAL_MS = 5000;
const SLOW_DOWN_STEP_MS = 5000;

export type FeishuUserIdentityErrorCode =
  | "not_configured"
  | "not_authorized"
  | "authorization_expired"
  | "authorization_denied"
  | "authorization_failed";

export class FeishuUserIdentityError extends Error {
  constructor(public readonly code: FeishuUserIdentityErrorCode, message: string) {
    super(message);
    this.name = "FeishuUserIdentityError";
  }
}

export interface FeishuUserIdentityRecord {
  openId: string;
  name: string;
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAt: number;
  refreshTokenExpiresAt: number;
  scopes: string[];
  authorizedAt: string;
}

export interface FeishuDeviceAuthorization {
  deviceCode: string;
  userCode: string;
  verificationUriComplete: string;
  intervalMs: number;
  expiresAt: number;
}

export interface FeishuUserIdentityOptions {
  appId: string;
  appSecret: string;
  brand: "feishu" | "lark";
  scopes: string[];
  stateDir: string;
  fetchImpl?: typeof fetch;
  sleep?: (ms: number, signal?: AbortSignal) => Promise<void>;
}

export class FeishuUserIdentity {
  private readonly fetchImpl: typeof fetch;
  private readonly sleep: (ms: number, signal?: AbortSignal) => Promise<void>;
  private readonly recordPath: string;
  private record: FeishuUserIdentityRecord | null;
  private refreshInFlight: Promise<string> | null = null;

  constructor(private readonly options: FeishuUserIdentityOptions) {
    this.fetchImpl = options.fetchImpl ?? fetch;
    this.sleep = options.sleep ?? defaultSleep;
    this.recordPath = join(options.stateDir, RECORD_FILE_NAME);
    this.record = this.readRecord();
  }

  /** User identity stays off until an operator lists the scopes the app has enabled. */
  isConfigured(): boolean {
    return this.options.scopes.length > 0;
  }

  getBinding(): { openId: string; name: string } | null {
    if (!this.record) return null;
    return { openId: this.record.openId, name: this.record.name };
  }

  async getAccessToken(): Promise<string> {
    if (!this.isConfigured()) {
      throw new FeishuUserIdentityError(
        "not_configured",
        "Feishu user identity is not enabled for this channel.",
      );
    }
    const record = this.record;
    if (!record) {
      throw new FeishuUserIdentityError("not_authorized", "No Feishu account is authorized yet.");
    }
    if (Date.now() < record.accessTokenExpiresAt - REFRESH_LEAD_MS) return record.accessToken;
    if (Date.now() >= record.refreshTokenExpiresAt) {
      this.clearBinding();
      throw new FeishuUserIdentityError(
        "authorization_expired",
        "The authorized Feishu account needs to be authorized again.",
      );
    }
    this.refreshInFlight ??= this.refresh(record).finally(() => {
      this.refreshInFlight = null;
    });
    return await this.refreshInFlight;
  }

  async requestAuthorization(): Promise<FeishuDeviceAuthorization> {
    if (!this.isConfigured()) {
      throw new FeishuUserIdentityError(
        "not_configured",
        "Feishu user identity is not enabled for this channel.",
      );
    }
    const body = new URLSearchParams({
      client_id: this.options.appId,
      scope: this.requestedScopes().join(" "),
    });
    const payload = await this.postForm(`${accountsBaseUrl(this.options.brand)}/oauth/v1/device_authorization`, body, {
      Authorization: `Basic ${basicCredential(this.options.appId, this.options.appSecret)}`,
    });
    const deviceCode = readString(payload, "device_code");
    const userCode = readString(payload, "user_code");
    const verificationUriComplete = readString(payload, "verification_uri_complete")
      || readString(payload, "verification_uri");
    if (!deviceCode || !userCode || !verificationUriComplete) {
      throw new FeishuUserIdentityError(
        "authorization_failed",
        "Feishu did not return a usable authorization link.",
      );
    }
    const intervalSeconds = readNumber(payload, "interval");
    const expiresInSeconds = readNumber(payload, "expires_in");
    logInfo("user_identity.authorization.requested", { scopes: this.requestedScopes().length });
    return {
      deviceCode,
      userCode,
      verificationUriComplete,
      intervalMs: intervalSeconds > 0 ? intervalSeconds * 1000 : DEFAULT_POLL_INTERVAL_MS,
      expiresAt: Date.now() + (expiresInSeconds > 0 ? expiresInSeconds * 1000 : 300_000),
    };
  }

  async waitForAuthorization(
    authorization: FeishuDeviceAuthorization,
    signal?: AbortSignal,
  ): Promise<FeishuUserIdentityRecord> {
    let intervalMs = authorization.intervalMs;
    for (;;) {
      await this.sleep(intervalMs, signal);
      if (signal?.aborted) {
        throw new FeishuUserIdentityError("authorization_failed", "Authorization was cancelled.");
      }
      if (Date.now() >= authorization.expiresAt) {
        throw new FeishuUserIdentityError("authorization_expired", "The authorization link expired.");
      }
      const payload = await this.postForm(
        `${openBaseUrl(this.options.brand)}/open-apis/authen/v2/oauth/token`,
        new URLSearchParams({
          grant_type: "urn:ietf:params:oauth:grant-type:device_code",
          client_id: this.options.appId,
          client_secret: this.options.appSecret,
          device_code: authorization.deviceCode,
        }),
      );
      const pending = readString(payload, "error");
      if (pending === "slow_down") {
        intervalMs += SLOW_DOWN_STEP_MS;
        continue;
      }
      if (pending === "authorization_pending") continue;
      if (pending === "access_denied") {
        throw new FeishuUserIdentityError("authorization_denied", "The authorization request was declined.");
      }
      if (pending === "expired_token") {
        throw new FeishuUserIdentityError("authorization_expired", "The authorization link expired.");
      }
      return await this.acceptToken(payload);
    }
  }

  /** Clears the local binding; a full revoke also needs the operator to remove the app authorization. */
  clearBinding(): void {
    this.record = null;
    try {
      rmSync(this.recordPath, { force: true });
    } catch {
      logWarn("user_identity.record.remove_failed", {});
    }
  }

  private requestedScopes(): string[] {
    const scopes = this.options.scopes.filter((scope) => scope.trim().length > 0);
    return scopes.includes("offline_access") ? scopes : [...scopes, "offline_access"];
  }

  private async refresh(record: FeishuUserIdentityRecord): Promise<string> {
    const payload = await this.postForm(
      `${openBaseUrl(this.options.brand)}/open-apis/authen/v2/oauth/token`,
      new URLSearchParams({
        grant_type: "refresh_token",
        client_id: this.options.appId,
        client_secret: this.options.appSecret,
        refresh_token: record.refreshToken,
      }),
    );
    if (!readString(payload, "access_token")) {
      this.clearBinding();
      throw new FeishuUserIdentityError(
        "authorization_expired",
        "The authorized Feishu account needs to be authorized again.",
      );
    }
    const refreshed = await this.acceptToken(payload, record);
    logInfo("user_identity.token.refreshed", {});
    return refreshed.accessToken;
  }

  private async acceptToken(
    payload: Record<string, unknown>,
    previous?: FeishuUserIdentityRecord,
  ): Promise<FeishuUserIdentityRecord> {
    const accessToken = readString(payload, "access_token");
    const refreshToken = readString(payload, "refresh_token") || previous?.refreshToken || "";
    if (!accessToken) {
      throw new FeishuUserIdentityError("authorization_failed", "Feishu did not return an access token.");
    }
    const grantedScopes = readString(payload, "scope").split(/\s+/).filter(Boolean);
    const profile = previous ? { openId: previous.openId, name: previous.name } : await this.readProfile(accessToken);
    const record: FeishuUserIdentityRecord = {
      openId: profile.openId,
      name: profile.name,
      accessToken,
      refreshToken,
      accessTokenExpiresAt: Date.now() + secondsToMs(readNumber(payload, "expires_in"), 2 * 60 * 60 * 1000),
      refreshTokenExpiresAt: Date.now()
        + secondsToMs(readNumber(payload, "refresh_token_expires_in"), 30 * 24 * 60 * 60 * 1000),
      scopes: grantedScopes.length ? grantedScopes : (previous?.scopes ?? this.requestedScopes()),
      authorizedAt: previous?.authorizedAt ?? new Date().toISOString(),
    };
    this.writeRecord(record);
    return record;
  }

  /** A binding whose account cannot be named is not stored: the operator must be able to see who is bound. */
  private async readProfile(accessToken: string): Promise<{ openId: string; name: string }> {
    const response = await this.fetchImpl(`${openBaseUrl(this.options.brand)}/open-apis/authen/v1/user_info`, {
      headers: { Authorization: `Bearer ${accessToken}` },
    });
    const payload = (await response.json()) as { data?: Record<string, unknown> };
    const openId = readString(payload.data ?? {}, "open_id");
    if (!openId) {
      throw new FeishuUserIdentityError("authorization_failed", "Feishu did not identify the authorized account.");
    }
    return { openId, name: readString(payload.data ?? {}, "name") };
  }

  private async postForm(
    url: string,
    body: URLSearchParams,
    headers: Record<string, string> = {},
  ): Promise<Record<string, unknown>> {
    let response: Response;
    try {
      response = await this.fetchImpl(url, {
        method: "POST",
        headers: { "Content-Type": "application/x-www-form-urlencoded", ...headers },
        body: body.toString(),
      });
    } catch {
      throw new FeishuUserIdentityError("authorization_failed", "Could not reach the Feishu authorization service.");
    }
    try {
      return (await response.json()) as Record<string, unknown>;
    } catch {
      throw new FeishuUserIdentityError("authorization_failed", "The Feishu authorization service returned no result.");
    }
  }

  private readRecord(): FeishuUserIdentityRecord | null {
    if (!existsSync(this.recordPath)) return null;
    try {
      const parsed = JSON.parse(readFileSync(this.recordPath, "utf8")) as FeishuUserIdentityRecord;
      return parsed.accessToken && parsed.refreshToken ? parsed : null;
    } catch {
      return null;
    }
  }

  private writeRecord(record: FeishuUserIdentityRecord): void {
    this.record = record;
    try {
      mkdirSync(this.options.stateDir, { recursive: true });
      writeFileSync(this.recordPath, JSON.stringify(record, null, 2), "utf8");
      chmodSync(this.recordPath, 0o600);
    } catch {
      logWarn("user_identity.record.write_failed", {});
    }
  }
}

function accountsBaseUrl(brand: "feishu" | "lark"): string {
  return brand === "lark" ? "https://accounts.larksuite.com" : "https://accounts.feishu.cn";
}

function openBaseUrl(brand: "feishu" | "lark"): string {
  return brand === "lark" ? "https://open.larksuite.com" : "https://open.feishu.cn";
}

function basicCredential(appId: string, appSecret: string): string {
  return Buffer.from(`${appId}:${appSecret}`, "utf8").toString("base64");
}

function readString(payload: Record<string, unknown>, key: string): string {
  const value = payload[key];
  return typeof value === "string" ? value : "";
}

function readNumber(payload: Record<string, unknown>, key: string): number {
  const value = payload[key];
  return typeof value === "number" && Number.isFinite(value) ? value : 0;
}

function secondsToMs(seconds: number, fallbackMs: number): number {
  return seconds > 0 ? seconds * 1000 : fallbackMs;
}

function defaultSleep(ms: number, signal?: AbortSignal): Promise<void> {
  return new Promise((resolveSleep) => {
    const timer = setTimeout(finish, ms);
    function finish(): void {
      clearTimeout(timer);
      signal?.removeEventListener("abort", finish);
      resolveSleep();
    }
    signal?.addEventListener("abort", finish, { once: true });
  });
}
