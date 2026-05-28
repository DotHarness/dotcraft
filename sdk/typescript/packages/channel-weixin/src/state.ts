/**
 * Persistent state: credentials, sync cursor, per-user context tokens.
 */

import { mkdirSync, readFileSync, writeFileSync, existsSync } from "node:fs";
import { join } from "node:path";

export interface WeixinCredentials {
  botToken: string;
  ilinkBotId: string;
  baseUrl: string;
  ilinkUserId?: string;
  savedAt: string;
}

export class WeixinState {
  readonly dataDir: string;

  constructor(dataDir: string) {
    this.dataDir = dataDir;
    mkdirSync(dataDir, { recursive: true });
  }

  credentialsPath(): string {
    return join(this.dataDir, "credentials.json");
  }

  syncPath(): string {
    return join(this.dataDir, "sync.json");
  }

  contextTokensPath(): string {
    return join(this.dataDir, "context-tokens.json");
  }

  loadCredentials(): WeixinCredentials | null {
    const p = this.credentialsPath();
    if (!existsSync(p)) return null;
    try {
      return JSON.parse(readFileSync(p, "utf-8")) as WeixinCredentials;
    } catch {
      return null;
    }
  }

  saveCredentials(c: WeixinCredentials): void {
    writeFileSync(this.credentialsPath(), JSON.stringify(c, null, 2), "utf-8");
  }

  loadSyncBuf(): string {
    const p = this.syncPath();
    if (!existsSync(p)) return "";
    try {
      const j = JSON.parse(readFileSync(p, "utf-8")) as { get_updates_buf?: string };
      return j.get_updates_buf ?? "";
    } catch {
      return "";
    }
  }

  saveSyncBuf(buf: string): void {
    writeFileSync(this.syncPath(), JSON.stringify({ get_updates_buf: buf }, null, 2), "utf-8");
  }

  loadContextTokens(): Record<string, string> {
    const p = this.contextTokensPath();
    if (!existsSync(p)) return {};
    try {
      return JSON.parse(readFileSync(p, "utf-8")) as Record<string, string>;
    } catch {
      return {};
    }
  }

  saveContextTokens(tokens: Record<string, string>): void {
    writeFileSync(this.contextTokensPath(), JSON.stringify(tokens, null, 2), "utf-8");
  }
}
