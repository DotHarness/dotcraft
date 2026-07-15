import { create } from 'zustand'

export type AppConnectionState = 'notConnected' | 'connecting' | 'connected' | 'needsAuth' | 'error'
export type AppBindingState = 'connecting' | 'syncing' | 'active' | 'offline' | 'needsConfirmation' | 'revoked' | 'failed' | 'cancelled'
export type AppNativeStatus = 'installed' | 'missing' | 'unknown'
export type AppListSurface = 'pluginDetail' | 'welcome' | 'threadBinding' | 'sdk/default'
export type AppBindingKind = 'app' | 'socialChannel' | 'managedApp' | string
export type SocialBindingTargetSelection = 'confirmInChannel' | 'currentConversation' | string

export interface SocialBindingIntent {
  channelName: string
  targetSelection?: SocialBindingTargetSelection
  displayHint?: string | null
}

export interface SocialChannelBoundBy {
  platformUserId: string
  displayName?: string | null
}

export interface SocialChannelTarget {
  channelName: string
  accountId?: string | null
  conversationKind: string
  conversationId: string
  deliveryTarget: string
  displayName?: string | null
  boundBy?: SocialChannelBoundBy | null
}

export interface AppHandoffModeDescriptor {
  mode: 'url' | 'customProtocol' | string
  uriTemplate?: string | null
}

export interface ThreadAppBindingSummary {
  threadId: string
  bindingId: string
  appId: string
  bindingKind?: AppBindingKind | null
  displayName?: string | null
  state: AppBindingState | string
  connectionState?: AppConnectionState | string
  managed?: boolean
  requiresExternalConnection?: boolean
  icon?: string | null
  socialTarget?: SocialChannelTarget | null
  authorityRevision?: number
  approvedCapabilityRevision?: number
  candidateCapabilityRevision?: number | null
  approvedTools?: Array<{ namespace?: string; name?: string; [key: string]: unknown }>
  failureReason?: string | null
}

export interface AppNativeApplication {
  displayName: string
  protocol: string
  status: AppNativeStatus | string
  installUrl?: string | null
}

export interface AppInfo {
  appId: string
  displayName: string
  developerName: string
  description: string
  category?: string | null
  icon?: string | null
  pluginId: string
  installed: boolean
  enabled: boolean
  catalogVisible: boolean
  managed?: boolean
  requiresExternalConnection?: boolean
  releasePage?: string | null
  downloadUrl?: string | null
  nativeApp?: AppNativeApplication | null
  connectionState: AppConnectionState | string
  accountLabel?: string | null
  handoffModes: AppHandoffModeDescriptor[]
  bindingSummary?: ThreadAppBindingSummary | null
  diagnostics?: Array<{ severity: string; code: string; message: string; pluginId?: string | null; path?: string | null }>
}

export interface AppHandoff {
  mode: string
  uri?: string | null
  bindCode?: string | null
  instructions?: string | null
}

export interface AppConnectionStartResult {
  connectionRequestId: string
  requestToken?: string
  expiresAt: string
  handoff: AppHandoff
}

export interface AppBindingRequestCreateResult {
  bindingRequestId: string
  bindingId?: string
  threadId: string
  appId: string
  state: AppBindingState | string
  tokenExpiresAt: string
  handoff: AppHandoff
}

export interface ThreadAppBinding {
  bindingRequestId?: string | null
  bindingId: string
  threadId: string
  appId: string
  bindingKind?: AppBindingKind | null
  displayName?: string | null
  icon?: string | null
  state: AppBindingState | string
  connectionState?: AppConnectionState | string
  managed?: boolean
  requiresExternalConnection?: boolean
  socialTarget?: SocialChannelTarget | null
  authorityRevision?: number
  approvedCapabilityRevision?: number
  candidateCapabilityRevision?: number | null
  approvedTools?: Array<Record<string, unknown>>
  pendingChanges?: Array<{ kind: string; tool: string; detail: string }>
  failureReason?: string | null
}

interface AppBindingStore {
  apps: AppInfo[]
  appsThreadId: string | null
  appsSurface: AppListSurface
  appsLoading: boolean
  appsError: string | null
  bindingsByThread: Record<string, ThreadAppBinding[]>
  bindingsLoadingByThread: Record<string, boolean>
  bindingsErrorByThread: Record<string, string | null>

  fetchApps(threadId?: string | null, forceRefresh?: boolean, surface?: AppListSurface): Promise<void>
  startConnection(appId: string, handoffMode?: string | null): Promise<AppConnectionStartResult>
  revokeConnection(appId: string): Promise<void>
  createBindingRequest(params: {
    threadId: string
    appId: string
    reason?: string
    source: 'pluginDetail' | 'threadMenu' | 'welcome' | 'agentSuggestion' | 'sdk'
    bindingKind?: AppBindingKind
    socialIntent?: SocialBindingIntent
  }): Promise<AppBindingRequestCreateResult>
  fetchThreadBindings(threadId: string, includeRevoked?: boolean): Promise<void>
  refreshThreadBindings(threadId: string, bindingId?: string): Promise<void>
  cancelBindingRequest(threadId: string, bindingRequestId: string, reason?: string): Promise<void>
  revokeThreadBinding(threadId: string, bindingId: string, reason?: string): Promise<void>
  confirmCapabilities(threadId: string, bindingId: string, candidateRevision: number, decision: 'accept' | 'reject'): Promise<void>
  waitForConnection(appId: string, options?: AppBindingWaitOptions): Promise<AppInfo>
  waitForThreadBinding(params: {
    threadId: string
    appId: string
    bindingRequestId?: string | null
  }, options?: AppBindingWaitOptions): Promise<ThreadAppBinding>
  handleNotification(method: string, params: Record<string, unknown>): void
  reset(): void
}

export interface AppBindingWaitOptions {
  timeoutMs?: number
  intervalMs?: number
}

const DEFAULT_WAIT_TIMEOUT_MS = 120_000
const DEFAULT_WAIT_INTERVAL_MS = 800

const initialState = {
  apps: [] as AppInfo[],
  appsThreadId: null as string | null,
  appsSurface: 'sdk/default' as AppListSurface,
  appsLoading: false,
  appsError: null as string | null,
  bindingsByThread: {} as Record<string, ThreadAppBinding[]>,
  bindingsLoadingByThread: {} as Record<string, boolean>,
  bindingsErrorByThread: {} as Record<string, string | null>
}

export const useAppBindingStore = create<AppBindingStore>((set, get) => ({
  ...initialState,

  async fetchApps(threadId = null, forceRefresh = false, surface = 'sdk/default') {
    set({ appsLoading: true, appsError: null, appsThreadId: threadId ?? null, appsSurface: surface })
    try {
      const result = await window.api.appServer.sendRequest('app/list', {
        includeCatalog: true,
        includeDisabled: true,
        threadId: threadId || undefined,
        forceRefresh,
        surface
      }) as { apps?: AppInfo[] }
      const apps = await withNativeAppStatus((result.apps ?? []).map(normalizeAppInfo))
      set({
        apps,
        appsThreadId: threadId ?? null,
        appsLoading: false
      })
    } catch (err) {
      set({ appsError: errorMessage(err), appsLoading: false })
    }
  },

  async startConnection(appId, handoffMode = null) {
    const result = await window.api.appServer.sendRequest('app/connection/start', {
      appId,
      handoffMode: handoffMode || undefined
    }) as AppConnectionStartResult
    await get().fetchApps(get().appsThreadId, false, get().appsSurface)
    return result
  },

  async revokeConnection(appId) {
    await window.api.appServer.sendRequest('app/connection/revoke', { appId })
    await get().fetchApps(get().appsThreadId, false, get().appsSurface)
  },

  async createBindingRequest(params) {
    const result = await window.api.appServer.sendRequest(
      params.bindingKind === 'socialChannel' ? 'thread/socialBindings/request/create' : 'thread/appBindings/enable',
      params.bindingKind === 'socialChannel'
        ? { threadId: params.threadId, channelName: params.socialIntent?.channelName }
        : { threadId: params.threadId, appId: params.appId }
    ) as { bindingRequestId: string; bindingId: string; state?: string; expiresAt: string; code?: string; handoff?: AppHandoff }
    return {
      bindingRequestId: result.bindingRequestId,
      threadId: params.threadId,
      appId: params.appId,
      state: result.state ?? 'connecting',
      tokenExpiresAt: result.expiresAt,
      handoff: result.handoff ?? { mode: 'bindCode', bindCode: result.code }
    }
  },

  async fetchThreadBindings(threadId, includeRevoked = false) {
    set((state) => ({
      bindingsLoadingByThread: { ...state.bindingsLoadingByThread, [threadId]: true },
      bindingsErrorByThread: { ...state.bindingsErrorByThread, [threadId]: null }
    }))
    try {
      const result = await window.api.appServer.sendRequest('thread/appBindings/list', {
        threadId,
        includeRevoked
      }) as { bindings?: ThreadAppBinding[] }
      set((state) => ({
        bindingsByThread: {
          ...state.bindingsByThread,
          [threadId]: (result.bindings ?? []).map(normalizeThreadBinding)
        },
        bindingsLoadingByThread: { ...state.bindingsLoadingByThread, [threadId]: false }
      }))
    } catch (err) {
      set((state) => ({
        bindingsLoadingByThread: { ...state.bindingsLoadingByThread, [threadId]: false },
        bindingsErrorByThread: { ...state.bindingsErrorByThread, [threadId]: errorMessage(err) }
      }))
    }
  },

  async refreshThreadBindings(threadId, bindingId) {
    await get().fetchThreadBindings(threadId)
    if (get().appsThreadId === threadId) await get().fetchApps(threadId, false, get().appsSurface)
  },

  async cancelBindingRequest(threadId, bindingRequestId, reason) {
    await get().fetchThreadBindings(threadId)
    const binding = (get().bindingsByThread[threadId] ?? []).find(item => item.bindingRequestId === bindingRequestId)
    if (binding) await window.api.appServer.sendRequest('thread/appBindings/revoke', { threadId, bindingId: binding.bindingId, reason })
    await get().fetchThreadBindings(threadId)
    if (get().appsThreadId === threadId) await get().fetchApps(threadId, false, get().appsSurface)
  },

  async revokeThreadBinding(threadId, bindingId, reason) {
    await window.api.appServer.sendRequest('thread/appBindings/revoke', {
      threadId,
      bindingId,
      reason
    })
    await get().fetchThreadBindings(threadId, true)
    if (get().appsThreadId === threadId) await get().fetchApps(threadId, false, get().appsSurface)
  },

  async confirmCapabilities(threadId, bindingId, candidateRevision, decision) {
    await window.api.appServer.sendRequest('thread/appBindings/confirmCapabilities', {
      threadId, bindingId, candidateRevision, decision
    })
    await get().fetchThreadBindings(threadId)
  },

  async waitForConnection(appId, options = {}) {
    const { maxAttempts, intervalMs } = waitSettings(options)
    let lastState = 'notConnected'
    for (let attempt = 0; attempt < maxAttempts; attempt += 1) {
      await get().fetchApps(get().appsThreadId, true, get().appsSurface)
      const app = get().apps.find((candidate) => candidate.appId === appId)
      if (app?.connectionState === 'connected') return app
      if (app?.connectionState === 'error') {
        throw new Error(`App connection failed for ${app.displayName || appId}.`)
      }
      if (app?.connectionState) lastState = app.connectionState
      if (attempt < maxAttempts - 1) await delay(intervalMs)
    }
    throw new Error(`Timed out waiting for app connection '${appId}' to become connected. Last state: ${lastState}.`)
  },

  async waitForThreadBinding(params, options = {}) {
    const { maxAttempts, intervalMs } = waitSettings(options)
    let lastState = 'connecting'
    for (let attempt = 0; attempt < maxAttempts; attempt += 1) {
      await get().fetchThreadBindings(params.threadId)
      const bindings = get().bindingsByThread[params.threadId] ?? []
      const binding = findMatchingBinding(bindings, params.appId, params.bindingRequestId)
      if (binding != null) {
        lastState = binding.state
        if (binding.state === 'active') return binding
        if (
          binding.state === 'cancelled'
          || binding.state === 'revoked'
          || binding.state === 'failed'
        ) {
          throw new Error(`App binding '${params.appId}' ended with state ${binding.state}.`)
        }
      }
      if (attempt < maxAttempts - 1) await delay(intervalMs)
    }
    throw new Error(`Timed out waiting for app binding '${params.appId}' to become active. Last state: ${lastState}.`)
  },

  handleNotification(method, params) {
    if (method === 'app/list/updated' || method === 'app/connection/changed') {
      void get().fetchApps(get().appsThreadId, false, get().appsSurface)
      return
    }

    if (method !== 'thread/appBindings/changed') return
    const threadId = typeof params.threadId === 'string' ? params.threadId : null
    if (!threadId) return
    void get().fetchThreadBindings(threadId)
    if (get().appsThreadId === threadId) void get().fetchApps(threadId, false, get().appsSurface)
  },

  reset() {
    set(initialState)
  }
}))

function normalizeAppInfo(app: AppInfo): AppInfo {
  return {
    ...app,
    nativeApp: app.nativeApp ?? null,
    managed: app.managed === true,
    requiresExternalConnection: app.requiresExternalConnection !== false,
    handoffModes: app.handoffModes ?? [],
    diagnostics: app.diagnostics ?? []
  }
}

async function withNativeAppStatus(apps: AppInfo[]): Promise<AppInfo[]> {
  return await Promise.all(apps.map(async (app) => {
    const nativeApp = app.nativeApp
    if (app.managed === true || app.requiresExternalConnection === false) return app
    const protocol = nativeApp?.protocol?.trim()
    if (!protocol || !window.api.shell?.getProtocolHandlerName) return app
    try {
      const handlerName = await window.api.shell.getProtocolHandlerName(protocol)
      return {
        ...app,
        nativeApp: {
          ...nativeApp,
          status: handlerName ? 'installed' : 'missing'
        }
      }
    } catch {
      return app
    }
  }))
}

function normalizeThreadBinding(binding: ThreadAppBinding): ThreadAppBinding {
  return {
    ...binding,
    managed: binding.managed === true,
    requiresExternalConnection: binding.requiresExternalConnection !== false,
    approvedTools: binding.approvedTools ?? [],
    pendingChanges: binding.pendingChanges ?? []
  }
}

function waitSettings(options: AppBindingWaitOptions): { maxAttempts: number; intervalMs: number } {
  const timeoutMs = options.timeoutMs ?? DEFAULT_WAIT_TIMEOUT_MS
  const intervalMs = options.intervalMs ?? DEFAULT_WAIT_INTERVAL_MS
  const maxAttempts = Math.max(1, Math.ceil(timeoutMs / Math.max(1, intervalMs)))
  return { maxAttempts, intervalMs }
}

function delay(ms: number): Promise<void> {
  if (ms <= 0) return Promise.resolve()
  return new Promise((resolve) => window.setTimeout(resolve, ms))
}

function findMatchingBinding(
  bindings: ThreadAppBinding[],
  appId: string,
  bindingRequestId?: string | null
): ThreadAppBinding | undefined {
  const matchingRequest = bindingRequestId
    ? bindings.find((binding) => binding.bindingRequestId === bindingRequestId || binding.bindingId === bindingRequestId)
    : undefined
  if (matchingRequest != null && matchingRequest.state !== 'active') return matchingRequest
  return bindings.find((binding) => binding.appId === appId && binding.state === 'active')
    ?? matchingRequest
    ?? bindings.find((binding) => binding.appId === appId)
}

function errorMessage(err: unknown): string {
  if (err instanceof Error) return err.message
  return String(err)
}
