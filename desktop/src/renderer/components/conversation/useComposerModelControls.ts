import { buildReasoningPayload, readReasoningObject, DEFAULT_REASONING_CONFIG, type ResolvedReasoningConfig } from './modelReasoning'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useConfig } from '../../stores/configStore'
import { useConnectionStore } from '../../stores/connectionStore'
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
import { useT } from '../../contexts/LocaleContext'
import {
  findProviderPreference,
  readProviderPreferences,
  type ModelPreference
} from '../../../shared/modelPreference'


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
  const [modelApplying, setModelApplying] = useState(false)
  // Every change reads the thread configuration before writing it back, so changes apply one after another.
  const updates = useRef(Promise.resolve())
  const enqueue = (task: () => Promise<void>): void => {
    updates.current = updates.current.then(task)
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
  const modelLoading = modelApiAvailable && modelCatalogStatus === 'loading'

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

  const threadConfiguration = activeThread?.configuration ?? null
  // Thread snapshots arrive on a timer with new objects, so the resolve effect compares values.
  const threadConfigurationKey = useMemo(() => JSON.stringify([
    threadConfiguration?.providerId ?? threadConfiguration?.ProviderId ?? null,
    threadConfiguration?.model ?? threadConfiguration?.Model ?? null,
    readReasoningObject(threadConfiguration?.reasoning ?? threadConfiguration?.Reasoning),
    threadConfiguration?.speed ?? threadConfiguration?.Speed ?? null,
  ]), [threadConfiguration])

  useEffect(() => {
    if (!modelApiAvailable) return
    void reloadProviders()
  }, [modelApiAvailable, reloadProviders])

  useEffect(() => {
    const workspaceCfg = workspaceConfig ?? {}
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
  }, [
    activeThreadId,
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
      const workspaceCfg = workspaceConfig ?? {}
      await loadModels(false, nextProviderId)
      const catalogState = useModelCatalogStore.getState()
      const remembered = readWorkspacePreference(workspaceCfg, nextProviderId)
      const nextModel = remembered?.model || catalogState.modelOptions[0]
      if (!nextModel) {
        addToast(t('composer.providerModelUnavailable'), 'error')
        return
      }

      const readRes = await window.api.appServer.sendRequest('thread/read', {
        threadId: activeThread.id,
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
      addToast(`Provider switched to ${nextProviderId}`, 'success')
    } catch (err) {
      await loadModels(false, providerId)
      addToast(`Failed to switch provider: ${err instanceof Error ? err.message : String(err)}`, 'error')
    } finally {
      setModelApplying(false)
    }
  }, [activeThread, detached, loadModels, providerId, setCaseInsensitiveField, t, workspaceConfig])

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
      enqueue(() => handleModelChange(model))
    },
    onProviderChange: (nextProviderId) => {
      enqueue(() => handleProviderChange(nextProviderId))
    },
    onReasoningChange: (reasoning) => {
      enqueue(() => handleReasoningChange(reasoning))
    },
    onSpeedChange: (speed) => {
      enqueue(() => handleSpeedChange(speed))
    },
    onModelCatalogRetry: () => {
      void loadModels(true, providerId)
    },
    threadStartConfig
  }
}

function reasoningQuickToastLabel(value: ReasoningQuickValue): string {
  if (value === 'off') return 'Off'
  if (value === 'low') return 'Low'
  if (value === 'medium') return 'Medium'
  if (value === 'high') return 'High'
  if (value === 'extraHigh') return 'Extra High'
  if (value === 'max') return 'Max'
  if (value === 'ultra') return 'Ultra'
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

}

function resolveReasoningFromConfiguration(config: Record<string, unknown>): ResolvedReasoningConfig {
  const key = Object.keys(config).find((candidate) => candidate.toLowerCase() === 'reasoning')
  return readReasoningObject(key ? config[key] : null) ?? DEFAULT_REASONING_CONFIG
}
