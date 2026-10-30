import type { MouseEvent, ReactPortal } from 'react'
import type { ForgeRefLoad } from '../../../stores/forgeRefStore'
import type { ForgeRepo } from '../../../../shared/forgeReferences'
import {
  useReferenceHoverCard,
  type ReferenceCardSide,
  type ReferenceHoverCardAnchorProps
} from '../referenceHoverCard'
import { ForgeRefCardBody } from './ForgeRefCard'

export function useForgeRefHoverCard<A extends HTMLElement>({
  repo,
  load,
  label,
  preferredSide = 'below',
  onOpenLink,
  onOpen,
  overComposer = false
}: {
  repo: ForgeRepo | null
  load: ForgeRefLoad
  label: string
  preferredSide?: ReferenceCardSide
  onOpenLink: (url: string, event: MouseEvent<HTMLElement>) => void
  onOpen?: () => void
  overComposer?: boolean
}): { anchorProps: ReferenceHoverCardAnchorProps<A>; card: ReactPortal | null } {
  return useReferenceHoverCard<A>({
    label,
    className: 'dc-gh-card dc-gh-card--floating',
    layoutKey: load,
    compact: load.status === 'error',
    preferredSide,
    onOpen,
    overComposer,
    children: <ForgeRefCardBody repo={repo} load={load} onOpenLink={onOpenLink} />
  })
}
