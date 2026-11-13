import { buildReasoningPayload, readReasoningObject, DEFAULT_REASONING_CONFIG, type ResolvedReasoningConfig } from './modelReasoning'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useConfig } from '../../stores/configStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { useModelCatalog } from '../../hooks/useModelCatalog'
import {
  useModelCatalogStore,
  type ModelCatalogItem,
  type InferenceSpeedWire,
} from '../../stores/modelCatalogStore'
import { addToast } from '../../stores/toastStore'
import { useThreadStore } from '../../stores/threadStore'
import { useProvidersStore, type ProviderSummary } from '../../stores/providersStore'
import type { Thread, ThreadConfigurationWire } from '../../types/thread'
import {
  resolveWorkspaceModelFromConfig,
  resolveWorkspaceProviderFromConfig
} from '../../utils/workspaceCoreConfig'
import type { ReasoningQuickValue } from './ModelPicker'
import { modelSettingsFromConfiguration, type TurnModelSettings } from '../../utils/turnModelSettings'
import { useT } from '../../contexts/LocaleContext'
import { applyModelCompatibility, applyPreferenceToThreadConfig, deleteCaseInsensitiveField, readThreadSpeed, readWorkspacePreference, resolveReasoningFromConfiguration, setCaseInsensitiveField } from './composerModelConfiguration'


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
  onModelChange: (model: string) => void
  onProviderChange: (providerId: string) => void
  onReasoningChange: (value: ReasoningQuickValue) => void
  onSpeedChange: (value: InferenceSpeedWire) => void
  onModelCatalogRetry: () => void
  threadStartConfig: ThreadConfigurationWire
  captureTurnSettings: () => Promise<TurnModelSettings>
}

interface UseComposerModelControlsOptions {
  activeThread?: Thread | null
  activeThreadId?: string | null
  mode?: 'thread' | 'detached'
}


export function useComposerModelControls({
  activeThread = null,
  activeThreadId = null,
  mode = 'thread'
}: UseComposerModelControlsOptions): ComposerModelControls {
  const t = useT()
  const detached = mode === 'detached'
  const connectionStatus = useConnectionStore((s) => s.status)
  const capabilities = useConnectionStore((s) => s.capabilities)
  const workspaceConfig = useConfig()
  const loadModels = useModelCatalogStore((s) => s.loadIfNeeded)
  const providerOptions = useProvidersStore((s) => s.providers)
  const [providerId, setProviderId] = useState<string>('')
  const [modelName, setModelName] = useState<string>('Default')
  const [reasoningConfig, setReasoningConfig] = useState<ResolvedReasoningConfig>(DEFAULT_REASONING_CONFIG)
  const [speedValue, setSpeedValue] = useState<InferenceSpeedWire>('standard')
  const [modelApplying, setModelApplying] = useState(false)
  const catalogRevision = useModelCatalogStore((s) => s.revision)
  const actionScope = useMemo(() => ({ activeThreadId, threadId: activeThread?.id, detached, catalogRevision }),
    [activeThreadId, activeThread?.id, detached, catalogRevision])
  const currentActionScope = useRef<typeof actionScope | null>(actionScope)
  currentActionScope.current = actionScope
  const updates = useRef(Promise.resolve())
  const selectionFailure = useRef<unknown>(null)
  useEffect(() => {
    currentActionScope.current = actionScope
    updates.current = Promise.resolve()
    selectionFailure.current = null
    setModelApplying(false)
    return () => { currentActionScope.current = null }
  }, [actionScope])
  const enqueue = (task: (isCurrent: () => boolean) => Promise<void>): void => {
    const isCurrent = (): boolean => currentActionScope.current === actionScope &&
      useModelCatalogStore.getState().revision === actionScope.catalogRevision &&
      useConnectionStore.getState().status === 'connected' &&
      (detached || useThreadStore.getState().activeThreadId === activeThreadId)
    updates.current = updates.current.then(async () => {
      if (!isCurrent()) return
      selectionFailure.current = null
      try { await task(isCurrent) }
      catch (error) { if (isCurrent()) selectionFailure.current = error }
    })
  }
  const [detachedModelTouched, setDetachedModelTouched] = useState(false)
  const [detachedReasoningTouched, setDetachedReasoningTouched] = useState(false)
  const [detachedReasoningOverride, setDetachedReasoningOverride] = useState<ResolvedReasoningConfig | null>(null)
  const [detachedSpeedTouched, setDetachedSpeedTouched] = useState(false)

  const modelApiAvailable =
    capabilities?.modelCatalogManagement === true &&
    capabilities?.workspaceConfigManagement === true &&
    connectionStatus === 'connected' &&
    (detached || Boolean(activeThreadId))
  const {
    models: modelCatalog,
    modelOptions,
    status: modelCatalogStatus,
    modelListUnsupportedEndpoint,
    errorCode: modelCatalogErrorCode,
    errorMessage: modelCatalogErrorMessage
  } = useModelCatalog(providerId, modelApiAvailable && Boolean(providerId))
  const modelLoading = modelApiAvailable && (modelCatalogStatus === 'loading' || modelCatalogStatus === 'idle')

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

  const threadConfiguration = activeThread?.configuration ?? null
  // Thread snapshots arrive on a timer with new objects, so the resolve effect compares values.
  const threadConfigurationKey = useMemo(() => JSON.stringify([
    threadConfiguration?.providerId ?? threadConfiguration?.ProviderId ?? null,
    threadConfiguration?.model ?? threadConfiguration?.Model ?? null,
    readReasoningObject(threadConfiguration?.reasoning ?? threadConfiguration?.Reasoning),
    threadConfiguration?.speed ?? threadConfiguration?.Speed ?? null,
  ]), [threadConfiguration])

  useEffect(() => {
    const workspaceCfg = workspaceConfig ?? {}
    const effectiveProviderId = resolveEffectiveProvider(activeThread, workspaceCfg)
    setProviderId(effectiveProviderId)
    if (!detached || !detachedModelTouched) {
      setModelName(resolveEffectiveModel(activeThread, workspaceCfg, effectiveProviderId))
    }
    if (!detached || !detachedReasoningTouched) {
      setReasoningConfig(resolveEffectiveReasoning(activeThread, workspaceCfg, effectiveProviderId))
    }
    if (!detached || !detachedSpeedTouched) {
      setSpeedValue(resolveEffectiveSpeed(activeThread, workspaceCfg, effectiveProviderId))
    }
  }, [
    activeThreadId,
    catalogRevision,
    threadConfigurationKey,
    detached,
    detachedModelTouched,
    detachedReasoningTouched,
    detachedSpeedTouched,
    resolveEffectiveModel,
    resolveEffectiveProvider,
    resolveEffectiveReasoning,
    resolveEffectiveSpeed,
    workspaceConfig
  ])

  const handleModelChange = useCallback(
    async (nextModel: string, isCurrent: () => boolean): Promise<void> => {
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
        })) as { thread?: { configuration?: ThreadConfigurationWire | null } }
        if (!isCurrent()) return
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
        if (!isCurrent()) return
        const active = useThreadStore.getState().activeThread
        if (active && active.id === activeThread.id) {
          useThreadStore.getState().setActiveThread({
            ...active,
            configuration: existingConfig as typeof active.configuration
          })
        }
        setModelName(nextModel)
        setReasoningConfig(resolveReasoningFromConfiguration(existingConfig))
      } catch (err) {
        if (!isCurrent()) return
        const msg = err instanceof Error ? err.message : String(err)
        addToast(`Failed to switch model: ${msg}`, 'error')
        throw err
      } finally {
        if (isCurrent()) setModelApplying(false)
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

  const handleProviderChange = useCallback(async (nextProviderId: string, isCurrent: () => boolean): Promise<void> => {
    if (!nextProviderId || nextProviderId === providerId || detached || !activeThread) return
    setModelApplying(true)
    try {
      const workspaceCfg = workspaceConfig ?? {}
      const catalogState = await loadModels(false, nextProviderId)
      if (!isCurrent() || !catalogState) return
      const remembered = readWorkspacePreference(workspaceCfg, nextProviderId)
      const nextModel = remembered?.model || catalogState.modelOptions[0]
      if (!nextModel) {
        const error = new Error(t('composer.providerModelUnavailable'))
        selectionFailure.current = error
        addToast(error.message, 'error')
        return
      }

      const readRes = await window.api.appServer.sendRequest('thread/read', {
        threadId: activeThread.id,
      }) as { thread?: { configuration?: ThreadConfigurationWire | null } }
      if (!isCurrent()) return
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
      if (!isCurrent()) return
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
    } catch (err) {
      if (!isCurrent()) return
      await loadModels(false, providerId)
      if (!isCurrent()) return
      addToast(`Failed to switch provider: ${err instanceof Error ? err.message : String(err)}`, 'error')
      throw err
    } finally {
      if (isCurrent()) setModelApplying(false)
    }
  }, [activeThread, detached, loadModels, providerId, setCaseInsensitiveField, t, workspaceConfig])

  const handleReasoningChange = useCallback(
    async (nextReasoning: ReasoningQuickValue, isCurrent: () => boolean): Promise<void> => {
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
        })) as { thread?: { configuration?: ThreadConfigurationWire | null } }
        if (!isCurrent()) return
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
        if (!isCurrent()) return
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
      } catch (err) {
        if (!isCurrent()) return
        const msg = err instanceof Error ? err.message : String(err)
        setReasoningConfig(previousReasoning)
        addToast(`Failed to update thinking: ${msg}`, 'error')
        throw err
      } finally {
        if (isCurrent()) setModelApplying(false)
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
    async (nextSpeed: InferenceSpeedWire, isCurrent: () => boolean): Promise<void> => {
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
        }) as { thread?: { configuration?: ThreadConfigurationWire | null } }
        if (!isCurrent()) return
        const existingConfig = readRes.thread?.configuration && typeof readRes.thread.configuration === 'object'
          ? { ...(readRes.thread.configuration as Record<string, unknown>) }
          : {}
        setCaseInsensitiveField(existingConfig, 'speed', nextSpeed)
        await window.api.appServer.sendRequest('thread/config/update', {
          threadId: activeThread.id,
          config: existingConfig
        })
        if (!isCurrent()) return
        const active = useThreadStore.getState().activeThread
        if (active?.id === activeThread.id) {
          useThreadStore.getState().setActiveThread({
            ...active,
            configuration: { ...(active.configuration ?? {}), speed: nextSpeed }
          })
        }
      } catch (err) {
        if (!isCurrent()) return
        setSpeedValue(previousSpeed)
        addToast(`Failed to update speed: ${err instanceof Error ? err.message : String(err)}`, 'error')
        throw err
      } finally {
        if (isCurrent()) setModelApplying(false)
      }
    },
    [activeThread, detached, setCaseInsensitiveField, speedValue]
  )

  const reasoningValue: ReasoningQuickValue =
    detached && detachedReasoningTouched && detachedReasoningOverride == null
      ? 'default'
      : reasoningConfig.enabled
        ? reasoningConfig.effort
        : 'off'

  const threadStartConfig = useMemo<ThreadConfigurationWire>(() => {
    if (!detached) return {}
    const config: ThreadConfigurationWire = {}
    if (providerId) config.providerId = providerId
    if (modelName && modelName !== 'Default') config.model = modelName
    if (detachedReasoningTouched && detachedReasoningOverride != null) {
      config.reasoning = detachedReasoningOverride
    }
    if (detachedSpeedTouched) config.speed = speedValue
    return config
  }, [
    detached,
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
    onModelChange: (model) => {
      enqueue((isCurrent) => handleModelChange(model, isCurrent))
    },
    onProviderChange: (nextProviderId) => {
      enqueue((isCurrent) => handleProviderChange(nextProviderId, isCurrent))
    },
    onReasoningChange: (reasoning) => {
      enqueue((isCurrent) => handleReasoningChange(reasoning, isCurrent))
    },
    onSpeedChange: (speed) => {
      enqueue((isCurrent) => handleSpeedChange(speed, isCurrent))
    },
    onModelCatalogRetry: () => {
      void loadModels(true, providerId)
    },
    threadStartConfig,
    captureTurnSettings: async () => {
      await updates.current
      const current = useThreadStore.getState().activeThread
      if (currentActionScope.current !== actionScope || selectionFailure.current ||
        (!detached && current?.id !== activeThread?.id)) {
        throw new DOMException('Model selection was interrupted', 'AbortError')
      }
      if (detached) return modelSettingsFromConfiguration(threadStartConfig)
      return modelSettingsFromConfiguration(current?.configuration)
    }
  }
}
