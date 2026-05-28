export type QQUserRole = "unauthorized" | "whitelisted" | "admin";

export interface QQPermissionConfig {
  adminUsers?: Array<number | string>;
  whitelistedUsers?: Array<number | string>;
  whitelistedGroups?: Array<number | string>;
}

export class QQPermissionService {
  private readonly adminUsers: Set<string>;
  private readonly whitelistedUsers: Set<string>;
  private readonly whitelistedGroups: Set<string>;

  constructor(config: QQPermissionConfig) {
    this.adminUsers = new Set(normalizeIds(config.adminUsers));
    this.whitelistedUsers = new Set(normalizeIds(config.whitelistedUsers));
    this.whitelistedGroups = new Set(normalizeIds(config.whitelistedGroups));
  }

  getUserRole(userId: number | string, groupId?: number | string | null): QQUserRole {
    const user = normalizeId(userId);
    const group = groupId === undefined || groupId === null || groupId === "" ? "" : normalizeId(groupId);
    if (this.adminUsers.has(user)) return "admin";
    if (this.whitelistedUsers.has(user)) return "whitelisted";
    if (group && this.whitelistedGroups.has(group)) return "whitelisted";
    return "unauthorized";
  }
}

export function normalizeId(value: number | string): string {
  const text = String(value).trim();
  if (!/^\d+$/.test(text)) {
    throw new Error(`Invalid QQ id '${text}'.`);
  }
  return text;
}

export function normalizeIds(values: Array<number | string> | undefined): string[] {
  return (values ?? []).map((value) => normalizeId(value));
}
