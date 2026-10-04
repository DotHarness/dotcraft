import { app } from 'electron'
import { join, normalize } from 'path'
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'fs'
import { normalizeLocale, type AppLocale } from '../shared/locales'
import { isValidAppVersion } from '../shared/whatsNew'
import { normalizeSshMachines, type SshMachine } from '../shared/sshMachines'
import type { CreatedSatelliteInvite, SatelliteThreadRoute } from '../shared/satellites'
export type { CreatedSatelliteInvite, SatelliteThreadRoute } from '../shared/satellites'
import {
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
import { normalizePetSetting, type PetSettings } from '../shared/pet'
import {
  normalizeManualThreadOrder,
  normalizeProjectKeyList,
  normalizeSidebarThreadSortMode,
  normalizeThreadOrderByProject,
  type SidebarThreadOrderSettings
} from '../shared/sidebarThreadOrder'
import type {
  BinarySource,
  BrowserUseApprovalMode,
  ConnectionMode,
  FollowUpQueueMode,
  TaskCompletionNotificationMode
} from '../shared/desktopSettings'
export type {
  BinarySource,
  BrowserUseApprovalMode,
  ConnectionMode,
  TaskCompletionNotificationMode
} from '../shared/desktopSettings'

export interface LocalProjectDetails {
  name?: string
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

export interface ActiveRemoteProjectSettings {
  machineId: string
  projectId: string
}

export interface BrowserUseSettings {
  approvalMode?: BrowserUseApprovalMode
  blockedDomains?: string[]
  allowedDomains?: string[]
}

export interface ComputerUseAllowedApp {
  id: string
  displayName: string
}

export interface ComputerUseSettings {
  alwaysAllowedApps?: ComputerUseAllowedApp[]
}

export interface NotificationSettings {
  taskCompletionMode?: TaskCompletionNotificationMode
  approvalRequests?: boolean
  questions?: boolean
}

export interface ProfileSettings {
  githubUsername?: string
}

export interface VoiceSettings {
  /** Omitted follows the operating-system default. */
  deviceId?: string
  chatGptTranscription?: boolean
}

export interface AppSettings extends SidebarThreadOrderSettings {
  remoteDesktopPluginGrants?: string[]
  /** Desktop follow-up behavior during an active turn; omitted defaults to steer. */
  followUpQueueMode?: FollowUpQueueMode
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
  activeRemoteProject?: ActiveRemoteProjectSettings
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
  localProjectDetails?: Record<string, LocalProjectDetails>
  lastOpenEditorId?: LastOpenEditorId
  browserUse?: BrowserUseSettings
  computerUse?: ComputerUseSettings
  notifications?: NotificationSettings
  profile?: ProfileSettings
  voice?: VoiceSettings
  /** Desktop-local pinned thread ids, keyed by normalized workspace path. */
  pinnedThreadIdsByWorkspace?: Record<string, string[]>
  /** Desktop-local pinned project identities (normalized local paths or remote ids). */
  pinnedProjectIds?: string[]
  remoteHosts?: SshMachine[]
  /** Last explicit satellite route per thread, keyed `<workspace>::<threadId>`. */
  satelliteRouteByThread?: Record<string, SatelliteThreadRoute>
  /** Bookmarked turn navigation entry ids (`<turnId>:<userItemId>`), keyed `<workspace>::<threadId>`. */
  turnBookmarksByThread?: Record<string, string[]>
  /** Invitations this Desktop minted, so an arriving machine can be announced. */
  createdSatelliteInviteIds?: CreatedSatelliteInvite[]
  screenViewDockWidth?: number
  screenViewDockPosition?: { x: number; y: number }
  /** Omitted while it equals the defaults. */
  pet?: PetSettings
}

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

export function normalizeComputerUseSettings(settings: AppSettings): ComputerUseSettings {
  const raw = settings.computerUse
  const source = raw != null && typeof raw === 'object' && !Array.isArray(raw) ? raw : {}
  const seen = new Set<string>()
  const alwaysAllowedApps: ComputerUseAllowedApp[] = []
  for (const entry of Array.isArray(source.alwaysAllowedApps) ? source.alwaysAllowedApps : []) {
    if (!entry || typeof entry !== 'object') continue
    const id = typeof entry.id === 'string' ? entry.id.trim() : ''
    if (!id || seen.has(id.toLowerCase())) continue
    seen.add(id.toLowerCase())
    const displayName = typeof entry.displayName === 'string' && entry.displayName.trim() ? entry.displayName.trim() : id
    alwaysAllowedApps.push({ id, displayName })
  }
  return { alwaysAllowedApps }
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
    taskCompletionMode: normalizeTaskCompletionNotificationMode(source.taskCompletionMode),
    approvalRequests: source.approvalRequests !== false,
    questions: source.questions !== false
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

export function normalizeVoiceSettings(settings: AppSettings): VoiceSettings | undefined {
  const raw = settings.voice
  if (raw == null || typeof raw !== 'object' || Array.isArray(raw)) return undefined
  const voice: VoiceSettings = {}
  const deviceId = typeof raw.deviceId === 'string' ? raw.deviceId.trim() : ''
  if (deviceId && !/[\u0000-\u001f]/.test(deviceId)) voice.deviceId = deviceId
  if (raw.chatGptTranscription === false) voice.chatGptTranscription = false
  return Object.keys(voice).length > 0 ? voice : undefined
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

function normalizeSidebarThreadOrderSettings(settings: AppSettings): void {
  settings.recentsThreadSort = normalizeSidebarThreadSortMode(settings.recentsThreadSort, 'updated')
  settings.projectsThreadSort = normalizeSidebarThreadSortMode(settings.projectsThreadSort, 'updated')
  settings.pinnedThreadSort = normalizeSidebarThreadSortMode(settings.pinnedThreadSort, 'manual')
  settings.recentsShowProjects = settings.recentsShowProjects === true ? true : undefined
  settings.recentsThreadOrder = normalizeManualThreadOrder(settings.recentsThreadOrder)
  settings.pinnedThreadOrder = normalizeManualThreadOrder(settings.pinnedThreadOrder)
  settings.threadOrderByProject = normalizeThreadOrderByProject(settings.threadOrderByProject)
  settings.projectSort = normalizeSidebarThreadSortMode(settings.projectSort, 'manual')
  settings.projectOrder = normalizeProjectKeyList(settings.projectOrder)
  settings.collapsedProjectIds = normalizeProjectKeyList(settings.collapsedProjectIds)
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

export function normalizeRemoteHostsSetting(settings: AppSettings): SshMachine[] | undefined {
  const hosts = normalizeSshMachines(settings.remoteHosts)
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

export function normalizeTurnBookmarksByThread(settings: AppSettings): Record<string, string[]> | undefined {
  const raw = settings.turnBookmarksByThread
  if (raw == null || typeof raw !== 'object' || Array.isArray(raw)) return undefined

  const normalized: Record<string, string[]> = {}
  for (const [key, entryIds] of Object.entries(raw)) {
    const trimmedKey = key.trim()
    if (!trimmedKey.includes('::') || !Array.isArray(entryIds)) continue
    const ids = [...new Set(entryIds.flatMap((id) => (typeof id === 'string' && id.trim() ? [id.trim()] : [])))]
    if (ids.length > 0) normalized[trimmedKey] = ids
  }
  return Object.keys(normalized).length > 0 ? normalized : undefined
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

function normalizeActiveRemoteProject(settings: AppSettings): ActiveRemoteProjectSettings | undefined {
  const raw = settings.activeRemoteProject
  if (raw == null || typeof raw !== 'object' || Array.isArray(raw)) {
    return undefined
  }
  const machineId = typeof raw.machineId === 'string' ? raw.machineId.trim() : ''
  const projectId = typeof raw.projectId === 'string' ? raw.projectId.trim() : ''
  return machineId && projectId ? { machineId, projectId } : undefined
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

function toLocalProjectDetails(primaryPath: string, name: unknown, secondaryFolders: unknown): LocalProjectDetails | null {
  const trimmedName = typeof name === 'string' ? name.trim() : ''
  const folders = sanitizeSecondaryFolders(secondaryFolders, primaryPath)
  if (!trimmedName && folders.length === 0) return null
  return {
    ...(trimmedName ? { name: trimmedName } : {}),
    ...(folders.length > 0 ? { secondaryFolders: folders } : {})
  }
}

function normalizeLocalProjectDetails(settings: AppSettings): Record<string, LocalProjectDetails> | undefined {
  const raw = settings.localProjectDetails
  if (!raw || typeof raw !== 'object') return undefined
  const result: Record<string, LocalProjectDetails> = {}
  for (const [path, value] of Object.entries(raw)) {
    const key = normalizeWorkspaceProjectKey(path)
    const details = key ? toLocalProjectDetails(key, value?.name, value?.secondaryFolders) : null
    if (details) result[key] = details
  }
  return Object.keys(result).length > 0 ? result : undefined
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
      raw.computerUse = normalizeComputerUseSettings(raw)
      raw.notifications = normalizeNotificationSettings(raw)
      raw.activeModuleVariants = normalizeActiveModuleVariants(raw)
      raw.showThinkingContent = normalizeShowThinkingContent(raw)
      raw.projectsSectionCollapsed = normalizeProjectsSectionCollapsed(raw)
      raw.pinnedSectionCollapsed = normalizePinnedSectionCollapsed(raw)
      raw.chatsSectionCollapsed = normalizeChatsSectionCollapsed(raw)
      normalizeSidebarThreadOrderSettings(raw)
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
      raw.localProjectDetails = normalizeLocalProjectDetails(raw)
      raw.remoteHosts = normalizeRemoteHostsSetting(raw)
      raw.satelliteRouteByThread = normalizeSatelliteRouteByThread(raw)
      raw.turnBookmarksByThread = normalizeTurnBookmarksByThread(raw)
      raw.createdSatelliteInviteIds = normalizeCreatedSatelliteInviteIds(raw)
      raw.activeRemoteStack = normalizeActiveRemoteStack(raw)
      raw.activeRemoteProject = normalizeActiveRemoteProject(raw)
      raw.pet = normalizePetSetting(raw.pet)
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
    settings.computerUse = normalizeComputerUseSettings(settings)
    settings.notifications = normalizeNotificationSettings(settings)
    settings.activeModuleVariants = normalizeActiveModuleVariants(settings)
    settings.showThinkingContent = normalizeShowThinkingContent(settings)
    settings.projectsSectionCollapsed = normalizeProjectsSectionCollapsed(settings)
    settings.pinnedSectionCollapsed = normalizePinnedSectionCollapsed(settings)
    settings.chatsSectionCollapsed = normalizeChatsSectionCollapsed(settings)
    normalizeSidebarThreadOrderSettings(settings)
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
    settings.localProjectDetails = normalizeLocalProjectDetails(settings)
    settings.remoteHosts = normalizeRemoteHostsSetting(settings)
    settings.satelliteRouteByThread = normalizeSatelliteRouteByThread(settings)
    settings.turnBookmarksByThread = normalizeTurnBookmarksByThread(settings)
    settings.createdSatelliteInviteIds = normalizeCreatedSatelliteInviteIds(settings)
    settings.pet = normalizePetSetting(settings.pet)
    settings.activeRemoteStack = normalizeActiveRemoteStack(settings)
    settings.activeRemoteProject = normalizeActiveRemoteProject(settings)
    writeFileSync(filePath, JSON.stringify(settings, null, 2), 'utf8')
  } catch {
    // Non-fatal
  }
}

export function getLocalProjectDetails(settings: AppSettings, path: string): LocalProjectDetails | undefined {
  return settings.localProjectDetails?.[normalizeWorkspaceProjectKey(path)]
}

/**
 * A `previousPath` whose identity differs from `primaryFolder` moves the Project's
 * details and pinned, ordered, and collapsed state to the new key, while
 * `pinnedThreadIdsByWorkspace` is left alone because existing threads keep their
 * original workspace. Returns the normalized primary folder.
 */
export function saveLocalProjectDetails(
  settings: AppSettings,
  params: {
    previousPath?: string
    primaryFolder: string
    secondaryFolders: string[]
    name?: string
  }
): string {
  const primaryFolder = normalize(params.primaryFolder.trim())
  const primaryKey = normalizeWorkspaceProjectKey(primaryFolder)
  const previousKey = normalizeWorkspaceProjectKey(params.previousPath)
  const allDetails = { ...settings.localProjectDetails }

  if (previousKey && previousKey !== primaryKey) {
    delete allDetails[previousKey]
    const rekey = (ids: string[] | undefined): string[] | undefined =>
      ids?.map((id) => (id === previousKey ? primaryKey : id))
    settings.pinnedProjectIds = rekey(settings.pinnedProjectIds)
    settings.projectOrder = rekey(settings.projectOrder)
    settings.collapsedProjectIds = rekey(settings.collapsedProjectIds)
  }

  const secondaryFolders = (params.secondaryFolders ?? [])
    .map((folder) => (typeof folder === 'string' ? folder.trim() : ''))
    .filter((folder) => folder.length > 0)
    .map((folder) => normalize(folder))
  const details = toLocalProjectDetails(primaryFolder, params.name, secondaryFolders)
  if (details) allDetails[primaryKey] = details
  else delete allDetails[primaryKey]
  settings.localProjectDetails = Object.keys(allDetails).length > 0 ? allDetails : undefined
  return primaryFolder
}

export function forgetLocalProject(settings: AppSettings, path: string): void {
  const allDetails = { ...settings.localProjectDetails }
  delete allDetails[normalizeWorkspaceProjectKey(path)]
  settings.localProjectDetails = Object.keys(allDetails).length > 0 ? allDetails : undefined
  settings.pinnedProjectIds = settings.pinnedProjectIds?.filter((projectId) => !sameWorkspaceProjectKey(projectId, path))
}
