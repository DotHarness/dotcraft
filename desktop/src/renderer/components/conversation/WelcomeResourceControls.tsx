import { useT } from '../../contexts/LocaleContext'
import { useConfigStore } from '../../stores/configStore'
import { useProvidersStore } from '../../stores/providersStore'
import { ACTION_SHORTCUTS } from '../ui/shortcutKeys'
import { ApprovalPolicyPicker, type VisibleApprovalPolicy } from './ApprovalPolicyPicker'
import { composerModelPillStyle } from './ComposerShell'
import { ModelPicker } from './ModelPicker'
import type { useWelcomeModelControls } from './useWelcomeModelControls'

export function WelcomeApprovalPolicyPicker({ value, onChange, ready }: {
  value: VisibleApprovalPolicy
  onChange: (value: VisibleApprovalPolicy) => void
  ready: boolean
}): JSX.Element {
  const t = useT()
  const config = useConfigStore((state) => state.config)
  const error = useConfigStore((state) => state.error)
  return <ApprovalPolicyPicker
    value={value}
    onChange={onChange}
    disabled={!ready || config == null}
    placeholder={config == null ? t(error ? 'workspace.config.loadFailed' : 'settings.loading') : undefined}
  />
}

export function WelcomeModelPicker({ controls }: {
  controls: ReturnType<typeof useWelcomeModelControls>
}): JSX.Element {
  const t = useT()
  const config = useConfigStore((state) => state.config)
  const error = useConfigStore((state) => state.error)
  const providers = useProvidersStore((state) => state.providers)
  const providerError = useProvidersStore((state) => state.error)
  const loading = controls.modelApiAvailable && (config == null ? !error : controls.modelLoading)
  const modelError = controls.modelCatalogErrorCode
    ? `${controls.modelCatalogErrorCode}: ${controls.modelCatalogErrorMessage ?? ''}`.trim()
    : controls.modelCatalogErrorMessage
  return <ModelPicker
    providerId={controls.providerId}
    providerOptions={providers}
    modelName={controls.modelName}
    modelOptions={controls.modelApiAvailable ? controls.modelOptions : []}
    modelCatalog={controls.modelCatalog}
    reasoningValue={controls.reasoningConfig.enabled ? controls.reasoningConfig.effort : 'off'}
    speedValue={controls.speedValue}
    loading={loading}
    unsupported={controls.modelListUnsupportedEndpoint}
    modelListReady={controls.modelApiAvailable && controls.modelCatalogStatus === 'ready' && controls.modelOptions.length > 0}
    errorMessage={error ? t('workspace.config.loadFailed')
      : providerError ? t('settings.llm.toast.loadProvidersFailed', { error: providerError }) : modelError}
    disabled={controls.modelApplying || !controls.modelApiAvailable}
    onChange={(model) => { void controls.handleModelChange(model) }}
    onProviderChange={(provider) => { void controls.handleProviderChange(provider) }}
    onReasoningChange={(reasoning) => { void controls.handleReasoningChange(reasoning) }}
    onSpeedChange={(speed) => { void controls.handleSpeedChange(speed) }}
    onRetry={() => {
      if (error || config == null) void useConfigStore.getState().refresh()
      else if (providerError) void useProvidersStore.getState().reload()
      else void controls.loadModels(true, controls.providerId)
    }}
    shortcut={ACTION_SHORTCUTS.selectModel}
    triggerStyle={composerModelPillStyle(
      controls.modelApplying || loading ? 'var(--composer-footer-muted)' : 'var(--composer-footer-highlight)',
      controls.modelApplying || loading
    )}
  />
}
