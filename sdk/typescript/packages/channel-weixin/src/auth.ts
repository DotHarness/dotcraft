/**
 * QR code login against iLink (get_bot_qrcode / get_qrcode_status).
 */

import type { WeixinCredentials } from "./state.js";

const QR_LONG_POLL_MS = 35_000;
export const DEFAULT_BOT_TYPE = "3";

interface QrFetchResponse {
  qrcode: string;
  qrcode_img_content: string;
}

interface QrStatusResponse {
  status: "wait" | "scaned" | "confirmed" | "expired";
  bot_token?: string;
  ilink_bot_id?: string;
  baseurl?: string;
  ilink_user_id?: string;
}

export async function fetchQrCode(apiBaseUrl: string, botType: string): Promise<QrFetchResponse> {
  const base = apiBaseUrl.endsWith("/") ? apiBaseUrl : `${apiBaseUrl}/`;
  const url = new URL(`ilink/bot/get_bot_qrcode?bot_type=${encodeURIComponent(botType)}`, base);
  const res = await fetch(url);
  if (!res.ok) {
    const body = await res.text();
    throw new Error(`QR fetch failed: ${res.status} ${body}`);
  }
  return (await res.json()) as QrFetchResponse;
}

async function pollQrStatus(apiBaseUrl: string, qrcode: string): Promise<QrStatusResponse> {
  const base = apiBaseUrl.endsWith("/") ? apiBaseUrl : `${apiBaseUrl}/`;
  const url = new URL(`ilink/bot/get_qrcode_status?qrcode=${encodeURIComponent(qrcode)}`, base);
  const controller = new AbortController();
  const t = setTimeout(() => controller.abort(), QR_LONG_POLL_MS);
  try {
    const res = await fetch(url, {
      headers: { "iLink-App-ClientVersion": "1" },
      signal: controller.signal,
    });
    clearTimeout(t);
    if (!res.ok) {
      const body = await res.text();
      throw new Error(`QR status failed: ${res.status} ${body}`);
    }
    return (await res.json()) as QrStatusResponse;
  } catch (e) {
    clearTimeout(t);
    if (e instanceof Error && e.name === "AbortError") {
      return { status: "wait" };
    }
    throw e;
  }
}

export async function waitForQrLogin(opts: {
  apiBaseUrl: string;
  botType: string;
  onQrUrl: (url: string) => void;
  onStatus?: (s: string) => void;
  deadlineMs?: number;
  abortSignal?: AbortSignal;
}): Promise<WeixinCredentials> {
  const deadline = Date.now() + (opts.deadlineMs ?? 480_000);
  if (opts.abortSignal?.aborted) {
    throw new Error("QR login aborted");
  }
  const qr = await fetchQrCode(opts.apiBaseUrl, opts.botType);
  opts.onQrUrl(qr.qrcode_img_content);
  let qrcode = qr.qrcode;
  let scannedPrinted = false;

  while (Date.now() < deadline) {
    if (opts.abortSignal?.aborted) {
      throw new Error("QR login aborted");
    }
    const st = await pollQrStatus(opts.apiBaseUrl, qrcode);
    opts.onStatus?.(st.status);

    if (st.status === "scaned" && !scannedPrinted) {
      scannedPrinted = true;
      console.error("\nScanned — confirm in WeChat...\n");
    }

    if (st.status === "expired") {
      console.error("QR expired, fetching new QR...");
      const next = await fetchQrCode(opts.apiBaseUrl, opts.botType);
      qrcode = next.qrcode;
      opts.onQrUrl(next.qrcode_img_content);
    }

    if (st.status === "confirmed") {
      if (!st.ilink_bot_id) {
        throw new Error("Login confirmed but ilink_bot_id missing");
      }
      return {
        botToken: st.bot_token ?? "",
        ilinkBotId: st.ilink_bot_id,
        baseUrl: st.baseurl ?? opts.apiBaseUrl,
        ilinkUserId: st.ilink_user_id,
        savedAt: new Date().toISOString(),
      };
    }

    await sleep(1000, opts.abortSignal);
  }

  throw new Error("QR login timed out");
}

function sleep(ms: number, abortSignal?: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      abortSignal?.removeEventListener("abort", onAbort);
      resolve();
    }, ms);

    const onAbort = () => {
      clearTimeout(timer);
      abortSignal?.removeEventListener("abort", onAbort);
      reject(new Error("QR login aborted"));
    };

    if (abortSignal) {
      abortSignal.addEventListener("abort", onAbort, { once: true });
    }
  });
}
