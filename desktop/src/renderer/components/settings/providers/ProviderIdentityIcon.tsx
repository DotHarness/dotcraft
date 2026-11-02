import type { CSSProperties, JSX } from 'react'
import { Plug } from 'lucide-react'
import { ProviderMark } from '../../ui/ProviderMark'
import { providerPresetMark } from './providerPresets'

interface ProviderIdentityIconProps {
  provider: Parameters<typeof providerPresetMark>[0]
  size?: number
}

export function ProviderIdentityIcon({ provider, size = 28 }: ProviderIdentityIconProps): JSX.Element {
  const mark = providerPresetMark(provider)
  return (
    <span style={frameStyle(size)} aria-hidden="true">
      {mark ? <ProviderMark kind={mark} size={size} style={MARK_STYLE} /> : <Plug size={Math.round(size * 0.75)} strokeWidth={1.8} />}
    </span>
  )
}

const MARK_STYLE: CSSProperties = {
  width: '75%',
  height: '75%',
  display: 'block'
}

function frameStyle(size: number): CSSProperties {
  return {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: size + 12,
    height: size + 12,
    borderRadius: 'var(--identity-mark-radius-list)',
    background: 'var(--bg-tertiary)',
    border: '1px solid var(--border-default)',
    color: 'var(--text-primary)',
    flexShrink: 0
  }
}
