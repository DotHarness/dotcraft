import { useT } from '../../contexts/LocaleContext'
import { useConfigStore } from '../../stores/configStore'
import { SettingsTroubleNotice } from './SettingsTroubleNotice'

export function WorkspaceResourceNotice({ providerError, onRetryProviders }: {
  providerError: string | null
  onRetryProviders: () => void
}): JSX.Element | null {
  const t = useT()
  const error = useConfigStore((state) => state.error)
  if (!error && !providerError) return null
  return <>
    {error && <SettingsTroubleNotice
      message={t('workspace.config.loadFailed')}
      action={{ label: t('common.retry'), run: () => { void useConfigStore.getState().refresh() } }}
    />}
    {providerError && <SettingsTroubleNotice
      message={t('settings.llm.toast.loadProvidersFailed', { error: providerError })}
      action={{ label: t('common.retry'), run: onRetryProviders }}
    />}
  </>
}
