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
