/**
 * Long-poll getUpdates loop for inbound Weixin messages.
 */

import { getUpdates, SESSION_EXPIRED_ERRCODE } from "./weixin-api.js";
import type { WeixinMessage } from "./weixin-types.js";
import { MessageType } from "./weixin-types.js";

const DEFAULT_LONG_POLL = 35_000;
const MAX_FAILURES = 3;
const BACKOFF_MS = 30_000;
const RETRY_MS = 2000;

export interface MonitorCallbacks {
  onMessage: (msg: WeixinMessage) => void | Promise<void>;
  onSessionExpired?: () => void | Promise<void>;
}

export async function runMonitorLoop(opts: {
  baseUrl: string;
  token: string;
  getInitialBuf: () => string;
  saveBuf: (buf: string) => void;
  callbacks: MonitorCallbacks;
  abortSignal?: AbortSignal;
  longPollMs?: number;
}): Promise<void> {
  let buf = opts.getInitialBuf();
  let nextTimeout = opts.longPollMs ?? DEFAULT_LONG_POLL;
  let consecutiveFailures = 0;

  while (!opts.abortSignal?.aborted) {
    try {
      const resp = await getUpdates({
        baseUrl: opts.baseUrl,
        token: opts.token,
        get_updates_buf: buf,
        timeoutMs: nextTimeout,
      });

      if (resp.longpolling_timeout_ms != null && resp.longpolling_timeout_ms > 0) {
        nextTimeout = resp.longpolling_timeout_ms;
      }

      const isApiError =
        (resp.ret !== undefined && resp.ret !== 0) ||
        (resp.errcode !== undefined && resp.errcode !== 0);

      if (isApiError) {
        const sessionExpired =
          resp.errcode === SESSION_EXPIRED_ERRCODE || resp.ret === SESSION_EXPIRED_ERRCODE;
        if (sessionExpired) {
          console.error("Weixin session expired; re-login required.");
          await opts.callbacks.onSessionExpired?.();
          consecutiveFailures = 0;
          await sleep(60_000, opts.abortSignal);
          continue;
        }
        consecutiveFailures++;
        console.error(
          `getUpdates error ret=${resp.ret} errcode=${resp.errcode} (${consecutiveFailures}/${MAX_FAILURES})`,
        );
        if (consecutiveFailures >= MAX_FAILURES) {
          consecutiveFailures = 0;
          await sleep(BACKOFF_MS, opts.abortSignal);
        } else {
          await sleep(RETRY_MS, opts.abortSignal);
        }
        continue;
      }

      consecutiveFailures = 0;
      if (resp.get_updates_buf != null && resp.get_updates_buf !== "") {
        opts.saveBuf(resp.get_updates_buf);
        buf = resp.get_updates_buf;
      }

      const list = resp.msgs ?? [];
      for (const full of list) {
        if (full.message_type === MessageType.USER) {
          await opts.callbacks.onMessage(full);
        }
      }
    } catch (e) {
      if (opts.abortSignal?.aborted) break;
      consecutiveFailures++;
      console.error(`getUpdates exception (${consecutiveFailures}/${MAX_FAILURES}):`, e);
      if (consecutiveFailures >= MAX_FAILURES) {
        consecutiveFailures = 0;
        await sleep(BACKOFF_MS, opts.abortSignal);
      } else {
        await sleep(RETRY_MS, opts.abortSignal);
      }
    }
  }
}

function sleep(ms: number, signal?: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    const t = setTimeout(resolve, ms);
    signal?.addEventListener(
      "abort",
      () => {
        clearTimeout(t);
        reject(new Error("aborted"));
      },
      { once: true },
    );
  });
}
