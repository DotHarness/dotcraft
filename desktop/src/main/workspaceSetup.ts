import { execFile } from 'child_process'
import { copyFileSync, existsSync, mkdirSync, readFileSync, statSync, writeFileSync } from 'fs'
import { homedir } from 'os'
import { dirname, join, relative, resolve } from 'path'
import { resolveBinaryLocation } from './AppServerManager'
import { parseJsonConfig } from '../shared/jsonConfig'
import type { AppSettings } from './settings'
import {
  ANTHROPIC_PROTOCOL,
  defaultProviderEndpoint,
  normalizeProviderProtocol,
  OPENAI_RESPONSES_PROTOCOL,
  type DesktopProviderProtocol
} from '../shared/providerProtocols'

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
}

export interface WorkspaceStatusPayload {
  status: WorkspaceSetupState
  workspacePath: string
  hasUserConfig: boolean
  userConfigDefaults?: WorkspaceUserConfigDefaults
  providers: WorkspaceSetupProviderSummary[]
  bootstrapImportSources?: WorkspaceSetupBootstrapImportSource[]
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
  | { kind: 'success'; models: string[] }
  | { kind: 'unsupported' }
  | { kind: 'missing-key' }
  | { kind: 'error' }

const CHATGPT_CODEX_FALLBACK_MODELS = [
  'gpt-5.5',
  'gpt-5.4',
  'gpt-5.4-mini',
  'gpt-5.3-codex',
  'gpt-5.2'
]

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

function getUserConfigStatus(
  userConfigPath?: string
): Pick<WorkspaceStatusPayload, 'hasUserConfig' | 'userConfigDefaults' | 'providers'> {
  const globalConfigPath = userConfigPath ?? join(homedir(), '.craft', 'config.json')
  const parsed = readJsonObject(globalConfigPath)
  if (!parsed) {
    return {
      hasUserConfig: false,
      providers: []
    }
  }

  const providers = readExplicitProviders(parsed)
  const providerId = normalizeOptionalString(getConfigValueCaseInsensitive(parsed, 'ProviderId'))
  const model = normalizeOptionalString(getConfigValueCaseInsensitive(parsed, 'Model'))
  const explicitProviderIds = new Set(providers.map((provider) => provider.id.toLowerCase()))
  return {
    hasUserConfig: true,
    userConfigDefaults: {
      providerId:
        providerId && !isImplicitProviderId(providerId) && explicitProviderIds.has(providerId.toLowerCase())
          ? providerId
          : undefined,
      model
    },
    providers
  }
}

function parseModelIds(payload: unknown): string[] {
  const typed = payload as { data?: Array<{ id?: string; Id?: string }> }
  if (!Array.isArray(typed.data)) return []
  return Array.from(
    new Set(
      typed.data
        .map((item) => String(item.id ?? item.Id ?? '').trim())
        .filter(Boolean)
    )
  ).sort((a, b) => a.localeCompare(b))
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

async function fetchOpenAiModels(
  provider: WorkspaceSetupProviderDraft,
  fetchImpl: typeof fetch
): Promise<WorkspaceSetupModelListResult> {
  const endpoint = provider.endPoint.trim() || defaultProviderEndpoint(OPENAI_RESPONSES_PROTOCOL)

  let modelsUrl: string
  try {
    const normalizedEndpoint = endpoint.endsWith('/') ? endpoint : `${endpoint}/`
    modelsUrl = new URL('models', normalizedEndpoint).toString()
  } catch {
    return { kind: 'error' }
  }

  const response = await fetchImpl(modelsUrl, {
    method: 'GET',
    headers: {
      Accept: 'application/json',
      Authorization: `Bearer ${provider.apiKey.trim()}`
    }
  })
  if (!response.ok) {
    if (response.status === 404 || response.status === 405 || response.status === 501) {
      return { kind: 'unsupported' }
    }
    if (response.status === 401 || response.status === 403) {
      return { kind: 'missing-key' }
    }
    return { kind: 'error' }
  }

  const payload = (await response.json()) as unknown
  const models = parseModelIds(payload)
  return models.length > 0 ? { kind: 'success', models } : { kind: 'error' }
}

async function fetchAnthropicModels(
  provider: WorkspaceSetupProviderDraft,
  fetchImpl: typeof fetch
): Promise<WorkspaceSetupModelListResult> {
  const endpoint = provider.endPoint.trim() || defaultProviderEndpoint(ANTHROPIC_PROTOCOL)
  let modelsUrl: string
  try {
    modelsUrl = new URL('v1/models?limit=1000', endpoint.endsWith('/') ? endpoint : `${endpoint}/`).toString()
  } catch {
    return { kind: 'error' }
  }

  const response = await fetchImpl(modelsUrl, {
    method: 'GET',
    headers: {
      Accept: 'application/json',
      'anthropic-version': '2023-06-01',
      'x-api-key': provider.apiKey.trim()
    }
  })
  if (!response.ok) {
    if (response.status === 404 || response.status === 405 || response.status === 501) {
      return { kind: 'unsupported' }
    }
    if (response.status === 401 || response.status === 403) {
      return { kind: 'missing-key' }
    }
    return { kind: 'error' }
  }

  const payload = (await response.json()) as unknown
  const models = parseModelIds(payload)
  return models.length > 0 ? { kind: 'success', models } : { kind: 'error' }
}

export async function listSetupModels(
  request: WorkspaceSetupModelListRequest,
  options?: {
    userConfigPath?: string
    fetchImpl?: typeof fetch
  }
): Promise<WorkspaceSetupModelListResult> {
  const provider = normalizeProviderForModelList(request, options)
  if (!provider) {
    return { kind: 'error' }
  }

  // Before sign-in, setup cannot call the account-scoped ChatGPT catalog. Use the bundled
  // ChatGPT fallback; AppServer refreshes it from /backend-api/codex/models after login.
  if (provider.authMethod === 'chatgptOAuth') {
    return { kind: 'success', models: CHATGPT_CODEX_FALLBACK_MODELS }
  }

  if (!provider.apiKey.trim()) {
    return { kind: 'missing-key' }
  }

  const fetchImpl = options?.fetchImpl ?? fetch
  try {
    return provider.protocol === ANTHROPIC_PROTOCOL
      ? await fetchAnthropicModels(provider, fetchImpl)
      : await fetchOpenAiModels(provider, fetchImpl)
  } catch {
    return { kind: 'error' }
  }
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
  const userConfigStatus = getUserConfigStatus(options?.userConfigPath)
  const trimmed = workspacePath?.trim() ?? ''
  if (!trimmed) {
    return {
      status: 'no-workspace',
      workspacePath: '',
      ...userConfigStatus
    }
  }

  const configPath = join(trimmed, '.craft', 'config.json')
  const status: WorkspaceSetupState = existsSync(configPath) ? 'ready' : 'needs-setup'
  const bootstrapImportSources = status === 'needs-setup'
    ? detectWorkspaceSetupBootstrapImportSources(trimmed)
    : []

  return {
    status,
    workspacePath: trimmed,
    ...userConfigStatus,
    ...(bootstrapImportSources.length > 0 ? { bootstrapImportSources } : {})
  }
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
