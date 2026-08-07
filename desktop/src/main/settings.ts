import { app } from 'electron'
import { join, basename, normalize } from 'path'
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'fs'
import { normalizeLocale, type AppLocale } from '../shared/locales'
import { isValidAppVersion } from '../shared/whatsNew'
import { normalizeRemoteHosts, type RemoteHost } from '../shared/remoteServers'
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

export interface RecentWorkspace {
  path: string
  name: string
  lastOpenedAt: string
  /**
   * When this workspace was first added. Stable across re-opens; used to keep a
   * fixed project order in the sidebar (the recents array itself stays MRU for
   * the "Recent Workspaces" menu).
   */
  firstOpenedAt?: string
  /**
   * Local multi-folder Project: additional runtime roots beyond the primary
   * folder (`path`), stored as absolute normalized paths. The primary folder is
   * the identity and is never a member of this list. Omitted for single-folder
   * projects.
   */
  secondaryFolders?: string[]
}

export type UiTheme = 'system' | 'dark' | 'light'
export type ConnectionMode = 'local' | 'remote'
export type BinarySource = 'bundled' | 'path' | 'custom'
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

export interface VoiceSettings {
  /** Preferred audio input. Omitted follows the operating-system default. */
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
  /** Code font size in px; omitted uses the token default. */
  codeFontSize?: number
  /** Diff rendering style; omitted is treated as `color`. */
  diffMarkers?: DiffMarkerMode
  /** Motion preference; omitted is treated as `system`. */
  reduceMotion?: ReduceMotionMode
  /** Whether interactive elements show a pointer cursor on hover; omitted defaults to true. */
  pointerCursors?: boolean
  /** Whole-interface zoom factor (1 = 100%); omitted defaults to 1. */
  interfaceZoom?: number
  /** Whether the window chrome around the sidebar stays translucent; omitted defaults to true. */
  translucentSidebar?: boolean
  /** Display language (BCP 47); omitted or invalid values are treated as English */
  locale?: AppLocale
  /** Renderer-only preference; omitted or invalid values are treated as true */
  showThinkingContent?: boolean
  /** Sidebar preference: whether the Projects section is collapsed. Omitted defaults to expanded. */
  projectsSectionCollapsed?: boolean
  /** Sidebar preference: whether the Pinned section is collapsed. Omitted defaults to expanded. */
  pinnedSectionCollapsed?: boolean
  /** Sidebar preference: whether the Chats section is collapsed. Omitted defaults to expanded. */
  chatsSectionCollapsed?: boolean
  /** macOS-only preference controlling whether DotCraft appears in the menu bar. */
  showInMenuBar?: boolean
  /** Desktop-local What's New read marker. */
  lastSeenWhatsNewVersion?: string
  recentWorkspaces?: RecentWorkspace[]
  lastOpenEditorId?: LastOpenEditorId
  browserUse?: BrowserUseSettings
  notifications?: NotificationSettings
  /** Desktop-local profile identity for the Profile page. */
  profile?: ProfileSettings
  /** Desktop-local microphone preference for Voice Input. */
  voice?: VoiceSettings
  /** Desktop-local pinned thread ids, keyed by normalized workspace path. */
  pinnedThreadIdsByWorkspace?: Record<string, string[]>
  /** Desktop-local pinned project identities (normalized local paths or remote ids). */
  pinnedProjectIds?: string[]
  /** Desktop-local saved remote servers (SSH targets + DotCraft stacks). */
  remoteHosts?: RemoteHost[]
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
  // Pointer cursors default to on, so only persist an explicit opt-out (false).
  return settings.pointerCursors === false ? false : undefined
}

function normalizeInterfaceZoomSetting(settings: AppSettings): number | undefined {
  const zoom = normalizeInterfaceZoom(settings.interfaceZoom)
  return zoom === DEFAULT_INTERFACE_ZOOM ? undefined : zoom
}

function normalizeTranslucentSidebarSetting(settings: AppSettings): boolean | undefined {
  // Translucent sidebar defaults to on, so only persist an explicit opt-out (false).
  return settings.translucentSidebar === false ? false : undefined
}

function normalizeShowThinkingContent(settings: AppSettings): boolean | undefined {
  return typeof settings.showThinkingContent === 'boolean'
    ? settings.showThinkingContent
    : undefined
}

function normalizeProjectsSectionCollapsed(settings: AppSettings): boolean | undefined {
  // Sections default to expanded, so only persist an explicit collapse (true).
  return settings.projectsSectionCollapsed === true ? true : undefined
}

function normalizeChatsSectionCollapsed(settings: AppSettings): boolean | undefined {
  // Sections default to expanded, so only persist an explicit collapse (true).
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
  // Sections default to expanded, so only persist an explicit collapse (true).
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

/**
 * Sanitizes a Project's secondary folder list: keep only non-empty strings,
 * dedupe by normalized project key (preserving first occurrence), and drop any
 * folder that resolves to the primary path. The primary folder is the identity
 * and is never a member of this list.
 */
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

/** Normalizes each recent workspace's persisted `secondaryFolders` in place. */
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
  // Remove duplicate if present, then prepend (recents array stays MRU).
  const filtered = existing.filter((r) => !sameWorkspaceProjectKey(r.path, workspacePath))
  settings.recentWorkspaces = [entry, ...filtered].slice(0, MAX_RECENT)
  settings.lastWorkspacePath = workspacePath
  settings.lastForegroundEntry = 'workspace'
  return settings
}

export function getRecentWorkspaces(settings: AppSettings): RecentWorkspace[] {
  return settings.recentWorkspaces ?? []
}

/**
 * Removes a workspace from the recent list.
 * Mutates and returns the settings object.
 */
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
 * Creates or updates a local multi-folder Project, keyed by its primary folder.
 *
 * The primary folder is the Project identity (equal to `RecentWorkspace.path`);
 * secondary folders are additional runtime roots. When `previousPath` is given
 * and its identity differs from `primaryFolder` (a "make primary" reassignment),
 * the previous entry is removed and its pinned state migrates to the new key.
 * Existing threads keep their original workspace, so `pinnedThreadIdsByWorkspace`
 * is intentionally left untouched.
 *
 * Mutates and returns the settings object.
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
  // Normalize each incoming secondary folder to an absolute path, then dedupe by
  // key and drop any that equals the primary identity.
  const normalizedSecondaries = (params.secondaryFolders ?? [])
    .map((folder) => (typeof folder === 'string' ? folder.trim() : ''))
    .filter((folder) => folder.length > 0)
    .map((folder) => normalize(folder))
  const secondaryFolders = sanitizeSecondaryFolders(normalizedSecondaries, primaryFolder)
  const displayName = params.name?.trim() || basename(primaryFolder)
  const now = new Date().toISOString()
  const primaryKey = normalizeWorkspaceProjectKey(primaryFolder)
  const previousPath = params.previousPath?.trim()

  // "Make primary" reassigned the identity folder: drop the previous entry and
  // carry its pinned state over to the new primary-folder key.
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
    // New identity: prepend as MRU, respecting the recents cap.
    settings.recentWorkspaces = [entry, ...existing].slice(0, MAX_RECENT)
  }
  return settings
}

/**
 * Clears the persisted recent workspace list.
 * Mutates and returns the settings object.
 */
export function clearRecentWorkspaces(settings: AppSettings): AppSettings {
  settings.recentWorkspaces = []
  // Remote identities are not members of the local recent-project list and
  // remain pinned so reconnecting restores their rail position.
  settings.pinnedProjectIds = settings.pinnedProjectIds?.filter(isRemoteProjectKey)
  return settings
}
