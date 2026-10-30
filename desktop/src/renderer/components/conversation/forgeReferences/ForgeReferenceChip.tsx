import { useCallback, type AnchorHTMLAttributes, type JSX, type MouseEvent } from 'react'
import { CircleDot, GitPullRequest } from 'lucide-react'
import { useT } from '../../../contexts/LocaleContext'
import { useConversationStore } from '../../../stores/conversationStore'
import { useForgeRefLoad, useForgeRefStore, useWorkspaceForgeRepo } from '../../../stores/forgeRefStore'
import {
  forgeRefShortLabel,
  truncateForgeRefTitle,
  usesForgeRefLabel,
  type ForgeRefTarget
} from '../../../../shared/forgeReferences'
import { forgeRefState } from './forgeRefState'
import { useForgeRefHoverCard } from './useForgeRefHoverCard'

export function ForgeReferenceChip({
  target,
  href,
  text,
  linkProps,
  onOpenLink
}: {
  target: ForgeRefTarget
  href: string
  text: string
  linkProps: AnchorHTMLAttributes<HTMLAnchorElement>
  onOpenLink: (url: string, event: MouseEvent<HTMLElement>) => void
}): JSX.Element {
  const t = useT()
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const workspaceRepo = useWorkspaceForgeRepo(workspacePath)
  const load = useForgeRefLoad(target)
  const known = load.status === 'ready' ? load.ref : null
  const state = known ? forgeRefState(known) : null
  const Icon = state?.Icon ?? (target.kind === 'issue' ? CircleDot : GitPullRequest)
  const labelled = usesForgeRefLabel(text, href, target)
  const shortLabel = forgeRefShortLabel(target, workspaceRepo)
  const ariaLabel = [state ? t(state.labelKey) : null, shortLabel, known?.title].filter(Boolean).join(' · ')
  const { provider, host, path, kind, number } = target
  const revalidate = useCallback(() => {
    void useForgeRefStore.getState().ensureRef({ provider, host, path, kind, number })
  }, [host, kind, number, path, provider])
  const { anchorProps, card } = useForgeRefHoverCard<HTMLAnchorElement>({
    repo: target,
    load,
    label: ariaLabel,
    onOpenLink,
    onOpen: revalidate
  })

  return (
    <>
      <a
        className="dc-ref dc-ref-link dc-gh-ref"
        data-tone={state?.tone ?? 'unknown'}
        aria-label={labelled ? ariaLabel : undefined}
        {...linkProps}
        {...anchorProps}
      >
        <Icon size={12} strokeWidth={2.1} aria-hidden className="dc-gh-ref__icon" />
        <span>
          {labelled ? shortLabel : text}
          {labelled && known?.title ? <span className="dc-gh-ref__title"> · {truncateForgeRefTitle(known.title)}</span> : null}
        </span>
      </a>
      {card}
    </>
  )
}
