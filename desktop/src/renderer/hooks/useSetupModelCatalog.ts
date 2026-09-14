import { useCallback, useEffect, useRef, useState, type Dispatch, type SetStateAction } from 'react'
import type { WorkspaceSetupProviderDraft, WorkspaceSetupProviderSummary } from '../../shared/workspaceSetup'
import type { ModelPreference } from '../../shared/modelPreference'
import { parseModelCatalogItems, type ModelCatalogItem } from '../stores/modelCatalogStore'
import { createCatalogDefaultPreference, normalizePreferenceForModel } from '../components/conversation/PreferenceModelPicker'

export function useSetupModelCatalog({
  step,
  configStepIndex,
  providerChoice,
  activeDraft,
  activeExistingProvider,
  modelDirty,
  userConfigDefaults,
  setPreference
}: {
  step: number
  configStepIndex: number
  providerChoice: string
  activeDraft: WorkspaceSetupProviderDraft | null
  activeExistingProvider: WorkspaceSetupProviderSummary | null
  modelDirty: boolean
  userConfigDefaults: { preference?: ModelPreference | null } | null | undefined
  setPreference: Dispatch<SetStateAction<ModelPreference>>
}) {
  const [modelLoadState, setModelLoadState] = useState<'idle' | 'loading' | 'ready' | 'auth-required' | 'unsupported' | 'missing-key' | 'error'>('idle')
  const [modelCatalog, setModelCatalog] = useState<ModelCatalogItem[]>([])
  const [chatGptLoginPending, setChatGptLoginPending] = useState(false)
  const [modelReloadSeq, setModelReloadSeq] = useState(0)
  const [loginError, setLoginError] = useState<string | null>(null)
  const requestGeneration = useRef(0)
  useEffect(() => {
    setLoginError(null)
    return () => { requestGeneration.current += 1 }
  }, [providerChoice, activeDraft?.id, activeExistingProvider?.id, step])
  useEffect(() => {
    if (step !== configStepIndex) {
      return
    }

    const controller = new AbortController()
    setModelLoadState('loading')

    const request = providerChoice === 'existing'
      ? activeExistingProvider
        ? { providerId: activeExistingProvider.id }
        : null
      : activeDraft
        ? { provider: activeDraft }
        : null

    if (request == null) {
      setModelLoadState('error')
      setModelCatalog([])
      return
    }

    void window.api.workspace
      .listSetupModels(request)
      .then((result) => {
        if (controller.signal.aborted) return

        if (result.kind === 'success') {
          const parsedModels = parseModelCatalogItems({ success: true, models: result.models })
          const parsedById = new Map(parsedModels.map((item) => [item.id, item]))
          const models = result.models
            .map((item) => parsedById.get(item.id))
            .filter((item): item is ModelCatalogItem => item != null)
          setModelCatalog(models)
          setModelLoadState('ready')
          if (!modelDirty && models.length > 0) {
            const remembered = userConfigDefaults?.preference
            setPreference(remembered && models.some((item) => item.id === remembered.model)
              ? normalizePreferenceForModel(remembered, models)
              : createCatalogDefaultPreference(models[0], models[0].id))
          }
          return
        }

        setModelCatalog([])
        setModelLoadState(result.kind)
      })
      .catch(() => {
        if (controller.signal.aborted) return
        setModelCatalog([])
        setModelLoadState('error')
      })

    return () => {
      controller.abort()
    }
  }, [activeDraft, activeExistingProvider, configStepIndex, modelDirty, modelReloadSeq, providerChoice, step, userConfigDefaults?.preference])

  const loginChatGptForSetup = useCallback(async (): Promise<void> => {
    const providerId = providerChoice === 'existing' ? activeExistingProvider?.id : activeDraft?.id
    if (!providerId || chatGptLoginPending) return
    const generation = requestGeneration.current
    setLoginError(null)
    setChatGptLoginPending(true)
    try {
      const result = await window.api.workspace.loginSetupChatGpt(providerId)
      if (generation !== requestGeneration.current) return
      if (result.kind === 'success') setModelReloadSeq((value) => value + 1)
      else setLoginError(result.errorMessage || 'ChatGPT sign-in failed.')
    } catch (error) {
      if (generation === requestGeneration.current) setLoginError(error instanceof Error ? error.message : 'ChatGPT sign-in failed.')
    } finally {
      setChatGptLoginPending(false)
    }
  }, [activeDraft?.id, activeExistingProvider?.id, chatGptLoginPending, providerChoice])

  return {
    modelLoadState,
    modelCatalog,
    chatGptLoginPending,
    loginError,
    loginChatGptForSetup,
    retry: () => setModelReloadSeq((value) => value + 1)
  }
}
