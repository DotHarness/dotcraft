import { useCallback, useEffect, useMemo, useState } from 'react'
import { useConnectionStore } from '../../stores/connectionStore'
import {
  useModelCatalogStore,
  type ModelCatalogItem,
  type InferenceSpeedWire,
  type ReasoningEffortWire,
  type ReasoningOutputWire
} from '../../stores/modelCatalogStore'
import { addToast } from '../../stores/toastStore'
import { useThreadStore } from '../../stores/threadStore'
import { useConversationStore } from '../../stores/conversationStore'
import { useProvidersStore, type ProviderSummary } from '../../stores/providersStore'
import type { Thread, ThreadConfigurationWire, ContextWindowMode } from '../../types/thread'
import type { WorkspaceConfigChangedPayload } from '../../utils/workspaceConfigChanged'
import { parseJsonConfig } from '../../../shared/jsonConfig'
import {
  configObjectFromWorkspaceCore,
  resolveWorkspaceModelFromConfig,
  resolveWorkspaceProviderFromConfig,
  type WorkspaceCoreConfigLike
} from '../../utils/workspaceCoreConfig'
import type { ReasoningQuickValue } from './ModelPicker'
import { useT } from '../../contexts/LocaleContext'
import {
  findProviderPreference,
  readProviderPreferences,
  type ModelPreference
} from '../../../shared/modelPreference'

export interface ResolvedReasoningConfig {
  enabled: boolean
  effort: ReasoningEffortWire
  output: ReasoningOutputWire
}

export interface ComposerModelControls {
  providerId: string
  providerOptions: ProviderSummary[]
  modelName: string
  modelOptions: string[]
  modelCatalog: ModelCatalogItem[]
  reasoningValue: ReasoningQuickValue
  speedValue: InferenceSpeedWire
  modelLoading: boolean
  modelDisabled: boolean
  modelListUnsupportedEndpoint: boolean
  modelCatalogError: boolean
  modelCatalogErrorMessage: string | null
  /** Effective per-thread context-window mode (MAX vs default). */
  contextMode: ContextWindowMode
  /** Whether the effective model's catalog entry advertises MAX support. */
  contextSupportsMax: boolean
  /** Thread wants MAX but the effective model no longer supports it (catalog resolved). */
  contextDegraded: boolean
  /** Effective model's default compaction window, used for degraded copy. */
  contextConfiguredWindow: number
  onModelChange: (model: string) => void
  onProviderChange: (providerId: string) => void
  onReasoningChange: (value: ReasoningQuickValue) => void
  onSpeedChange: (value: InferenceSpeedWire) => void
  onContextModeChange: (mode: ContextWindowMode) => void
  onModelCatalogRetry: () => void
  threadStartConfig: ThreadConfigurationWire
}

interface UseComposerModelControlsOptions {
  workspacePath: string
  remoteWorkspace?: boolean
  activeThread?: Thread | null
  activeThreadId?: string | null
  workspaceConfigChange?: WorkspaceConfigChangedPayload | null
  workspaceConfigChangeSeq?: number
  mode?: 'thread' | 'detached'
}

export const DEFAULT_REASONING_CONFIG: ResolvedReasoningConfig = {
  enabled: false,
  effort: 'medium',
  output: 'full'
}

export function useComposerModelControls({
  workspacePath,
  remoteWorkspace = false,
  activeThread = null,
  activeThreadId = null,
  workspaceConfigChange = null,
  workspaceConfigChangeSeq = 0,
  mode = 'thread'
}: UseComposerModelControlsOptions): ComposerModelControls {
  const t = useT()
  const detached = mode === 'detached'
  const connectionStatus = useConnectionStore((s) => s.status)
  const capabilities = useConnectionStore((s) => s.capabilities)
  const modelCatalog = useModelCatalogStore((s) => s.models)
  const modelOptions = useModelCatalogStore((s) => s.modelOptions)
  const modelCatalogStatus = useModelCatalogStore((s) => s.status)
  const modelListUnsupportedEndpoint = useModelCatalogStore((s) => s.modelListUnsupportedEndpoint)
  const modelCatalogErrorCode = useModelCatalogStore((s) => s.errorCode)
  const modelCatalogErrorMessage = useModelCatalogStore((s) => s.errorMessage)
  const loadModels = useModelCatalogStore((s) => s.loadIfNeeded)
  const providerOptions = useProvidersStore((s) => s.providers)
  const reloadProviders = useProvidersStore((s) => s.reload)
  const [providerId, setProviderId] = useState<string>('')
  const [modelName, setModelName] = useState<string>('Default')
  const [reasoningConfig, setReasoningConfig] = useState<ResolvedReasoningConfig>(DEFAULT_REASONING_CONFIG)
  const [speedValue, setSpeedValue] = useState<InferenceSpeedWire>('standard')
  const [contextMode, setContextMode] = useState<ContextWindowMode>('default')
  const [modelApplying, setModelApplying] = useState(false)
  const [detachedModelTouched, setDetachedModelTouched] = useState(false)
  const [detachedReasoningTouched, setDetachedReasoningTouched] = useState(false)
  const [detachedReasoningOverride, setDetachedReasoningOverride] = useState<ResolvedReasoningConfig | null>(null)
  const [detachedSpeedTouched, setDetachedSpeedTouched] = useState(false)
  const [detachedContextTouched, setDetachedContextTouched] = useState(false)

  const modelApiAvailable =
    capabilities?.modelCatalogManagement === true &&
    capabilities?.workspaceConfigManagement === true &&
    connectionStatus === 'connected' &&
    (detached || Boolean(activeThreadId))
  const modelLoading = modelApiAvailable && modelCatalogStatus === 'loading'

  const workspaceConfigPath = useMemo(() => {
    if (!workspacePath) return ''
    const normalized = workspacePath.replace(/[\\/]+$/, '')
    const sep = normalized.includes('\\') ? '\\' : '/'
    return `${normalized}${sep}.craft${sep}config.json`
  }, [workspacePath])

  const readWorkspaceConfig = useCallback(async (): Promise<Record<string, unknown>> => {
    if (remoteWorkspace) {
      const getCore = window.api.workspaceConfig?.getCore
      if (typeof getCore !== 'function') return {}
      return configObjectFromWorkspaceCore(await getCore() as WorkspaceCoreConfigLike)
    }
    if (!workspaceConfigPath) return {}
    const readFile = window.api.file?.readFile
    if (typeof readFile !== 'function') return {}
    const raw = await readFile(workspaceConfigPath)
    return parseJsonConfig<Record<string, unknown>>(raw, {})
  }, [remoteWorkspace, workspaceConfigPath])

  const setCaseInsensitiveField = useCallback(
    (target: Record<string, unknown>, key: string, value: unknown): void => {
      const lower = key.toLowerCase()
      const existingKey = Object.keys(target).find((k) => k.toLowerCase() === lower)
      if (existingKey) {
        target[existingKey] = value
      } else {
        target[key] = value
      }
    },
    []
  )

  const deleteCaseInsensitiveField = useCallback((target: Record<string, unknown>, key: string): void => {
    const lower = key.toLowerCase()
    const existingKey = Object.keys(target).find((k) => k.toLowerCase() === lower)
    if (existingKey) delete target[existingKey]
  }, [])

  const resolveEffectiveModel = useCallback(
    (thread: Thread | null, workspaceCfg: Record<string, unknown>, effectiveProviderId: string): string => {
      const threadRaw = thread?.configuration?.model ?? thread?.configuration?.Model
      return resolveWorkspaceModelFromConfig(workspaceCfg, effectiveProviderId, threadRaw)
    },
    []
  )

  const resolveEffectiveReasoning = useCallback(
    (thread: Thread | null, workspaceCfg: Record<string, unknown>, effectiveProviderId: string): ResolvedReasoningConfig => {
      const threadReasoning = readReasoningObject(thread?.configuration?.reasoning ?? thread?.configuration?.Reasoning)
      if (threadReasoning) return threadReasoning
      return readWorkspacePreference(workspaceCfg, effectiveProviderId)?.reasoning ?? DEFAULT_REASONING_CONFIG
    },
    []
  )

  const resolveEffectiveProvider = useCallback(
    (thread: Thread | null, workspaceCfg: Record<string, unknown>): string => {
      const threadRaw = thread?.configuration?.providerId ?? thread?.configuration?.ProviderId
      const threadProvider = typeof threadRaw === 'string' ? threadRaw.trim() : ''
      if (threadProvider) return threadProvider
      return resolveWorkspaceProviderFromConfig(workspaceCfg)
    },
    []
  )

  const resolveEffectiveSpeed = useCallback(
    (thread: Thread | null, workspaceCfg: Record<string, unknown>, effectiveProviderId: string): InferenceSpeedWire => {
      const raw = thread?.configuration?.speed
        ?? thread?.configuration?.Speed
        ?? readWorkspacePreference(workspaceCfg, effectiveProviderId)?.speed
      return typeof raw === 'string' && raw.toLowerCase() === 'fast' ? 'fast' : 'standard'
    },
    []
  )

  // Context-window mode is read from the thread's captured configuration only. New
  // threads already capture the workspace default at creation (see spec §4), so the
  // composer does not need to read the workspace default separately.
  const resolveEffectiveContextMode = useCallback((
    thread: Thread | null,
    workspaceCfg: Record<string, unknown>,
    effectiveProviderId: string
  ): ContextWindowMode => {
    const raw = thread?.configuration?.contextWindow ?? thread?.configuration?.ContextWindow
    if (!raw || typeof raw !== 'object') {
      return readWorkspacePreference(workspaceCfg, effectiveProviderId)?.contextWindow.mode ?? 'default'
    }
    const modeRaw = (raw as { mode?: unknown; Mode?: unknown }).mode ?? (raw as { Mode?: unknown }).Mode
    return modeRaw === 'max' ? 'max' : 'default'
  }, [])

  useEffect(() => {
    if (!modelApiAvailable) return
    void reloadProviders()
  }, [modelApiAvailable, reloadProviders])

  useEffect(() => {
    let disposed = false
    const loadEffectiveModel = async (): Promise<void> => {
      try {
        const workspaceCfg = await readWorkspaceConfig()
        if (disposed) return
        const effectiveProviderId = resolveEffectiveProvider(activeThread, workspaceCfg)
        setProviderId(effectiveProviderId)
        if (effectiveProviderId) void loadModels(false, effectiveProviderId)
        if (!detached || !detachedModelTouched) {
          setModelName(resolveEffectiveModel(activeThread, workspaceCfg, effectiveProviderId))
        }
        if (!detached || !detachedReasoningTouched) {
          setReasoningConfig(resolveEffectiveReasoning(activeThread, workspaceCfg, effectiveProviderId))
        }
        if (!detached || !detachedSpeedTouched) {
          setSpeedValue(resolveEffectiveSpeed(activeThread, workspaceCfg, effectiveProviderId))
        }
        if (!detached || !detachedContextTouched) {
          setContextMode(resolveEffectiveContextMode(activeThread, workspaceCfg, effectiveProviderId))
        }
      } catch {
        if (disposed) return
        if (!detached || !detachedModelTouched) {
          const modelFromThread = activeThread?.configuration?.model ?? activeThread?.configuration?.Model
          const mt = typeof modelFromThread === 'string' ? modelFromThread.trim() : ''
          setModelName(mt.length > 0 && mt !== 'Default' ? mt : 'Default')
        }
        const providerFromThread = activeThread?.configuration?.providerId ?? activeThread?.configuration?.ProviderId
        setProviderId(typeof providerFromThread === 'string' ? providerFromThread.trim() : '')
        if (!detached || !detachedReasoningTouched) {
          setReasoningConfig(
            readReasoningObject(activeThread?.configuration?.reasoning ?? activeThread?.configuration?.Reasoning)
              ?? DEFAULT_REASONING_CONFIG
          )
        }
        if (!detached || !detachedSpeedTouched) {
          setSpeedValue(resolveEffectiveSpeed(activeThread, {}, ''))
        }
        if (!detached || !detachedContextTouched) {
          setContextMode(resolveEffectiveContextMode(activeThread, {}, ''))
        }
      }
    }

    void loadEffectiveModel()
    return () => {
      disposed = true
    }
  }, [
    activeThreadId,
    activeThread?.configuration?.Model,
    activeThread?.configuration?.model,
    activeThread?.configuration?.ProviderId,
    activeThread?.configuration?.providerId,
    activeThread?.configuration?.Reasoning,
    activeThread?.configuration?.reasoning,
    activeThread?.configuration?.speed,
    activeThread?.configuration?.Speed,
    activeThread?.configuration?.contextWindow,
    activeThread?.configuration?.ContextWindow,
    detached,
    detachedModelTouched,
    detachedReasoningTouched,
    detachedSpeedTouched,
    detachedContextTouched,
    readWorkspaceConfig,
    resolveEffectiveModel,
    resolveEffectiveProvider,
    resolveEffectiveReasoning,
    resolveEffectiveSpeed,
    resolveEffectiveContextMode,
    workspaceConfigChange,
    workspaceConfigChangeSeq
  ])

  const handleModelChange = useCallback(
    async (nextModel: string): Promise<void> => {
      if (!nextModel || nextModel === 'Default' || nextModel === modelName) return
      if (detached) {
        setDetachedModelTouched(true)
        setModelName(nextModel)
        return
      }
      if (!activeThread) return

      setModelApplying(true)
      try {
        const readRes = (await window.api.appServer.sendRequest('thread/read', {
          threadId: activeThread.id,
          includeTurns: false
        })) as { thread?: { configuration?: ThreadConfigurationWire | null } }
        const existingConfig =
          readRes.thread?.configuration && typeof readRes.thread.configuration === 'object'
            ? { ...(readRes.thread.configuration as Record<string, unknown>) }
            : {}
        setCaseInsensitiveField(existingConfig, 'providerId', providerId)
        setCaseInsensitiveField(existingConfig, 'model', nextModel)
        applyModelCompatibility(existingConfig, modelCatalog.find((item) => item.id === nextModel) ?? null)

        await window.api.appServer.sendRequest('thread/config/update', {
          threadId: activeThread.id,
          config: existingConfig
        })
        const active = useThreadStore.getState().activeThread
        if (active && active.id === activeThread.id) {
          useThreadStore.getState().setActiveThread({
            ...active,
            configuration: existingConfig as typeof active.configuration
          })
        }
        setModelName(nextModel)
        setReasoningConfig(resolveReasoningFromConfiguration(existingConfig))
        setContextMode(resolveContextFromConfiguration(existingConfig))
        addToast(`Model switched to ${nextModel}`, 'success')
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err)
        addToast(`Failed to switch model: ${msg}`, 'error')
      } finally {
        setModelApplying(false)
      }
    },
    [
      activeThread,
      detached,
      modelCatalog,
      modelName,
      providerId,
      resolveEffectiveReasoning,
      setCaseInsensitiveField
    ]
  )

  const handleProviderChange = useCallback(async (nextProviderId: string): Promise<void> => {
    if (!nextProviderId || nextProviderId === providerId || detached || !activeThread) return
    setModelApplying(true)
    try {
      const workspaceCfg = await readWorkspaceConfig()
      await loadModels(true, nextProviderId)
      const catalogState = useModelCatalogStore.getState()
      const remembered = readWorkspacePreference(workspaceCfg, nextProviderId)
      const nextModel = remembered?.model || catalogState.modelOptions[0]
      if (!nextModel) {
        addToast(t('composer.providerModelUnavailable'), 'error')
        return
      }

      const readRes = await window.api.appServer.sendRequest('thread/read', {
        threadId: activeThread.id,
        includeTurns: false
      }) as { thread?: { configuration?: ThreadConfigurationWire | null } }
      const existingConfig = readRes.thread?.configuration && typeof readRes.thread.configuration === 'object'
        ? { ...(readRes.thread.configuration as Record<string, unknown>) }
        : {}
      setCaseInsensitiveField(existingConfig, 'providerId', nextProviderId)
      if (remembered) {
        applyPreferenceToThreadConfig(existingConfig, remembered)
      } else {
        setCaseInsensitiveField(existingConfig, 'model', nextModel)
      }
      applyModelCompatibility(existingConfig, catalogState.models.find((item) => item.id === nextModel) ?? null)
      await window.api.appServer.sendRequest('thread/config/update', {
        threadId: activeThread.id,
        config: existingConfig
      })
      const active = useThreadStore.getState().activeThread
      if (active?.id === activeThread.id) {
        useThreadStore.getState().setActiveThread({
          ...active,
          configuration: existingConfig as typeof active.configuration
        })
      }
      setProviderId(nextProviderId)
      setModelName(nextModel)
      setReasoningConfig(resolveReasoningFromConfiguration(existingConfig))
      setSpeedValue(readThreadSpeed(existingConfig))
      setContextMode(resolveContextFromConfiguration(existingConfig))
      addToast(`Provider switched to ${nextProviderId}`, 'success')
    } catch (err) {
      await loadModels(true, providerId)
      addToast(`Failed to switch provider: ${err instanceof Error ? err.message : String(err)}`, 'error')
    } finally {
      setModelApplying(false)
    }
  }, [activeThread, detached, loadModels, providerId, readWorkspaceConfig, setCaseInsensitiveField, t])

  const handleReasoningChange = useCallback(
    async (nextReasoning: ReasoningQuickValue): Promise<void> => {
      const nextPayload = buildReasoningPayload(nextReasoning, reasoningConfig)
      if (detached) {
        setDetachedReasoningTouched(true)
        setDetachedReasoningOverride(nextPayload)
        setReasoningConfig(nextPayload ?? DEFAULT_REASONING_CONFIG)
        return
      }
      if (!activeThread) return

      setModelApplying(true)
      const previousReasoning = reasoningConfig
      setReasoningConfig(nextPayload ?? DEFAULT_REASONING_CONFIG)
      try {
        const readRes = (await window.api.appServer.sendRequest('thread/read', {
          threadId: activeThread.id,
          includeTurns: false
        })) as { thread?: { configuration?: ThreadConfigurationWire | null } }
        const existingConfig =
          readRes.thread?.configuration && typeof readRes.thread.configuration === 'object'
            ? { ...(readRes.thread.configuration as Record<string, unknown>) }
            : {}
        if (nextReasoning === 'default') {
          deleteCaseInsensitiveField(existingConfig, 'reasoning')
        } else {
          setCaseInsensitiveField(existingConfig, 'reasoning', nextPayload)
        }

        await window.api.appServer.sendRequest('thread/config/update', {
          threadId: activeThread.id,
          config: existingConfig
        })
        const active = useThreadStore.getState().activeThread
        if (active && active.id === activeThread.id) {
          const mergedCfg: Record<string, unknown> = { ...(active.configuration ?? {}) }
          if (nextReasoning === 'default') {
            deleteCaseInsensitiveField(mergedCfg, 'reasoning')
          } else {
            mergedCfg.reasoning = nextPayload
          }
          useThreadStore.getState().setActiveThread({
            ...active,
            configuration: mergedCfg as typeof active.configuration
          })
        }
        addToast(
          nextReasoning === 'default'
            ? 'Using default thinking setting'
            : `Thinking set to ${reasoningQuickToastLabel(nextReasoning)}`,
          'success'
        )
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err)
        setReasoningConfig(previousReasoning)
        addToast(`Failed to update thinking: ${msg}`, 'error')
      } finally {
        setModelApplying(false)
      }
    },
    [
      activeThread,
      deleteCaseInsensitiveField,
      detached,
      reasoningConfig,
      setCaseInsensitiveField
    ]
  )

  const handleSpeedChange = useCallback(
    async (nextSpeed: InferenceSpeedWire): Promise<void> => {
      if (nextSpeed === speedValue) return
      if (detached) {
        setDetachedSpeedTouched(true)
        setSpeedValue(nextSpeed)
        return
      }
      if (!activeThread) return

      setModelApplying(true)
      const previousSpeed = speedValue
      setSpeedValue(nextSpeed)
      try {
        const readRes = await window.api.appServer.sendRequest('thread/read', {
          threadId: activeThread.id,
          includeTurns: false
        }) as { thread?: { configuration?: ThreadConfigurationWire | null } }
        const existingConfig = readRes.thread?.configuration && typeof readRes.thread.configuration === 'object'
          ? { ...(readRes.thread.configuration as Record<string, unknown>) }
          : {}
        setCaseInsensitiveField(existingConfig, 'speed', nextSpeed)
        await window.api.appServer.sendRequest('thread/config/update', {
          threadId: activeThread.id,
          config: existingConfig
        })
        const active = useThreadStore.getState().activeThread
        if (active?.id === activeThread.id) {
          useThreadStore.getState().setActiveThread({
            ...active,
            configuration: { ...(active.configuration ?? {}), speed: nextSpeed }
          })
        }
        addToast(nextSpeed === 'fast' ? 'Fast speed enabled' : 'Standard speed enabled', 'success')
      } catch (err) {
        setSpeedValue(previousSpeed)
        addToast(`Failed to update speed: ${err instanceof Error ? err.message : String(err)}`, 'error')
      } finally {
        setModelApplying(false)
      }
    },
    [activeThread, detached, setCaseInsensitiveField, speedValue]
  )

  // MAX context is a per-thread override only. Unlike model/reasoning, it does NOT
  // dual-write the workspace default (Settings owns that), so toggling MAX here never
  // changes other or new threads. The server validates `max` and may reject it.
  const handleContextModeChange = useCallback(
    async (nextMode: ContextWindowMode): Promise<void> => {
      if (nextMode === contextMode) return
      if (detached) {
        setDetachedContextTouched(true)
        setContextMode(nextMode)
        return
      }
      if (!activeThread) return

      setModelApplying(true)
      const previousMode = contextMode
      setContextMode(nextMode)
      try {
        const readRes = (await window.api.appServer.sendRequest('thread/read', {
          threadId: activeThread.id,
          includeTurns: false
        })) as { thread?: { configuration?: ThreadConfigurationWire | null } }
        const existingConfig =
          readRes.thread?.configuration && typeof readRes.thread.configuration === 'object'
            ? { ...(readRes.thread.configuration as Record<string, unknown>) }
            : {}
        if (nextMode === 'max') {
          setCaseInsensitiveField(existingConfig, 'contextWindow', { mode: 'max' })
        } else {
          deleteCaseInsensitiveField(existingConfig, 'contextWindow')
        }

        await window.api.appServer.sendRequest('thread/config/update', {
          threadId: activeThread.id,
          config: existingConfig
        })

        const active = useThreadStore.getState().activeThread
        if (active && active.id === activeThread.id) {
          const mergedCfg: Record<string, unknown> = { ...(active.configuration ?? {}) }
          if (nextMode === 'max') {
            deleteCaseInsensitiveField(mergedCfg, 'contextWindow')
            mergedCfg.contextWindow = { mode: 'max' }
          } else {
            deleteCaseInsensitiveField(mergedCfg, 'contextWindow')
          }
          useThreadStore.getState().setActiveThread({
            ...active,
            configuration: mergedCfg as typeof active.configuration
          })
        }

        // MAX changes the effective context window (the ring denominator), but the
        // thread/updated broadcast does not refresh contextUsage. Re-read to update it.
        try {
          const refreshed = (await window.api.appServer.sendRequest('thread/read', {
            threadId: activeThread.id,
            includeTurns: false
          })) as { thread?: { contextUsage?: unknown } }
          const usage = refreshed.thread?.contextUsage
          if (usage && useThreadStore.getState().activeThread?.id === activeThread.id) {
            useConversationStore.getState().setContextUsage(usage as never)
          }
        } catch {
          // Non-fatal: the ring will refresh on the next usage delta.
        }

        addToast(nextMode === 'max' ? 'MAX context on for this thread' : 'MAX context off', 'success')
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err)
        setContextMode(previousMode)
        addToast(
          nextMode === 'max' ? `Couldn't enable MAX context: ${msg}` : `Failed to update MAX context: ${msg}`,
          'error'
        )
      } finally {
        setModelApplying(false)
      }
    },
    [activeThread, contextMode, deleteCaseInsensitiveField, detached, setCaseInsensitiveField]
  )

  const reasoningValue: ReasoningQuickValue =
    detached && detachedReasoningTouched && detachedReasoningOverride == null
      ? 'default'
      : reasoningConfig.enabled
        ? reasoningConfig.effort
        : 'off'

  const activeCatalogItem = modelCatalog.find((item) => item.id === modelName)
  const contextSupportsMax = activeCatalogItem?.contextWindow?.supportsMax === true
  const contextConfiguredWindow = activeCatalogItem?.contextWindow?.configuredWindow ?? 0
  // Only flag degraded once the catalog is resolved, so we do not false-alarm while it loads.
  const contextDegraded = contextMode === 'max' && modelCatalogStatus === 'ready' && !contextSupportsMax

  const threadStartConfig = useMemo<ThreadConfigurationWire>(() => {
    if (!detached) return {}
    const config: ThreadConfigurationWire = {}
    if (providerId) config.providerId = providerId
    if (modelName && modelName !== 'Default') config.model = modelName
    if (detachedReasoningTouched && detachedReasoningOverride != null) {
      config.reasoning = detachedReasoningOverride
    }
    if (detachedSpeedTouched) config.speed = speedValue
    if (detachedContextTouched && contextMode === 'max') {
      config.contextWindow = { mode: 'max' }
    }
    return config
  }, [
    contextMode,
    detached,
    detachedContextTouched,
    detachedReasoningOverride,
    detachedReasoningTouched,
    detachedSpeedTouched,
    modelName,
    providerId,
    speedValue
  ])

  return {
    providerId,
    providerOptions,
    modelName,
    modelOptions,
    modelCatalog,
    reasoningValue,
    speedValue,
    modelLoading,
    modelDisabled: modelApplying || !modelApiAvailable,
    modelListUnsupportedEndpoint,
    modelCatalogError: modelCatalogStatus === 'error',
    modelCatalogErrorMessage:
      modelCatalogStatus === 'error' && modelCatalogErrorCode
        ? `${modelCatalogErrorCode}: ${modelCatalogErrorMessage ?? ''}`.trim()
        : modelCatalogErrorMessage,
    contextMode,
    contextSupportsMax,
    contextDegraded,
    contextConfiguredWindow,
    onModelChange: (model) => {
      void handleModelChange(model)
    },
    onProviderChange: (nextProviderId) => {
      void handleProviderChange(nextProviderId)
    },
    onReasoningChange: (reasoning) => {
      void handleReasoningChange(reasoning)
    },
    onSpeedChange: (speed) => {
      void handleSpeedChange(speed)
    },
    onContextModeChange: (nextMode) => {
      void handleContextModeChange(nextMode)
    },
    onModelCatalogRetry: () => {
      void loadModels(true, providerId)
    },
    threadStartConfig
  }
}

function buildReasoningPayload(
  value: ReasoningQuickValue,
  current: ResolvedReasoningConfig
): ResolvedReasoningConfig | null {
  if (value === 'default') return null
  if (value === 'off') {
    return {
      enabled: false,
      effort: current.effort || 'medium',
      output: current.output || 'full'
    }
  }
  return {
    enabled: true,
    effort: value,
    output: current.output || 'full'
  }
}

function readReasoningObject(value: unknown): ResolvedReasoningConfig | null {
  if (!value || typeof value !== 'object') return null
  const obj = value as Record<string, unknown>
  const enabledRaw = obj.enabled ?? obj.Enabled
  const effort = normalizeReasoningEffort(obj.effort ?? obj.Effort)
  const output = normalizeReasoningOutput(obj.output ?? obj.Output)
  return {
    enabled: typeof enabledRaw === 'boolean' ? enabledRaw : false,
    effort: effort ?? 'medium',
    output: output ?? 'full'
  }
}

function normalizeReasoningEffort(value: unknown): ReasoningEffortWire | null {
  if (typeof value !== 'string') return null
  const normalized = value.replace(/[-_\s]/g, '').toLowerCase()
  if (normalized === 'low') return 'low'
  if (normalized === 'medium') return 'medium'
  if (normalized === 'high') return 'high'
  if (normalized === 'extrahigh') return 'extraHigh'
  return null
}

function normalizeReasoningOutput(value: unknown): ReasoningOutputWire | null {
  if (typeof value !== 'string') return null
  const normalized = value.trim().toLowerCase()
  if (normalized === 'none') return 'none'
  if (normalized === 'summary') return 'summary'
  if (normalized === 'full') return 'full'
  return null
}

function reasoningQuickToastLabel(value: ReasoningQuickValue): string {
  if (value === 'off') return 'Off'
  if (value === 'low') return 'Low'
  if (value === 'medium') return 'Medium'
  if (value === 'high') return 'High'
  if (value === 'extraHigh') return 'Extra High'
  return 'Default'
}

function readWorkspacePreference(
  config: Record<string, unknown>,
  providerId: string
): ModelPreference | null {
  const key = Object.keys(config).find((candidate) => candidate.toLowerCase() === 'providerpreferences')
  return findProviderPreference(readProviderPreferences(key ? config[key] : null), providerId)
}

function applyPreferenceToThreadConfig(
  config: Record<string, unknown>,
  preference: ModelPreference
): void {
  config.model = preference.model
  config.reasoning = { ...preference.reasoning }
  config.speed = preference.speed
  config.contextWindow = { ...preference.contextWindow }
}

function readThreadSpeed(config: Record<string, unknown>): InferenceSpeedWire {
  const key = Object.keys(config).find((candidate) => candidate.toLowerCase() === 'speed')
  return key && config[key] === 'fast' ? 'fast' : 'standard'
}

function applyModelCompatibility(config: Record<string, unknown>, model: ModelCatalogItem | null): void {
  const reasoningKey = Object.keys(config).find((key) => key.toLowerCase() === 'reasoning')
  if (reasoningKey && config[reasoningKey] && typeof config[reasoningKey] === 'object' && model?.reasoning) {
    const current = readReasoningObject(config[reasoningKey]) ?? DEFAULT_REASONING_CONFIG
    const effortSupported = model.reasoning.supportedEfforts.some((option) => option.effort === current.effort)
    if ((!current.enabled && !model.reasoning.supportsDisable) || (current.enabled && !effortSupported)) {
      config[reasoningKey] = {
        enabled: true,
        effort: model.reasoning.defaultEffort,
        output: model.reasoning.supportedOutputs.includes(current.output)
          ? current.output
          : model.reasoning.defaultOutput
      }
    }
  }

  if (model?.contextWindow?.supportsMax !== true) {
    const contextKey = Object.keys(config).find((key) => key.toLowerCase() === 'contextwindow')
    if (contextKey) delete config[contextKey]
  }
}

function resolveReasoningFromConfiguration(config: Record<string, unknown>): ResolvedReasoningConfig {
  const key = Object.keys(config).find((candidate) => candidate.toLowerCase() === 'reasoning')
  return readReasoningObject(key ? config[key] : null) ?? DEFAULT_REASONING_CONFIG
}

function resolveContextFromConfiguration(config: Record<string, unknown>): ContextWindowMode {
  const key = Object.keys(config).find((candidate) => candidate.toLowerCase() === 'contextwindow')
  const raw = key ? config[key] : null
  if (!raw || typeof raw !== 'object') return 'default'
  const record = raw as Record<string, unknown>
  return (record.mode ?? record.Mode) === 'max' ? 'max' : 'default'
}
