import { execFile, spawn } from 'child_process'
import { copyFileSync, existsSync, mkdirSync, readFileSync, statSync, writeFileSync } from 'fs'
import { homedir } from 'os'
import { dirname, join, relative, resolve } from 'path'
import { resolveBinaryLocation } from './AppServerManager'
import { parseJsonConfig } from '../shared/jsonConfig'
import type { AppSettings } from './settings'
import {
  normalizeProviderProtocol,
  type DesktopProviderProtocol
} from '../shared/providerProtocols'
import {
  findProviderPreference,
  readProviderPreferences,
  type ModelPreference
} from '../shared/modelPreference'

export type WorkspaceSetupState = 'no-workspace' | 'needs-setup' | 'ready'
export type WorkspaceBootstrapProfile = 'default' | 'developer' | 'personal-assistant'
export type WorkspaceSetupProviderProtocol = DesktopProviderProtocol
export type WorkspaceSetupProviderMode = 'existing' | 'create' | 'skip'
export type WorkspaceSetupBootstrapImportSourceId = 'codex' | 'claude'

export interface WorkspaceSetupBootstrapImportSource {
  id: WorkspaceSetupBootstrapImportSourceId
  fileName: 'AGENTS.md' | 'CLAUDE.md'
  path: string
  relativePath: string
}

export interface WorkspaceSetupProviderSummary {
  id: string
  displayName: string
  protocol: WorkspaceSetupProviderProtocol
  hasApiKey: boolean
  endPoint: string
  networkTimeoutSeconds?: number | null
}

export interface WorkspaceUserConfigDefaults {
  providerId?: string
  model?: string
  preference?: ModelPreference
}

export interface RemoteWorkspaceStatusPayload {
  source?: 'servers' | 'manual' | 'cli'
  projectId?: string
  displayName?: string
  endpoint?: string
  hostId?: string
  stackId?: string
  serverName?: string
  stackName?: string
  workspaceDir?: string
  appServerWorkspacePath?: string
  composeDir?: string
  projectName?: string
}

export interface WorkspaceStatusPayload {
  status: WorkspaceSetupState
  workspacePath: string
  hasUserConfig: boolean
  userConfigDefaults?: WorkspaceUserConfigDefaults
  providers: WorkspaceSetupProviderSummary[]
  bootstrapImportSources?: WorkspaceSetupBootstrapImportSource[]
  remote?: RemoteWorkspaceStatusPayload
}

export interface WorkspaceSetupProviderDraft {
  id: string
  displayName: string
  protocol: WorkspaceSetupProviderProtocol
  apiKey: string
  endPoint: string
  networkTimeoutSeconds?: number | null
  authMethod?: 'apiKey' | 'chatgptOAuth'
  chatGptAccountId?: string | null
  chatGptPlanType?: string | null
}

export interface WorkspaceSetupRequest {
  model: string
  preference: ModelPreference
  profile: WorkspaceBootstrapProfile
  providerMode: WorkspaceSetupProviderMode
  providerId?: string
  provider?: WorkspaceSetupProviderDraft
  setAsUserDefault: boolean
  bootstrapImportSourceId?: WorkspaceSetupBootstrapImportSourceId | null
}

export interface WorkspaceSetupBootstrapImportResult {
  sourceId: WorkspaceSetupBootstrapImportSourceId
  status: 'success' | 'failed'
  warning?: string
}

export type WorkspaceSetupModelListRequest =
  | { providerId: string }
  | { provider: WorkspaceSetupProviderDraft }

export type WorkspaceSetupModelListResult =
  | { kind: 'success'; models: WorkspaceSetupModelCatalogItem[] }
  | { kind: 'auth-required' }
  | { kind: 'unsupported' }
  | { kind: 'missing-key' }
  | { kind: 'error'; retryable?: boolean }

export interface WorkspaceSetupModelCatalogItem {
  id: string
  ownedBy?: string
  createdAt?: string
  reasoning?: {
    supportsDisable: boolean
    supportedEfforts: Array<{ effort: 'low' | 'medium' | 'high' | 'extraHigh'; label: string; description: string }>
    defaultEffort: 'low' | 'medium' | 'high' | 'extraHigh'
    supportedOutputs: Array<'none' | 'summary' | 'full'>
    defaultOutput: 'none' | 'summary' | 'full'
  } | null
  speed?: {
    supportedModes: Array<'standard' | 'fast'>
    defaultMode: 'standard' | 'fast'
  } | null
  contextWindow?: {
    catalogWindow: number
    configuredWindow: number
    supportsMax: boolean
    maxWindow: number
  } | null
}

function normalizeSetupModelListResult(value: unknown): WorkspaceSetupModelListResult {
  if (!value || typeof value !== 'object') return { kind: 'error' }

  const result = value as {
    kind?: unknown
    models?: unknown
    retryable?: unknown
  }
  if (result.kind !== 'success') {
    if (result.kind === 'auth-required' || result.kind === 'unsupported' || result.kind === 'missing-key') {
      return { kind: result.kind }
    }
    return {
      kind: 'error',
      ...(typeof result.retryable === 'boolean' ? { retryable: result.retryable } : {})
    }
  }

  if (!Array.isArray(result.models)) return { kind: 'error' }
  const models = result.models
    .map((item): WorkspaceSetupModelCatalogItem | null => {
      if (typeof item === 'string') {
        const id = item.trim()
        return id ? { id } : null
      }
      if (!item || typeof item !== 'object' || Array.isArray(item)) return null
      const record = item as Record<string, unknown>
      const rawId = record.id ?? record.Id
      const id = typeof rawId === 'string' ? rawId.trim() : ''
      return id ? { ...record, id } as WorkspaceSetupModelCatalogItem : null
    })
    .filter((item): item is WorkspaceSetupModelCatalogItem => item != null)

  return { kind: 'success', models }
}


function buildBinaryResolutionError(settings: AppSettings): Error {
  const resolved = resolveBinaryLocation({
    binarySource: settings.binarySource,
    binaryPath: settings.appServerBinaryPath,
    preferDevBuild: import.meta.env.DEV,
    requireDevBuild: import.meta.env.DEV
  })

  if (resolved.path) {
    return new Error('DotCraft binary resolved unexpectedly.')
  }

  if (resolved.source === 'custom') {
    const configuredPath = settings.appServerBinaryPath?.trim()
    return new Error(
      configuredPath
        ? `Configured DotCraft binary not found: ${configuredPath}`
        : 'Custom DotCraft binary path is empty. Please choose a binary or switch to another source.'
    )
  }

  if (resolved.source === 'path') {
    return new Error(
      'DotCraft binary not found on PATH. Install dotcraft or switch to the bundled binary in Settings.'
    )
  }

  return new Error(
    import.meta.env.DEV
      ? 'Local DotCraft build not found. Run build_app.bat from the repository root before starting Desktop dev.'
      : 'Bundled DotCraft binary not found. Reinstall DotCraft or switch to another binary source in Settings.'
  )
}

function resolveDesktopBinary(settings: AppSettings): string {
  const resolved = resolveBinaryLocation({
    binarySource: settings.binarySource,
    binaryPath: settings.appServerBinaryPath,
    preferDevBuild: import.meta.env.DEV,
    requireDevBuild: import.meta.env.DEV
  })

  if (resolved.path) {
    return resolved.path
  }

  throw buildBinaryResolutionError(settings)
}

function getConfigValueCaseInsensitive(
  config: Record<string, unknown>,
  key: string
): unknown {
  const loweredKey = key.toLowerCase()
  const matchedKey = Object.keys(config).find((candidate) => candidate.toLowerCase() === loweredKey)
  return matchedKey ? config[matchedKey] : undefined
}

function readJsonObject(path: string): Record<string, unknown> | null {
  if (!existsSync(path)) {
    return null
  }

  try {
    const rawContent = readFileSync(path, 'utf8')
    return parseJsonConfig<Record<string, unknown> | null>(rawContent, null)
  } catch {
    return null
  }
}

function normalizeOptionalString(value: unknown): string | undefined {
  return typeof value === 'string' && value.trim() ? value.trim() : undefined
}

function readConfigString(
  config: Record<string, unknown> | null,
  key: string
): { present: boolean; value: string } {
  if (!config) return { present: false, value: '' }
  const matchedKey = Object.keys(config).find((candidate) => candidate.toLowerCase() === key.toLowerCase())
  if (!matchedKey) return { present: false, value: '' }
  const value = config[matchedKey]
  return { present: true, value: typeof value === 'string' ? value.trim() : '' }
}

function normalizeOptionalNumber(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value) ? value : null
}

function isImplicitProviderId(_providerId: string): boolean {
  return false
}

function readExplicitProviders(config: Record<string, unknown>): WorkspaceSetupProviderSummary[] {
  const providers = getConfigValueCaseInsensitive(config, 'Providers')
  if (providers == null || typeof providers !== 'object' || Array.isArray(providers)) {
    return []
  }

  return Object.entries(providers as Record<string, unknown>)
    .map(([rawId, rawProvider]): WorkspaceSetupProviderSummary | null => {
      const id = rawId.trim()
      if (!id || isImplicitProviderId(id) || rawProvider == null || typeof rawProvider !== 'object' || Array.isArray(rawProvider)) {
        return null
      }

      const provider = rawProvider as Record<string, unknown>
      const displayName = normalizeOptionalString(getConfigValueCaseInsensitive(provider, 'DisplayName')) ?? id
      const apiKey = normalizeOptionalString(getConfigValueCaseInsensitive(provider, 'ApiKey'))
      return {
        id,
        displayName,
        protocol: normalizeProviderProtocol(getConfigValueCaseInsensitive(provider, 'Protocol')),
        hasApiKey: apiKey != null,
        endPoint: normalizeOptionalString(getConfigValueCaseInsensitive(provider, 'EndPoint')) ?? '',
        networkTimeoutSeconds: normalizeOptionalNumber(getConfigValueCaseInsensitive(provider, 'NetworkTimeoutSeconds'))
      }
    })
    .filter((provider): provider is WorkspaceSetupProviderSummary => provider != null)
    .sort((a, b) => a.displayName.localeCompare(b.displayName))
}

function getUserConfigStatusFromParsed(
  parsed: Record<string, unknown> | null
): Pick<WorkspaceStatusPayload, 'hasUserConfig' | 'userConfigDefaults' | 'providers'> {
  if (!parsed) {
    return {
      hasUserConfig: false,
      providers: []
    }
  }

  const providers = readExplicitProviders(parsed)
  const providerId = normalizeOptionalString(getConfigValueCaseInsensitive(parsed, 'ProviderId'))
  const preference = providerId ? readProviderPreference(parsed, providerId) : null
  const explicitProviderIds = new Set(providers.map((provider) => provider.id.toLowerCase()))
  return {
    hasUserConfig: true,
    userConfigDefaults: {
      providerId:
        providerId && !isImplicitProviderId(providerId) && explicitProviderIds.has(providerId.toLowerCase())
          ? providerId
          : undefined,
      model: preference?.model,
      preference: preference ?? undefined
    },
    providers
  }
}

function mergeProviderSummaries(
  ...providerGroups: WorkspaceSetupProviderSummary[][]
): WorkspaceSetupProviderSummary[] {
  const merged = new Map<string, WorkspaceSetupProviderSummary>()
  for (const providers of providerGroups) {
    for (const provider of providers) {
      merged.set(provider.id.toLowerCase(), provider)
    }
  }
  return [...merged.values()].sort((a, b) => a.displayName.localeCompare(b.displayName))
}

function resolveEffectiveProviderId(
  workspaceConfig: Record<string, unknown> | null,
  userConfig: Record<string, unknown> | null
): string {
  const workspaceProviderId = readConfigString(workspaceConfig, 'ProviderId')
  if (workspaceProviderId.present) return workspaceProviderId.value

  const userProviderId = readConfigString(userConfig, 'ProviderId')
  return userProviderId.present ? userProviderId.value : ''
}

function readProviderPreference(
  config: Record<string, unknown> | null,
  providerId: string
): ModelPreference | null {
  if (!config || !providerId.trim()) return null
  return findProviderPreference(
    readProviderPreferences(getConfigValueCaseInsensitive(config, 'ProviderPreferences')),
    providerId
  )
}

function resolveEffectiveModel(
  workspaceConfig: Record<string, unknown> | null,
  userConfig: Record<string, unknown> | null
): ModelPreference | null {
  const providerId = resolveEffectiveProviderId(workspaceConfig, userConfig)
  return readProviderPreference(workspaceConfig, providerId) ?? readProviderPreference(userConfig, providerId)
}

function hasConfiguredProvider(
  providerId: string,
  providers: WorkspaceSetupProviderSummary[]
): boolean {
  if (!providerId || isImplicitProviderId(providerId)) return false
  return providers.some((provider) => provider.id.toLowerCase() === providerId.toLowerCase())
}

function normalizeProviderForModelList(
  request: WorkspaceSetupModelListRequest,
  options?: { userConfigPath?: string }
): WorkspaceSetupProviderDraft | null {
  if ('provider' in request) {
    return {
      ...request.provider,
      id: request.provider.id.trim(),
      displayName: request.provider.displayName.trim(),
      protocol: normalizeProviderProtocol(request.provider.protocol),
      apiKey: request.provider.apiKey.trim(),
      endPoint: request.provider.endPoint.trim(),
      authMethod: request.provider.authMethod === 'chatgptOAuth' ? 'chatgptOAuth' : 'apiKey',
      chatGptAccountId: request.provider.chatGptAccountId ?? null,
      chatGptPlanType: request.provider.chatGptPlanType ?? null
    }
  }

  const globalConfigPath = options?.userConfigPath ?? join(homedir(), '.craft', 'config.json')
  const parsed = readJsonObject(globalConfigPath)
  if (!parsed) return null
  const providerId = request.providerId.trim()
  const provider = readExplicitProviders(parsed).find((item) => item.id === providerId)
  if (!provider) return null

  const providers = getConfigValueCaseInsensitive(parsed, 'Providers') as Record<string, unknown> | undefined
  const providerKey = providers
    ? Object.keys(providers).find((key) => key.toLowerCase() === provider.id.toLowerCase())
    : undefined
  const rawProvider = providerKey ? providers?.[providerKey] : undefined
  const rawProviderRecord =
    rawProvider != null && typeof rawProvider === 'object' && !Array.isArray(rawProvider)
      ? rawProvider as Record<string, unknown>
      : {}
  const authMethodValue = normalizeOptionalString(getConfigValueCaseInsensitive(rawProviderRecord, 'AuthMethod'))
  const authMethod = authMethodValue?.toLowerCase() === 'chatgptoauth' ? 'chatgptOAuth' : 'apiKey'
  return {
    id: provider.id,
    displayName: provider.displayName,
    protocol: provider.protocol,
    apiKey: normalizeOptionalString(getConfigValueCaseInsensitive(rawProviderRecord, 'ApiKey')) ?? '',
    endPoint: provider.endPoint,
    networkTimeoutSeconds: provider.networkTimeoutSeconds,
    authMethod
  }
}

export async function listSetupModels(
  request: WorkspaceSetupModelListRequest,
  options?: {
    userConfigPath?: string
    settings?: AppSettings
    runBackend?: (args: string[], stdin?: string, timeoutMs?: number) => Promise<WorkspaceSetupModelListResult>
  }
): Promise<WorkspaceSetupModelListResult> {
  const provider = normalizeProviderForModelList(request, options)
  if (!provider) {
    return { kind: 'error' }
  }

  const args = ['model-catalog']
  const stdin = 'provider' in request ? JSON.stringify(provider) : undefined
  if (stdin) args.push('--stdin')
  else if ('providerId' in request) args.push('--provider-id', request.providerId)
  const result = options?.runBackend
    ? await options.runBackend(args, stdin, 30_000)
    : await runSetupBackend(resolveDesktopBinary(options?.settings ?? ({} as AppSettings)), args, stdin, 30_000)
  return normalizeSetupModelListResult(result)
}

export async function loginSetupChatGpt(
  providerId: string,
  settings: AppSettings,
  runBackend?: (args: string[], stdin?: string, timeoutMs?: number) => Promise<WorkspaceSetupModelListResult>
): Promise<{ kind: 'success' | 'error' }> {
  const args = ['auth', 'openai', 'login', '--provider-id', providerId, '--no-bind']
  try {
    if (runBackend) await runBackend(args, undefined, 15 * 60_000)
    else await runSetupBackend(resolveDesktopBinary(settings), args, undefined, 15 * 60_000, false)
    return { kind: 'success' }
  } catch {
    return { kind: 'error' }
  }
}

function runSetupBackend(
  binaryPath: string,
  args: string[],
  stdin: string | undefined,
  timeoutMs: number,
  parseJson = true
): Promise<WorkspaceSetupModelListResult> {
  return new Promise((resolve, reject) => {
    const child = spawn(binaryPath, args, { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] })
    let stdout = ''
    let stderr = ''
    const timer = setTimeout(() => child.kill(), timeoutMs)
    child.stdout.setEncoding('utf8')
    child.stderr.setEncoding('utf8')
    child.stdout.on('data', (chunk: string) => { stdout += chunk })
    child.stderr.on('data', (chunk: string) => { stderr += chunk })
    child.on('error', (error) => {
      clearTimeout(timer)
      reject(error)
    })
    child.on('close', (code) => {
      clearTimeout(timer)
      if (code !== 0) {
        reject(new Error(stderr.trim() || `DotCraft backend exited with code ${code ?? 'unknown'}.`))
        return
      }
      if (!parseJson) {
        resolve({ kind: 'success', models: [] })
        return
      }
      try {
        resolve(JSON.parse(stdout.trim()) as WorkspaceSetupModelListResult)
      } catch {
        reject(new Error(stderr.trim() || 'DotCraft backend returned invalid JSON.'))
      }
    })
    child.stdin.end(stdin)
  })
}

export function createUniqueSetupProviderId(
  preferredId: string,
  existingProviders: Array<{ id: string }>
): string {
  const normalizedBase = preferredId
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9_-]+/g, '-')
    .replace(/^-+|-+$/g, '')
  const base = normalizedBase || 'provider'
  const used = new Set(existingProviders.map((provider) => provider.id.trim().toLowerCase()))
  if (!used.has(base)) return base

  for (let index = 2; index < 1000; index++) {
    const candidate = `${base}-${index}`
    if (!used.has(candidate)) return candidate
  }
  return `${base}-${Date.now()}`
}

function isRegularFile(path: string): boolean {
  try {
    return statSync(path).isFile()
  } catch {
    return false
  }
}

function findNearestSetupImportFile(
  workspacePath: string,
  fileName: 'AGENTS.md' | 'CLAUDE.md'
): string | null {
  let current = resolve(workspacePath)

  while (true) {
    const candidate = join(current, fileName)
    if (isRegularFile(candidate)) {
      return candidate
    }

    const parent = dirname(current)
    if (parent === current) {
      return null
    }
    current = parent
  }
}

function sourceRelativePath(workspacePath: string, sourcePath: string): string {
  const rel = relative(resolve(workspacePath), sourcePath)
  return (rel || sourcePath).replace(/\\/g, '/')
}

export function detectWorkspaceSetupBootstrapImportSources(
  workspacePath: string
): WorkspaceSetupBootstrapImportSource[] {
  const trimmed = workspacePath.trim()
  if (!trimmed) return []

  const agentsPath = findNearestSetupImportFile(trimmed, 'AGENTS.md')
  const claudePath = findNearestSetupImportFile(trimmed, 'CLAUDE.md')
  const sources: WorkspaceSetupBootstrapImportSource[] = []

  if (agentsPath) {
    sources.push({
      id: 'codex',
      fileName: 'AGENTS.md',
      path: agentsPath,
      relativePath: sourceRelativePath(trimmed, agentsPath)
    })
  }

  if (claudePath) {
    sources.push({
      id: 'claude',
      fileName: 'CLAUDE.md',
      path: claudePath,
      relativePath: sourceRelativePath(trimmed, claudePath)
    })
  }

  return sources
}

export function getWorkspaceStatus(
  workspacePath: string | null | undefined,
  options?: { userConfigPath?: string }
): WorkspaceStatusPayload {
  const userConfigPath = options?.userConfigPath ?? join(homedir(), '.craft', 'config.json')
  const userConfig = readJsonObject(userConfigPath)
  const userConfigStatus = getUserConfigStatusFromParsed(userConfig)
  const trimmed = workspacePath?.trim() ?? ''
  if (!trimmed) {
    return {
      status: 'no-workspace',
      workspacePath: '',
      ...userConfigStatus
    }
  }

  const configPath = join(trimmed, '.craft', 'config.json')
  const workspaceConfigExists = existsSync(configPath)
  const workspaceConfig = workspaceConfigExists ? readJsonObject(configPath) : null
  const workspaceProviders = workspaceConfig ? readExplicitProviders(workspaceConfig) : []
  const providers = mergeProviderSummaries(userConfigStatus.providers, workspaceProviders)
  const effectiveProviderId = resolveEffectiveProviderId(workspaceConfig, userConfig)
  const effectivePreference = resolveEffectiveModel(workspaceConfig, userConfig)
  const status: WorkspaceSetupState =
    workspaceConfigExists &&
    hasConfiguredProvider(effectiveProviderId, providers) &&
    Boolean(effectivePreference?.model)
      ? 'ready'
      : 'needs-setup'
  const bootstrapImportSources = status === 'needs-setup'
    ? detectWorkspaceSetupBootstrapImportSources(trimmed)
    : []

  return {
    status,
    workspacePath: trimmed,
    ...userConfigStatus,
    providers,
    ...(bootstrapImportSources.length > 0 ? { bootstrapImportSources } : {})
  }
}

export function shouldRouteWorkspaceThroughSetupBeforeAppServerStart(
  workspacePath: string | null | undefined,
  options?: { userConfigPath?: string; usingRemoteConnection?: boolean }
): boolean {
  if (options?.usingRemoteConnection) return false
  const statusOptions = options?.userConfigPath ? { userConfigPath: options.userConfigPath } : undefined
  return getWorkspaceStatus(workspacePath, statusOptions).status === 'needs-setup'
}

function appendProviderArgs(args: string[], request: WorkspaceSetupRequest): void {
  if (request.providerMode === 'skip') {
    args.push('--skip-provider')
    return
  }

  args.push('--provider-mode', request.providerMode)
  if (request.model.trim()) {
    args.push('--model', request.model.trim())
  }
  args.push('--preference-json', JSON.stringify(request.preference))

  if (request.providerMode === 'existing') {
    args.push('--provider-id', request.providerId?.trim() ?? '')
    return
  }

  const provider = request.provider
  if (!provider) {
    throw new Error('Provider draft is required.')
  }
  args.push(
    '--provider-id',
    provider.id.trim(),
    '--provider-display-name',
    provider.displayName.trim() || provider.id.trim(),
    '--provider-protocol',
    normalizeProviderProtocol(provider.protocol)
  )
  if (provider.endPoint.trim()) {
    args.push('--endpoint', provider.endPoint.trim())
  }
  if (provider.apiKey.trim()) {
    args.push('--api-key', provider.apiKey.trim())
  }
  if (provider.networkTimeoutSeconds != null) {
    args.push('--provider-timeout-seconds', String(provider.networkTimeoutSeconds))
  }
  if (provider.authMethod && provider.authMethod !== 'apiKey') {
    args.push('--auth-method', provider.authMethod)
  }
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}

export function applyWorkspaceSetupBootstrapImport(
  workspacePath: string,
  sourceId: WorkspaceSetupBootstrapImportSourceId | null | undefined
): WorkspaceSetupBootstrapImportResult | undefined {
  if (!sourceId) return undefined

  const source = detectWorkspaceSetupBootstrapImportSources(workspacePath)
    .find((candidate) => candidate.id === sourceId)
  if (!source) {
    return {
      sourceId,
      status: 'failed',
      warning: 'The selected bootstrap import source could not be found.'
    }
  }

  const craftPath = join(workspacePath, '.craft')
  const destinationPath = join(craftPath, 'AGENTS.md')

  try {
    mkdirSync(craftPath, { recursive: true })
    copyFileSync(source.path, destinationPath)
  } catch (error) {
    return {
      sourceId,
      status: 'failed',
      warning: `DotCraft setup completed, but bootstrap import failed: ${errorMessage(error)}`
    }
  }

  try {
    const importsPath = join(craftPath, 'imports')
    mkdirSync(importsPath, { recursive: true })
    writeFileSync(
      join(importsPath, 'bootstrap-import.json'),
      `${JSON.stringify({
        importedAt: new Date().toISOString(),
        source: {
          id: source.id,
          fileName: source.fileName,
          path: source.path,
          relativePath: source.relativePath
        },
        destination: '.craft/AGENTS.md',
        status: 'success'
      }, null, 2)}\n`,
      'utf8'
    )
  } catch (error) {
    return {
      sourceId,
      status: 'success',
      warning: `Bootstrap import completed, but DotCraft could not write import metadata: ${errorMessage(error)}`
    }
  }

  return {
    sourceId,
    status: 'success'
  }
}

export function runWorkspaceSetup(
  workspacePath: string,
  request: WorkspaceSetupRequest,
  settings: AppSettings
): Promise<WorkspaceSetupResult> {
  const binaryPath = resolveDesktopBinary(settings)
  const args = [
    'setup',
    '--profile',
    request.profile
  ]

  appendProviderArgs(args, request)
  if (request.setAsUserDefault) {
    args.push('--set-user-default')
  }

  return new Promise<WorkspaceSetupResult>((resolve, reject) => {
    execFile(
      binaryPath,
      args,
      {
        cwd: workspacePath,
        windowsHide: true
      },
      (error: Error | null, stdout: string, stderr: string) => {
        if (error) {
          const detail = stderr?.trim() || stdout?.trim() || error.message
          reject(new Error(detail))
          return
        }

        const bootstrapImport = applyWorkspaceSetupBootstrapImport(
          workspacePath,
          request.bootstrapImportSourceId
        )

        resolve({
          stdout: stdout.trim(),
          stderr: stderr.trim(),
          exitCode: 0,
          ...(bootstrapImport ? { bootstrapImport } : {})
        })
      }
    )
  })
}

export interface WorkspaceSetupResult {
  stdout: string
  stderr: string
  exitCode: number
  bootstrapImport?: WorkspaceSetupBootstrapImportResult
}
