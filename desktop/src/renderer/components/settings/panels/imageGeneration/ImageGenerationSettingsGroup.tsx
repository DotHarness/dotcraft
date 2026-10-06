import type { JSX } from 'react'
import { TriangleAlert } from 'lucide-react'
import { useT } from '../../../../contexts/LocaleContext'
import { useConfigSetting } from '../../../../stores/configStore'
import { ActionTooltip } from '../../../ui/ActionTooltip'
import { PillSwitch } from '../../../ui/PillSwitch'
import { ProviderMark } from '../../../ui/ProviderMark'
import type { ProviderInfoWire } from '../../providerInfo'
import { SettingsGroup, SettingsRow } from '../../SettingsGroup'
import { SettingsSelect, type SettingsSelectOption } from '../../ui/SettingsSelect'
import { getProviderProtocolMarkKind } from '../ProviderProtocolIcon'
import { canProviderCreateImages, resolveImageGenerationTrouble } from './imageGenerationModel'
import styles from './ImageGenerationSettingsGroup.module.css'

const SELECT_ID = 'settings-image-generation-provider'

interface ImageGenerationSettingsGroupProps {
  providers: ProviderInfoWire[]
  providersLoading: boolean
  workspaceProviderId: string
  onEditProvider?: (provider: ProviderInfoWire) => void
  onAddProvider?: () => void
}

export function ImageGenerationSettingsGroup({
  providers,
  providersLoading,
  workspaceProviderId,
  onEditProvider,
  onAddProvider
}: ImageGenerationSettingsGroupProps): JSX.Element {
  const t = useT()
  const failureMessage = (error: string): string => t('settings.llm.imageGeneration.saveFailed', { error })
  const enabledSetting = useConfigSetting('Tools.ImageGeneration.Enabled', failureMessage)
  const providerSetting = useConfigSetting('Tools.ImageGeneration.Provider', failureMessage)
  const pending = enabledSetting.pending || providerSetting.pending
  const config = {
    enabled: enabledSetting.value === true,
    providerId: typeof providerSetting.value === 'string' ? providerSetting.value : ''
  }
  const anyEligible = providers.some(canProviderCreateImages)
  const trouble = providersLoading ? null : resolveImageGenerationTrouble(config, providers, workspaceProviderId)
  const workspaceProvider = providers.find((provider) => provider.id === workspaceProviderId)
  const cantCreate = t('settings.llm.imageGeneration.cantCreate')

  const options: SettingsSelectOption[] = [
    {
      value: '',
      label: t('settings.llm.imageGeneration.sameAsChat'),
      description: workspaceProvider?.displayName
    },
    ...providers.map((provider) => {
      const eligible = canProviderCreateImages(provider)
      return {
        value: provider.id,
        label: provider.displayName,
        icon: <ProviderMark kind={getProviderProtocolMarkKind(provider.protocol)} size={15} />,
        disabled: !eligible,
        description: eligible ? undefined : cantCreate
      }
    })
  ]
  if (config.providerId && !providers.some((provider) => provider.id === config.providerId)) {
    options.push({ value: config.providerId, label: config.providerId, disabled: true, description: cantCreate })
  }

  const troubleProvider = trouble?.kind === 'provider' ? trouble.provider : null
  const troubleAction = trouble?.kind === 'none'
    ? onAddProvider && { label: t('settings.llm.imageGeneration.addProvider'), run: onAddProvider }
    : troubleProvider && onEditProvider
      ? { label: t('settings.llm.imageGeneration.editProvider'), run: () => onEditProvider(troubleProvider) }
      : undefined

  return (
    <SettingsGroup title={t('settings.llm.imageGeneration.title')}>
      <SettingsRow
        label={t('settings.llm.imageGeneration.enabled')}
        description={t('settings.llm.imageGeneration.enabledHint')}
        control={
          <PillSwitch
            checked={config.enabled}
            disabled={pending}
            aria-busy={pending || undefined}
            aria-label={t('settings.llm.imageGeneration.enabled')}
            onChange={(enabled) => void enabledSetting.set(enabled)}
          />
        }
      />
      <SettingsRow
        label={t('settings.llm.imageGeneration.provider')}
        description={t('settings.llm.imageGeneration.providerHint')}
        htmlFor={SELECT_ID}
        control={
          <ActionTooltip
            label=""
            disabledReason={config.enabled ? undefined : t('settings.llm.imageGeneration.turnOnFirst')}
          >
            <SettingsSelect
              id={SELECT_ID}
              value={config.providerId}
              options={options}
              disabled={!config.enabled || pending || (!providersLoading && !anyEligible)}
              ariaLabel={t('settings.llm.imageGeneration.provider')}
              style={{ width: '240px' }}
              onValueChange={(providerId) => void providerSetting.set(providerId || null)}
            />
          </ActionTooltip>
        }
      />
      {trouble && (
        <SettingsRow>
          <div className={styles.notice} role="status">
            <TriangleAlert size={20} aria-hidden="true" />
            <div className={styles.title}>
              {trouble.kind === 'none'
                ? t('settings.llm.imageGeneration.none')
                : t('settings.llm.imageGeneration.providerCantCreate', {
                  name: trouble.provider?.displayName ?? trouble.providerId
                })}
              {troubleAction && (
                <>
                  {' '}
                  <button type="button" className={styles.link} onClick={troubleAction.run}>
                    {troubleAction.label}
                  </button>
                </>
              )}
            </div>
          </div>
        </SettingsRow>
      )}
    </SettingsGroup>
  )
}
