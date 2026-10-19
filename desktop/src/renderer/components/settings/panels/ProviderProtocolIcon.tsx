import type { CSSProperties, JSX } from 'react'
import { ProviderMark, type ProviderMarkKind } from '../../ui/ProviderMark'
import { Plug } from 'lucide-react'
import {
  ANTHROPIC_PROTOCOL,
  normalizeProviderProtocol,
  tryNormalizeProviderProtocol
} from '../../../../shared/providerProtocols'

export type ProviderProtocol = string

interface ProviderProtocolIconProps {
  protocol: ProviderProtocol
  size?: number
}

export function getProviderProtocolMarkKind(protocol: ProviderProtocol): ProviderMarkKind {
  return normalizeProviderProtocol(protocol) === ANTHROPIC_PROTOCOL ? 'anthropic' : 'openai'
}

export function ProviderProtocolIcon({
  protocol,
  size = 28
}: ProviderProtocolIconProps): JSX.Element {
  return (
    <span style={frameStyle(size)} aria-hidden="true">
      {tryNormalizeProviderProtocol(protocol)
        ? <ProviderMark kind={getProviderProtocolMarkKind(protocol)} size={size} style={MARK_STYLE} />
        : <Plug size={size} />}
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
