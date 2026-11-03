import { useEffect, useRef, useState, type JSX } from 'react'
import { useT } from '../../../../contexts/LocaleContext'
import { useModelCatalog } from '../../../../hooks/useModelCatalog'
import { readConfigValue, useConfig } from '../../../../stores/configStore'
import { useModelCatalogStore } from '../../../../stores/modelCatalogStore'
import { addToast } from '../../../../stores/toastStore'
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
  findSubAgentSelection,
  providerTrouble,
  readSubAgentSelections,
  setSubAgentSelection,
  toContractSubAgentSelections,
  type SubAgentModelSelection,
  type SubAgentSelections
} from './subAgentModels'
import styles from './SubAgentModels.module.css'

const MODEL_INPUT_ID = 'settings-subagent-model'

interface SubAgentDefaultModelFieldProps {
  mainProviderId: string
  mainPreference: ModelPreference
  providers: ProviderInfoWire[]
  disabled?: boolean
  refreshKey?: number
  onEditProvider?: (provider: ProviderInfoWire) => void
}

export function SubAgentDefaultModelField({
  mainProviderId,
  mainPreference,
  providers,
  disabled = false,
  refreshKey = 0,
  onEditProvider
}: SubAgentDefaultModelFieldProps): JSX.Element {
  const t = useT()
  const config = useConfig()
  const [selections, setSelections] = useState<SubAgentSelections>({})
  const selectionsRef = useRef<SubAgentSelections>({})
  const [applying, setApplying] = useState(false)
  const [pendingProviderId, setPendingProviderId] = useState<string | null>(null)
  const [manualDraft, setManualDraft] = useState('')

  const current = findSubAgentSelection(selections, mainProviderId)
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
    let cancelled = false
    void window.api.appServer.sendRequest('subagent/profiles/list', {})
      .then((result) => {
        if (cancelled) return
        const settings = (result as { settings?: { providerPreferences?: unknown } }).settings
        const next = readSubAgentSelections(settings?.providerPreferences)
        selectionsRef.current = next
        setSelections(next)
      })
      .catch(() => undefined)
    return () => {
      cancelled = true
    }
  }, [refreshKey])

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
    void persist({ providerId: pendingProviderId, preference: next })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [catalog.models, catalog.status, pendingProviderId])

  async function persist(next: SubAgentModelSelection | null): Promise<void> {
    const previous = selectionsRef.current
    const nextSelections = setSubAgentSelection(previous, mainProviderId, next)
    selectionsRef.current = nextSelections
    setSelections(nextSelections)
    setApplying(true)
    try {
      await window.api.appServer.sendRequest('subagent/settings/update', {
        providerPreferences: toContractSubAgentSelections(nextSelections)
      }, 20_000)
    } catch (err) {
      selectionsRef.current = previous
      setSelections(previous)
      addToast(t('settings.llm.toast.saveSubAgentModelFailed', {
        error: err instanceof Error ? err.message : String(err)
      }), 'error')
    } finally {
      setApplying(false)
    }
  }

  function commitManualModel(): void {
    const model = manualDraft.trim()
    if (!model || (pendingProviderId == null && model === current?.preference.model)) return
    const providerId = pendingProviderId ?? targetProviderId
    const base = current && pendingProviderId == null ? current.preference : createManualModelPreference(model)
    setPendingProviderId(null)
    void persist({ providerId, preference: { ...cloneModelPreference(base), model } })
  }

  const busy = disabled || applying

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
