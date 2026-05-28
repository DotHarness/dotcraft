export type WeComUserRole = "unauthorized" | "whitelisted" | "admin";

export interface WeComPermissionConfig {
  adminUsers?: string[];
  whitelistedUsers?: string[];
  whitelistedChats?: string[];
}

export class WeComPermissionService {
  private readonly adminUsers: Set<string>;
  private readonly whitelistedUsers: Set<string>;
  private readonly whitelistedChats: Set<string>;

  constructor(config: WeComPermissionConfig) {
    this.adminUsers = new Set(normalizeIds(config.adminUsers));
    this.whitelistedUsers = new Set(normalizeIds(config.whitelistedUsers));
    this.whitelistedChats = new Set(normalizeIds(config.whitelistedChats));
  }

  getUserRole(userId: string, chatId?: string | null): WeComUserRole {
    const user = normalizeId(userId);
    const chat = chatId ? normalizeId(chatId) : "";
    if (this.adminUsers.has(user)) return "admin";
    if (this.whitelistedUsers.has(user)) return "whitelisted";
    if (chat && this.whitelistedChats.has(chat)) return "whitelisted";
    return "unauthorized";
  }
}

function normalizeId(value: string): string {
  const text = String(value ?? "").trim();
  if (!text) throw new Error("WeCom id values must be non-empty strings.");
  return text;
}

function normalizeIds(values: string[] | undefined): string[] {
  return (values ?? []).map((value) => normalizeId(value));
}

