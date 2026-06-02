import { app } from 'electron'
import { join, basename, normalize } from 'path'
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'fs'
import { normalizeLocale, type AppLocale } from '../shared/locales'
import { isValidAppVersion } from '../shared/whatsNew'
import { normalizeRemoteHosts, type RemoteHost } from '../shared/remoteServers'

export interface RecentWorkspace {
  path: string
  name: string
  lastOpenedAt: string
}

export type UiTheme = 'dark' | 'light'
export type ConnectionMode = 'local' | 'remote'
export type BinarySource = 'bundled' | 'path' | 'custom'
export type LastOpenEditorId =
  | 'explorer'
  | 'vs'
  | 'cursor'
  | 'vscode'
  | 'rider'
  | 'webstorm'
  | 'idea'
  | 'github-desktop'
  | 'git-bash'
  | 'terminal'

export interface WebSocketConnectionSettings {
  host?: string
  port?: number
}

export interface RemoteConnectionSettings {
  url?: string
  token?: string
}

export interface ActiveRemoteStackSettings {
  hostId: string
  stackId: string
}

export type BrowserUseApprovalMode = 'alwaysAsk' | 'askUnknown' | 'neverAsk'
export type TaskCompletionNotificationMode = 'whenUnfocused' | 'always' | 'never'

export interface BrowserUseSettings {
  approvalMode?: BrowserUseApprovalMode
  blockedDomains?: string[]
  allowedDomains?: string[]
}

export interface NotificationSettings {
  taskCompletionMode?: TaskCompletionNotificationMode
}

export interface ProfileSettings {
  /** GitHub login used to source the profile avatar/handle on the Profile page. */
  githubUsername?: string
}

export interface AppSettings {
  lastWorkspacePath?: string
  modulesDirectory?: string
  activeModuleVariants?: Record<string, string>
  binarySource?: BinarySource
  appServerBinaryPath?: string
  connectionMode?: ConnectionMode
  /** Legacy local AppServer port settings retained only for reading older settings files. */
  webSocket?: WebSocketConnectionSettings
  remote?: RemoteConnectionSettings
  /** Persisted Servers-surface connection target; tunnels are rebuilt from this on startup. */
  activeRemoteStack?: ActiveRemoteStackSettings
  /** UI theme; omitted or invalid values are treated as light by the renderer */
  theme?: UiTheme
  /** Display language (BCP 47); omitted or invalid values are treated as English */
  locale?: AppLocale
  /** Renderer-only preference; omitted or invalid values are treated as true */
  showThinkingContent?: boolean
  /** Desktop-local What's New read marker. */
  lastSeenWhatsNewVersion?: string
  recentWorkspaces?: RecentWorkspace[]
  /** Legacy Desktop-local cross-channel filter. Retained only so older settings files parse. */
  visibleChannels?: string[]
  lastOpenEditorId?: LastOpenEditorId
  browserUse?: BrowserUseSettings
  notifications?: NotificationSettings
  /** Desktop-local profile identity for the Profile page. */
  profile?: ProfileSettings
  /** Desktop-local pinned thread ids, keyed by normalized workspace path. */
  pinnedThreadIdsByWorkspace?: Record<string, string[]>
  /** Desktop-local saved remote servers (SSH targets + DotCraft stacks). */
  remoteHosts?: RemoteHost[]
}

const MAX_RECENT = 20

function normalizeBinarySource(settings: AppSettings): BinarySource {
  const source = settings.binarySource
  if (source === 'bundled' || source === 'path' || source === 'custom') {
    return source
  }
  return settings.appServerBinaryPath?.trim() ? 'custom' : 'bundled'
}

function normalizeModulesDirectory(settings: AppSettings): string | undefined {
  const raw = settings.modulesDirectory?.trim()
  if (!raw) return undefined
  return normalize(raw)
}

function normalizeLastOpenEditorId(settings: AppSettings): LastOpenEditorId | undefined {
  const value = settings.lastOpenEditorId
  if (
    value === 'explorer' ||
    value === 'vs' ||
    value === 'cursor' ||
    value === 'vscode' ||
    value === 'rider' ||
    value === 'webstorm' ||
    value === 'idea' ||
    value === 'github-desktop' ||
    value === 'git-bash' ||
    value === 'terminal'
  ) {
    return value
  }
  return undefined
}

function normalizeBrowserUseApprovalMode(value: unknown): BrowserUseApprovalMode {
  return value === 'askUnknown' || value === 'neverAsk' ? value : 'alwaysAsk'
}

function normalizeDomainList(value: unknown): string[] {
  if (!Array.isArray(value)) return []
  const seen = new Set<string>()
  const domains: string[] = []
  for (const item of value) {
    if (typeof item !== 'string') continue
    const trimmed = item.trim()
    if (!trimmed || /[\u0000-\u001f]/.test(trimmed)) continue
    let domain = trimmed.toLowerCase().replace(/\.+$/, '')
    try {
      const candidate = /^[a-zA-Z][a-zA-Z\d+\-.]*:/.test(trimmed)
        ? trimmed
        : `https://${trimmed}`
      domain = new URL(candidate).hostname.trim().toLowerCase().replace(/\.+$/, '')
    } catch {
      // Keep the trimmed domain-like value below so older simple entries survive.
    }
    if (!domain || /[\u0000-\u001f]/.test(domain)) continue
    if (seen.has(domain)) continue
    seen.add(domain)
    domains.push(domain)
  }
  return domains
}

function normalizeBrowserUseSettings(settings: AppSettings): BrowserUseSettings {
  const raw = settings.browserUse
  const source: BrowserUseSettings = raw != null && typeof raw === 'object' && !Array.isArray(raw) ? raw : {}
  return {
    approvalMode: normalizeBrowserUseApprovalMode(source.approvalMode),
    blockedDomains: normalizeDomainList(source.blockedDomains),
    allowedDomains: normalizeDomainList(source.allowedDomains)
  }
}

function normalizeTaskCompletionNotificationMode(value: unknown): TaskCompletionNotificationMode {
  return value === 'always' || value === 'never' ? value : 'whenUnfocused'
}

export function resolveTaskCompletionNotificationMode(settings?: AppSettings): TaskCompletionNotificationMode {
  return normalizeTaskCompletionNotificationMode(settings?.notifications?.taskCompletionMode)
}

function normalizeNotificationSettings(settings: AppSettings): NotificationSettings {
  const raw = settings.notifications
  const source: NotificationSettings = raw != null && typeof raw === 'object' && !Array.isArray(raw) ? raw : {}
  return {
    taskCompletionMode: normalizeTaskCompletionNotificationMode(source.taskCompletionMode)
  }
}

function normalizeActiveModuleVariants(settings: AppSettings): Record<string, string> | undefined {
  const raw = settings.activeModuleVariants
  if (raw == null || typeof raw !== 'object' || Array.isArray(raw)) {
    return undefined
  }
  const normalized: Record<string, string> = {}
  for (const [key, value] of Object.entries(raw)) {
    if (typeof value !== 'string') continue
    const trimmedKey = key.trim().toLowerCase()
    const trimmedValue = value.trim()
    if (!trimmedKey || !trimmedValue) continue
    normalized[trimmedKey] = trimmedValue
  }
  return Object.keys(normalized).length > 0 ? normalized : undefined
}

/** GitHub logins are 1–39 chars of alphanumerics or single hyphens (not leading/trailing). */
const GITHUB_USERNAME_PATTERN = /^[a-zA-Z\d](?:[a-zA-Z\d]|-(?=[a-zA-Z\d])){0,38}$/

export function normalizeProfileSettings(settings: AppSettings): ProfileSettings | undefined {
  const raw = settings.profile
  if (raw == null || typeof raw !== 'object' || Array.isArray(raw)) {
    return undefined
  }
  const username = typeof raw.githubUsername === 'string' ? raw.githubUsername.trim() : ''
  if (!username || !GITHUB_USERNAME_PATTERN.test(username)) {
    return undefined
  }
  return { githubUsername: username }
}

function normalizeShowThinkingContent(settings: AppSettings): boolean | undefined {
  return typeof settings.showThinkingContent === 'boolean'
    ? settings.showThinkingContent
    : undefined
}

function normalizeLastSeenWhatsNewVersion(settings: AppSettings): string | undefined {
  const raw = settings.lastSeenWhatsNewVersion
  if (!isValidAppVersion(raw)) return undefined
  return raw.trim()
}

export function normalizeRemoteHostsSetting(settings: AppSettings): RemoteHost[] | undefined {
  const hosts = normalizeRemoteHosts(settings.remoteHosts)
  return hosts.length > 0 ? hosts : undefined
}

function normalizeActiveRemoteStack(settings: AppSettings): ActiveRemoteStackSettings | undefined {
  const raw = settings.activeRemoteStack
  if (raw == null || typeof raw !== 'object' || Array.isArray(raw)) {
    return undefined
  }
  const hostId = typeof raw.hostId === 'string' ? raw.hostId.trim() : ''
  const stackId = typeof raw.stackId === 'string' ? raw.stackId.trim() : ''
  return hostId && stackId ? { hostId, stackId } : undefined
}

export function normalizePinnedThreadIdsByWorkspace(settings: AppSettings): Record<string, string[]> | undefined {
  const raw = settings.pinnedThreadIdsByWorkspace
  if (raw == null || typeof raw !== 'object' || Array.isArray(raw)) {
    return undefined
  }

  const normalized: Record<string, string[]> = {}
  for (const [workspacePath, threadIds] of Object.entries(raw)) {
    const trimmedWorkspacePath = workspacePath.trim()
    if (!trimmedWorkspacePath || !Array.isArray(threadIds)) continue
    const normalizedWorkspacePath = normalize(trimmedWorkspacePath)
    if (!normalizedWorkspacePath) continue

    const seen = new Set<string>()
    const ids: string[] = []
    for (const value of threadIds) {
      if (typeof value !== 'string') continue
      const id = value.trim()
      if (!id || /[\u0000-\u001f]/.test(id) || seen.has(id)) continue
      seen.add(id)
      ids.push(id)
    }

    if (ids.length > 0) {
      normalized[normalizedWorkspacePath] = ids
    }
  }

  return Object.keys(normalized).length > 0 ? normalized : undefined
}

function getSettingsPath(): string {
  return join(app.getPath('userData'), 'settings.json')
}

export function loadSettings(): AppSettings {
  const filePath = getSettingsPath()
  const systemLocale = normalizeLocale(app.getLocale())
  try {
    if (existsSync(filePath)) {
      const raw = JSON.parse(readFileSync(filePath, 'utf8')) as AppSettings
      raw.binarySource = normalizeBinarySource(raw)
      raw.connectionMode = normalizeConnectionMode(raw)
      raw.modulesDirectory = normalizeModulesDirectory(raw)
      raw.lastOpenEditorId = normalizeLastOpenEditorId(raw)
      raw.browserUse = normalizeBrowserUseSettings(raw)
      raw.notifications = normalizeNotificationSettings(raw)
      raw.activeModuleVariants = normalizeActiveModuleVariants(raw)
      raw.showThinkingContent = normalizeShowThinkingContent(raw)
      raw.lastSeenWhatsNewVersion = normalizeLastSeenWhatsNewVersion(raw)
      raw.profile = normalizeProfileSettings(raw)
      raw.pinnedThreadIdsByWorkspace = normalizePinnedThreadIdsByWorkspace(raw)
      raw.remoteHosts = normalizeRemoteHostsSetting(raw)
      raw.activeRemoteStack = normalizeActiveRemoteStack(raw)
      if (raw.locale !== undefined) {
        raw.locale = normalizeLocale(raw.locale)
      } else {
        raw.locale = systemLocale
      }
      return raw
    }
  } catch {
    // Ignore corrupt settings
  }
  return { locale: systemLocale }
}

function normalizeConnectionMode(settings: AppSettings): ConnectionMode | undefined {
  return settings.connectionMode === 'remote' ? 'remote' : 'local'
}

export function saveSettings(settings: AppSettings): void {
  const filePath = getSettingsPath()
  try {
    const dir = join(filePath, '..')
    if (!existsSync(dir)) mkdirSync(dir, { recursive: true })
    settings.binarySource = normalizeBinarySource(settings)
    settings.connectionMode = normalizeConnectionMode(settings)
    settings.modulesDirectory = normalizeModulesDirectory(settings)
    settings.lastOpenEditorId = normalizeLastOpenEditorId(settings)
    settings.browserUse = normalizeBrowserUseSettings(settings)
    settings.notifications = normalizeNotificationSettings(settings)
    settings.activeModuleVariants = normalizeActiveModuleVariants(settings)
    settings.showThinkingContent = normalizeShowThinkingContent(settings)
    settings.lastSeenWhatsNewVersion = normalizeLastSeenWhatsNewVersion(settings)
    settings.profile = normalizeProfileSettings(settings)
    settings.pinnedThreadIdsByWorkspace = normalizePinnedThreadIdsByWorkspace(settings)
    settings.remoteHosts = normalizeRemoteHostsSetting(settings)
    settings.activeRemoteStack = normalizeActiveRemoteStack(settings)
    writeFileSync(filePath, JSON.stringify(settings, null, 2), 'utf8')
  } catch {
    // Non-fatal
  }
}

/**
 * Adds (or moves) a workspace to the top of the recent list with LRU eviction.
 * Mutates and returns the settings object.
 */
export function addRecentWorkspace(settings: AppSettings, workspacePath: string): AppSettings {
  const name = basename(workspacePath)
  const entry: RecentWorkspace = {
    path: workspacePath,
    name,
    lastOpenedAt: new Date().toISOString()
  }
  const existing = settings.recentWorkspaces ?? []
  // Remove duplicate if present, then prepend
  const filtered = existing.filter((r) => r.path !== workspacePath)
  settings.recentWorkspaces = [entry, ...filtered].slice(0, MAX_RECENT)
  settings.lastWorkspacePath = workspacePath
  return settings
}

export function getRecentWorkspaces(settings: AppSettings): RecentWorkspace[] {
  return settings.recentWorkspaces ?? []
}

/**
 * Clears the persisted recent workspace list.
 * Mutates and returns the settings object.
 */
export function clearRecentWorkspaces(settings: AppSettings): AppSettings {
  settings.recentWorkspaces = []
  return settings
}
