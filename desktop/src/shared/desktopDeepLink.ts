export interface WorkspaceOpenDeepLink {
  workspacePath: string
  threadId?: string
}

export function buildWorkspaceOpenDeepLink(workspacePath: string, threadId?: string | null): string {
  const url = new URL('dotcraft://workspace/open')
  url.searchParams.set('path', workspacePath)
  if (threadId?.trim()) {
    url.searchParams.set('threadId', threadId.trim())
  }
  return url.toString()
}

export function parseWorkspaceOpenDeepLink(value: string): WorkspaceOpenDeepLink | null {
  try {
    const parsed = new URL(value)
    if (
      parsed.protocol !== 'dotcraft:' ||
      parsed.hostname !== 'workspace' ||
      parsed.pathname.replace(/\/+$/, '') !== '/open'
    ) {
      return null
    }

    const workspacePath = parsed.searchParams.get('path')?.trim()
    if (!workspacePath) return null

    const threadId = parsed.searchParams.get('threadId')?.trim()
    return {
      workspacePath,
      ...(threadId ? { threadId } : {})
    }
  } catch {
    return null
  }
}

export function findWorkspaceOpenDeepLink(argv: readonly string[]): WorkspaceOpenDeepLink | null {
  for (const value of argv) {
    const parsed = parseWorkspaceOpenDeepLink(value)
    if (parsed) return parsed
  }
  return null
}

export interface SatelliteJoinDeepLink {
  /** The original link, forwarded verbatim to the Satellite executable. */
  link: string
  inviteUrl: string
}

/** Parses `dotcraft://satellite/join?invite=<url-encoded http(s) invitation URL>`. */
export function parseSatelliteJoinDeepLink(value: string): SatelliteJoinDeepLink | null {
  try {
    const parsed = new URL(value)
    if (
      parsed.protocol !== 'dotcraft:' ||
      parsed.hostname !== 'satellite' ||
      parsed.pathname.replace(/\/+$/, '') !== '/join'
    ) {
      return null
    }

    const invite = parsed.searchParams.get('invite')?.trim()
    if (!invite) return null
    const inviteUrl = new URL(invite)
    if (inviteUrl.protocol !== 'http:' && inviteUrl.protocol !== 'https:') return null

    return { link: value, inviteUrl: invite }
  } catch {
    return null
  }
}

export function findSatelliteJoinDeepLink(argv: readonly string[]): SatelliteJoinDeepLink | null {
  for (const value of argv) {
    const parsed = parseSatelliteJoinDeepLink(value)
    if (parsed) return parsed
  }
  return null
}
