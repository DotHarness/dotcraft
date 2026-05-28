export interface QQConfig {
  dotcraft: {
    wsUrl: string;
    token?: string;
  };
  qq: {
    host?: string;
    port?: number;
    accessToken?: string;
    adminUsers?: Array<number | string>;
    whitelistedUsers?: Array<number | string>;
    whitelistedGroups?: Array<number | string>;
    approvalTimeoutMs?: number;
    requireMentionInGroups?: boolean;
  };
}
