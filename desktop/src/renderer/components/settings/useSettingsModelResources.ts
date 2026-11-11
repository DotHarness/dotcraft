import { useCallback } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useModelCatalog } from '../../hooks/useModelCatalog'
import { useModelCatalogStore, type ModelCatalogItem } from '../../stores/modelCatalogStore'
import { isSettingsResourceScopeCurrent, useSettingsResourceScope } from './useSettingsProviderResources'

export function useSettingsModelResources(providerId: string, enabled: boolean, providerMissing: boolean) {
  const t = useT()
  const scope = useSettingsResourceScope(enabled)
  const available = scope != null && !providerMissing && Boolean(providerId.trim())
  const catalog = useModelCatalog(providerId, available)
  const fetchWorkspaceProviderModelOptions = useCallback(async (nextProviderId: string): Promise<ModelCatalogItem[] | null> => {
    if (!scope || !nextProviderId.trim() || !isSettingsResourceScopeCurrent(scope)) return null
    const result = await useModelCatalogStore.getState().loadIfNeeded(false, nextProviderId)
    return isSettingsResourceScopeCurrent(scope) && result?.status === 'ready' ? result.models : null
  }, [scope])
  const reloadProviderPreferences = useCallback(async (nextProviderId = providerId): Promise<void> => {
    if (!scope || providerMissing || !nextProviderId.trim() || !isSettingsResourceScopeCurrent(scope)) return
    await useModelCatalogStore.getState().revalidate(nextProviderId)
  }, [providerId, providerMissing, scope])
  return {
    providerModelCatalog: available ? catalog.models : [],
    providerModelLoading: available && (catalog.status === 'idle' || catalog.status === 'loading'),
    providerModelError: providerMissing
      ? t('settings.llm.workspaceProviderMissing', { providerId })
      : available ? catalog.errorMessage || (catalog.errorCode ? t('settings.llm.modelListUnavailable') : '') : '',
    fetchWorkspaceProviderModelOptions,
    reloadProviderPreferences
  }
}
