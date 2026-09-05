import { app, ipcMain, BrowserWindow, dialog, Notification, shell, session, type OpenDialogOptions } from 'electron'
import { promises as fs, existsSync } from 'fs'
import { execFile } from 'child_process'
import * as os from 'os'
import * as path from 'path'
import type { DesktopAppServerClient } from './DesktopAppServerClient'
import type {
  AppSettings,
  RecentWorkspace,
  BinarySource
} from './settings'
import { resolveTaskCompletionNotificationMode } from './settings'
import type { GitHeadInspection } from '../shared/gitHead'
import { sameWorkspaceProjectKey } from '../shared/workspaceProjectKey'
import type { InlineVisualizationCaptureRect } from '../shared/inlineVisualization'
import {
  readProviderPreferences,
  type ProviderPreferences
} from '../shared/modelPreference'
import { copyInlineVisualizationImage } from './inlineVisualizationCapture'
import { resolveBinaryLocation } from './AppServerManager'
import { openDesktopServiceHandoff } from './desktopServiceHandoff'
import { RemoteServersManager } from './remoteServers/remoteServersManager'
import {
  registerRemoteServersHandlers,
  REMOTE_SERVERS_CHANNELS
} from './remoteServers/remoteServersIpc'
import {
  registerSatellitesHandlers,
  SATELLITES_CHANNELS
} from './satellites/satellitesIpc'
import { getSatellitesHubBridge } from './satellites/satellitesHubBridge'
import type { DesktopHubClient } from './desktopHub'
import { toAppServerErrorEnvelope } from '../shared/appServerError'
import { checkWorkspaceLock } from './workspaceLock'
import { applyNativeChromeTheme, resolveThemeSurface } from './windowTheme'
import {
  activateFileIndexWorkspace,
  cleanupWorkspaceCache,
  listWorkspaceFiles,
  readImageAsDataUrl,
  saveImageDataUrlToTemp,
  warmFileSearchIndex
} from './workspaceComposerIpc'
import {
  classifyFile,
  readTextFile,
  listViewerFiles,
  listDirectory
} from './viewerIpc'
import { authorizeViewerFile, buildViewerUrl } from './viewerFileProtocol'
import {
  clearDesktopPluginModuleRoutes,
  registerDesktopPluginModuleRoute,
  removeDesktopPluginModuleRoute,
  type DesktopPluginModuleRequest
} from './pluginFileProtocol'
import { partitionForWorkspace, viewerBrowserManager } from './viewerBrowser'
import { viewerTerminalManager } from './viewerTerminal'
import { browserUseManager } from './browserUseManager'
import type { BrowserUseApprovalResponsePayload } from '../shared/viewer/types'
import {
  checkChromeSetup,
  installChromeNativeHost,
  openChromeWindow,
  type ChromeOpenRequest
} from './chromeSetup'
import {
  scanModules,
  groupModulesByChannel,
  type DiscoveredModule
} from './moduleScanner'
import {
  ChannelModuleManager,
  type ModuleStatusMap
} from './channelModuleManager'
import type { QrUpdatePayload } from './qrWatcher'
import type {
  WorkspaceSetupRequest,
  WorkspaceStatusPayload,
  WorkspaceSetupModelListRequest,
  WorkspaceSetupModelListResult
} from '../shared/workspaceSetup'
import type { WorkspaceSetupResult } from './workspaceSetup'
import { normalizeRemoteHosts, type RemoteHost, type RemoteStack } from '../shared/remoteServers'
import { translate, normalizeLocale, DEFAULT_LOCALE, type AppLocale } from '../shared/locales'
import { parseJsonObjectConfig } from '../shared/jsonConfig'
import { detectEditors, launchEditor, type EditorId } from './externalEditors'
import {
  bindDotCraftSkillInstall,
  cleanupDotCraftSkillInstall,
  getSkillMarketDetail,
  installSkillFromMarket,
  prepareDotCraftSkillInstall,
  searchSkillMarket
} from './skillMarket'
import type {
  SkillMarketDetailRequest,
  SkillMarketInstallRequest,
  SkillMarketBindDotCraftInstallRequest,
  SkillMarketCleanupDotCraftInstallRequest,
  SkillMarketPrepareDotCraftInstallRequest,
  SkillMarketSearchRequest
} from '../shared/skillMarket'
import {
  resolveRemoteWebSocketConfig,
  type ConnectionSettingsDraft
} from '../shared/remoteConnection'
import { sendDesktopAppServerRequest } from './desktopRuntimeThreadTools'
import { resolveBundledBuiltInPluginRoot } from './ripgrepRuntime'
import type { WorkspaceProjectsPayload } from '../shared/workspaceProjects'
import type { AppServerRequestMethod } from '../shared/appServerBoundary'
import type { AppListResult } from '@dotcraft/sdk/contracts'
import type {
  ConnectionStatusPayload,
  RetryConnectionRequest
} from '../shared/connectionStatus'
import type { UserInputAutoResolutionState } from '../shared/userInputAutoResolution'

interface WindowVisibilityState {
  minimized: boolean
  visible: boolean
  focused: boolean
}

export interface ResolvedBinaryRequest {
  binarySource?: BinarySource
  binaryPath?: string
}

interface ModulesRescanSummaryPayload {
  addedModuleIds: string[]
  removedModuleIds: string[]
  changedModuleIds: string[]
  changedRunningModuleIds: string[]
}

interface GitCommandResult {
  stdout: string
  stderr: string
  exitCode: number
}

interface GitBranchEntry {
  name: string
  current: boolean
}

interface GitBranchListResult {
  current: string | null
  detachedHead: string | null
  branches: GitBranchEntry[]
}

interface ExecFileError extends Error {
  code?: number | string
}

export interface ServerRequestPayload {
  bridgeId: string
  method: string
  params: unknown
}

function runGitCommand(
  cwd: string,
  args: string[],
  allowedExitCodes: number[] = [0]
): Promise<GitCommandResult> {
  return new Promise((resolve, reject) => {
    execFile('git', args, { cwd }, (err, stdout, stderr) => {
      const execError = err as ExecFileError | null
      const exitCode = err ? (typeof execError?.code === 'number' ? execError.code : null) : 0
      if (exitCode !== null && allowedExitCodes.includes(exitCode)) {
        resolve({
          stdout: String(stdout),
          stderr: String(stderr),
          exitCode
        })
        return
      }
      if (err) {
        reject(new Error(String(stderr || err.message).trim()))
        return
      }
      resolve({
        stdout: String(stdout),
        stderr: String(stderr),
        exitCode: exitCode ?? 0
      })
    })
  })
}

function isSameOrInsidePath(candidatePath: string, parentPath: string): boolean {
  const relativePath = path.relative(parentPath, candidatePath)
  return relativePath === '' || (!relativePath.startsWith('..') && !path.isAbsolute(relativePath))
}

/** Rejects the characters and trailing dots/spaces that are invalid in Windows path segments. */
function sanitizeProjectFolderName(name: string): string {
  const cleaned = Array.from(name)
    .map((ch) => (ch.charCodeAt(0) < 0x20 || '<>:"/\\|?*'.includes(ch) ? ' ' : ch))
    .join('')
    .replace(/\s+/g, ' ')
    .trim()
    .replace(/[. ]+$/g, '')
    .trim()
  return cleaned || 'New project'
}

function toGitRelativeWorkspacePath(
  filePath: string,
  workspacePath: string,
  locale: AppLocale
): string | null {
  if (typeof filePath !== 'string' || filePath.trim() === '') return null
  const wsResolved = path.resolve(workspacePath)
  const resolved = path.isAbsolute(filePath)
    ? path.resolve(filePath)
    : path.resolve(wsResolved, filePath)

  if (!isSameOrInsidePath(resolved, wsResolved)) {
    throw new Error(
      translate(locale, 'ipc.pathOutsideWorkspace', { path: filePath })
    )
  }

  const relativePath = path.relative(wsResolved, resolved)
  if (!relativePath || relativePath.startsWith('..') || path.isAbsolute(relativePath)) return null
  return relativePath.split(path.sep).join('/')
}

function parseGitStatusPorcelainZ(stdout: string): string[] {
  const paths: string[] = []
  const entries = stdout.split('\0').filter(Boolean)
  for (let i = 0; i < entries.length; i += 1) {
    const entry = entries[i]
    if (entry.length < 4) continue
    const status = entry.slice(0, 2)
    if (status === '!!') continue
    const filePath = entry.slice(3)
    if (!filePath) continue
    paths.push(filePath.replace(/\\/g, '/'))
    if (status[0] === 'R' || status[0] === 'C') {
      i += 1
    }
  }
  return paths
}

async function resolveCommitFilePaths(
  workspacePath: string,
  files: string[],
  locale: AppLocale
): Promise<string[]> {
  const seen = new Set<string>()
  const requestedPaths: string[] = []
  for (const file of files) {
    const relativePath = toGitRelativeWorkspacePath(file, workspacePath, locale)
    if (!relativePath || seen.has(relativePath)) continue
    seen.add(relativePath)
    requestedPaths.push(relativePath)
  }
  if (requestedPaths.length === 0) return []

  const status = await runGitCommand(
    workspacePath,
    ['status', '--porcelain=v1', '-z', '--untracked-files=all', '--', ...requestedPaths]
  )
  const statusPaths = parseGitStatusPorcelainZ(status.stdout)
  return requestedPaths.filter((filePath) => statusPaths.includes(filePath))
}

function assertPathWithinWorkspace(
  absPath: string,
  workspacePath: string,
  locale: AppLocale
): string {
  const resolved = path.resolve(absPath)
  const wsResolved = path.resolve(workspacePath)
  if (!resolved.startsWith(wsResolved + path.sep) && resolved !== wsResolved) {
    throw new Error(
      translate(locale, 'ipc.pathOutsideWorkspace', { path: absPath })
    )
  }
  return resolved
}

function assertGitWorkspacePath(
  requestedPath: string,
  workspacePath: string,
  locale: AppLocale
): string {
  if (!workspacePath) {
    throw new Error(translate(locale, 'ipc.noWorkspaceOpen'))
  }
  if (typeof requestedPath !== 'string' || requestedPath.trim() === '') {
    throw new Error(translate(locale, 'ipc.workspacePathMismatch'))
  }

  const resolved = path.resolve(requestedPath)
  const workspaceResolved = path.resolve(workspacePath)
  if (resolved === workspaceResolved) return resolved

  const worktreesRoot = path.resolve(workspaceResolved, '.craft', 'worktrees')
  if (isSameOrInsidePath(resolved, worktreesRoot) && resolved !== worktreesRoot) {
    return resolved
  }

  throw new Error(translate(locale, 'ipc.workspacePathMismatch'))
}

function assertGitInspectionPath(
  requestedPath: string,
  workspacePath: string,
  recentWorkspaces: RecentWorkspace[],
  locale: AppLocale
): string {
  try {
    return assertGitWorkspacePath(requestedPath, workspacePath, locale)
  } catch {
    // Read-only inspection may target a known recent local project without
    // granting that path to commit, checkout, or branch-management handlers.
  }

  if (typeof requestedPath !== 'string' || requestedPath.trim() === '') {
    throw new Error(translate(locale, 'ipc.workspacePathMismatch'))
  }
  const resolved = path.resolve(requestedPath)
  for (const recent of recentWorkspaces) {
    if (sameWorkspaceProjectKey(recent.path, requestedPath)) return path.resolve(recent.path)
    const worktreesRoot = path.resolve(recent.path, '.craft', 'worktrees')
    if (isSameOrInsidePath(resolved, worktreesRoot) && resolved !== worktreesRoot) return resolved
  }
  throw new Error(translate(locale, 'ipc.workspacePathMismatch'))
}

async function assertExistingLocalPath(targetPath: string): Promise<string> {
  if (typeof targetPath !== 'string' || targetPath.trim() === '') {
    throw new Error('Invalid local path')
  }
  const trimmed = targetPath.trim()
  const isWindowsAbsolutePath = /^[A-Za-z]:[\\/]/.test(trimmed)
  if (!isWindowsAbsolutePath && /^[A-Za-z][A-Za-z0-9+.-]*:/.test(trimmed)) {
    throw new Error('Local path must be an absolute filesystem path')
  }
  if (!path.isAbsolute(trimmed)) {
    throw new Error('Local path must be absolute')
  }

  const resolved = path.resolve(trimmed)
  await fs.stat(resolved)
  return resolved
}

const MAX_EXTERNAL_URL_LENGTH = 4096

export function sanitizeHttpOrHttpsUrl(url: string | undefined): string | null {
  if (url === undefined || typeof url !== 'string') return null
  const trimmed = url.trim()
  if (trimmed === '') return null
  let parsed: URL
  try {
    parsed = new URL(trimmed)
  } catch {
    return null
  }
  if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') return null
  return parsed.href
}

/**
 * Allows browser-style URLs and OS-registered custom protocols, but blocks local or
 * scriptable schemes that would turn an untrusted URL into code execution.
 */
export function sanitizeExternalUrl(url: string | undefined): string | null {
  if (url === undefined || typeof url !== 'string') return null
  const trimmed = url.trim()
  if (trimmed === '' || trimmed.length > MAX_EXTERNAL_URL_LENGTH) return null
  let parsed: URL
  try {
    parsed = new URL(trimmed)
  } catch {
    return null
  }
  const protocol = parsed.protocol.toLowerCase()
  if (
    protocol === 'file:' ||
    protocol === 'javascript:' ||
    protocol === 'data:' ||
    protocol === 'vbscript:' ||
    !/^[a-z][a-z0-9+.-]*:$/.test(protocol)
  ) {
    return null
  }
  return parsed.href
}

export async function openExternalUrl(url: string): Promise<void> {
  if (typeof url !== 'string' || url.trim() === '') {
    throw new Error('Invalid URL')
  }
  if (url.trim().length > MAX_EXTERNAL_URL_LENGTH) {
    throw new Error('URL too long')
  }
  const safe = sanitizeExternalUrl(url)
  if (safe === null) {
    try {
      new URL(url.trim())
    } catch {
      throw new Error('Invalid URL')
    }
    throw new Error('URL scheme is not allowed')
  }
  await shell.openExternal(safe)
}

export async function openExternalHttpUrl(url: string): Promise<void> {
  if (typeof url !== 'string' || url.trim() === '') {
    throw new Error('Invalid URL')
  }
  const safe = sanitizeHttpOrHttpsUrl(url)
  if (safe === null) {
    try {
      new URL(url.trim())
    } catch {
      throw new Error('Invalid URL')
    }
    throw new Error('Only http(s) URLs are allowed')
  }
  await shell.openExternal(safe)
}

function isLoopbackHostname(hostname: string): boolean {
  const normalized = hostname.toLowerCase()
  return normalized === 'localhost'
    || normalized === '::1'
    || normalized === '[::1]'
    || /^127(?:\.\d{1,3}){3}$/.test(normalized)
}

async function readDesktopPluginResponse(response: Response): Promise<unknown> {
  if (!response.ok) {
    throw new Error(`Request failed with HTTP ${response.status}`)
  }
  const text = await response.text()
  if (text.length > 1_000_000) {
    throw new Error('Response is too large')
  }
  return text.trim() === '' ? null : JSON.parse(text)
}

interface ResolvedDesktopAppSurface {
  appId: string
  surfaceId: string
  endpoint: string
  bearer: string
  expiresAt: string
}

function appSurfaceUnavailable(): Error {
  return new Error('AppSurfaceUnavailable')
}

function validateDesktopPluginRelativePath(relativePath: string): URL {
  if (typeof relativePath !== 'string' || relativePath.length === 0 || relativePath.length > MAX_EXTERNAL_URL_LENGTH) {
    throw new Error('Invalid App Surface relative path')
  }
  if (!relativePath.startsWith('/') || relativePath.startsWith('//') || relativePath.includes('\\') || relativePath.includes('#')) {
    throw new Error('App Surface path must be origin-relative')
  }
  const pathOnly = relativePath.split('?', 1)[0]
  for (const rawSegment of pathOnly.split('/')) {
    let segment = rawSegment
    for (let depth = 0; depth < 4; depth++) {
      let decoded: string
      try {
        decoded = decodeURIComponent(segment)
      } catch {
        throw new Error('Invalid App Surface relative path')
      }
      if (decoded.split(/[\\/]/).some((part) => part === '.' || part === '..')) {
        throw new Error('App Surface path traversal is not allowed')
      }
      if (decoded === segment) break
      segment = decoded
    }
  }
  const parsed = new URL(relativePath, 'http://dotcraft-app-surface.invalid')
  if (parsed.origin !== 'http://dotcraft-app-surface.invalid' || parsed.username || parsed.password || parsed.hash) {
    throw new Error('App Surface path must be origin-relative')
  }
  return parsed
}

function resolveDesktopPluginAppSurfaceUrl(endpoint: string, relativePath: string): string {
  let base: URL
  try {
    base = new URL(endpoint)
  } catch {
    throw appSurfaceUnavailable()
  }
  if (
    (base.protocol !== 'http:' && base.protocol !== 'https:')
    || !isLoopbackHostname(base.hostname)
    || base.username !== ''
    || base.password !== ''
    || base.hash !== ''
  ) {
    throw appSurfaceUnavailable()
  }
  const relative = validateDesktopPluginRelativePath(relativePath)
  const basePath = base.pathname.endsWith('/') ? base.pathname : `${base.pathname}/`
  const target = new URL(base.href)
  target.pathname = `${basePath}${relative.pathname.slice(1)}`
  target.search = relative.search
  target.hash = ''
  if (target.origin !== base.origin || !target.pathname.startsWith(basePath)) {
    throw new Error('App Surface path must stay within the resolved endpoint base path')
  }
  return target.href
}

async function resolveDesktopPluginAppSurface(
  client: DesktopAppServerClient,
  appId: string,
  surfaceId: string
): Promise<ResolvedDesktopAppSurface> {
  const result: unknown = await client.sendRequest('app/surface/resolve', { appId, surfaceId }, 20_000)
  if (result == null || typeof result !== 'object' || Array.isArray(result)) throw appSurfaceUnavailable()
  const surface = result as Partial<ResolvedDesktopAppSurface>
  if (
    surface.appId !== appId
    || surface.surfaceId !== surfaceId
    || typeof surface.endpoint !== 'string'
    || typeof surface.bearer !== 'string'
    || surface.bearer.trim() === ''
    || typeof surface.expiresAt !== 'string'
    || !Number.isFinite(Date.parse(surface.expiresAt))
  ) {
    throw appSurfaceUnavailable()
  }
  return surface as ResolvedDesktopAppSurface
}

async function requestDesktopPluginAppSurfaceJson(
  client: DesktopAppServerClient,
  method: 'GET' | 'POST',
  appId: string,
  surfaceId: string,
  relativePath: string,
  body?: unknown,
  timeoutMs?: number
): Promise<unknown> {
  const surface = await resolveDesktopPluginAppSurface(client, appId, surfaceId)
  if (Date.parse(surface.expiresAt) <= Date.now()) throw appSurfaceUnavailable()
  const url = resolveDesktopPluginAppSurfaceUrl(surface.endpoint, relativePath)
  const controller = new AbortController()
  const timeout = setTimeout(() => controller.abort(), Math.min(Math.max(timeoutMs ?? 10_000, 1_000), 30_000))
  try {
    const response = await fetch(url, {
      method,
      headers: {
        Accept: 'application/json',
        Authorization: `Bearer ${surface.bearer}`,
        ...(method === 'POST' ? { 'Content-Type': 'application/json' } : {})
      },
      ...(method === 'POST' ? { body: JSON.stringify(body ?? {}) } : {}),
      redirect: 'error',
      signal: controller.signal
    })
    return await readDesktopPluginResponse(response)
  } finally {
    clearTimeout(timeout)
  }
}

async function invokeLoopbackHandoff(url: string): Promise<void> {
  const controller = new AbortController()
  const timeout = setTimeout(() => controller.abort(), 15000)
  try {
    const response = await fetch(url, {
      method: 'GET',
      redirect: 'follow',
      signal: controller.signal
    })
    if (!response.ok) {
      throw new Error(`App handoff failed with HTTP ${response.status}`)
    }
  } finally {
    clearTimeout(timeout)
  }
}

/**
 * Loopback HTTP handoffs are invoked in-process so local app servers can complete
 * connect/bind without opening a browser tab.
 */
export async function openAppHandoffUrl(url: string): Promise<void> {
  if (typeof url !== 'string' || url.trim() === '') {
    throw new Error('Invalid URL')
  }
  let parsedUrl: URL | null = null
  try { parsedUrl = new URL(url) } catch { /* validated by the existing fallbacks below */ }
  if (parsedUrl?.protocol === 'dotcraft-service:') {
    await openDesktopServiceHandoff(url)
    return
  }
  const safeHttp = sanitizeHttpOrHttpsUrl(url)
  if (safeHttp != null) {
    const parsed = new URL(safeHttp)
    if (isLoopbackHostname(parsed.hostname)) {
      await invokeLoopbackHandoff(safeHttp)
      return
    }
  }

  await openExternalUrl(url)
}

export function getProtocolHandlerName(protocol: string): string {
  if (typeof protocol !== 'string' || protocol.trim() === '') return ''
  const normalized = protocol.trim().replace(/:$/, '')
  if (!/^[a-z][a-z0-9+.-]*$/i.test(normalized)) return ''
  try {
    return app.getApplicationNameForProtocol(`${normalized}://`) || ''
  } catch {
    return ''
  }
}

function isSafeConfigFileName(configFileName: string): boolean {
  if (configFileName.trim() === '') return false
  if (configFileName.includes('..')) return false
  return !configFileName.includes('/') && !configFileName.includes('\\')
}

function ensureObjectConfig(config: unknown): Record<string, unknown> {
  if (config == null || typeof config !== 'object' || Array.isArray(config)) {
    throw new Error('Config payload must be a JSON object')
  }
  return config as Record<string, unknown>
}

function normalizeOptionalStringValue(value: unknown): string | null {
  if (typeof value !== 'string') return null
  const trimmed = value.trim()
  return trimmed === '' ? null : trimmed
}

interface WorkspaceCoreConfigSnapshot {
  providerId: string | null
  providerPreferences: ProviderPreferences
  welcomeSuggestionsEnabled: boolean | null
  skillsSelfLearningEnabled: boolean | null
  memoryAutoConsolidateEnabled: boolean | null
  dreamsEnabled: boolean | null
  dreamsInterval: string | null
  dreamsThreadLookbackCount: number | null
  dreamsAutoApply: boolean | null
  defaultApprovalPolicy: 'default' | 'autoApprove' | null
}

function getCaseInsensitiveRecordValue(
  record: Record<string, unknown>,
  key: string
): unknown {
  const expected = key.toLowerCase()
  for (const [candidate, value] of Object.entries(record)) {
    if (candidate.toLowerCase() === expected) {
      return value
    }
  }
  return undefined
}

function readNestedBoolean(
  record: Record<string, unknown>,
  sectionKey: string,
  fieldKey: string
): boolean | null {
  const section = getCaseInsensitiveRecordValue(record, sectionKey)
  if (section == null || typeof section !== 'object' || Array.isArray(section)) {
    return null
  }
  const raw = getCaseInsensitiveRecordValue(section as Record<string, unknown>, fieldKey)
  return typeof raw === 'boolean' ? raw : null
}

function readNestedString(
  record: Record<string, unknown>,
  sectionKey: string,
  fieldKey: string
): string | null {
  const section = getCaseInsensitiveRecordValue(record, sectionKey)
  if (section == null || typeof section !== 'object' || Array.isArray(section)) {
    return null
  }
  return normalizeOptionalStringValue(getCaseInsensitiveRecordValue(section as Record<string, unknown>, fieldKey))
}

function readNestedInteger(
  record: Record<string, unknown>,
  sectionKey: string,
  fieldKey: string
): number | null {
  const section = getCaseInsensitiveRecordValue(record, sectionKey)
  if (section == null || typeof section !== 'object' || Array.isArray(section)) {
    return null
  }
  const raw = getCaseInsensitiveRecordValue(section as Record<string, unknown>, fieldKey)
  return typeof raw === 'number' && Number.isInteger(raw) ? raw : null
}

function readSkillsSelfLearningEnabled(record: Record<string, unknown>): boolean | null {
  const skills = getCaseInsensitiveRecordValue(record, 'Skills')
  if (skills == null || typeof skills !== 'object' || Array.isArray(skills)) {
    return null
  }
  return readNestedBoolean(skills as Record<string, unknown>, 'SelfLearning', 'Enabled')
}

function readDefaultApprovalPolicy(record: Record<string, unknown>): 'default' | 'autoApprove' | null {
  const permissions = getCaseInsensitiveRecordValue(record, 'Permissions')
  if (permissions == null || typeof permissions !== 'object' || Array.isArray(permissions)) {
    return null
  }
  const raw = getCaseInsensitiveRecordValue(permissions as Record<string, unknown>, 'DefaultApprovalPolicy')
  return raw === 'default' || raw === 'autoApprove' ? raw : null
}

function createEmptyCoreConfigSnapshot(): WorkspaceCoreConfigSnapshot {
  return {
    providerId: null,
    providerPreferences: {},
    welcomeSuggestionsEnabled: null,
    skillsSelfLearningEnabled: null,
    memoryAutoConsolidateEnabled: null,
    dreamsEnabled: null,
    dreamsInterval: null,
    dreamsThreadLookbackCount: null,
    dreamsAutoApply: null,
    defaultApprovalPolicy: null
  }
}

function readCoreConfigSnapshotFromText(raw: string): WorkspaceCoreConfigSnapshot {
  if (!raw.trim()) return createEmptyCoreConfigSnapshot()
  const parsed = parseJsonObjectConfig(raw)
  return {
    providerId: normalizeOptionalStringValue(parsed.ProviderId ?? parsed.providerId),
    providerPreferences: readProviderPreferences(
      getCaseInsensitiveRecordValue(parsed, 'ProviderPreferences')
    ),
    welcomeSuggestionsEnabled: readNestedBoolean(parsed, 'WelcomeSuggestions', 'Enabled'),
    skillsSelfLearningEnabled: readSkillsSelfLearningEnabled(parsed),
    memoryAutoConsolidateEnabled: readNestedBoolean(parsed, 'Memory', 'AutoConsolidateEnabled'),
    dreamsEnabled: readNestedBoolean(parsed, 'Dreams', 'Enabled'),
    dreamsInterval: readNestedString(parsed, 'Dreams', 'Interval'),
    dreamsThreadLookbackCount: readNestedInteger(parsed, 'Dreams', 'ThreadLookbackCount'),
    dreamsAutoApply: readNestedBoolean(parsed, 'Dreams', 'AutoApply'),
    defaultApprovalPolicy: readDefaultApprovalPolicy(parsed)
  }
}

async function readCoreConfigSnapshot(configPath: string): Promise<WorkspaceCoreConfigSnapshot> {
  try {
    const raw = await fs.readFile(configPath, 'utf8')
    return readCoreConfigSnapshotFromText(raw)
  } catch (error) {
    const code = (error as NodeJS.ErrnoException | undefined)?.code
    if (code === 'ENOENT') {
      return createEmptyCoreConfigSnapshot()
    }
    throw error
  }
}

async function readActiveRemoteCoreConfigSnapshot(
  callbacks?: IpcHandlerCallbacks
): Promise<{ workspace: WorkspaceCoreConfigSnapshot; userDefaults: WorkspaceCoreConfigSnapshot } | null> {
  const settings = callbacks?.getSettings()
  if (!settings || settings.connectionMode !== 'remote') return null
  const ref = settings.activeRemoteStack
  if (!ref?.hostId || !ref.stackId) return null

  const host = normalizeRemoteHosts(settings.remoteHosts).find((candidate) => candidate.id === ref.hostId)
  const stack = host?.stacks.find((candidate) => candidate.id === ref.stackId)
  if (!host || !stack) return null

  const raw = await getRemoteServersManager().readCoreConfig(host, stack)
  return {
    workspace: readCoreConfigSnapshotFromText(raw.workspaceRaw),
    userDefaults: readCoreConfigSnapshotFromText(raw.userDefaultsRaw)
  }
}

function resolveConnectionMode(settings: AppSettings): 'stdio' | 'websocket' | 'stdioAndWebSocket' | 'remote' {
  const mode = settings.connectionMode
  return mode === 'remote' ? 'remote' : 'stdioAndWebSocket'
}

function resolveModuleWsConfig(
  settings: AppSettings,
  runtime?: { wsUrl: string; token?: string } | null
): { wsUrl: string; token?: string } {
  const mode = resolveConnectionMode(settings)
  if (mode === 'remote') {
    if (settings.activeRemoteStack) {
      if (!runtime?.wsUrl?.trim()) {
        throw new Error('Remote stack AppServer tunnel is not connected.')
      }
      return runtime.token?.trim()
        ? { wsUrl: runtime.wsUrl.trim(), token: runtime.token.trim() }
        : { wsUrl: runtime.wsUrl.trim() }
    }

    const resolved = resolveRemoteWebSocketConfig(settings.remote)
    if (!resolved.ok) {
      throw new Error(resolved.message)
    }
    return resolved.token
      ? { wsUrl: resolved.wsUrl, token: resolved.token }
      : { wsUrl: resolved.wsUrl }
  }

  if (runtime?.wsUrl?.trim()) {
    return runtime.token?.trim()
      ? { wsUrl: runtime.wsUrl.trim(), token: runtime.token.trim() }
      : { wsUrl: runtime.wsUrl.trim() }
  }

  const host = settings.webSocket?.host?.trim() || '127.0.0.1'
  const candidatePort = settings.webSocket?.port
  const port =
    typeof candidatePort === 'number' &&
    Number.isInteger(candidatePort) &&
    candidatePort > 0 &&
    candidatePort <= 65535
      ? candidatePort
      : 9100
  return { wsUrl: `ws://${host}:${port}/ws` }
}

function injectModuleDotcraftConfig(
  config: Record<string, unknown>,
  wsConfig: { wsUrl: string; token?: string }
): Record<string, unknown> {
  const next: Record<string, unknown> = { ...config }
  const dotcraftRaw = next.dotcraft
  const dotcraft =
    dotcraftRaw != null && typeof dotcraftRaw === 'object' && !Array.isArray(dotcraftRaw)
      ? { ...(dotcraftRaw as Record<string, unknown>) }
      : {}
  dotcraft.wsUrl = wsConfig.wsUrl
  if (wsConfig.token !== undefined) {
    dotcraft.token = wsConfig.token
  }
  next.dotcraft = dotcraft
  return next
}

let nextBridgeId = 1
interface PendingServerRequest {
  resolve: (result: unknown) => void
  reject: (error: Error) => void
  onSettled?: () => void
}

const pendingServerRequests = new Map<string, PendingServerRequest>()

export function createServerRequestBridge(
  onSettled?: () => void
): { bridgeId: string; promise: Promise<unknown> } {
  const bridgeId = String(nextBridgeId++)
  const promise = new Promise<unknown>((resolve, reject) => {
    pendingServerRequests.set(bridgeId, { resolve, reject, onSettled })
  })
  return { bridgeId, promise }
}

export function resolveServerRequestBridge(bridgeId: string, result: unknown): void {
  const pending = pendingServerRequests.get(bridgeId)
  if (!pending) return
  pendingServerRequests.delete(bridgeId)
  pending.onSettled?.()
  pending.resolve(result)
}

export function cancelServerRequestBridge(bridgeId: string): void {
  const pending = pendingServerRequests.get(bridgeId)
  if (!pending) return
  pendingServerRequests.delete(bridgeId)
  pending.onSettled?.()
  pending.reject(new Error('Interactive request was cancelled with its turn.'))
}

export interface IpcHandlerCallbacks {
  onSwitchWorkspace: (newPath: string) => Promise<void>
  onClearWorkspaceSelection: () => Promise<void>
  onRunWorkspaceSetup: (request: WorkspaceSetupRequest) => Promise<WorkspaceSetupResult>
  onListSetupModels: (
    request: WorkspaceSetupModelListRequest
  ) => Promise<WorkspaceSetupModelListResult>
  onLoginSetupChatGpt?: (providerId: string) => Promise<{ kind: 'success' | 'error' }>
  onOpenNewWindow: () => void
  onRestartManagedAppServer: () => Promise<void>
  onRetryAppServerConnection?: (request?: RetryConnectionRequest) => Promise<void>
  onApplyConnectionSettings?: (draft: ConnectionSettingsDraft) => Promise<void>
  onConnectRemoteStack?: (host: RemoteHost, stack: RemoteStack) => Promise<{ localPort?: number }>
  /** Disconnects a saved remote stack; if it is the active one, Desktop returns to local mode. */
  onDisconnectRemoteStack?: (hostId: string, stackId: string) => Promise<void>
  onDisconnectRemoteProject?: () => Promise<void>
  getSettings: () => AppSettings
  /** Absent leaves the satellite channels unregistered. */
  getHubClient?: () => DesktopHubClient
  /** Null unless the AppServer is a Hub-managed local one. */
  getAppServerWsConfig?: () => { wsUrl: string; token?: string } | null
  updateSettings: (partial: Partial<AppSettings>) => void | Promise<void>
  getRecentWorkspaces: () => RecentWorkspace[]
  getWorkspaceProjects?: () => WorkspaceProjectsPayload
  removeRecentWorkspace?: (workspacePath: string) => void
  /** Creates or updates a local multi-folder Project (primary + secondary folders). */
  saveLocalProject?: (params: {
    previousPath?: string
    primaryFolder: string
    secondaryFolders: string[]
    name?: string
  }) => void | Promise<void>
  restartWorkspace?: (workspacePath: string) => void | Promise<void>
  stopWorkspace?: (workspacePath: string) => void | Promise<void>
  /** Archives a thread in a (possibly non-foreground) workspace connection. */
  archiveThreadInWorkspace?: (workspacePath: string, threadId: string) => void | Promise<void>
  clearRecentWorkspaces?: () => void
  getConnectionStatus: () => ConnectionStatusPayload
  getWorkspaceStatus: () => WorkspaceStatusPayload
  /** Observes only successful renderer AppServer requests, feeding Desktop-local routing state. */
  onAppServerRequestCompleted?: (
    client: DesktopAppServerClient,
    method: string,
    params: unknown,
    result: unknown
  ) => void
  getUserInputAutoResolutionSnapshot?: () => UserInputAutoResolutionState[]
  onUserInputConversationPresented?: (threadId: string | null) => void
  onUserInputConversationActivity?: (threadId: string) => void
  onUserInputAutoResolutionSnoozed?: (threadId: string, requestId: string) => void
}

function mainLocale(callbacks?: IpcHandlerCallbacks): AppLocale {
  return normalizeLocale(callbacks?.getSettings()?.locale ?? DEFAULT_LOCALE)
}

export function withDesktopRequestIdentity(
  method: string,
  params: unknown,
  workspacePath: string | null | undefined
): unknown {
  if (
    method !== 'thread/start'
    || params == null
    || typeof params !== 'object'
    || Array.isArray(params)
    || (params as Record<string, unknown>).identity != null
  ) {
    return params
  }
  if (!workspacePath) throw new Error('Desktop thread/start requires an active workspace.')
  return {
    ...(params as Record<string, unknown>),
    identity: {
      channelName: 'dotcraft-desktop',
      userId: 'local',
      channelContext: `workspace:${workspacePath}`,
      workspacePath
    }
  }
}

let channelModuleManager: ChannelModuleManager | null = null
let ensureModulesScanned: (() => Promise<DiscoveredModule[]>) | null = null
let getSettingsSnapshotForModules: (() => AppSettings) | null = null

let remoteServersManager: RemoteServersManager | null = null
export function getRemoteServersManager(): RemoteServersManager {
  if (!remoteServersManager) remoteServersManager = new RemoteServersManager()
  return remoteServersManager
}
const terminalCleanupHookedWindows = new Set<number>()

function normalizeChannelName(channelName: string): string {
  return channelName.trim().toLowerCase()
}

function getNestedValue(config: Record<string, unknown>, dottedKey: string): unknown {
  const parts = dottedKey.split('.').filter(Boolean)
  if (parts.length === 0) return undefined
  let current: unknown = config
  for (const part of parts) {
    if (current == null || typeof current !== 'object' || Array.isArray(current)) return undefined
    current = (current as Record<string, unknown>)[part]
  }
  return current
}

function findMissingRequiredFields(
  config: Record<string, unknown>,
  module: DiscoveredModule
): string[] {
  const missing: string[] = []
  for (const descriptor of module.configDescriptors) {
    if (!descriptor.required) continue
    if (descriptor.key.startsWith('dotcraft.')) continue
    const value = getNestedValue(config, descriptor.key)
    const isMissing =
      value == null ||
      (typeof value === 'string' && value.trim() === '') ||
      (Array.isArray(value) && value.length === 0)
    if (isMissing) {
      missing.push(descriptor.displayLabel || descriptor.key)
    }
  }
  return missing
}

function isRunningProcessState(state: ModuleStatusMap[string]['processState'] | undefined): boolean {
  return state === 'starting' || state === 'running'
}

function areModulesEquivalent(previous: DiscoveredModule, next: DiscoveredModule): boolean {
  return JSON.stringify(previous) === JSON.stringify(next)
}

export function getChannelModuleManager(): ChannelModuleManager | null {
  return channelModuleManager
}

export async function restoreModuleTrackingByChannelName(
  enabledChannelNames: string[]
): Promise<void> {
  const discoveredModules = ensureModulesScanned ? await ensureModulesScanned() : []
  const grouped = groupModulesByChannel(
    discoveredModules,
    getSettingsSnapshotForModules?.().activeModuleVariants
  )

  const enabledNames = new Set(
    enabledChannelNames.map((name) => normalizeChannelName(name)).filter(Boolean)
  )
  const moduleIdsToStart = grouped
    .filter((group) => enabledNames.has(normalizeChannelName(group.channelName)))
    .map((group) => group.activeModuleId)
    .filter(Boolean)
  await channelModuleManager?.restoreModules(moduleIdsToStart)
}

export function registerIpcHandlers(
  _wireClient: DesktopAppServerClient | null,
  getWireClient: () => DesktopAppServerClient | null,
  workspacePath: string,
  callbacks?: IpcHandlerCallbacks
): void {
  activateFileIndexWorkspace(workspacePath)
  if (workspacePath) {
    void cleanupWorkspaceCache(workspacePath).catch(() => {})
  }
  const handleSafe = (
    channel: string,
    listener: Parameters<typeof ipcMain.handle>[1]
  ): void => {
    ipcMain.removeHandler(channel)
    ipcMain.handle(channel, listener)
  }
  let cachedModules: DiscoveredModule[] | null = null

  handleSafe('visualization:copy-image', async (event, rect: InlineVisualizationCaptureRect) => {
    const owner = BrowserWindow.fromWebContents(event.sender)
    if (!owner || owner.isDestroyed() || event.sender.isDestroyed()) {
      throw new Error('The visualization window is unavailable.')
    }
    return copyInlineVisualizationImage(event.sender, rect)
  })
  const configWriteQueues = new Map<string, Promise<void>>()
  const scanAndCacheModules = async (
    options?: { emitSummary?: boolean }
  ): Promise<DiscoveredModule[]> => {
    const previousModules = cachedModules ?? []
    const nextModules = await scanModules(callbacks?.getSettings() ?? {}, !app.isPackaged)
    const previousById = new Map(previousModules.map((module) => [module.moduleId, module] as const))
    const nextById = new Map(nextModules.map((module) => [module.moduleId, module] as const))

    const addedModuleIds: string[] = []
    const removedModuleIds: string[] = []
    const changedModuleIds: string[] = []
    for (const module of nextModules) {
      const previous = previousById.get(module.moduleId)
      if (!previous) {
        addedModuleIds.push(module.moduleId)
        continue
      }
      if (!areModulesEquivalent(previous, module)) {
        changedModuleIds.push(module.moduleId)
      }
    }
    for (const previous of previousModules) {
      if (!nextById.has(previous.moduleId)) {
        removedModuleIds.push(previous.moduleId)
      }
    }

    for (const moduleId of removedModuleIds) {
      await channelModuleManager?.stop(moduleId)
    }

    const statusMap = channelModuleManager?.getStatusMap() ?? {}
    const changedRunningModuleIds = changedModuleIds.filter((moduleId) =>
      isRunningProcessState(statusMap[moduleId]?.processState)
    )

    cachedModules = nextModules

    if (options?.emitSummary === true) {
      const summary: ModulesRescanSummaryPayload = {
        addedModuleIds,
        removedModuleIds,
        changedModuleIds,
        changedRunningModuleIds
      }
      for (const win of BrowserWindow.getAllWindows()) {
        if (!win.isDestroyed()) {
          win.webContents.send('modules:rescan-summary', summary)
        }
      }
    }

    return nextModules
  }
  ensureModulesScanned = scanAndCacheModules
  getSettingsSnapshotForModules = () => callbacks?.getSettings() ?? {}
  channelModuleManager = new ChannelModuleManager({
    workspacePath,
    getWireClient,
    getCachedModules: () => cachedModules,
    onStatusChanged: (statusMap) => {
      for (const win of BrowserWindow.getAllWindows()) {
        broadcastModuleStatus(win, statusMap)
      }
    },
    onQrUpdate: (payload) => {
      for (const win of BrowserWindow.getAllWindows()) {
        broadcastModuleQrUpdate(win, payload)
      }
    }
  })

  // Failures answer with an envelope rather than throwing: Electron would otherwise
  // flatten the JSON-RPC code and `data` to a message.
  const sendAppServerRequest = async (
    method: string,
    params: unknown,
    timeoutMs: number | undefined,
    onCompleted?: (
      client: DesktopAppServerClient,
      requestParams: unknown,
      result: unknown
    ) => void
  ): Promise<unknown> => {
    try {
      const client = getWireClient()
      if (!client) {
        throw new Error(`${translate(mainLocale(callbacks), 'ipc.appServerNotConnected')} (${method})`)
      }
      const requestParams = withDesktopRequestIdentity(
        method,
        params ?? {},
        callbacks?.getWorkspaceStatus().workspacePath
      )
      const result = await sendDesktopAppServerRequest(client, method, requestParams, timeoutMs, {
        supportsDynamicToolRebind: callbacks?.getConnectionStatus().capabilities?.dynamicToolRebind === true
      })
      onCompleted?.(client, requestParams, result)
      return result
    } catch (error) {
      return toAppServerErrorEnvelope(error)
    }
  }

  handleSafe(
    'appserver:send-request',
    (_event, method: string, params?: unknown, timeoutMs?: number) => (
      sendAppServerRequest(method, params, timeoutMs, (client, requestParams, result) => {
        callbacks?.onAppServerRequestCompleted?.(client, method, requestParams, result)
      })
    )
  )

  // Explicit escape hatch for third-party and dynamically discovered extension methods.
  handleSafe(
    'appserver:send-request-raw',
    (_event, method: AppServerRequestMethod, params?: unknown, timeoutMs?: number) => (
      sendAppServerRequest(method, params, timeoutMs)
    )
  )

  handleSafe('appserver:model-list', async (_event, providerId?: string | null) => {
    const client = getWireClient()
    if (!client) {
      return null
    }
    const normalizedProviderId = providerId?.trim() || null
    return client.sendRequest(
      'model/list',
      normalizedProviderId ? { providerId: normalizedProviderId } : {},
      20_000
    )
  })

  handleSafe('appserver:workspace-config-schema', async () => {
    const client = getWireClient()
    if (!client) {
      throw new Error(translate(mainLocale(callbacks), 'ipc.appServerNotConnected'))
    }

    try {
      return await client.sendRequest('workspace/config/schema', {}, 20_000)
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error)
      if (message.toLowerCase().includes('method not found')) {
        return null
      }
      throw error
    }
  })

  handleSafe('workspace-config:get-core', async () => {
    const remoteCore = await readActiveRemoteCoreConfigSnapshot(callbacks)
    if (remoteCore) {
      return remoteCore
    }

    const localWorkspacePath = workspacePath.trim()
    if (!localWorkspacePath) {
      return {
        workspace: createEmptyCoreConfigSnapshot(),
        userDefaults: await readCoreConfigSnapshot(path.join(os.homedir(), '.craft', 'config.json'))
      }
    }

    return {
      workspace: await readCoreConfigSnapshot(path.join(localWorkspacePath, '.craft', 'config.json')),
      userDefaults: await readCoreConfigSnapshot(path.join(os.homedir(), '.craft', 'config.json'))
    }
  })

  handleSafe('appserver:get-connection-status', () => {
    return callbacks?.getConnectionStatus() ?? { status: 'disconnected' }
  })

  handleSafe('appserver:resolved-binary', (_event, request?: ResolvedBinaryRequest) => {
    const settings = callbacks?.getSettings() ?? {}
    return resolveBinaryLocation({
      binarySource: request?.binarySource ?? settings.binarySource,
      binaryPath: request?.binaryPath ?? settings.appServerBinaryPath,
      preferDevBuild: import.meta.env.DEV,
      requireDevBuild: import.meta.env.DEV
    })
  })

  handleSafe('appserver:pick-binary', async (_event) => {
    const focusedWin = BrowserWindow.getFocusedWindow()
    const options: OpenDialogOptions =
      process.platform === 'win32'
        ? {
            title: translate(mainLocale(callbacks), 'settings.pickBinaryTitle'),
            properties: ['openFile'],
            filters: [{ name: 'DotCraft', extensions: ['exe'] }]
          }
        : {
            title: translate(mainLocale(callbacks), 'settings.pickBinaryTitle'),
            properties: ['openFile']
          }
    const result = await dialog.showOpenDialog(
      focusedWin ?? BrowserWindow.getAllWindows()[0],
      options
    )
    if (result.canceled || result.filePaths.length === 0) return null
    return result.filePaths[0]
  })

  handleSafe('appserver:restart-managed', async () => {
    await callbacks?.onRestartManagedAppServer()
  })

  handleSafe('appserver:retry-connection', async (_event, request?: RetryConnectionRequest) => {
    if (!callbacks?.onRetryAppServerConnection) {
      throw new Error('AppServer connection retry is not available right now.')
    }
    await callbacks.onRetryAppServerConnection(request)
  })

  handleSafe('appserver:apply-connection-settings', async (_event, draft: ConnectionSettingsDraft) => {
    if (!callbacks?.onApplyConnectionSettings) {
      throw new Error('Connection settings cannot be applied right now.')
    }
    await callbacks.onApplyConnectionSettings(draft)
  })

  handleSafe('appserver:server-response', (_event, bridgeId: string, result: unknown) => {
    resolveServerRequestBridge(bridgeId, result)
  })

  handleSafe('appserver:cancel-server-request', (_event, bridgeId: string) => {
    cancelServerRequestBridge(bridgeId)
  })

  handleSafe('appserver:user-input-auto-resolution-snapshot', () =>
    callbacks?.getUserInputAutoResolutionSnapshot?.() ?? [])

  handleSafe('appserver:user-input-conversation-presented', (_event, threadId: string | null) => {
    callbacks?.onUserInputConversationPresented?.(threadId)
  })

  handleSafe('appserver:user-input-conversation-activity', (_event, threadId: string) => {
    callbacks?.onUserInputConversationActivity?.(threadId)
  })

  handleSafe(
    'appserver:user-input-auto-resolution-snooze',
    (_event, threadId: string, requestId: string) => {
      callbacks?.onUserInputAutoResolutionSnoozed?.(threadId, requestId)
    }
  )

  // Titles the sender's own window, which is not necessarily the focused one.
  handleSafe('window:set-title', (event, title: string) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    win?.setTitle(title)
  })

  // Windows and Linux only; macOS has no title bar overlay to recolor.
  handleSafe('window:set-title-bar-overlay-theme', (event, theme: 'dark' | 'light') => {
    if (process.platform === 'darwin') return
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) return
    const t = theme === 'light' ? 'light' : 'dark'
    applyNativeChromeTheme(win, t, resolveThemeSurface(callbacks?.getSettings() ?? {}, t))
  })

  handleSafe('window:minimize', (event) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) return
    win.minimize()
  })

  handleSafe('window:toggle-maximize', (event) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) return false
    if (win.isMaximized()) {
      win.unmaximize()
    } else {
      win.maximize()
    }
    return win.isMaximized()
  })

  handleSafe('window:close', (event) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) return
    win.close()
  })

  handleSafe('window:is-maximized', (event) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    return win != null && !win.isDestroyed() && win.isMaximized()
  })

  handleSafe('window:get-visibility-state', (event): WindowVisibilityState => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) {
      return { minimized: false, visible: false, focused: false }
    }
    return {
      minimized: win.isMinimized(),
      visible: win.isVisible(),
      focused: win.isFocused()
    }
  })

  handleSafe('window:get-workspace-path', () => workspacePath)

  handleSafe('skill-market:search', async (_event, request: SkillMarketSearchRequest) => {
    return searchSkillMarket(workspacePath, request)
  })

  handleSafe('skill-market:detail', async (_event, request: SkillMarketDetailRequest) => {
    return getSkillMarketDetail(workspacePath, request)
  })

  handleSafe('skill-market:install', async (_event, request: SkillMarketInstallRequest) => {
    return installSkillFromMarket(workspacePath, request)
  })

  handleSafe('skill-market:prepare-dotcraft-install', async (_event, request: SkillMarketPrepareDotCraftInstallRequest) => {
    return prepareDotCraftSkillInstall(workspacePath, request)
  })

  handleSafe('skill-market:bind-dotcraft-install', async (_event, request: SkillMarketBindDotCraftInstallRequest) => {
    return bindDotCraftSkillInstall(workspacePath, request)
  })

  handleSafe('skill-market:cleanup-dotcraft-install', async (_event, request: SkillMarketCleanupDotCraftInstallRequest) => {
    return cleanupDotCraftSkillInstall(workspacePath, request)
  })

  handleSafe('shell:open-external', async (_event, url: string) => {
    await openExternalUrl(url)
  })

  handleSafe('shell:open-app-handoff', async (_event, url: string) => {
    await openAppHandoffUrl(url)
  })

  handleSafe('shell:get-protocol-handler-name', async (_event, protocol: string) => {
    return getProtocolHandlerName(protocol)
  })

  handleSafe('editors:list', async () => {
    return detectEditors()
  })

  handleSafe('editors:launch', async (_event, editorId: EditorId, targetPath: string) => {
    const locale = mainLocale(callbacks)
    const resolved = assertPathWithinWorkspace(targetPath, workspacePath, locale)
    await launchEditor(editorId, resolved)
  })

  handleSafe('editors:launch-local-path', async (_event, editorId: EditorId, targetPath: string) => {
    const resolved = await assertExistingLocalPath(targetPath)
    await launchEditor(editorId, resolved)
  })

  handleSafe('shell:open-local-path', async (_event, targetPath: string) => {
    const resolved = await assertExistingLocalPath(targetPath)
    const error = await shell.openPath(resolved)
    if (error) {
      throw new Error(error)
    }
  })

  handleSafe('shell:reveal-local-path', async (_event, targetPath: string) => {
    const resolved = await assertExistingLocalPath(targetPath)
    shell.showItemInFolder(resolved)
  })

  handleSafe('shell:show-item-in-folder', async (_event, targetPath: string) => {
    const locale = mainLocale(callbacks)
    const resolved = assertPathWithinWorkspace(targetPath, workspacePath, locale)
    shell.showItemInFolder(resolved)
  })

  handleSafe('file:write', async (_event, absPath: string, content: string) => {
    const resolved = assertPathWithinWorkspace(absPath, workspacePath, mainLocale(callbacks))
    await fs.mkdir(path.dirname(resolved), { recursive: true })
    await fs.writeFile(resolved, content, 'utf-8')
  })

  handleSafe('file:read', async (_event, absPath: string): Promise<string> => {
    const resolved = assertPathWithinWorkspace(absPath, workspacePath, mainLocale(callbacks))
    try {
      return await fs.readFile(resolved, 'utf-8')
    } catch (err: unknown) {
      const code = (err as NodeJS.ErrnoException)?.code
      if (code === 'ENOENT') return ''
      throw err
    }
  })

  handleSafe('file:delete', async (_event, absPath: string) => {
    const resolved = assertPathWithinWorkspace(absPath, workspacePath, mainLocale(callbacks))
    await fs.unlink(resolved)
  })

  handleSafe('file:exists', async (_event, absPath: string): Promise<boolean> => {
    const resolved = assertPathWithinWorkspace(absPath, workspacePath, mainLocale(callbacks))
    try {
      await fs.access(resolved)
      return true
    } catch {
      return false
    }
  })

  handleSafe(
    'git:commit',
    async (_event, wsPath: string, files: string[], message: string): Promise<string> => {
      const locale = mainLocale(callbacks)
      const gitWorkspacePath = assertGitWorkspacePath(wsPath, workspacePath, locale)
      if (!Array.isArray(files)) {
        throw new Error(translate(locale, 'ipc.noGitChangesToCommit'))
      }

      const commitFiles = await resolveCommitFilePaths(gitWorkspacePath, files, locale)
      if (commitFiles.length === 0) {
        throw new Error(translate(locale, 'ipc.noGitChangesToCommit'))
      }

      await runGitCommand(gitWorkspacePath, ['add', '--', ...commitFiles])
      const stagedDiff = await runGitCommand(
        gitWorkspacePath,
        ['diff', '--cached', '--quiet', '--', ...commitFiles],
        [0, 1]
      )
      if (stagedDiff.exitCode === 0) {
        throw new Error(translate(locale, 'ipc.noGitChangesToCommit'))
      }

      const commit = await runGitCommand(gitWorkspacePath, ['commit', '-m', message, '--', ...commitFiles])
      return commit.stdout.trim()
    }
  )
  handleSafe('git:branch', async (_event, wsPath: string): Promise<string | null> => {
    const locale = mainLocale(callbacks)
    const gitWorkspacePath = assertGitWorkspacePath(wsPath, workspacePath, locale)
    try {
      await runGitCommand(gitWorkspacePath, ['rev-parse', '--is-inside-work-tree'])
      const branch = (await runGitCommand(gitWorkspacePath, ['branch', '--show-current'])).stdout.trim()
      if (branch) return branch
      const head = (await runGitCommand(gitWorkspacePath, ['rev-parse', '--short', 'HEAD'])).stdout.trim()
      return head || null
    } catch {
      return null
    }
  })
  handleSafe('git:inspectHead', async (_event, wsPath: string): Promise<GitHeadInspection> => {
    const locale = mainLocale(callbacks)
    const gitWorkspacePath = assertGitInspectionPath(
      wsPath,
      workspacePath,
      callbacks?.getRecentWorkspaces() ?? [],
      locale
    )
    try {
      await runGitCommand(gitWorkspacePath, ['rev-parse', '--is-inside-work-tree'])
      const branch = (await runGitCommand(gitWorkspacePath, ['branch', '--show-current'])).stdout.trim()
      if (branch) return { kind: 'branch', label: branch }
      const head = (await runGitCommand(gitWorkspacePath, ['rev-parse', '--short', 'HEAD'])).stdout.trim()
      return head ? { kind: 'detached', label: head } : { kind: 'none' }
    } catch {
      return { kind: 'none' }
    }
  })
  handleSafe('git:listBranches', async (_event, wsPath: string): Promise<GitBranchListResult> => {
    const locale = mainLocale(callbacks)
    const gitWorkspacePath = assertGitWorkspacePath(wsPath, workspacePath, locale)
    await runGitCommand(gitWorkspacePath, ['rev-parse', '--is-inside-work-tree'])
    const current = (await runGitCommand(gitWorkspacePath, ['branch', '--show-current'])).stdout.trim() || null
    const detachedHead = current
      ? null
      : ((await runGitCommand(gitWorkspacePath, ['rev-parse', '--short', 'HEAD'])).stdout.trim() || null)
    const refs = await runGitCommand(gitWorkspacePath, [
      'for-each-ref',
      '--sort=refname',
      '--format=%(refname:short)',
      'refs/heads'
    ])
    const branches = refs.stdout
      .split(/\r?\n/)
      .map((name) => name.trim())
      .filter((name) => name.length > 0)
      .map((name) => ({ name, current: current === name }))
    return { current, detachedHead, branches }
  })
  handleSafe('git:checkoutBranch', async (_event, wsPath: string, branchName: string): Promise<void> => {
    const locale = mainLocale(callbacks)
    const gitWorkspacePath = assertGitWorkspacePath(wsPath, workspacePath, locale)
    const branch = typeof branchName === 'string' ? branchName.trim() : ''
    if (!branch) throw new Error('Branch name is required.')
    await runGitCommand(gitWorkspacePath, ['switch', branch])
  })
  handleSafe('git:createAndCheckoutBranch', async (_event, wsPath: string, branchName: string): Promise<void> => {
    const locale = mainLocale(callbacks)
    const gitWorkspacePath = assertGitWorkspacePath(wsPath, workspacePath, locale)
    const branch = typeof branchName === 'string' ? branchName.trim() : ''
    if (!branch) throw new Error('Branch name is required.')
    await runGitCommand(gitWorkspacePath, ['check-ref-format', '--branch', branch])
    await runGitCommand(gitWorkspacePath, ['switch', '-c', branch])
  })


  // The renderer owns localization, so callers pass the picker title in already translated.
  handleSafe('workspace:pick-folder', async (_event, options?: { title?: string }) => {
    const focusedWin = BrowserWindow.getFocusedWindow()
    const result = await dialog.showOpenDialog(
      focusedWin ?? BrowserWindow.getAllWindows()[0],
      {
        title: options?.title?.trim() || 'Select Workspace Folder',
        properties: ['openDirectory', 'createDirectory']
      }
    )
    if (result.canceled || result.filePaths.length === 0) return null
    return result.filePaths[0]
  })

  handleSafe('workspace:create-local-project', async (_event, params: { name?: string }) => {
    const folderName = sanitizeProjectFolderName(typeof params?.name === 'string' ? params.name : '')
    const baseDir = app.getPath('documents')
    let target = path.join(baseDir, folderName)
    for (let suffix = 2; existsSync(target); suffix++) {
      target = path.join(baseDir, `${folderName} ${suffix}`)
    }
    await fs.mkdir(target, { recursive: true })
    let gitInitialized = true
    try {
      await runGitCommand(target, ['init'])
    } catch {
      // git is unavailable or failed; the folder is still usable as a workspace.
      gitInitialized = false
    }
    return { path: target, gitInitialized }
  })

  handleSafe('workspace:switch', async (_event, newPath: string) => {
    if (callbacks?.onSwitchWorkspace) {
      await callbacks.onSwitchWorkspace(newPath)
    }
  })

  handleSafe('workspace:clear-selection', async () => {
    await callbacks?.onClearWorkspaceSelection()
  })

  handleSafe('workspace:get-recent', () => {
    return callbacks?.getRecentWorkspaces() ?? []
  })

  handleSafe('workspace:get-projects', () => {
    return callbacks?.getWorkspaceProjects?.() ?? {
      foregroundWorkspacePath: '',
      foregroundProjectId: '',
      secondaryLimit: 8,
      projects: []
    }
  })

  handleSafe('workspace:remove-recent', (_event, workspacePath: string) => {
    callbacks?.removeRecentWorkspace?.(workspacePath)
  })

  // A `previousPath` that differs from `primaryFolder` reassigns the Project identity.
  handleSafe(
    'workspace:save-local-project',
    async (
      _event,
      params: {
        previousPath?: string
        primaryFolder: string
        secondaryFolders: string[]
        name?: string
      }
    ) => {
      const primaryFolder = typeof params?.primaryFolder === 'string' ? params.primaryFolder.trim() : ''
      if (!primaryFolder) {
        throw new Error('A primary folder is required to save a project.')
      }
      let isDirectory = false
      try {
        isDirectory = (await fs.stat(primaryFolder)).isDirectory()
      } catch {
        isDirectory = false
      }
      if (!isDirectory) {
        throw new Error(`Primary folder does not exist: ${primaryFolder}`)
      }
      await callbacks?.saveLocalProject?.({
        previousPath: typeof params?.previousPath === 'string' ? params.previousPath : undefined,
        primaryFolder,
        secondaryFolders: Array.isArray(params?.secondaryFolders) ? params.secondaryFolders : [],
        name: typeof params?.name === 'string' ? params.name : undefined
      })
      return { path: primaryFolder }
    }
  )

  handleSafe('workspace:restart', async (_event, workspacePath: string) => {
    await callbacks?.restartWorkspace?.(workspacePath)
  })

  handleSafe('workspace:stop', async (_event, workspacePath: string) => {
    await callbacks?.stopWorkspace?.(workspacePath)
  })

  handleSafe(
    'workspace:archive-thread',
    async (_event, params: { workspacePath: string; threadId: string }) => {
      await callbacks?.archiveThreadInWorkspace?.(params.workspacePath, params.threadId)
    }
  )

  handleSafe('workspace:disconnect-remote', async () => {
    await callbacks?.onDisconnectRemoteProject?.()
  })

  handleSafe('workspace:clear-recent', () => {
    callbacks?.clearRecentWorkspaces?.()
  })

  handleSafe('workspace:get-status', () => {
    return callbacks?.getWorkspaceStatus() ?? { status: 'no-workspace', workspacePath: '', hasUserConfig: false, providers: [] }
  })

  handleSafe('workspace:run-setup', async (_event, request: WorkspaceSetupRequest) => {
    return callbacks?.onRunWorkspaceSetup(request)
  })

  handleSafe(
    'workspace:list-setup-models',
    async (_event, request: WorkspaceSetupModelListRequest) => {
      if (!callbacks?.onListSetupModels) {
        return { kind: 'error' } satisfies WorkspaceSetupModelListResult
      }
      return callbacks.onListSetupModels(request)
    }
  )

  handleSafe('workspace:login-setup-chatgpt', async (_event, providerId: string) => {
    if (!callbacks?.onLoginSetupChatGpt) return { kind: 'error' }
    return callbacks.onLoginSetupChatGpt(providerId)
  })

  handleSafe('workspace:open-new-window', () => {
    callbacks?.onOpenNewWindow()
  })

  handleSafe('workspace:check-lock', (_event, wsPath: string) => {
    return checkWorkspaceLock(wsPath)
  })

  handleSafe(
    'workspace:save-image-to-temp',
    async (_event, params: { dataUrl: string; fileName?: string }) => {
      const ws = workspacePath
      if (!ws) {
        throw new Error(translate(mainLocale(callbacks), 'ipc.noWorkspaceOpen'))
      }
      const loc = mainLocale(callbacks)
      const pathAbs = await saveImageDataUrlToTemp(ws, params.dataUrl, params.fileName, loc)
      return { path: pathAbs }
    }
  )

  handleSafe(
    'workspace:read-image-as-data-url',
    async (_event, params: { path: string }) => {
      const ws = workspacePath
      if (!ws) {
        throw new Error(translate(mainLocale(callbacks), 'ipc.noWorkspaceOpen'))
      }
      const loc = mainLocale(callbacks)
      const dataUrl = await readImageAsDataUrl(ws, params.path, loc)
      return { dataUrl }
    }
  )

  // Mirrors the workspace:viewer:list-files shape so the @-mention popover can show
  // "indexing in progress" instead of silently rendering empty.
  handleSafe(
    'workspace:search-files',
    async (
      _event,
      params: { query: string; workspacePath: string; limit?: number }
    ) => {
      if (!workspacePath.trim()) {
        return { files: [], indexStatus: 'empty', indexedCount: 0, stale: false }
      }
      const ws = path.resolve(workspacePath)
      const req = path.resolve(params.workspacePath)
      if (ws !== req) {
        throw new Error(translate(mainLocale(callbacks), 'ipc.workspacePathMismatch'))
      }
      const limit = Math.min(20, Math.max(1, params.limit ?? 10))
      return listWorkspaceFiles(ws, params.query, limit)
    }
  )

  const ensureTerminalWindowCleanup = (win: BrowserWindow): void => {
    if (terminalCleanupHookedWindows.has(win.id)) return
    terminalCleanupHookedWindows.add(win.id)
    win.once('closed', () => {
      terminalCleanupHookedWindows.delete(win.id)
      viewerTerminalManager.destroyAllTabs(win)
    })
    win.webContents.on('did-finish-load', () => {
      viewerTerminalManager.destroyAllTabs(win)
    })
  }

  handleSafe(
    'workspace:viewer:list-files',
    async (
      _event,
      params: { workspacePath: string; query: string; limit: number }
    ) => {
      const ws = path.resolve(workspacePath)
      const req = path.resolve(params.workspacePath)
      if (ws !== req) {
        throw new Error(translate(mainLocale(callbacks), 'ipc.workspacePathMismatch'))
      }
      if (!ws) {
        return { files: [], indexStatus: 'empty', indexedCount: 0, stale: false }
      }
      const limit = Math.min(500, Math.max(1, params.limit ?? 100))
      return listViewerFiles(ws, params.query, limit)
    }
  )

  handleSafe(
    'workspace:viewer:list-dir',
    async (_event, params: { dirPath?: string }) => {
      if (!workspacePath) {
        throw new Error(translate(mainLocale(callbacks), 'ipc.noWorkspaceOpen'))
      }
      const target = params.dirPath && params.dirPath.trim()
        ? params.dirPath
        : workspacePath
      const resolved = assertPathWithinWorkspace(target, workspacePath, mainLocale(callbacks))
      return listDirectory(resolved, workspacePath)
    }
  )

  handleSafe(
    'workspace:viewer:classify',
    async (_event, params: { absolutePath: string }) => {
      if (!workspacePath) {
        throw new Error(translate(mainLocale(callbacks), 'ipc.noWorkspaceOpen'))
      }
      return classifyFile(params.absolutePath, workspacePath)
    }
  )

  handleSafe(
    'workspace:viewer:read-text',
    async (_event, params: { absolutePath: string; limitBytes?: number }) => {
      if (!workspacePath) {
        throw new Error(translate(mainLocale(callbacks), 'ipc.noWorkspaceOpen'))
      }
      return readTextFile(params.absolutePath, workspacePath, params.limitBytes)
    }
  )

  handleSafe(
    'workspace:viewer:authorize-file',
    async (_event, params: { absolutePath: string }): Promise<{ absolutePath: string }> => {
      if (!workspacePath) {
        throw new Error(translate(mainLocale(callbacks), 'ipc.noWorkspaceOpen'))
      }
      const absolutePath = await authorizeViewerFile(params.absolutePath)
      return { absolutePath }
    }
  )

  handleSafe(
    'workspace:viewer:to-viewer-url',
    async (_event, params: { absolutePath: string }): Promise<{ url: string }> => {
      if (!workspacePath) {
        throw new Error(translate(mainLocale(callbacks), 'ipc.noWorkspaceOpen'))
      }
      const resolved = assertPathWithinWorkspace(params.absolutePath, workspacePath, mainLocale(callbacks))
      return { url: buildViewerUrl(resolved) }
    }
  )

  const sendDesktopPluginAppBindingRequest = async (
    appId: string,
    method: 'app/connection/status' | 'app/connection/start'
  ): Promise<unknown> => {
    if (typeof appId !== 'string' || !appId.trim()) throw new Error('App id is required.')
    const client = getWireClient()
    if (!client) {
      throw new Error(translate(mainLocale(callbacks), 'ipc.appServerNotConnected'))
    }
    return sendDesktopAppServerRequest(client, method, { appId }, 20_000, {
      supportsDynamicToolRebind: callbacks?.getConnectionStatus().capabilities?.dynamicToolRebind === true
    })
  }

  handleSafe(
    'desktop-plugin:register-module',
    async (_event, params: DesktopPluginModuleRequest) => {
      const settings = callbacks?.getSettings()
      const remote = settings?.connectionMode === 'remote'
      return registerDesktopPluginModuleRoute(params, {
        remote,
        packagedPluginRoots: remote
          ? resolveBundledBuiltInPluginRoot().split(path.delimiter).filter(Boolean)
          : []
      })
    }
  )

  handleSafe(
    'desktop-plugin:remove-module',
    async (_event, params: { pluginId: string; revision: string }): Promise<{ ok: boolean }> => {
      removeDesktopPluginModuleRoute(params.pluginId, params.revision)
      return { ok: true }
    }
  )

  const requestDesktopPluginAppSurface = async (
    params: {
      appId: string
      surfaceId: string
      relativePath: string
      body?: unknown
      timeoutMs?: number
    },
    method: 'GET' | 'POST'
  ): Promise<unknown> => {
    const client = getWireClient()
    if (!client) {
      throw new Error(translate(mainLocale(callbacks), 'ipc.appServerNotConnected'))
    }
    return requestDesktopPluginAppSurfaceJson(
      client,
      method,
      params.appId,
      params.surfaceId,
      params.relativePath,
      params.body,
      params.timeoutMs
    )
  }

  handleSafe(
    'desktop-plugin:app-surface-get-json',
    async (_event, params: {
      appId: string
      surfaceId: string
      relativePath: string
      timeoutMs?: number
    }) => requestDesktopPluginAppSurface(params, 'GET')
  )

  handleSafe(
    'desktop-plugin:app-surface-post-json',
    async (_event, params: {
      appId: string
      surfaceId: string
      relativePath: string
      body?: unknown
      timeoutMs?: number
    }) => requestDesktopPluginAppSurface(params, 'POST')
  )

  handleSafe(
    'desktop-plugin:app-connection-status',
    async (_event, params: { appId: string }) => {
      return sendDesktopPluginAppBindingRequest(params.appId, 'app/connection/status')
    }
  )

  handleSafe(
    'desktop-plugin:app-connection-start',
    async (_event, params: { appId: string }) => {
      return sendDesktopPluginAppBindingRequest(params.appId, 'app/connection/start')
    }
  )

  handleSafe(
    'desktop-plugin:app-open',
    async (_event, params: { appId: string; url: string }) => {
      const client = getWireClient()
      if (!client) throw new Error(translate(mainLocale(callbacks), 'ipc.appServerNotConnected'))
      const result = await sendDesktopAppServerRequest<AppListResult>(
        client,
        'app/list',
        { includeCatalog: true, includeDisabled: true },
        20_000,
        { supportsDynamicToolRebind: callbacks?.getConnectionStatus().capabilities?.dynamicToolRebind === true }
      )
      const protocol = result.apps
        ?.find((app) => app.appId === params.appId)
        ?.nativeApp?.protocol
        ?.trim()
        .replace(/:$/, '')
        .toLowerCase()
      let url: URL
      try {
        url = new URL(params.url)
      } catch {
        throw new Error('Invalid app URL.')
      }
      if (!protocol || url.protocol.toLowerCase() !== `${protocol}:`) {
        throw new Error('App URL does not match the app native protocol.')
      }
      await openAppHandoffUrl(params.url)
    }
  )

  handleSafe(
    'viewer:browser:create',
    async (event, params: { tabId: string; threadId?: string; workspacePath: string; initialUrl?: string }) => {
      const win = BrowserWindow.fromWebContents(event.sender)
      if (!win || win.isDestroyed()) {
        throw new Error('Browser window not available')
      }
      const ws = path.resolve(workspacePath)
      const req = path.resolve(params.workspacePath)
      if (ws !== req) {
        throw new Error(translate(mainLocale(callbacks), 'ipc.workspacePathMismatch'))
      }
      return viewerBrowserManager.createTab(win, {
        tabId: params.tabId,
        ...(params.threadId ? { threadId: params.threadId } : {}),
        workspacePath: params.workspacePath,
        ...(params.initialUrl ? { initialUrl: params.initialUrl } : {})
      })
    }
  )
  handleSafe('viewer:browser:destroy', async (event, params: { tabId: string }) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) return
    viewerBrowserManager.destroyTab(win, params.tabId)
  })
  handleSafe('viewer:browser:navigate', async (event, params: { tabId: string; url: string }) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) return
    void viewerBrowserManager.navigate(win, params)
  })
  handleSafe('viewer:browser:back', async (event, params: { tabId: string }) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) return
    viewerBrowserManager.goBack(win, params.tabId)
  })
  handleSafe('viewer:browser:forward', async (event, params: { tabId: string }) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) return
    viewerBrowserManager.goForward(win, params.tabId)
  })
  handleSafe('viewer:browser:reload', async (event, params: { tabId: string }) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) return
    viewerBrowserManager.reload(win, params.tabId)
  })
  handleSafe('viewer:browser:stop', async (event, params: { tabId: string }) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) return
    viewerBrowserManager.stop(win, params.tabId)
  })
  handleSafe(
    'viewer:browser:set-bounds',
    async (event, params: { tabId: string; x: number; y: number; width: number; height: number }) => {
      const win = BrowserWindow.fromWebContents(event.sender)
      if (!win || win.isDestroyed()) return
      viewerBrowserManager.setBounds(win, params)
    }
  )
  handleSafe('viewer:browser:set-visible', async (event, params: { tabId: string; visible: boolean }) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) return
    viewerBrowserManager.setVisible(win, params)
  })
  handleSafe('viewer:browser:set-active', async (event, params: { tabId: string }) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) return
    viewerBrowserManager.setActiveTab(win, params.tabId)
  })
  handleSafe('viewer:browser:open-external', async (event, params: { tabId: string }) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) return
    await viewerBrowserManager.openInOsBrowser(win, params.tabId)
  })
  handleSafe('viewer:browser:snapshot', async (event, params: { tabId: string }) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) return null
    return viewerBrowserManager.snapshotState(win, params.tabId)
  })
  handleSafe('viewer:browser:approval-response', async (_event, payload: BrowserUseApprovalResponsePayload) => {
    browserUseManager.handleApprovalResponse(payload)
  })
  handleSafe('viewer:browser:clear-cookies', async () => {
    if (!workspacePath) {
      throw new Error(translate(mainLocale(callbacks), 'ipc.noWorkspaceOpen'))
    }
    const partition = partitionForWorkspace(workspacePath)
    await session.fromPartition(partition).clearStorageData({ storages: ['cookies'] })
    return { ok: true }
  })

  handleSafe(
    'viewer:terminal:create',
    async (
      event,
      params: { tabId: string; threadId: string; workspacePath: string; cols: number; rows: number }
    ) => {
      const win = BrowserWindow.fromWebContents(event.sender)
      if (!win || win.isDestroyed()) {
        throw new Error('Browser window not available')
      }
      const ws = path.resolve(workspacePath)
      const req = path.resolve(params.workspacePath)
      if (ws !== req) {
        throw new Error(translate(mainLocale(callbacks), 'ipc.workspacePathMismatch'))
      }
      ensureTerminalWindowCleanup(win)
      return viewerTerminalManager.createTab(win, params)
    }
  )
  handleSafe('viewer:terminal:attach', async (event, params: { tabId: string }) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) {
      throw new Error('Browser window not available')
    }
    ensureTerminalWindowCleanup(win)
    return viewerTerminalManager.attachTab(win, params.tabId)
  })
  handleSafe('viewer:terminal:write', async (event, params: { tabId: string; data: string }) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) return
    viewerTerminalManager.write(win, params)
  })
  handleSafe('viewer:terminal:resize', async (event, params: { tabId: string; cols: number; rows: number }) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) return
    viewerTerminalManager.resize(win, params)
  })
  handleSafe('viewer:terminal:dispose', async (event, params: { tabId: string }) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) return
    viewerTerminalManager.destroyTab(win, params.tabId)
  })


  handleSafe('chrome:check-setup', async () => {
    return checkChromeSetup(workspacePath)
  })

  handleSafe('chrome:install-native-host', async () => {
    return installChromeNativeHost(workspacePath)
  })

  handleSafe('chrome:open', async (_event, params?: ChromeOpenRequest) => {
    return openChromeWindow(workspacePath, params)
  })


  handleSafe('settings:get', () => {
    return callbacks?.getSettings() ?? {}
  })

  handleSafe(
    'settings:set',
    async (_event, partial: Partial<AppSettings>) => {
      await callbacks?.updateSettings(partial)
    }
  )

  registerRemoteServersHandlers({
    handleSafe,
    getSettings: () => callbacks?.getSettings() ?? {},
    updateSettings: (partial) => callbacks?.updateSettings(partial),
    connectRemoteStack: callbacks?.onConnectRemoteStack,
    disconnectRemoteStack: callbacks?.onDisconnectRemoteStack,
    manager: getRemoteServersManager()
  })

  const hubCallbacks = callbacks
  if (hubCallbacks?.getHubClient) {
    const getHubClient = hubCallbacks.getHubClient
    registerSatellitesHandlers({
      handleSafe,
      getHubClient,
      bridge: getSatellitesHubBridge({ getHubClient }),
      getSettings: () => hubCallbacks.getSettings(),
      updateSettings: (partial) => hubCallbacks.updateSettings(partial)
    })
  }

  handleSafe('modules:list', async () => {
    if (cachedModules !== null) {
      return cachedModules
    }
    return scanAndCacheModules()
  })

  handleSafe('modules:pick-directory', async (): Promise<string | null> => {
    const focusedWin = BrowserWindow.getFocusedWindow()
    const result = await dialog.showOpenDialog(
      focusedWin ?? BrowserWindow.getAllWindows()[0],
      {
        title: 'Select Module Directory',
        properties: ['openDirectory', 'createDirectory']
      }
    )
    if (result.canceled || result.filePaths.length === 0) {
      return null
    }
    return result.filePaths[0]
  })

  handleSafe('modules:rescan', async () => scanAndCacheModules({ emitSummary: true }))

  handleSafe(
    'modules:set-active-variant',
    async (
      _event,
      params: { channelName: string; moduleId: string }
    ): Promise<{ ok: boolean; error?: string }> => {
      if (cachedModules === null) {
        await scanAndCacheModules()
      }
      const channelName = params?.channelName
      const moduleId = params?.moduleId
      if (typeof channelName !== 'string' || typeof moduleId !== 'string') {
        return { ok: false, error: 'Invalid payload' }
      }
      const normalizedChannelName = normalizeChannelName(channelName)
      if (!normalizedChannelName || !moduleId.trim()) {
        return { ok: false, error: 'Invalid payload' }
      }
      const module = cachedModules?.find((item) => item.moduleId === moduleId)
      if (!module) {
        return { ok: false, error: `Module '${moduleId}' not found` }
      }
      if (normalizeChannelName(module.channelName) !== normalizedChannelName) {
        return { ok: false, error: `Module '${moduleId}' does not belong to channel '${channelName}'` }
      }

      const groups = groupModulesByChannel(cachedModules ?? [], callbacks?.getSettings().activeModuleVariants)
      const currentGroup = groups.find(
        (group) => normalizeChannelName(group.channelName) === normalizedChannelName
      )
      if (currentGroup && currentGroup.activeModuleId !== moduleId) {
        await channelModuleManager?.stop(currentGroup.activeModuleId)
      }

      const currentSettings = callbacks?.getSettings() ?? {}
      await callbacks?.updateSettings({
        activeModuleVariants: {
          ...(currentSettings.activeModuleVariants ?? {}),
          [normalizedChannelName]: moduleId
        }
      })
      return { ok: true }
    }
  )

  handleSafe(
    'modules:read-config',
    async (
      _event,
      params: { configFileName: string }
    ): Promise<{ exists: boolean; config: Record<string, unknown> | null }> => {
      if (!isSafeConfigFileName(params.configFileName)) {
        throw new Error('Invalid config file name')
      }
      const configPath = path.join(workspacePath, '.craft', params.configFileName)
      try {
        const stat = await fs.stat(configPath)
        if (stat.size > 1_000_000) {
          throw new Error(`Config file is too large to load: ${params.configFileName}`)
        }
        const raw = await fs.readFile(configPath, 'utf-8')
        return { exists: true, config: parseJsonObjectConfig(raw) }
      } catch (error) {
        const code = (error as NodeJS.ErrnoException | null)?.code
        if (code === 'ENOENT') {
          return { exists: false, config: null }
        }
        throw error
      }
    }
  )

  handleSafe(
    'modules:write-config',
    async (
      _event,
      params: { configFileName: string; config: Record<string, unknown> }
    ): Promise<{ ok: true }> => {
      if (!isSafeConfigFileName(params.configFileName)) {
        throw new Error('Invalid config file name')
      }
      const configPath = path.join(workspacePath, '.craft', params.configFileName)
      const settings = callbacks?.getSettings() ?? {}
      const wsConfig = resolveModuleWsConfig(settings, callbacks?.getAppServerWsConfig?.())
      const mergedConfig = injectModuleDotcraftConfig(ensureObjectConfig(params.config), wsConfig)
      const previous = configWriteQueues.get(configPath) ?? Promise.resolve()
      const writeTask = previous
        .catch(() => {})
        .then(async () => {
          await fs.mkdir(path.dirname(configPath), { recursive: true })
          await fs.writeFile(
            configPath,
            `${JSON.stringify(mergedConfig, null, 2)}\n`,
            'utf-8'
          )
        })
      configWriteQueues.set(configPath, writeTask)
      await writeTask
      if (configWriteQueues.get(configPath) === writeTask) {
        configWriteQueues.delete(configPath)
      }
      return { ok: true }
    }
  )

  handleSafe(
    'modules:start',
    async (
      _event,
      params: { moduleId: string }
    ): Promise<{ ok: boolean; error?: string; missingFields?: string[] }> => {
      if (cachedModules === null) {
        await scanAndCacheModules()
      }
      if (!params?.moduleId || typeof params.moduleId !== 'string') {
        return { ok: false, error: 'Invalid module id' }
      }
      const module = cachedModules?.find((item) => item.moduleId === params.moduleId)
      if (!module) {
        return { ok: false, error: `Module '${params.moduleId}' not found` }
      }
      try {
        const configPath = path.join(workspacePath, '.craft', module.configFileName)
        const raw = await fs.readFile(configPath, 'utf-8')
        const parsed = parseJsonObjectConfig(raw)
        const settings = callbacks?.getSettings() ?? {}
        const wsConfig = resolveModuleWsConfig(settings, callbacks?.getAppServerWsConfig?.())
        const merged = injectModuleDotcraftConfig(parsed, wsConfig)
        const missingFields = findMissingRequiredFields(merged, module)
        if (missingFields.length > 0) {
          return {
            ok: false,
            error: `Required fields missing: ${missingFields.join(', ')}`,
            missingFields
          }
        }
        if (JSON.stringify(merged) !== JSON.stringify(parsed)) {
          await fs.writeFile(configPath, `${JSON.stringify(merged, null, 2)}\n`, 'utf-8')
        }
      } catch (error) {
        const code = (error as NodeJS.ErrnoException | null)?.code
        if (code !== 'ENOENT') {
          return { ok: false, error: error instanceof Error ? error.message : String(error) }
        }
      }
      return channelModuleManager?.start(params.moduleId) ?? { ok: false, error: 'Channel module manager is not available' }
    }
  )

  handleSafe(
    'modules:stop',
    async (_event, params: { moduleId: string }): Promise<{ ok: boolean; error?: string }> => {
      if (!params?.moduleId || typeof params.moduleId !== 'string') {
        return { ok: false, error: 'Invalid module id' }
      }
      return channelModuleManager?.stop(params.moduleId) ?? { ok: false, error: 'Channel module manager is not available' }
    }
  )

  handleSafe('modules:running', async (): Promise<ModuleStatusMap> => {
    const statusMap = channelModuleManager?.getStatusMap() ?? {}
    return statusMap
  })

  handleSafe(
    'modules:get-logs',
    async (_event, params: { moduleId: string }): Promise<{ lines: string[] }> => {
      if (!params?.moduleId || typeof params.moduleId !== 'string') {
        return { lines: [] }
      }
      return { lines: (await channelModuleManager?.getRecentLogs(params.moduleId)) ?? [] }
    }
  )

  handleSafe(
    'modules:qr-status',
    async (
      _event,
      params: { moduleId: string }
    ): Promise<{ active: boolean; qrDataUrl: string | null }> => {
      if (!params?.moduleId || typeof params.moduleId !== 'string') {
        return { active: false, qrDataUrl: null }
      }
      return channelModuleManager?.getQrStatus(params.moduleId) ?? { active: false, qrDataUrl: null }
    }
  )

  if (workspacePath) {
    warmFileSearchIndex(workspacePath)
  }
}

export function broadcastConnectionStatus(
  win: BrowserWindow,
  payload: ConnectionStatusPayload
): void {
  if (!win.isDestroyed()) {
    win.webContents.send('appserver:connection-status', payload)
  }
}

export function broadcastWorkspaceStatus(
  win: BrowserWindow,
  payload: WorkspaceStatusPayload
): void {
  if (!win.isDestroyed()) {
    win.webContents.send('workspace:status-changed', payload)
  }
}

export function broadcastModuleStatus(
  win: BrowserWindow,
  payload: ModuleStatusMap
): void {
  if (!win.isDestroyed()) {
    win.webContents.send('modules:status-changed', payload)
  }
}

export function broadcastModuleQrUpdate(
  win: BrowserWindow,
  payload: QrUpdatePayload
): void {
  if (!win.isDestroyed()) {
    win.webContents.send('modules:qr-update', payload)
  }
}

function stripMarkdownForNotify(text: string): string {
  return text
    .replace(/\r?\n/g, ' ')
    .replace(/\*\*(.+?)\*\*/g, '$1')
    .replace(/`([^`]+)`/g, '$1')
    .replace(/\s+/g, ' ')
    .trim()
}

export function shouldShowTaskCompletionNotification(win: BrowserWindow, settings?: AppSettings): boolean {
  const mode = resolveTaskCompletionNotificationMode(settings)
  if (mode === 'never') return false
  if (mode === 'always') return true
  return !win.isFocused()
}

/** Job results also raise a native OS notification, subject to the Desktop notification setting. */
export function broadcastNotification(
  win: BrowserWindow,
  method: string,
  params: unknown,
  settings?: AppSettings,
  workspacePath?: string,
  foreground?: boolean
): void {
  if (
    method === 'system/jobResult' &&
    !win.isDestroyed() &&
    shouldShowTaskCompletionNotification(win, settings)
  ) {
    const p = (params ?? {}) as Record<string, unknown>
    const jobName = String((p.jobName as string) ?? (p.name as string) ?? 'Job')
    const err = (p.error as string) ?? ''
    const result = (p.result as string) ?? (p.text as string) ?? ''
    const bodyRaw = err || result || 'Job completed'
    const body = stripMarkdownForNotify(bodyRaw).slice(0, 240)
    try {
      if (Notification.isSupported()) {
        new Notification({ title: jobName, body }).show()
      }
    } catch {
      /* ignore — notification optional */
    }
  }
  if (!win.isDestroyed()) {
    win.webContents.send('appserver:notification', {
      method,
      params,
      ...(workspacePath !== undefined ? { workspacePath } : {}),
      ...(foreground !== undefined ? { foreground } : {})
    })
  }
}

function interactiveRequestNotification(
  payload: ServerRequestPayload,
  settings?: AppSettings
): { title: string; body: string } | null {
  const locale = normalizeLocale(settings?.locale ?? DEFAULT_LOCALE)
  const params = (payload.params ?? {}) as Record<string, unknown>

  if (payload.method === 'item/tool/requestUserInput') {
    const questions = Array.isArray(params.questions) ? params.questions : []
    const firstQuestion = questions[0] as Record<string, unknown> | undefined
    const questionText = typeof firstQuestion?.question === 'string'
      ? firstQuestion.question.trim()
      : ''
    const body = questionText.length > 0
      ? questionText
      : translate(locale, 'notification.userInput.body')
    return {
      title: translate(locale, 'notification.userInput.title'),
      body: stripMarkdownForNotify(body).slice(0, 240)
    }
  }

  if (payload.method === 'item/approval/request') {
    const reason = typeof params.reason === 'string' ? params.reason.trim() : ''
    const operation = typeof params.operation === 'string' ? params.operation.trim() : ''
    const target = typeof params.target === 'string' ? params.target.trim() : ''
    const body = reason || [operation, target].filter(Boolean).join(' ') || translate(locale, 'notification.approval.body')
    return {
      title: translate(locale, 'notification.approval.title'),
      body: stripMarkdownForNotify(body).slice(0, 240)
    }
  }

  return null
}

/** The renderer must answer by calling sendServerResponse(bridgeId, result). */
export function broadcastServerRequest(
  win: BrowserWindow,
  payload: ServerRequestPayload,
  settings?: AppSettings
): void {
  if (!win.isDestroyed()) {
    if (!win.isFocused()) {
      const notification = interactiveRequestNotification(payload, settings)
      if (notification != null) {
        try {
          if (Notification.isSupported()) {
            new Notification(notification).show()
          }
        } catch {
          /* ignore — notification optional */
        }
      }
    }
    win.webContents.send('appserver:server-request', payload)
  }
}

/** Must run before re-registering handlers on a workspace switch. */
export function unregisterIpcHandlers(): void {
  for (const channel of REMOTE_SERVERS_CHANNELS) {
    ipcMain.removeHandler(channel)
  }
  for (const channel of SATELLITES_CHANNELS) {
    ipcMain.removeHandler(channel)
  }
  ipcMain.removeHandler('appserver:send-request')
  ipcMain.removeHandler('appserver:send-request-raw')
  ipcMain.removeHandler('visualization:copy-image')
  ipcMain.removeHandler('appserver:model-list')
  ipcMain.removeHandler('appserver:workspace-config-schema')
  ipcMain.removeHandler('workspace-config:get-core')
  ipcMain.removeHandler('appserver:get-connection-status')
  ipcMain.removeHandler('appserver:resolved-binary')
  ipcMain.removeHandler('appserver:pick-binary')
  ipcMain.removeHandler('appserver:restart-managed')
  ipcMain.removeHandler('appserver:retry-connection')
  ipcMain.removeHandler('appserver:apply-connection-settings')
  ipcMain.removeHandler('appserver:server-response')
  ipcMain.removeHandler('appserver:cancel-server-request')
  ipcMain.removeHandler('appserver:user-input-auto-resolution-snapshot')
  ipcMain.removeHandler('appserver:user-input-conversation-presented')
  ipcMain.removeHandler('appserver:user-input-conversation-activity')
  ipcMain.removeHandler('appserver:user-input-auto-resolution-snooze')
  ipcMain.removeHandler('window:set-title')
  ipcMain.removeHandler('window:set-title-bar-overlay-theme')
  ipcMain.removeHandler('window:minimize')
  ipcMain.removeHandler('window:toggle-maximize')
  ipcMain.removeHandler('window:close')
  ipcMain.removeHandler('window:is-maximized')
  ipcMain.removeHandler('window:get-visibility-state')
  ipcMain.removeHandler('window:get-workspace-path')
  ipcMain.removeHandler('shell:open-external')
  ipcMain.removeHandler('shell:get-protocol-handler-name')
  ipcMain.removeHandler('editors:list')
  ipcMain.removeHandler('editors:launch')
  ipcMain.removeHandler('editors:launch-local-path')
  ipcMain.removeHandler('shell:open-local-path')
  ipcMain.removeHandler('shell:reveal-local-path')
  ipcMain.removeHandler('file:write')
  ipcMain.removeHandler('file:read')
  ipcMain.removeHandler('file:delete')
  ipcMain.removeHandler('file:exists')
  ipcMain.removeHandler('git:commit')
  ipcMain.removeHandler('git:branch')
  ipcMain.removeHandler('git:inspectHead')
  ipcMain.removeHandler('git:listBranches')
  ipcMain.removeHandler('git:checkoutBranch')
  ipcMain.removeHandler('git:createAndCheckoutBranch')
  ipcMain.removeHandler('workspace:pick-folder')
  ipcMain.removeHandler('workspace:create-local-project')
  ipcMain.removeHandler('workspace:switch')
  ipcMain.removeHandler('workspace:clear-selection')
  ipcMain.removeHandler('workspace:get-recent')
  ipcMain.removeHandler('workspace:get-projects')
  ipcMain.removeHandler('workspace:remove-recent')
  ipcMain.removeHandler('workspace:save-local-project')
  ipcMain.removeHandler('workspace:restart')
  ipcMain.removeHandler('workspace:stop')
  ipcMain.removeHandler('workspace:archive-thread')
  ipcMain.removeHandler('workspace:disconnect-remote')
  ipcMain.removeHandler('workspace:clear-recent')
  ipcMain.removeHandler('workspace:get-status')
  ipcMain.removeHandler('workspace:run-setup')
  ipcMain.removeHandler('workspace:list-setup-models')
  ipcMain.removeHandler('workspace:login-setup-chatgpt')
  ipcMain.removeHandler('workspace:open-new-window')
  ipcMain.removeHandler('workspace:check-lock')
  ipcMain.removeHandler('workspace:save-image-to-temp')
  ipcMain.removeHandler('workspace:read-image-as-data-url')
  ipcMain.removeHandler('workspace:search-files')
  ipcMain.removeHandler('workspace:viewer:list-files')
  ipcMain.removeHandler('workspace:viewer:list-dir')
  ipcMain.removeHandler('workspace:viewer:classify')
  ipcMain.removeHandler('workspace:viewer:read-text')
  ipcMain.removeHandler('workspace:viewer:authorize-file')
  ipcMain.removeHandler('workspace:viewer:to-viewer-url')
  ipcMain.removeHandler('desktop-plugin:register-module')
  ipcMain.removeHandler('desktop-plugin:remove-module')
  ipcMain.removeHandler('desktop-plugin:app-surface-get-json')
  ipcMain.removeHandler('desktop-plugin:app-surface-post-json')
  ipcMain.removeHandler('desktop-plugin:app-connection-status')
  ipcMain.removeHandler('desktop-plugin:app-connection-start')
  ipcMain.removeHandler('desktop-plugin:app-open')
  clearDesktopPluginModuleRoutes()
  ipcMain.removeHandler('viewer:browser:create')
  ipcMain.removeHandler('viewer:browser:destroy')
  ipcMain.removeHandler('viewer:browser:navigate')
  ipcMain.removeHandler('viewer:browser:back')
  ipcMain.removeHandler('viewer:browser:forward')
  ipcMain.removeHandler('viewer:browser:reload')
  ipcMain.removeHandler('viewer:browser:stop')
  ipcMain.removeHandler('viewer:browser:set-bounds')
  ipcMain.removeHandler('viewer:browser:set-visible')
  ipcMain.removeHandler('viewer:browser:set-active')
  ipcMain.removeHandler('viewer:browser:open-external')
  ipcMain.removeHandler('viewer:browser:snapshot')
  ipcMain.removeHandler('viewer:terminal:create')
  ipcMain.removeHandler('viewer:terminal:attach')
  ipcMain.removeHandler('viewer:terminal:write')
  ipcMain.removeHandler('viewer:terminal:resize')
  ipcMain.removeHandler('viewer:terminal:dispose')
  ipcMain.removeHandler('chrome:check-setup')
  ipcMain.removeHandler('chrome:install-native-host')
  ipcMain.removeHandler('chrome:open')
  ipcMain.removeHandler('settings:get')
  ipcMain.removeHandler('settings:set')
  ipcMain.removeHandler('modules:list')
  ipcMain.removeHandler('modules:pick-directory')
  ipcMain.removeHandler('modules:rescan')
  ipcMain.removeHandler('modules:set-active-variant')
  ipcMain.removeHandler('modules:read-config')
  ipcMain.removeHandler('modules:write-config')
  ipcMain.removeHandler('modules:start')
  ipcMain.removeHandler('modules:stop')
  ipcMain.removeHandler('modules:running')
  ipcMain.removeHandler('modules:get-logs')
  ipcMain.removeHandler('modules:qr-status')
  if (channelModuleManager) {
    channelModuleManager.dispose()
  }
  for (const win of BrowserWindow.getAllWindows()) {
    viewerTerminalManager.destroyAllTabs(win)
  }
  terminalCleanupHookedWindows.clear()
  channelModuleManager = null
  ensureModulesScanned = null
  getSettingsSnapshotForModules = null
}
