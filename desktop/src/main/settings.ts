import { app } from 'electron'
import { join, basename, normalize } from 'path'
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'fs'
import { normalizeLocale, type AppLocale } from '../shared/locales'
import { isValidAppVersion } from '../shared/whatsNew'
import { normalizeRemoteHosts, type RemoteHost } from '../shared/remoteServers'
import type { CreatedSatelliteInvite, SatelliteThreadRoute } from '../shared/satellites'
export type { CreatedSatelliteInvite, SatelliteThreadRoute } from '../shared/satellites'
import {
  isRemoteProjectKey,
  normalizeWorkspaceProjectKey,
  sameWorkspaceProjectKey
} from '../shared/workspaceProjectKey'
import {
  DEFAULT_INTERFACE_ZOOM,
  normalizeAccentHex,
  normalizeCodeFontSize,
  normalizeInterfaceZoom,
  type DiffMarkerMode,
  type ReduceMotionMode
} from '../shared/appearance'
import { normalizeThemeSeeds, type ThemeSeedOverrides, type ThemeVariant } from '../shared/themeSeed'
import type {
  BinarySource,
  BrowserUseApprovalMode,
  ConnectionMode,
  TaskCompletionNotificationMode
} from '../shared/desktopSettings'
export type {
  BinarySource,
  BrowserUseApprovalMode,
  ConnectionMode,
  TaskCompletionNotificationMode
} from '../shared/desktopSettings'

export interface RecentWorkspace {
  path: string
  name: string
  lastOpenedAt: string
  /**
   * Stable across re-opens, so the sidebar keeps a fixed project order while the
   * recents array itself stays MRU for the "Recent Workspaces" menu.
   */
  firstOpenedAt?: string
  /**
   * Absolute normalized runtime roots beyond the primary folder (`path`), which is
   * the Project identity and never a member of this list.
   */
  secondaryFolders?: string[]
}

export type UiTheme = 'system' | 'dark' | 'light'
export type LastForegroundEntry = 'workspace' | 'chats' | 'welcome'
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

export interface BrowserUseSettings {
  approvalMode?: BrowserUseApprovalMode
  blockedDomains?: string[]
  allowedDomains?: string[]
}

export interface NotificationSettings {
  taskCompletionMode?: TaskCompletionNotificationMode
}

export interface ProfileSettings {
  githubUsername?: string
}

export interface VoiceSettings {
  /** Omitted follows the operating-system default. */
  deviceId?: string
}

export interface AppSettings {
  lastWorkspacePath?: string
  lastForegroundEntry?: LastForegroundEntry
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
  /** UI theme preference; omitted or invalid values are treated as light by the renderer. `system` follows the OS. */
  theme?: UiTheme
  /** Custom accent color (`#rrggbb`); omitted uses the per-theme token default. */
  accent?: string
  /** Per-variant background and contrast overrides; omitted uses the token defaults. */
  themeSeeds?: Record<ThemeVariant, ThemeSeedOverrides>
  /** Code font size in px; omitted uses the token default. */
  codeFontSize?: number
  /** Diff rendering style; omitted is treated as `color`. */
  diffMarkers?: DiffMarkerMode
  /** Motion preference; omitted is treated as `system`. */
  reduceMotion?: ReduceMotionMode
  /** Omitted defaults to true. */
  pointerCursors?: boolean
  /** 1 = 100%; omitted defaults to 1. */
  interfaceZoom?: number
  /** Omitted defaults to true. */
  translucentSidebar?: boolean
  /** Display language (BCP 47); omitted or invalid values are treated as English */
  locale?: AppLocale
  /** Renderer-only preference; omitted or invalid values are treated as true */
  showThinkingContent?: boolean
  /** Omitted defaults to expanded. */
  projectsSectionCollapsed?: boolean
  /** Omitted defaults to expanded. */
  pinnedSectionCollapsed?: boolean
  /** Omitted defaults to expanded. */
  chatsSectionCollapsed?: boolean
  /** macOS-only preference controlling whether DotCraft appears in the menu bar. */
  showInMenuBar?: boolean
  lastSeenWhatsNewVersion?: string
  recentWorkspaces?: RecentWorkspace[]
  lastOpenEditorId?: LastOpenEditorId
  browserUse?: BrowserUseSettings
  notifications?: NotificationSettings
  profile?: ProfileSettings
  voice?: VoiceSettings
  /** Desktop-local pinned thread ids, keyed by normalized workspace path. */
  pinnedThreadIdsByWorkspace?: Record<string, string[]>
  /** Desktop-local pinned project identities (normalized local paths or remote ids). */
  pinnedProjectIds?: string[]
  remoteHosts?: RemoteHost[]
  /** Last explicit satellite route per thread, keyed `<workspace>::<threadId>`. */
  satelliteRouteByThread?: Record<string, SatelliteThreadRoute>
  /** Invitations this Desktop minted, so an arriving machine can be announced. */
  createdSatelliteInviteIds?: CreatedSatelliteInvite[]
}

const MAX_RECENT = 20

function normalizeBinarySource(settings: AppSettings): BinarySource {
  const source = settings.binarySource
  if (source === 'bundled' || source === 'path' || source === 'custom') {
    return source
  }
  return 'bundled'
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

function normalizeVoiceSettings(settings: AppSettings): VoiceSettings | undefined {
  const raw = settings.voice
  if (raw == null || typeof raw !== 'object' || Array.isArray(raw)) return undefined
  const deviceId = typeof raw.deviceId === 'string' ? raw.deviceId.trim() : ''
  if (!deviceId || /[\u0000-\u001f]/.test(deviceId)) return undefined
  return { deviceId }
}

function normalizeUiTheme(settings: AppSettings): UiTheme | undefined {
  const theme = settings.theme
  return theme === 'system' || theme === 'dark' || theme === 'light' ? theme : undefined
}

function normalizeAccentSetting(settings: AppSettings): string | undefined {
  return normalizeAccentHex(settings.accent) ?? undefined
}

/** Persist only the variants the user actually moved, so settings.json stays minimal. */
function normalizeThemeSeedsSetting(settings: AppSettings): Record<ThemeVariant, ThemeSeedOverrides> | undefined {
  const seeds = normalizeThemeSeeds(settings.themeSeeds)
  const customized = Object.keys(seeds.dark).length > 0 || Object.keys(seeds.light).length > 0
  return customized ? seeds : undefined
}

function normalizeCodeFontSizeSetting(settings: AppSettings): number | undefined {
  return normalizeCodeFontSize(settings.codeFontSize) ?? undefined
}

/** Persist only the non-default value so settings.json stays minimal. */
function normalizeDiffMarkersSetting(settings: AppSettings): DiffMarkerMode | undefined {
  return settings.diffMarkers === 'sign' ? 'sign' : undefined
}

function normalizeReduceMotionSetting(settings: AppSettings): ReduceMotionMode | undefined {
  return settings.reduceMotion === 'on' || settings.reduceMotion === 'off' ? settings.reduceMotion : undefined
}

function normalizePointerCursorsSetting(settings: AppSettings): boolean | undefined {
  return settings.pointerCursors === false ? false : undefined
}

function normalizeInterfaceZoomSetting(settings: AppSettings): number | undefined {
  const zoom = normalizeInterfaceZoom(settings.interfaceZoom)
  return zoom === DEFAULT_INTERFACE_ZOOM ? undefined : zoom
}

function normalizeTranslucentSidebarSetting(settings: AppSettings): boolean | undefined {
  return settings.translucentSidebar === false ? false : undefined
}

function normalizeShowThinkingContent(settings: AppSettings): boolean | undefined {
  return typeof settings.showThinkingContent === 'boolean'
    ? settings.showThinkingContent
    : undefined
}

function normalizeProjectsSectionCollapsed(settings: AppSettings): boolean | undefined {
  return settings.projectsSectionCollapsed === true ? true : undefined
}

function normalizeChatsSectionCollapsed(settings: AppSettings): boolean | undefined {
  return settings.chatsSectionCollapsed === true ? true : undefined
}

export function normalizeShowInMenuBar(settings: AppSettings): boolean | undefined {
  return typeof settings.showInMenuBar === 'boolean'
    ? settings.showInMenuBar
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

const SATELLITE_ROUTE_MAX_AGE_MS = 30 * 24 * 60 * 60 * 1000
const SATELLITE_ROUTE_MAX_ENTRIES = 200
const CREATED_SATELLITE_INVITE_MAX_ENTRIES = 20

function satelliteRouteEntry(value: unknown): SatelliteThreadRoute | null {
  if (value == null || typeof value !== 'object' || Array.isArray(value)) return null
  const raw = value as Partial<SatelliteThreadRoute>
  const hostId = typeof raw.hostId === 'string' ? raw.hostId.trim() : ''
  const workspaceId = typeof raw.workspaceId === 'string' ? raw.workspaceId.trim() : ''
  const at = typeof raw.at === 'string' ? raw.at.trim() : ''
  if (!hostId || !workspaceId || !Number.isFinite(Date.parse(at))) return null
  return { hostId, workspaceId, at }
}

export function normalizeSatelliteRouteByThread(
  settings: AppSettings,
  now: number = Date.now()
): Record<string, SatelliteThreadRoute> | undefined {
  const raw = settings.satelliteRouteByThread
  if (raw == null || typeof raw !== 'object' || Array.isArray(raw)) return undefined

  const entries: Array<[string, SatelliteThreadRoute]> = []
  for (const [key, value] of Object.entries(raw)) {
    const trimmedKey = key.trim()
    if (!trimmedKey || !trimmedKey.includes('::')) continue
    const route = satelliteRouteEntry(value)
    if (!route || now - Date.parse(route.at) > SATELLITE_ROUTE_MAX_AGE_MS) continue
    entries.push([trimmedKey, route])
  }
  if (entries.length === 0) return undefined

  // Newest choices win when the map is over the cap.
  entries.sort((a, b) => Date.parse(b[1].at) - Date.parse(a[1].at))
  return Object.fromEntries(entries.slice(0, SATELLITE_ROUTE_MAX_ENTRIES))
}

export function normalizeCreatedSatelliteInviteIds(
  settings: AppSettings,
  now: number = Date.now()
): CreatedSatelliteInvite[] | undefined {
  const raw = settings.createdSatelliteInviteIds
  if (!Array.isArray(raw)) return undefined

  const seen = new Set<string>()
  const normalized: CreatedSatelliteInvite[] = []
  for (const value of raw) {
    if (value == null || typeof value !== 'object' || Array.isArray(value)) continue
    const entry = value as Partial<CreatedSatelliteInvite>
    const inviteId = typeof entry.inviteId === 'string' ? entry.inviteId.trim() : ''
    const expiresAt = typeof entry.expiresAt === 'string' ? entry.expiresAt.trim() : ''
    const expiry = Date.parse(expiresAt)
    if (!inviteId || seen.has(inviteId) || !Number.isFinite(expiry) || expiry <= now) continue
    seen.add(inviteId)
    normalized.push({ inviteId, expiresAt })
  }
  return normalized.length > 0
    ? normalized.slice(-CREATED_SATELLITE_INVITE_MAX_ENTRIES)
    : undefined
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

function normalizePinnedSectionCollapsed(settings: AppSettings): boolean | undefined {
  return settings.pinnedSectionCollapsed === true ? true : undefined
}

function normalizeLastForegroundEntry(settings: AppSettings): LastForegroundEntry | undefined {
  const value = settings.lastForegroundEntry
  return value === 'workspace' || value === 'chats' || value === 'welcome'
    ? value
    : undefined
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
    const normalizedWorkspacePath = normalizeWorkspaceProjectKey(trimmedWorkspacePath)
    if (!normalizedWorkspacePath) continue

    const seen = new Set(normalized[normalizedWorkspacePath] ?? [])
    const ids: string[] = normalized[normalizedWorkspacePath] ? [...normalized[normalizedWorkspacePath]] : []
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

export function normalizePinnedProjectIds(settings: AppSettings): string[] | undefined {
  const raw = settings.pinnedProjectIds
  if (!Array.isArray(raw)) return undefined

  const seen = new Set<string>()
  const normalized: string[] = []
  for (const value of raw) {
    if (typeof value !== 'string') continue
    const id = normalizeWorkspaceProjectKey(value)
    if (!id || seen.has(id)) continue
    seen.add(id)
    normalized.push(id)
  }
  return normalized.length > 0 ? normalized : undefined
}

/** The primary folder is the Project identity and is never a member of this list. */
function sanitizeSecondaryFolders(folders: unknown, primaryPath: string): string[] {
  if (!Array.isArray(folders)) return []
  const primaryKey = normalizeWorkspaceProjectKey(primaryPath)
  const seen = new Set<string>()
  const result: string[] = []
  for (const value of folders) {
    if (typeof value !== 'string') continue
    const trimmed = value.trim()
    if (!trimmed) continue
    const key = normalizeWorkspaceProjectKey(trimmed)
    if (!key || key === primaryKey || seen.has(key)) continue
    seen.add(key)
    result.push(trimmed)
  }
  return result
}

function normalizeRecentWorkspaces(settings: AppSettings): RecentWorkspace[] | undefined {
  const raw = settings.recentWorkspaces
  if (!Array.isArray(raw)) return undefined
  return raw.map((recent) => {
    const secondaryFolders = sanitizeSecondaryFolders(recent.secondaryFolders, recent.path)
    const entry: RecentWorkspace = {
      path: recent.path,
      name: recent.name,
      lastOpenedAt: recent.lastOpenedAt,
      ...(recent.firstOpenedAt ? { firstOpenedAt: recent.firstOpenedAt } : {}),
      ...(secondaryFolders.length > 0 ? { secondaryFolders } : {})
    }
    return entry
  })
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
      raw.lastForegroundEntry = normalizeLastForegroundEntry(raw)
      raw.binarySource = normalizeBinarySource(raw)
      raw.connectionMode = normalizeConnectionMode(raw)
      raw.modulesDirectory = normalizeModulesDirectory(raw)
      raw.lastOpenEditorId = normalizeLastOpenEditorId(raw)
      raw.browserUse = normalizeBrowserUseSettings(raw)
      raw.notifications = normalizeNotificationSettings(raw)
      raw.activeModuleVariants = normalizeActiveModuleVariants(raw)
      raw.showThinkingContent = normalizeShowThinkingContent(raw)
      raw.projectsSectionCollapsed = normalizeProjectsSectionCollapsed(raw)
      raw.pinnedSectionCollapsed = normalizePinnedSectionCollapsed(raw)
      raw.chatsSectionCollapsed = normalizeChatsSectionCollapsed(raw)
      raw.showInMenuBar = normalizeShowInMenuBar(raw)
      raw.theme = normalizeUiTheme(raw)
      raw.accent = normalizeAccentSetting(raw)
      raw.themeSeeds = normalizeThemeSeedsSetting(raw)
      raw.codeFontSize = normalizeCodeFontSizeSetting(raw)
      raw.diffMarkers = normalizeDiffMarkersSetting(raw)
      raw.reduceMotion = normalizeReduceMotionSetting(raw)
      raw.pointerCursors = normalizePointerCursorsSetting(raw)
      raw.interfaceZoom = normalizeInterfaceZoomSetting(raw)
      raw.translucentSidebar = normalizeTranslucentSidebarSetting(raw)
      raw.lastSeenWhatsNewVersion = normalizeLastSeenWhatsNewVersion(raw)
      raw.profile = normalizeProfileSettings(raw)
      raw.voice = normalizeVoiceSettings(raw)
      raw.pinnedThreadIdsByWorkspace = normalizePinnedThreadIdsByWorkspace(raw)
      raw.pinnedProjectIds = normalizePinnedProjectIds(raw)
      raw.recentWorkspaces = normalizeRecentWorkspaces(raw)
      raw.remoteHosts = normalizeRemoteHostsSetting(raw)
      raw.satelliteRouteByThread = normalizeSatelliteRouteByThread(raw)
      raw.createdSatelliteInviteIds = normalizeCreatedSatelliteInviteIds(raw)
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
    settings.lastForegroundEntry = normalizeLastForegroundEntry(settings)
    settings.connectionMode = normalizeConnectionMode(settings)
    settings.modulesDirectory = normalizeModulesDirectory(settings)
    settings.lastOpenEditorId = normalizeLastOpenEditorId(settings)
    settings.browserUse = normalizeBrowserUseSettings(settings)
    settings.notifications = normalizeNotificationSettings(settings)
    settings.activeModuleVariants = normalizeActiveModuleVariants(settings)
    settings.showThinkingContent = normalizeShowThinkingContent(settings)
    settings.projectsSectionCollapsed = normalizeProjectsSectionCollapsed(settings)
    settings.pinnedSectionCollapsed = normalizePinnedSectionCollapsed(settings)
    settings.chatsSectionCollapsed = normalizeChatsSectionCollapsed(settings)
    settings.showInMenuBar = normalizeShowInMenuBar(settings)
    settings.theme = normalizeUiTheme(settings)
    settings.accent = normalizeAccentSetting(settings)
    settings.themeSeeds = normalizeThemeSeedsSetting(settings)
    settings.codeFontSize = normalizeCodeFontSizeSetting(settings)
    settings.diffMarkers = normalizeDiffMarkersSetting(settings)
    settings.reduceMotion = normalizeReduceMotionSetting(settings)
    settings.pointerCursors = normalizePointerCursorsSetting(settings)
    settings.interfaceZoom = normalizeInterfaceZoomSetting(settings)
    settings.translucentSidebar = normalizeTranslucentSidebarSetting(settings)
    settings.lastSeenWhatsNewVersion = normalizeLastSeenWhatsNewVersion(settings)
    settings.profile = normalizeProfileSettings(settings)
    settings.voice = normalizeVoiceSettings(settings)
    settings.pinnedThreadIdsByWorkspace = normalizePinnedThreadIdsByWorkspace(settings)
    settings.pinnedProjectIds = normalizePinnedProjectIds(settings)
    settings.recentWorkspaces = normalizeRecentWorkspaces(settings)
    settings.remoteHosts = normalizeRemoteHostsSetting(settings)
    settings.satelliteRouteByThread = normalizeSatelliteRouteByThread(settings)
    settings.createdSatelliteInviteIds = normalizeCreatedSatelliteInviteIds(settings)
    settings.activeRemoteStack = normalizeActiveRemoteStack(settings)
    writeFileSync(filePath, JSON.stringify(settings, null, 2), 'utf8')
  } catch {
    // Non-fatal
  }
}

/** Mutates and returns the settings object. */
export function addRecentWorkspace(settings: AppSettings, workspacePath: string): AppSettings {
  const now = new Date().toISOString()
  const existing = settings.recentWorkspaces ?? []
  const prior = existing.find((r) => sameWorkspaceProjectKey(r.path, workspacePath))
  // Preserve a Project's custom name and its configured secondary folders when
  // re-touching an existing entry (e.g. a later `switch`); only brand-new entries
  // fall back to the folder basename.
  const name = prior?.name?.trim() || basename(workspacePath)
  const secondaryFolders = sanitizeSecondaryFolders(prior?.secondaryFolders, workspacePath)
  const entry: RecentWorkspace = {
    path: workspacePath,
    name,
    lastOpenedAt: now,
    // Preserve the original add time so the sidebar order stays stable; backfill
    // legacy entries from their last-opened time.
    firstOpenedAt: prior?.firstOpenedAt ?? prior?.lastOpenedAt ?? now,
    ...(secondaryFolders.length > 0 ? { secondaryFolders } : {})
  }
  const filtered = existing.filter((r) => !sameWorkspaceProjectKey(r.path, workspacePath))
  settings.recentWorkspaces = [entry, ...filtered].slice(0, MAX_RECENT)
  settings.lastWorkspacePath = workspacePath
  settings.lastForegroundEntry = 'workspace'
  return settings
}

export function getRecentWorkspaces(settings: AppSettings): RecentWorkspace[] {
  return settings.recentWorkspaces ?? []
}

/** Mutates and returns the settings object. */
export function removeRecentWorkspace(settings: AppSettings, workspacePath: string): AppSettings {
  settings.recentWorkspaces = (settings.recentWorkspaces ?? []).filter((recent) =>
    !sameWorkspaceProjectKey(recent.path, workspacePath)
  )
  settings.pinnedProjectIds = settings.pinnedProjectIds?.filter((projectId) =>
    !sameWorkspaceProjectKey(projectId, workspacePath)
  )
  return settings
}

/**
 * A `previousPath` whose identity differs from `primaryFolder` reassigns the Project:
 * the previous entry and its pinned state migrate to the new key, while
 * `pinnedThreadIdsByWorkspace` is intentionally left alone because existing threads
 * keep their original workspace. Mutates and returns the settings object.
 */
export function saveLocalProject(
  settings: AppSettings,
  params: {
    previousPath?: string
    primaryFolder: string
    secondaryFolders: string[]
    name?: string
  }
): AppSettings {
  const primaryFolder = normalize(params.primaryFolder.trim())
  const normalizedSecondaries = (params.secondaryFolders ?? [])
    .map((folder) => (typeof folder === 'string' ? folder.trim() : ''))
    .filter((folder) => folder.length > 0)
    .map((folder) => normalize(folder))
  const secondaryFolders = sanitizeSecondaryFolders(normalizedSecondaries, primaryFolder)
  const displayName = params.name?.trim() || basename(primaryFolder)
  const now = new Date().toISOString()
  const primaryKey = normalizeWorkspaceProjectKey(primaryFolder)
  const previousPath = params.previousPath?.trim()

  if (previousPath && !sameWorkspaceProjectKey(previousPath, primaryFolder)) {
    const previousKey = normalizeWorkspaceProjectKey(previousPath)
    settings.recentWorkspaces = (settings.recentWorkspaces ?? []).filter(
      (recent) => !sameWorkspaceProjectKey(recent.path, previousPath)
    )
    if (settings.pinnedProjectIds && previousKey) {
      settings.pinnedProjectIds = settings.pinnedProjectIds.map((id) =>
        id === previousKey ? primaryKey : id
      )
    }
  }

  const existing = settings.recentWorkspaces ?? []
  const prior = existing.find((recent) => sameWorkspaceProjectKey(recent.path, primaryFolder))
  const entry: RecentWorkspace = {
    path: primaryFolder,
    name: displayName,
    lastOpenedAt: now,
    firstOpenedAt: prior?.firstOpenedAt ?? prior?.lastOpenedAt ?? now,
    ...(secondaryFolders.length > 0 ? { secondaryFolders } : {})
  }

  if (prior) {
    // Upsert in place so the stable sidebar order is preserved.
    settings.recentWorkspaces = existing.map((recent) =>
      sameWorkspaceProjectKey(recent.path, primaryFolder) ? entry : recent
    )
  } else {
    settings.recentWorkspaces = [entry, ...existing].slice(0, MAX_RECENT)
  }
  return settings
}

/** Mutates and returns the settings object. */
export function clearRecentWorkspaces(settings: AppSettings): AppSettings {
  settings.recentWorkspaces = []
  // Remote identities are not members of the local recent-project list and
  // remain pinned so reconnecting restores their rail position.
  settings.pinnedProjectIds = settings.pinnedProjectIds?.filter(isRemoteProjectKey)
  return settings
}
