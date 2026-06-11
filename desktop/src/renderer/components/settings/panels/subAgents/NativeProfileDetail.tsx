import { useEffect, useMemo, useState, type JSX } from 'react'
import { useT } from '../../../../contexts/LocaleContext'
import { useModelCatalogStore } from '../../../../stores/modelCatalogStore'
import { SettingsGroup, SettingsRow } from '../../SettingsGroup'
import { SettingsSelect } from '../../ui/SettingsSelect'
import { PillSwitch } from '../../../ui/PillSwitch'
import { AgentIcon } from './AgentIcon'
import { SettingsBreadcrumb } from '../../SettingsBreadcrumb'
import type { SubAgentProfileEntryWire } from './wire'
import {
  inputStyle,
  pageDescriptionStyle,
  pageHeadingStyle,
  pageStyle,
  primaryButtonStyle
} from './styles'

interface NativeProfileDetailProps {
  profile: SubAgentProfileEntryWire
  model: string
  savingModel?: boolean
  onBack: () => void
  onSaveModel: (model: string) => void
}

export function NativeProfileDetail({
  profile,
  model,
  savingModel = false,
  onBack,
  onSaveModel
}: NativeProfileDetailProps): JSX.Element {
  const t = useT()
  const [draftModel, setDraftModel] = useState(model)
  const modelOptions = useModelCatalogStore((s) => s.modelOptions)
  const modelCatalogStatus = useModelCatalogStore((s) => s.status)
  const modelListUnsupportedEndpoint = useModelCatalogStore((s) => s.modelListUnsupportedEndpoint)
  const modelCatalogErrorCode = useModelCatalogStore((s) => s.errorCode)
  const modelCatalogErrorMessage = useModelCatalogStore((s) => s.errorMessage)
  const loadModels = useModelCatalogStore((s) => s.loadIfNeeded)

  useEffect(() => {
    setDraftModel(model)
  }, [model])

  useEffect(() => {
    void loadModels()
  }, [loadModels])

  const effectiveModelOptions = useMemo(() => {
    const normalized = modelOptions.map((item) => item.trim()).filter(Boolean)
    const current = draftModel.trim()
    if (!current || normalized.includes(current)) return normalized
    return [current, ...normalized]
  }, [draftModel, modelOptions])
  const modelSelectAvailable =
    modelCatalogStatus === 'ready' &&
    !modelListUnsupportedEndpoint &&
    effectiveModelOptions.length > 0
  const modelListLoading = modelCatalogStatus === 'loading'
  const modelChanged = draftModel.trim() !== model.trim()

  return (
    <div style={pageStyle()}>
      <SettingsBreadcrumb
        parentLabel={t('settings.subAgents.title')}
        currentLabel={t('settings.subAgents.preset.native.title')}
        onBack={onBack}
      />

      <div style={{ display: 'flex', alignItems: 'center', gap: '14px' }}>
        <AgentIcon name={profile.name} isBuiltIn size={40} />
        <div style={{ minWidth: 0 }}>
          <div style={pageHeadingStyle()}>{t('settings.subAgents.preset.native.title')}</div>
          <div style={pageDescriptionStyle()}>
            {t('settings.subAgents.preset.native.description')}
          </div>
        </div>
      </div>

      <SettingsGroup>
        <SettingsRow
          label={t('settings.subAgents.preset.enableTitle')}
          description={t('settings.subAgents.preset.nativeLockedHint')}
          control={
            <PillSwitch
              aria-label={t('settings.subAgents.toggleAria', { name: profile.name })}
              checked
              onChange={() => {
                /* native is always enabled */
              }}
              disabled
            />
          }
        />
        <SettingsRow
          label={t('settings.subAgents.preset.nativeModelTitle')}
          description={t('settings.subAgents.preset.nativeModelDescription')}
          htmlFor="native-subagent-model"
          orientation="block"
        >
          {modelListLoading ? (
            <div role="status" aria-live="polite" style={{ fontSize: '12px', color: 'var(--text-dimmed)' }}>
              {t('settings.subAgents.preset.modelListLoading')}
            </div>
          ) : modelSelectAvailable ? (
            <SettingsSelect
              id="native-subagent-model"
              value={draftModel}
              onValueChange={setDraftModel}
              options={[
                { value: '', label: t('settings.subAgents.preset.nativeModelInherit') },
                ...effectiveModelOptions.map((item) => ({ value: item, label: item }))
              ]}
            />
          ) : (
            <input
              id="native-subagent-model"
              type="text"
              value={draftModel}
              onChange={(e) => setDraftModel(e.target.value)}
              placeholder={t('settings.subAgents.preset.nativeModelPlaceholder')}
              style={inputStyle()}
            />
          )}
          {modelCatalogStatus === 'error' && (
            <div
              role="status"
              aria-live="polite"
              style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                gap: '12px',
                marginTop: '8px',
                color: 'var(--error)',
                fontSize: '12px'
              }}
            >
              <span style={{ minWidth: 0 }}>
                {modelCatalogErrorCode
                  ? `${modelCatalogErrorCode}: ${modelCatalogErrorMessage ?? ''}`.trim()
                  : (modelCatalogErrorMessage || t('composer.modelListError'))}
              </span>
              <button
                type="button"
                onClick={() => {
                  void loadModels(true)
                }}
                style={{
                  border: 'none',
                  background: 'transparent',
                  color: 'var(--accent)',
                  padding: 0,
                  cursor: 'pointer',
                  fontSize: '12px',
                  fontWeight: 600,
                  flexShrink: 0
                }}
              >
                {t('composer.modelListRetry')}
              </button>
            </div>
          )}
          <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: '10px' }}>
            <button
              type="button"
              disabled={!modelChanged || savingModel}
              onClick={() => onSaveModel(draftModel)}
              style={primaryButtonStyle(!modelChanged || savingModel)}
            >
              {savingModel ? t('settings.subAgents.saving') : t('settings.subAgents.save')}
            </button>
          </div>
        </SettingsRow>
      </SettingsGroup>
    </div>
  )
}
