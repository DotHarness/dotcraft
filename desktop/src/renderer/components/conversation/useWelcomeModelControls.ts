import { useCallback, useEffect, useRef, useState, type RefObject } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useModelCatalog } from '../../hooks/useModelCatalog'
import { useConnectionStore } from '../../stores/connectionStore'
import { readConfigValue, useConfigStore, type ConfigObject } from '../../stores/configStore'
import { useModelCatalogStore, type InferenceSpeedWire } from '../../stores/modelCatalogStore'
import type { WelcomeDraft } from '../../stores/uiStore'
import { addToast } from '../../stores/toastStore'
import { createManualModelPreference, findProviderPreference, readProviderPreferences, type ModelPreference } from '../../../shared/modelPreference'
import { providerPreferenceEdit, resolveWorkspaceProviderFromConfig } from '../../utils/workspaceCoreConfig'
import { buildReasoningPayload, DEFAULT_REASONING_CONFIG, readReasoningObject } from './modelReasoning'
import { createCatalogDefaultPreference, normalizePreferenceForModel } from './PreferenceModelPicker'
import type { ReasoningQuickValue } from './ModelPicker'

type Selection = { providerId: string; preference: ModelPreference }

function configurationKey(config: ConfigObject | null): string | null {
  return config == null ? null : JSON.stringify([
    resolveWorkspaceProviderFromConfig(config), readConfigValue(config, 'ProviderPreferences') ?? null
  ])
}

function draftSelection(draft: WelcomeDraft | null): Selection {
  return {
    providerId: draft?.providerId?.trim() ?? '',
    preference: {
      model: draft?.model || 'Default',
      reasoning: readReasoningObject(draft?.reasoning) ?? DEFAULT_REASONING_CONFIG,
      speed: draft?.speed === 'fast' ? 'fast' : 'standard'
    }
  }
}

export function useWelcomeModelControls({ workspaceConfig, initialDraftRef, workspaceReady = true }: {
  workspaceConfig: ConfigObject | null
  initialDraftRef: RefObject<WelcomeDraft | null>
  workspaceReady?: boolean
}) {
  const t = useT()
  const status = useConnectionStore((state) => state.status)
  const capabilities = useConnectionStore((state) => state.capabilities)
  const revision = useModelCatalogStore((state) => state.revision)
  const loadModels = useModelCatalogStore((state) => state.loadIfNeeded)
  const [selection, setSelection] = useState(() => draftSelection(initialDraftRef.current))
  const [modelApplying, setModelApplying] = useState(false)
  const resolvedWorkspace = useRef<Selection | null>(null)
  const lastConfigurationKey = useRef<string | null>(null)
  const configKey = configurationKey(workspaceConfig)
  const modelApiAvailable = workspaceReady && status === 'connected' && capabilities?.modelCatalogManagement === true &&
    capabilities?.workspaceConfigManagement === true
  const configuredProvider = resolveWorkspaceProviderFromConfig(workspaceConfig ?? {})
  const configuredPreference = findProviderPreference(
    readProviderPreferences(readConfigValue(workspaceConfig, 'ProviderPreferences')), configuredProvider
  )
  const pendingConfiguration = configKey !== lastConfigurationKey.current
  const current = pendingConfiguration && configuredProvider
    ? { providerId: configuredProvider, preference: configuredPreference ?? createManualModelPreference('') }
    : selection
  const { providerId, preference } = current
  const catalog = useModelCatalog(providerId, modelApiAvailable && Boolean(providerId))

  const isCurrentScope = useCallback((requestRevision: number): boolean =>
    useConnectionStore.getState().status === 'connected' &&
    useModelCatalogStore.getState().revision === requestRevision, [])

  useEffect(() => {
    if (!modelApiAvailable || workspaceConfig == null) return
    const workspacePreferenceChanged = lastConfigurationKey.current != null && lastConfigurationKey.current !== configKey
    lastConfigurationKey.current = configKey
    const nextProviderId = resolveWorkspaceProviderFromConfig(workspaceConfig)
    if (initialDraftRef.current && !workspacePreferenceChanged && !nextProviderId) return
    let disposed = false
    const active = (): boolean => !disposed && isCurrentScope(revision) &&
      configurationKey(useConfigStore.getState().config) === configKey
    const apply = (nextPreference: ModelPreference): void => {
      if (!active()) return
      const next = { providerId: nextProviderId, preference: nextPreference }
      resolvedWorkspace.current = next
      setSelection(next)
    }
    const remembered = findProviderPreference(
      readProviderPreferences(readConfigValue(workspaceConfig, 'ProviderPreferences')), nextProviderId
    )
    if (remembered || !nextProviderId) {
      apply(remembered ?? createManualModelPreference(''))
      return
    }
    const unresolved = { providerId: nextProviderId, preference: createManualModelPreference('') }
    resolvedWorkspace.current = unresolved
    setSelection(unresolved)
    const seed = async (): Promise<void> => {
      const result = await loadModels(false, nextProviderId)
      if (!result || !active()) return
      const nextPreference = createCatalogDefaultPreference(result.models[0], result.modelOptions[0] ?? '')
      if (!nextPreference.model) {
        apply(createManualModelPreference(''))
        return
      }
      try {
        await useConfigStore.getState().write([
          { keyPath: 'ProviderId', value: nextProviderId }, providerPreferenceEdit(nextProviderId, nextPreference)
        ])
        apply(nextPreference)
      } catch {
        apply(createManualModelPreference(''))
      }
    }
    void seed()
    return () => { disposed = true }
  }, [configKey, initialDraftRef, isCurrentScope, loadModels, modelApiAvailable, revision, workspaceConfig])

  const hydrateDraft = useCallback((draft: WelcomeDraft): void => {
    setSelection(resolvedWorkspace.current ?? draftSelection(draft))
  }, [])

  const persist = useCallback(async (next: ModelPreference, nextProviderId: string): Promise<boolean> => {
    if (!modelApiAvailable || configKey == null || !isCurrentScope(revision) ||
        configurationKey(useConfigStore.getState().config) !== configKey ||
        !nextProviderId.trim() || !next.model.trim() || next.model === 'Default') return false
    await useConfigStore.getState().write([
      { keyPath: 'ProviderId', value: nextProviderId }, providerPreferenceEdit(nextProviderId, next)
    ])
    return isCurrentScope(revision)
  }, [configKey, isCurrentScope, modelApiAvailable, revision])

  const saveSelection = useCallback(async (next: Selection, failure: string): Promise<void> => {
    if (!modelApiAvailable || workspaceConfig == null) return
    const previous = current
    setModelApplying(true)
    setSelection(next)
    try {
      if (!await persist(next.preference, next.providerId) && isCurrentScope(revision)) setSelection(previous)
    } catch (error) {
      if (isCurrentScope(revision)) {
        setSelection(previous)
        addToast(`${failure}: ${error instanceof Error ? error.message : String(error)}`, 'error')
      }
    } finally {
      if (isCurrentScope(revision)) setModelApplying(false)
    }
  }, [current, isCurrentScope, modelApiAvailable, persist, revision, workspaceConfig])

  useEffect(() => { setModelApplying(false) }, [revision, modelApiAvailable])

  const handleModelChange = useCallback(async (nextModel: string): Promise<void> => {
    if (!nextModel || nextModel === 'Default' || nextModel === preference.model) return
    await saveSelection({ providerId, preference: normalizePreferenceForModel({
      ...preference, model: nextModel
    }, catalog.models) }, 'Failed to save model')
  }, [catalog.models, preference, providerId, saveSelection])

  const handleProviderChange = useCallback(async (nextProviderId: string): Promise<void> => {
    if (!modelApiAvailable || workspaceConfig == null || !nextProviderId || nextProviderId === providerId) return
    setModelApplying(true)
    try {
      const result = await loadModels(false, nextProviderId)
      if (!result || !isCurrentScope(revision) || configurationKey(useConfigStore.getState().config) !== configKey) return
      const remembered = findProviderPreference(
        readProviderPreferences(readConfigValue(workspaceConfig, 'ProviderPreferences')), nextProviderId
      )
      const next = remembered
        ? normalizePreferenceForModel(remembered, result.models)
        : createCatalogDefaultPreference(result.models[0], result.modelOptions[0] ?? '')
      if (!next.model) {
        addToast(t('composer.providerModelUnavailable'), 'error')
        return
      }
      if (await persist(next, nextProviderId)) setSelection({ providerId: nextProviderId, preference: next })
    } catch (error) {
      if (isCurrentScope(revision)) {
        addToast(`Failed to switch provider: ${error instanceof Error ? error.message : String(error)}`, 'error')
      }
    } finally {
      if (isCurrentScope(revision)) setModelApplying(false)
    }
  }, [configKey, isCurrentScope, loadModels, modelApiAvailable, persist, providerId, revision, t, workspaceConfig])

  const handleReasoningChange = useCallback(async (value: ReasoningQuickValue): Promise<void> => {
    const fallback = createCatalogDefaultPreference(catalog.models.find((model) => model.id === preference.model), preference.model).reasoning
    await saveSelection({ providerId, preference: {
      ...preference, reasoning: buildReasoningPayload(value, preference.reasoning) ?? fallback
    } }, 'Failed to save thinking')
  }, [catalog.models, preference, providerId, saveSelection])

  const handleSpeedChange = useCallback(async (speed: InferenceSpeedWire): Promise<void> => {
    if (speed === preference.speed) return
    await saveSelection({ providerId, preference: { ...preference, speed } }, 'Failed to save speed')
  }, [preference, providerId, saveSelection])

  return {
    providerId,
    modelName: preference.model || 'Default',
    reasoningConfig: preference.reasoning,
    speedValue: preference.speed,
    modelApplying,
    modelApiAvailable,
    modelLoading: modelApiAvailable && Boolean(providerId) && (catalog.status === 'idle' || catalog.status === 'loading'),
    modelCatalog: catalog.models,
    modelOptions: catalog.modelOptions,
    modelCatalogStatus: catalog.status,
    modelListUnsupportedEndpoint: catalog.modelListUnsupportedEndpoint,
    modelCatalogErrorCode: catalog.errorCode,
    modelCatalogErrorMessage: catalog.errorMessage,
    loadModels,
    hydrateDraft,
    handleModelChange,
    handleProviderChange,
    handleReasoningChange,
    handleSpeedChange
  }
}
