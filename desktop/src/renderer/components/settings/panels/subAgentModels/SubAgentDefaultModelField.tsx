import { useEffect, useState, type JSX } from 'react'
import { useT } from '../../../../contexts/LocaleContext'
import { useModelCatalog } from '../../../../hooks/useModelCatalog'
import { configKeyPath, readConfigValue, useConfig, useConfigSetting } from '../../../../stores/configStore'
import { useModelCatalogStore } from '../../../../stores/modelCatalogStore'
import {
  cloneModelPreference,
  createManualModelPreference,
  findProviderPreference,
  readProviderPreferences,
  type ModelPreference
} from '../../../../../shared/modelPreference'
import {
  createCatalogDefaultPreference,
  normalizePreferenceForModel,
  PreferenceModelPicker
} from '../../../conversation/PreferenceModelPicker'
import type { ProviderInfoWire } from '../../providerInfo'
import { settingsSectionLabelStyle } from '../../settingsTypography'
import { SettingsTroubleNotice } from '../../SettingsTroubleNotice'
import { SegmentedControl } from '../../ui/SegmentedControl'
import { ProviderSelect } from '../../providers/ProviderSelect'
import {
  findProvider,
  providerTrouble,
  readSubAgentSelection,
  toSubAgentSelectionConfig,
  type SubAgentModelSelection
} from './subAgentModels'
import styles from './SubAgentModels.module.css'

const MODEL_INPUT_ID = 'settings-subagent-model'

interface SubAgentDefaultModelFieldProps {
  mainProviderId: string
  mainPreference: ModelPreference
  providers: ProviderInfoWire[]
  disabled?: boolean
  onEditProvider?: (provider: ProviderInfoWire) => void
}

export function SubAgentDefaultModelField({
  mainProviderId,
  mainPreference,
  providers,
  disabled = false,
  onEditProvider
}: SubAgentDefaultModelFieldProps): JSX.Element {
  const t = useT()
  const config = useConfig()
  const keyPath = configKeyPath('SubAgent', 'ProviderPreferences', mainProviderId.trim())
  const setting = useConfigSetting(keyPath, (error) =>
    t('settings.llm.toast.saveSubAgentModelFailed', { error }))
  const current = readSubAgentSelection(setting.value, mainProviderId)
  const [pendingProviderId, setPendingProviderId] = useState<string | null>(null)
  const [manualDraft, setManualDraft] = useState('')

  const custom = current != null || pendingProviderId != null
  const targetProviderId = pendingProviderId ?? current?.providerId ?? mainProviderId
  const catalog = useModelCatalog(targetProviderId)
  const catalogLoading = custom && (catalog.status === 'loading' || catalog.status === 'idle')
  const manualFallback = custom && !catalogLoading && catalog.models.length === 0
  const shown = pendingProviderId != null
    ? createManualModelPreference(manualDraft)
    : current?.preference ?? mainPreference
  const preference = manualFallback ? { ...cloneModelPreference(shown), model: manualDraft } : shown
  const trouble = current ? providerTrouble(current.providerId, providers) : null
  const troubleProvider = current ? findProvider(providers, current.providerId) : undefined

  useEffect(() => {
    setPendingProviderId(null)
  }, [mainProviderId])

  useEffect(() => {
    setManualDraft(current?.preference.model ?? '')
  }, [current?.preference.model])

  useEffect(() => {
    if (pendingProviderId == null || catalog.status !== 'ready' || catalog.models.length === 0) return
    const mainForTarget = findProviderPreference(
      readProviderPreferences(readConfigValue(config, 'ProviderPreferences')),
      pendingProviderId
    )
    const listed = mainForTarget && catalog.models.some((model) => model.id === mainForTarget.model)
    const next = listed
      ? normalizePreferenceForModel(mainForTarget, catalog.models)
      : createCatalogDefaultPreference(catalog.models[0], '')
    setPendingProviderId(null)
    persist({ providerId: pendingProviderId, preference: next })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [catalog.models, catalog.status, pendingProviderId])

  function persist(next: SubAgentModelSelection | null): void {
    if (!mainProviderId.trim()) return
    void setting.set(next == null ? null : toSubAgentSelectionConfig(mainProviderId, next))
  }

  function commitManualModel(): void {
    const model = manualDraft.trim()
    if (!model || (pendingProviderId == null && model === current?.preference.model)) return
    const providerId = pendingProviderId ?? targetProviderId
    const base = current && pendingProviderId == null ? current.preference : createManualModelPreference(model)
    setPendingProviderId(null)
    persist({ providerId, preference: { ...cloneModelPreference(base), model } })
  }

  const busy = disabled || setting.pending

  return (
    <div className={styles.field}>
      <div className={styles.fieldHeader}>
        <label htmlFor={MODEL_INPUT_ID} style={{ ...settingsSectionLabelStyle(), marginBottom: 0 }}>
          {t('settings.llm.subAgentModelTitle')}
        </label>
        <SegmentedControl<'inherit' | 'custom'>
          value={custom ? 'custom' : 'inherit'}
          options={[
            { value: 'inherit', label: t('settings.llm.subAgentPreferenceInherit') },
            { value: 'custom', label: t('settings.llm.subAgentPreferenceCustom') }
          ]}
          onChange={(mode) => {
            setPendingProviderId(null)
            void persist(mode === 'custom'
              ? { providerId: mainProviderId, preference: cloneModelPreference(mainPreference) }
              : null)
          }}
          disabled={busy || mainPreference.model.trim() === ''}
          ariaLabel={t('settings.llm.subAgentPreferenceAria')}
        />
      </div>
      <div className={styles.selectionRow}>
        <ProviderSelect
          requireReady
          id="settings-subagent-provider"
          value={targetProviderId}
          providers={providers}
          disabled={busy || !custom}
          ariaLabel={t('settings.llm.subAgentProvider')}
          style={{ width: 200, flexShrink: 0 }}
          onValueChange={(providerId) => {
            if (providerId.toLowerCase() === targetProviderId.toLowerCase()) return
            setManualDraft('')
            setPendingProviderId(providerId)
          }}
        />
        <div className={styles.modelPicker}>
          <PreferenceModelPicker
            preference={preference}
            models={catalog.models}
            loading={catalogLoading}
            disabled={busy || !custom}
            errorMessage={custom ? catalog.errorMessage : null}
            manualFallback={manualFallback}
            onRetry={() => void useModelCatalogStore.getState().loadIfNeeded(true, targetProviderId)}
            onChange={(next) => {
              if (manualFallback) {
                setManualDraft(next.model)
                return
              }
              void persist({ providerId: targetProviderId, preference: normalizePreferenceForModel(next, catalog.models) })
            }}
            onManualCommit={commitManualModel}
            inputId={MODEL_INPUT_ID}
            inputAriaLabel={t('settings.llm.subAgentModelTitle')}
            placeholder={t('settings.llm.subAgentModelPlaceholder')}
          />
        </div>
      </div>
      {trouble && current && (
        <div className={styles.fieldNotice}>
          <SettingsTroubleNotice
            message={t(trouble === 'missing' ? 'settings.llm.subAgentProviderMissing' : 'settings.llm.subAgentProviderSignedOut', {
              name: troubleProvider?.displayName ?? current.providerId
            })}
            action={troubleProvider && onEditProvider
              ? { label: t('settings.llm.subAgentModels.setUpProvider'), run: () => onEditProvider(troubleProvider) }
              : undefined}
          />
        </div>
      )}
    </div>
  )
}
