export interface TelegramConfig {
  dotcraft: {
    wsUrl: string;
    token?: string;
  };
  telegram: {
    botToken: string;
    httpsProxy?: string;
    approvalTimeoutMs?: number;
    pollTimeoutMs?: number;
  };
}

/** @deprecated Use TelegramConfig instead. */
export type AppConfig = TelegramConfig;
