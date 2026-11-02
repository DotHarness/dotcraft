import type { CSSProperties, JSX } from 'react'
import { Image as ImageIcon, Pencil, Plus } from 'lucide-react'
import type { ProviderBackendInfo } from '@dotcraft/sdk/contracts'
import { useT } from '../../../contexts/LocaleContext'
import {
  findProviderPreference,
  type ModelPreference,
  type ProviderPreferences
} from '../../../../shared/modelPreference'
import { ActionTooltip } from '../../ui/ActionTooltip'
import { Button } from '../../ui/Button'
import { IconButton } from '../../ui/IconButton'
import { canProviderCreateImages } from '../panels/imageGeneration/imageGenerationModel'
import type { ProviderInfoWire } from '../providerInfo'
import { ProviderModelSummary } from '../ProviderModelSummary'
import { SettingsGroup } from '../SettingsGroup'
import { settingsPlaceholderStyle } from '../settingsTypography'
import { providerDetails, providerProblem } from './providerDraft'
import { ProviderIdentityIcon } from './ProviderIdentityIcon'

interface ProviderListGroupProps {
  providers: ProviderInfoWire[]
  providersLoading: boolean
  providerBackends: ProviderBackendInfo[] | null
  selectedProviderId: string
  workspacePreference: ModelPreference
  providerPreferences: ProviderPreferences
  applying: boolean
  editingEnabled: boolean
  onUse: (providerId: string) => void
  onEdit: (provider: ProviderInfoWire) => void
  onAdd: () => void
}

function providerRowStyle(active: boolean): CSSProperties {
  return {
    border: '1px solid var(--border-default)',
    borderRadius: '10px',
    padding: '14px 16px',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '16px',
    cursor: 'pointer',
    textAlign: 'left',
    background: active
      ? 'color-mix(in srgb, var(--accent) 8%, var(--bg-secondary))'
      : 'var(--bg-secondary)',
    borderColor: active
      ? 'color-mix(in srgb, var(--accent) 45%, var(--border-default))'
      : 'var(--border-default)'
  }
}

export function ProviderListGroup({
  providers,
  providersLoading,
  providerBackends,
  selectedProviderId,
  workspacePreference,
  providerPreferences,
  applying,
  editingEnabled,
  onUse,
  onEdit,
  onAdd
}: ProviderListGroupProps): JSX.Element {
  const t = useT()
  const countLabel = providers.length === 1
    ? t('settings.llm.providersCount.one', { count: providers.length })
    : t('settings.llm.providersCount.other', { count: providers.length })

  return (
    <SettingsGroup
      title={t('settings.llm.providersTitle')}
      description={providersLoading ? t('settings.llm.providersLoading') : countLabel}
      headerAction={
        <Button variant="primary" iconLeft={<Plus size={15} aria-hidden />} disabled={!editingEnabled} onClick={onAdd}>
          {t('settings.llm.addProvider')}
        </Button>
      }
      flush
    >
      <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
        {providersLoading && (
          <div style={{ fontSize: '13px', color: 'var(--text-dimmed)' }}>
            {t('settings.llm.providersLoading')}
          </div>
        )}

        {!providersLoading && providers.length === 0 && (
          <div style={{ padding: '8px 0' }}>
            <div style={{ fontSize: '14px', color: 'var(--text-primary)', marginBottom: '6px' }}>
              {t('settings.llm.emptyProvidersTitle')}
            </div>
            <div style={settingsPlaceholderStyle()}>
              {t('settings.llm.emptyProvidersHint')}
            </div>
          </div>
        )}

        {!providersLoading && providers.map((provider) => {
          const active = provider.id === selectedProviderId
          const rememberedMainAgentPreference = active
            ? workspacePreference
            : findProviderPreference(providerPreferences, provider.id)
          const problem = providerProblem(provider, providerBackends, t)
          return (
            <div
              key={provider.id}
              role="button"
              tabIndex={0}
              aria-label={t('settings.llm.useProviderAria', { name: provider.displayName })}
              aria-pressed={active}
              aria-disabled={applying || undefined}
              onClick={() => {
                if (!active && !applying) onUse(provider.id)
              }}
              onKeyDown={(event) => {
                if (event.key === 'Enter' || event.key === ' ') {
                  event.preventDefault()
                  if (!active && !applying) onUse(provider.id)
                }
              }}
              style={providerRowStyle(active)}
            >
              <ActionTooltip label={providerDetails(provider, providerBackends, t)}>
                <span style={{ display: 'inline-flex', flexShrink: 0 }}>
                  <ProviderIdentityIcon provider={provider} size={30} />
                </span>
              </ActionTooltip>
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', minWidth: 0 }}>
                  <div style={{
                    flexShrink: 0,
                    maxWidth: '60%',
                    overflow: 'hidden',
                    textOverflow: 'ellipsis',
                    whiteSpace: 'nowrap',
                    fontSize: '15px',
                    fontWeight: 600,
                    color: 'var(--text-primary)'
                  }}>
                    {provider.displayName}
                  </div>
                  {canProviderCreateImages(provider) && (
                    <ActionTooltip label={t('settings.llm.imageGeneration.createsImages')}>
                      <span
                        role="img"
                        aria-label={t('settings.llm.imageGeneration.createsImages')}
                        style={{ display: 'inline-flex', flexShrink: 0, color: 'var(--text-dimmed)' }}
                      >
                        <ImageIcon aria-hidden size={14} strokeWidth={1.75} />
                      </span>
                    </ActionTooltip>
                  )}
                  {problem && (
                    <span style={{
                      flex: 1,
                      minWidth: 0,
                      overflow: 'hidden',
                      textOverflow: 'ellipsis',
                      whiteSpace: 'nowrap',
                      fontSize: '12px',
                      color: 'var(--warning-text)'
                    }}>
                      {problem}
                    </span>
                  )}
                </div>
                {rememberedMainAgentPreference ? (
                  <ProviderModelSummary main={rememberedMainAgentPreference} />
                ) : (
                  <div style={{ marginTop: '4px', fontSize: '12px', color: 'var(--text-dimmed)' }}>
                    {t('settings.llm.providerSummaryNoModel')}
                  </div>
                )}
              </div>
              <span
                onClick={(event) => event.stopPropagation()}
                onKeyDown={(event) => event.stopPropagation()}
                style={{ flexShrink: 0, display: 'inline-flex' }}
              >
                <IconButton
                  icon={<Pencil size={15} />}
                  label={t('settings.llm.editProviderAria', { name: provider.displayName })}
                  tooltipLabel={t('settings.llm.editTitle')}
                  disabled={!editingEnabled}
                  onClick={() => onEdit(provider)}
                />
              </span>
            </div>
          )
        })}
      </div>
    </SettingsGroup>
  )
}
