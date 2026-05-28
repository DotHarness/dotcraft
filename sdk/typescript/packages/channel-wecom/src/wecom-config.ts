export interface WeComRobotConfig {
  path: string;
  token: string;
  aesKey: string;
}

export interface WeComConfig {
  dotcraft: {
    wsUrl: string;
    token?: string;
  };
  wecom: {
    host?: string;
    port?: number;
    scheme?: "http" | "https";
    tls?: {
      certPath?: string;
      keyPath?: string;
    };
    adminUsers?: string[];
    whitelistedUsers?: string[];
    whitelistedChats?: string[];
    approvalTimeoutMs?: number;
    robots?: WeComRobotConfig[];
    defaultRobot?: Omit<WeComRobotConfig, "path">;
  };
}

