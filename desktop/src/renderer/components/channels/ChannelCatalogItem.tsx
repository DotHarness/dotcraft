import type { KeyboardEvent } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { PluginInstallButton } from '../plugins/PluginInstallButton'
import { ActionTooltip } from '../ui/ActionTooltip'
import { RunningSpinner } from '../ui/RunningSpinner'
import type { ChannelConnectionState } from './ChannelCard'

interface ChannelCatalogItemProps {
  logoPath?: string
  title: string
  subtitle: string
  badgeText?: string
  status: ChannelConnectionState
  statusLabel: string
  active: boolean
  onOpen: () => void
  onInstall?: () => void
}

export function ChannelCatalogItem({
  logoPath,
  title,
  subtitle,
  badgeText,
  status,
  statusLabel,
  active,
  onOpen,
  onInstall
}: ChannelCatalogItemProps): JSX.Element {
  const t = useT()

  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>): void {
    if (event.key !== 'Enter' && event.key !== ' ') return
    event.preventDefault()
    onOpen()
  }

  return (
    <div
      className="dc-channel-catalog-item"
      data-active={active || undefined}
      role="button"
      tabIndex={0}
      onClick={onOpen}
      onKeyDown={handleKeyDown}
    >
      <ChannelIcon logoPath={logoPath} title={title} />
      <span className="dc-channel-catalog-item__copy">
        <span className="dc-channel-catalog-item__title-line">
          <strong className="dc-channel-catalog-item__title">{title}</strong>
          {badgeText && <span className="dc-channel-catalog-item__badge">{badgeText}</span>}
        </span>
        <span className="dc-channel-catalog-item__description">{subtitle}</span>
      </span>
      <span className="dc-channel-catalog-item__action">
        {status === 'notConfigured' && onInstall ? (
          <PluginInstallButton
            onClick={(event) => {
              event.stopPropagation()
              onInstall()
            }}
          >
            {t('plugins.install')}
          </PluginInstallButton>
        ) : status === 'connecting' ? (
          <RunningSpinner size={12} borderWidth={1.5} label={statusLabel} />
        ) : (
          <ActionTooltip label={statusLabel}>
            <span
              className="dc-channel-catalog-item__status"
              data-state={status}
              role="img"
              aria-label={statusLabel}
            >
              <span className="dc-channel-catalog-item__status-dot" aria-hidden />
            </span>
          </ActionTooltip>
        )}
      </span>
    </div>
  )
}

export function ChannelIcon({ logoPath, title }: { logoPath?: string; title: string }): JSX.Element {
  if (logoPath) {
    return <img className="dc-channel-catalog-item__icon" src={logoPath} alt="" width={40} height={40} />
  }
  return (
    <span className="dc-channel-catalog-item__icon dc-channel-catalog-item__icon--fallback" aria-hidden>
      {title.slice(0, 1).toUpperCase()}
    </span>
  )
}
