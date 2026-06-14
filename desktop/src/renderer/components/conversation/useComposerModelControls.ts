import { useCallback, useEffect, useMemo, useState } from 'react'
import { useConnectionStore } from '../../stores/connectionStore'
import {
  useModelCatalogStore,
  type ModelCatalogItem,
  type ReasoningEffortWire,
  type ReasoningOutputWire
} from '../../stores/modelCatalogStore'
import { addToast } from '../../stores/toastStore'
import { useThreadStore } from '../../stores/threadStore'
import type { Thread, ThreadConfigurationWire } from '../../types/thread'
import type { WorkspaceConfigChangedPayload } from '../../utils/workspaceConfigChanged'
import { parseJsonConfig } from '../../../shared/jsonConfig'
import { configObjectFromWorkspaceCore, type WorkspaceCoreConfigLike } from '../../utils/workspaceCoreConfig'
import type { ReasoningQuickValue } from './ModelPicker'

export interface ResolvedReasoningConfig {
  enabled: boolean
  effort: ReasoningEffortWire
  output: ReasoningOutputWire
}

export interface ComposerModelControls {
  modelName: string
  modelOptions: string[]
  modelCatalog: ModelCatalogItem[]
  reasoningValue: ReasoningQuickValue
  modelLoading: boolean
  modelDisabled: boolean
  modelListUnsupportedEndpoint: boolean
  modelCatalogError: boolean
  modelCatalogErrorMessage: string | null
  onModelChange: (model: string) => void
  onReasoningChange: (value: ReasoningQuickValue) => void
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
  const [modelName, setModelName] = useState<string>('Default')
  const [reasoningConfig, setReasoningConfig] = useState<ResolvedReasoningConfig>(DEFAULT_REASONING_CONFIG)
  const [modelApplying, setModelApplying] = useState(false)
  const [detachedModelTouched, setDetachedModelTouched] = useState(false)
  const [detachedReasoningTouched, setDetachedReasoningTouched] = useState(false)
  const [detachedReasoningOverride, setDetachedReasoningOverride] = useState<ResolvedReasoningConfig | null>(null)

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
    (thread: Thread | null, workspaceCfg: Record<string, unknown>): string => {
      const workspaceModelRaw = workspaceCfg.Model ?? workspaceCfg.model
      const ws = typeof workspaceModelRaw === 'string' ? workspaceModelRaw.trim() : ''
      const workspaceModel = ws.length > 0 && ws !== 'Default' ? ws : null
      const threadRaw = thread?.configuration?.model ?? thread?.configuration?.Model
      const threadTrimmed = typeof threadRaw === 'string' ? threadRaw.trim() : ''
      if (threadTrimmed.length > 0 && threadTrimmed !== 'Default') {
        return threadTrimmed
      }
      return workspaceModel ?? 'Default'
    },
    []
  )

  const resolveEffectiveReasoning = useCallback(
    (thread: Thread | null, workspaceCfg: Record<string, unknown>): ResolvedReasoningConfig => {
      const threadReasoning = readReasoningObject(thread?.configuration?.reasoning ?? thread?.configuration?.Reasoning)
      if (threadReasoning) return threadReasoning
      const workspaceReasoning = readReasoningObject(workspaceCfg.Reasoning ?? workspaceCfg.reasoning)
      return workspaceReasoning ?? DEFAULT_REASONING_CONFIG
    },
    []
  )

  useEffect(() => {
    if (!modelApiAvailable) return
    void loadModels()
  }, [loadModels, modelApiAvailable])

  useEffect(() => {
    let disposed = false
    const loadEffectiveModel = async (): Promise<void> => {
      try {
        const workspaceCfg = await readWorkspaceConfig()
        if (disposed) return
        if (!detached || !detachedModelTouched) {
          setModelName(resolveEffectiveModel(activeThread, workspaceCfg))
        }
        if (!detached || !detachedReasoningTouched) {
          setReasoningConfig(resolveEffectiveReasoning(activeThread, workspaceCfg))
        }
      } catch {
        if (disposed) return
        if (!detached || !detachedModelTouched) {
          const modelFromThread = activeThread?.configuration?.model ?? activeThread?.configuration?.Model
          const mt = typeof modelFromThread === 'string' ? modelFromThread.trim() : ''
          setModelName(mt.length > 0 && mt !== 'Default' ? mt : 'Default')
        }
        if (!detached || !detachedReasoningTouched) {
          setReasoningConfig(
            readReasoningObject(activeThread?.configuration?.reasoning ?? activeThread?.configuration?.Reasoning)
              ?? DEFAULT_REASONING_CONFIG
          )
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
    activeThread?.configuration?.Reasoning,
    activeThread?.configuration?.reasoning,
    detached,
    detachedModelTouched,
    detachedReasoningTouched,
    readWorkspaceConfig,
    resolveEffectiveModel,
    resolveEffectiveReasoning,
    workspaceConfigChange,
    workspaceConfigChangeSeq
  ])

  const handleModelChange = useCallback(
    async (nextModel: string): Promise<void> => {
      if (!nextModel || nextModel === modelName) return
      if (detached) {
        setDetachedModelTouched(true)
        setModelName(nextModel)
        return
      }
      if (!activeThread) return

      setModelApplying(true)
      const previousModel = modelName
      setModelName(nextModel)
      try {
        await window.api.appServer.sendRequest('workspace/config/update', {
          model: nextModel === 'Default' ? null : nextModel
        })

        const readRes = (await window.api.appServer.sendRequest('thread/read', {
          threadId: activeThread.id,
          includeTurns: false
        })) as { thread?: { configuration?: ThreadConfigurationWire | null } }
        const existingConfig =
          readRes.thread?.configuration && typeof readRes.thread.configuration === 'object'
            ? { ...(readRes.thread.configuration as Record<string, unknown>) }
            : {}
        if (nextModel === 'Default') {
          deleteCaseInsensitiveField(existingConfig, 'model')
        } else {
          setCaseInsensitiveField(existingConfig, 'model', nextModel)
        }

        await window.api.appServer.sendRequest('thread/config/update', {
          threadId: activeThread.id,
          config: existingConfig
        })
        const active = useThreadStore.getState().activeThread
        if (active && active.id === activeThread.id) {
          const mergedCfg: Record<string, unknown> = { ...(active.configuration ?? {}) }
          if (nextModel === 'Default') {
            deleteCaseInsensitiveField(mergedCfg, 'model')
          } else {
            mergedCfg.model = nextModel
          }
          useThreadStore.getState().setActiveThread({
            ...active,
            configuration: mergedCfg as typeof active.configuration
          })
        }
        addToast(
          nextModel === 'Default' ? 'Using workspace default model' : `Model switched to ${nextModel}`,
          'success'
        )
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err)
        setModelName(previousModel)
        addToast(`Failed to switch model: ${msg}`, 'error')
      } finally {
        setModelApplying(false)
      }
    },
    [
      activeThread,
      deleteCaseInsensitiveField,
      detached,
      modelName,
      setCaseInsensitiveField
    ]
  )

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
        await window.api.appServer.sendRequest('workspace/config/update', {
          reasoning: nextPayload
        })

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

  const reasoningValue: ReasoningQuickValue =
    detached && detachedReasoningTouched && detachedReasoningOverride == null
      ? 'default'
      : reasoningConfig.enabled
        ? reasoningConfig.effort
        : 'off'

  const threadStartConfig = useMemo<ThreadConfigurationWire>(() => {
    if (!detached) return {}
    const config: ThreadConfigurationWire = {}
    if (detachedModelTouched && modelName && modelName !== 'Default') {
      config.model = modelName
    }
    if (detachedReasoningTouched && detachedReasoningOverride != null) {
      config.reasoning = detachedReasoningOverride
    }
    return config
  }, [detached, detachedModelTouched, detachedReasoningOverride, detachedReasoningTouched, modelName])

  return {
    modelName,
    modelOptions,
    modelCatalog,
    reasoningValue,
    modelLoading,
    modelDisabled: modelApplying || !modelApiAvailable,
    modelListUnsupportedEndpoint,
    modelCatalogError: modelCatalogStatus === 'error',
    modelCatalogErrorMessage:
      modelCatalogStatus === 'error' && modelCatalogErrorCode
        ? `${modelCatalogErrorCode}: ${modelCatalogErrorMessage ?? ''}`.trim()
        : modelCatalogErrorMessage,
    onModelChange: (model) => {
      void handleModelChange(model)
    },
    onReasoningChange: (reasoning) => {
      void handleReasoningChange(reasoning)
    },
    onModelCatalogRetry: () => {
      void loadModels(true)
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
