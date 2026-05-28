export type QQTarget = { kind: "group" | "user"; id: string };

export function parseQQTarget(target: string): QQTarget | null {
  const raw = String(target).trim();
  if (!raw) return null;
  if (raw.toLowerCase().startsWith("group:")) {
    const id = raw.slice("group:".length);
    return /^\d+$/.test(id) ? { kind: "group", id } : null;
  }
  if (raw.toLowerCase().startsWith("user:")) {
    const id = raw.slice("user:".length);
    return /^\d+$/.test(id) ? { kind: "user", id } : null;
  }
  return /^\d+$/.test(raw) ? { kind: "user", id: raw } : null;
}

export function channelContextForQQEvent(isGroup: boolean, groupId: number | undefined, userId: number): string {
  return isGroup ? `group:${groupId ?? 0}` : `user:${userId}`;
}
