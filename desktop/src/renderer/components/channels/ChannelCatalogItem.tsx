import type { KeyboardEvent } from 'react'
import { Button } from '../ui/Button'
import { ActionTooltip } from '../ui/ActionTooltip'
import { Spinner } from '../ui/Spinner'
import type { ChannelConnectionState } from './ChannelCard'
import { IdentityMark } from '../ui/IdentityMark'
import { IdentityMarkFallback } from '../ui/IdentityMarkFallback'

interface ChannelCatalogItemProps {
  logoPath?: string
  title: string
  subtitle: string
  badgeText?: string
  status: ChannelConnectionState
  statusLabel: string
  active: boolean
  onOpen: () => void
  action?: { label: string; run: () => void; loading?: boolean }
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
  action
}: ChannelCatalogItemProps): JSX.Element {
  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>): void {
    if (event.target !== event.currentTarget || (event.key !== 'Enter' && event.key !== ' ')) return
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
      <ChannelIcon logoPath={logoPath} />
      <span className="dc-channel-catalog-item__copy">
        <span className="dc-channel-catalog-item__title-line">
          <strong className="dc-channel-catalog-item__title">{title}</strong>
          {badgeText && <span className="dc-channel-catalog-item__badge">{badgeText}</span>}
        </span>
        <span className="dc-channel-catalog-item__description">{subtitle}</span>
      </span>
      <span className="dc-channel-catalog-item__action">
        {action ? (
          <Button size="sm" loading={action.loading}
            onClick={(event) => {
              event.stopPropagation()
              action.run()
            }}
          >
            {action.label}
          </Button>
        ) : status === 'connecting' ? (
          <Spinner size={14} label={statusLabel} />
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

export function ChannelIcon({ logoPath }: { logoPath?: string }): JSX.Element {
  return <IdentityMark role="list" src={logoPath} fallback={<IdentityMarkFallback kind="channel" />} />
}
