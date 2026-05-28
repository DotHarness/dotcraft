import type { CSSProperties } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { ActionTooltip } from '../ui/ActionTooltip'

interface VariantBadgeProps {
  compact?: boolean
}

export function VariantBadge({ compact = false }: VariantBadgeProps): JSX.Element {
  const t = useT()
  return (
    <ActionTooltip label={t('skillVariant.badgeTooltip')} placement="top">
      <span style={compact ? compactBadge : badge}>{t('skillVariant.badge')}</span>
    </ActionTooltip>
  )
}

const badge: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  height: '18px',
  padding: '0 8px',
  borderRadius: '999px',
  background: 'linear-gradient(135deg, rgba(64, 156, 255, 0.95), rgba(165, 98, 255, 0.92))',
  color: 'white',
  fontSize: '10px',
  fontWeight: 700,
  lineHeight: 1,
  whiteSpace: 'nowrap',
  boxShadow: '0 0 0 1px rgba(255, 255, 255, 0.14) inset'
}

const compactBadge: CSSProperties = {
  ...badge,
  height: '16px',
  padding: '0 7px',
  fontSize: '9px'
}
