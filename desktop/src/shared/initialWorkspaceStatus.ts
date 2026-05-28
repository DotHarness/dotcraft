import {
  tryNormalizeProviderProtocol,
  type DesktopProviderProtocol
} from './providerProtocols'

export type InitialWorkspaceSetupState = 'no-workspace' | 'needs-setup' | 'ready'
export type InitialWorkspaceLanguage = 'Chinese' | 'English'
export type InitialWorkspaceProviderProtocol = DesktopProviderProtocol
export type InitialWorkspaceBootstrapImportSourceId = 'codex' | 'claude'

export interface InitialWorkspaceProviderSummary {
  id: string
  displayName: string
  protocol: InitialWorkspaceProviderProtocol
  hasApiKey: boolean
  endPoint: string
  networkTimeoutSeconds?: number | null
}

export interface InitialWorkspaceUserConfigDefaults {
  language?: InitialWorkspaceLanguage
  providerId?: string
  model?: string
}

export interface InitialWorkspaceStatusPayload {
  status: InitialWorkspaceSetupState
  workspacePath: string
  hasUserConfig: boolean
  userConfigDefaults?: InitialWorkspaceUserConfigDefaults
  providers: InitialWorkspaceProviderSummary[]
  bootstrapImportSources?: InitialWorkspaceBootstrapImportSource[]
}

export interface InitialWorkspaceBootstrapImportSource {
  id: InitialWorkspaceBootstrapImportSourceId
  fileName: 'AGENTS.md' | 'CLAUDE.md'
  path: string
  relativePath: string
}

export const INITIAL_WORKSPACE_STATUS_ARG_PREFIX = '--dotcraft-initial-workspace-status='

export const DEFAULT_INITIAL_WORKSPACE_STATUS: InitialWorkspaceStatusPayload = {
  status: 'no-workspace',
  workspacePath: '',
  hasUserConfig: false,
  providers: []
}

export function encodeInitialWorkspaceStatusArg(status: InitialWorkspaceStatusPayload): string {
  return `${INITIAL_WORKSPACE_STATUS_ARG_PREFIX}${encodeURIComponent(JSON.stringify(status))}`
}

export function readInitialWorkspaceStatusFromArgv(argv: readonly string[]): InitialWorkspaceStatusPayload {
  const arg = argv.find((value) => value.startsWith(INITIAL_WORKSPACE_STATUS_ARG_PREFIX))
  if (!arg) return DEFAULT_INITIAL_WORKSPACE_STATUS

  const raw = arg.slice(INITIAL_WORKSPACE_STATUS_ARG_PREFIX.length)
  try {
    const parsed = JSON.parse(decodeURIComponent(raw)) as unknown
    return normalizeInitialWorkspaceStatus(parsed) ?? DEFAULT_INITIAL_WORKSPACE_STATUS
  } catch {
    return DEFAULT_INITIAL_WORKSPACE_STATUS
  }
}

function normalizeInitialWorkspaceStatus(value: unknown): InitialWorkspaceStatusPayload | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return null

  const raw = value as Record<string, unknown>
  if (!isWorkspaceSetupState(raw.status)) return null
  if (raw.status !== 'no-workspace' && typeof raw.workspacePath !== 'string') return null
  if (typeof raw.hasUserConfig !== 'boolean') return null
  if (!Array.isArray(raw.providers)) return null

  const providers = raw.providers
    .map(normalizeProviderSummary)
    .filter((provider): provider is InitialWorkspaceProviderSummary => provider != null)

  const normalized: InitialWorkspaceStatusPayload = {
    status: raw.status,
    workspacePath: typeof raw.workspacePath === 'string' ? raw.workspacePath : '',
    hasUserConfig: raw.hasUserConfig === true,
    providers
  }

  if (Array.isArray(raw.bootstrapImportSources)) {
    const bootstrapImportSources = raw.bootstrapImportSources
      .map(normalizeBootstrapImportSource)
      .filter((source): source is InitialWorkspaceBootstrapImportSource => source != null)
    if (bootstrapImportSources.length > 0) {
      normalized.bootstrapImportSources = bootstrapImportSources
    }
  }

  const userConfigDefaults = normalizeUserConfigDefaults(raw.userConfigDefaults)
  if (userConfigDefaults) {
    normalized.userConfigDefaults = userConfigDefaults
  }

  return normalized
}

function normalizeProviderSummary(value: unknown): InitialWorkspaceProviderSummary | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return null

  const raw = value as Record<string, unknown>
  const protocol = tryNormalizeProviderProtocol(raw.protocol)
  if (
    typeof raw.id !== 'string' ||
    typeof raw.displayName !== 'string' ||
    protocol == null ||
    typeof raw.endPoint !== 'string'
  ) {
    return null
  }

  const provider: InitialWorkspaceProviderSummary = {
    id: raw.id,
    displayName: raw.displayName,
    protocol,
    hasApiKey: raw.hasApiKey === true,
    endPoint: raw.endPoint
  }

  if (typeof raw.networkTimeoutSeconds === 'number' || raw.networkTimeoutSeconds === null) {
    provider.networkTimeoutSeconds = raw.networkTimeoutSeconds
  }

  return provider
}

function normalizeBootstrapImportSource(value: unknown): InitialWorkspaceBootstrapImportSource | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return null

  const raw = value as Record<string, unknown>
  if (
    !isBootstrapImportSourceId(raw.id) ||
    (raw.fileName !== 'AGENTS.md' && raw.fileName !== 'CLAUDE.md') ||
    typeof raw.path !== 'string' ||
    typeof raw.relativePath !== 'string'
  ) {
    return null
  }

  return {
    id: raw.id,
    fileName: raw.fileName,
    path: raw.path,
    relativePath: raw.relativePath
  }
}

function normalizeUserConfigDefaults(value: unknown): InitialWorkspaceUserConfigDefaults | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return null

  const raw = value as Record<string, unknown>
  const defaults: InitialWorkspaceUserConfigDefaults = {}
  if (raw.language === 'Chinese' || raw.language === 'English') {
    defaults.language = raw.language
  }
  if (typeof raw.providerId === 'string') {
    defaults.providerId = raw.providerId
  }
  if (typeof raw.model === 'string') {
    defaults.model = raw.model
  }

  return Object.keys(defaults).length > 0 ? defaults : null
}

function isWorkspaceSetupState(value: unknown): value is InitialWorkspaceSetupState {
  return value === 'no-workspace' || value === 'needs-setup' || value === 'ready'
}

function isBootstrapImportSourceId(value: unknown): value is InitialWorkspaceBootstrapImportSourceId {
  return value === 'codex' || value === 'claude'
}
