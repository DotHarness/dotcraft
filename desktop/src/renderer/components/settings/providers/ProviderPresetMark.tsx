import type { JSX } from 'react'
import { Plug } from 'lucide-react'
import { ProviderMark, type ProviderMarkKind } from '../../ui/ProviderMark'

export function ProviderPresetMark({ mark, size = 16 }: { mark: ProviderMarkKind | null; size?: number }): JSX.Element {
  return mark ? <ProviderMark kind={mark} size={size} /> : <Plug size={size} strokeWidth={1.8} aria-hidden="true" />
}
