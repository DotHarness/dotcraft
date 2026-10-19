import type { MouseEvent, ReactPortal } from 'react'
import type { GithubRefLoad } from '../../../stores/githubRefStore'
import {
  useReferenceHoverCard,
  type ReferenceCardSide,
  type ReferenceHoverCardAnchorProps
} from '../referenceHoverCard'
import { GithubRefCardBody } from './GithubRefCard'

export function useGithubRefHoverCard<A extends HTMLElement>({
  load,
  label,
  preferredSide = 'below',
  onOpenLink,
  onOpen,
  overComposer = false
}: {
  load: GithubRefLoad
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
    children: <GithubRefCardBody load={load} onOpenLink={onOpenLink} />
  })
}
