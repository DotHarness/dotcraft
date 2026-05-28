export interface WeixinConfig {
  dotcraft: {
    wsUrl: string;
    token?: string;
  };
  weixin: {
    apiBaseUrl: string;
    pollIntervalMs?: number;
    pollTimeoutMs?: number;
    approvalTimeoutMs?: number;
    botType?: string;
  };
}

/** @deprecated Use WeixinConfig instead. */
export type AppConfig = WeixinConfig;
