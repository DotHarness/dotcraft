import { useState, type JSX } from 'react'
import { Plus, X } from 'lucide-react'
import { useT } from '../../../../contexts/LocaleContext'
import { useModelCatalog } from '../../../../hooks/useModelCatalog'
import { useConfigSetting } from '../../../../stores/configStore'
import { Button } from '../../../ui/Button'
import { IconButton } from '../../../ui/IconButton'
import { Input } from '../../../ui/Input'
import { StatusIndicator } from '../../../ui/StatusIndicator'
import type { ProviderInfoWire } from '../../providerInfo'
import { ProviderPresetMark } from '../../providers/ProviderPresetMark'
import { providerPresetMark } from '../../providers/providerPresets'
import { SettingsGroup, SettingsRow } from '../../SettingsGroup'
import { settingsPlaceholderStyle } from '../../settingsTypography'
import { SettingsSelect } from '../../ui/SettingsSelect'
import { providerUsable } from '../../providers/providerDraft'
import { ProviderSelect } from '../../providers/ProviderSelect'
import {
  findProvider,
  hasSubAgentModelChoice,
  MAX_SUBAGENT_MODEL_CHOICES,
  providerTrouble,
  readSubAgentModelChoices,
  toSubAgentModelChoicesConfig,
  type SubAgentModelChoice
} from './subAgentModels'
import styles from './SubAgentModels.module.css'

interface SubAgentModelChoicesGroupProps {
  providers: ProviderInfoWire[]
  mainProviderId: string
}

export function SubAgentModelChoicesGroup({ providers, mainProviderId }: SubAgentModelChoicesGroupProps): JSX.Element {
  const t = useT()
  const setting = useConfigSetting('SubAgent.Models', (error) =>
    t('settings.llm.subAgentModels.saveFailed', { error }))
  const choices = readSubAgentModelChoices(setting.value)
  const [adding, setAdding] = useState(false)
  const full = choices.length >= MAX_SUBAGENT_MODEL_CHOICES

  function save(next: SubAgentModelChoice[]): void {
    void setting.set(next.length === 0 ? null : toSubAgentModelChoicesConfig(next))
  }

  return (
    <SettingsGroup
      title={t('settings.llm.subAgentModels.title')}
      headerAction={
        <Button
          iconLeft={<Plus size={15} />}
          disabled={adding || full || setting.pending}
          title={full ? t('settings.llm.subAgentModels.full', { count: MAX_SUBAGENT_MODEL_CHOICES }) : undefined}
          onClick={() => setAdding(true)}
        >
          {t('settings.llm.subAgentModels.add')}
        </Button>
      }
    >
      {choices.length === 0 && !adding && (
        <SettingsRow>
          <div style={{ ...settingsPlaceholderStyle(), width: '100%', textAlign: 'center' }}>
            {t('settings.llm.subAgentModels.empty')}
          </div>
        </SettingsRow>
      )}
      {choices.map((choice) => (
        <ChoiceRow
          key={`${choice.providerId}/${choice.model}`}
          choice={choice}
          providers={providers}
          disabled={setting.pending}
          onRemove={() => save(choices.filter((candidate) => candidate !== choice))}
        />
      ))}
      {adding && (
        <AddChoiceRow
          providers={providers}
          initialProviderId={initialProvider(providers, mainProviderId)}
          existing={choices}
          onAdd={(choice) => {
            setAdding(false)
            save([...choices, choice])
          }}
          onCancel={() => setAdding(false)}
        />
      )}
    </SettingsGroup>
  )
}

function ChoiceRow({
  choice,
  providers,
  disabled,
  onRemove
}: {
  choice: SubAgentModelChoice
  providers: ProviderInfoWire[]
  disabled: boolean
  onRemove: () => void
}): JSX.Element {
  const t = useT()
  const provider = findProvider(providers, choice.providerId)
  const trouble = providerTrouble(choice.providerId, providers)
  return (
    <SettingsRow>
      <div className={styles.choice}>
        <span className={styles.choiceMark}>
          <ProviderPresetMark mark={provider ? providerPresetMark(provider) : null} size={16} />
        </span>
        <span className={styles.choiceModel}>{choice.model}</span>
        <span className={styles.choiceProvider}>{provider?.displayName ?? choice.providerId}</span>
        {trouble && (
          <span className={styles.choiceStatus}>
            <StatusIndicator tone="warning" />
            {t(trouble === 'missing' ? 'settings.llm.subAgentModels.missing' : 'settings.llm.subAgentModels.signedOut')}
          </span>
        )}
      </div>
      <IconButton
        icon={<X size={14} />}
        label={t('settings.llm.subAgentModels.remove', { model: choice.model })}
        disabled={disabled}
        onClick={onRemove}
      />
    </SettingsRow>
  )
}

function AddChoiceRow({
  providers,
  initialProviderId,
  existing,
  onAdd,
  onCancel
}: {
  providers: ProviderInfoWire[]
  initialProviderId: string
  existing: SubAgentModelChoice[]
  onAdd: (choice: SubAgentModelChoice) => void
  onCancel: () => void
}): JSX.Element {
  const t = useT()
  const [providerId, setProviderId] = useState(initialProviderId)
  const [manualModel, setManualModel] = useState('')
  const catalog = useModelCatalog(providerId, Boolean(providerId))
  const loading = catalog.status === 'loading' || catalog.status === 'idle'
  const manual = !loading && catalog.models.length === 0
  const options = catalog.models.map((model) => {
    const taken = hasSubAgentModelChoice(existing, { providerId, model: model.id })
    return {
      value: model.id,
      label: model.id,
      disabled: taken,
      description: taken ? t('settings.llm.subAgentModels.added') : undefined
    }
  })

  function addManual(): void {
    const model = manualModel.trim()
    if (!model || hasSubAgentModelChoice(existing, { providerId, model })) return
    onAdd({ providerId, model })
  }

  return (
    <SettingsRow>
      <div className={styles.selectionRow}>
        <ProviderSelect
          requireReady
          id="settings-subagent-choice-provider"
          value={providerId}
          providers={providers}
          ariaLabel={t('settings.llm.subAgentProvider')}
          style={{ width: 200, flexShrink: 0 }}
          onValueChange={(next) => {
            setProviderId(next)
            setManualModel('')
          }}
        />
        <div className={styles.modelPicker}>
          {manual ? (
            <Input
              aria-label={t('settings.llm.subAgentModels.model')}
              value={manualModel}
              placeholder={t('settings.llm.subAgentModels.manualPlaceholder')}
              mono
              autoFocus
              onChange={(event) => setManualModel(event.currentTarget.value)}
              onKeyDown={(event) => {
                if (event.key === 'Enter') {
                  event.preventDefault()
                  addManual()
                }
                if (event.key === 'Escape') onCancel()
              }}
            />
          ) : (
            <SettingsSelect
              id="settings-subagent-choice-model"
              value=""
              options={options}
              disabled={loading}
              placeholder={loading ? t('composer.modelListLoading') : t('settings.llm.subAgentModels.chooseModel')}
              ariaLabel={t('settings.llm.subAgentModels.model')}
              style={{ width: '100%' }}
              adaptiveWidth={false}
              menuMaxHeight={320}
              onValueChange={(model) => onAdd({ providerId, model })}
            />
          )}
        </div>
      </div>
      <IconButton icon={<X size={14} />} label={t('common.cancel')} onClick={onCancel} />
    </SettingsRow>
  )
}

function initialProvider(providers: ProviderInfoWire[], mainProviderId: string): string {
  const ready = providers.filter(providerUsable)
  return ready.find((provider) => provider.id.toLowerCase() !== mainProviderId.toLowerCase())?.id
    ?? ready[0]?.id
    ?? mainProviderId
}
