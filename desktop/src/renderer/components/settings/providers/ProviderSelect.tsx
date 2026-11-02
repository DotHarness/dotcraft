import type { CSSProperties, JSX } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import type { ProviderInfoWire } from '../providerInfo'
import { findProvider } from '../panels/subAgentModels/subAgentModels'
import { SettingsSelect, type SettingsSelectOption } from '../ui/SettingsSelect'
import { ProviderPresetMark } from './ProviderPresetMark'
import { providerUsable } from './providerDraft'
import { providerPresetMark } from './providerPresets'

interface ProviderSelectProps {
  id: string
  value: string
  providers: ProviderInfoWire[]
  disabled?: boolean
  requireReady?: boolean
  ariaLabel: string
  style?: CSSProperties
  onValueChange: (providerId: string) => void
}

export function ProviderSelect({
  id,
  value,
  providers,
  disabled,
  requireReady = false,
  ariaLabel,
  style,
  onValueChange
}: ProviderSelectProps): JSX.Element {
  const t = useT()
  const selected = findProvider(providers, value)
  const options: SettingsSelectOption[] = providers.map((provider) => {
    const ready = !requireReady || providerUsable(provider)
    return {
      value: provider.id,
      label: provider.displayName,
      icon: <ProviderPresetMark mark={providerPresetMark(provider)} size={15} />,
      disabled: !ready,
      description: ready ? undefined : t('settings.llm.subAgentModels.signedOut')
    }
  })
  if (value && !selected) {
    options.push({ value, label: value, disabled: true, description: t('settings.llm.subAgentModels.missing') })
  }

  return (
    <SettingsSelect
      id={id}
      value={selected?.id ?? value}
      options={options}
      disabled={disabled}
      ariaLabel={ariaLabel}
      style={style}
      adaptiveWidth={false}
      menuMinWidth={240}
      placeholder={t('settings.llm.chooseProvider')}
      triggerIcon={selected ? <ProviderPresetMark mark={providerPresetMark(selected)} size={15} /> : undefined}
      onValueChange={onValueChange}
    />
  )
}
