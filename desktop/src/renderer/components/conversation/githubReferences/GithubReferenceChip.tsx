import type { AnchorHTMLAttributes, JSX, MouseEvent } from 'react'
import { CircleDot, GitPullRequest } from 'lucide-react'
import { useT } from '../../../contexts/LocaleContext'
import { useConversationStore } from '../../../stores/conversationStore'
import { useGithubRefLoad, useWorkspaceGithubRepo } from '../../../stores/githubRefStore'
import {
  githubRefShortLabel,
  truncateGithubRefTitle,
  usesGithubRefLabel,
  type GithubRefTarget
} from '../../../../shared/githubReferences'
import { githubRefState } from './githubRefState'
import { useGithubRefHoverCard } from './useGithubRefHoverCard'

export function GithubReferenceChip({
  target,
  href,
  text,
  linkProps,
  onOpenLink
}: {
  target: GithubRefTarget
  href: string
  text: string
  linkProps: AnchorHTMLAttributes<HTMLAnchorElement>
  onOpenLink: (url: string, event: MouseEvent<HTMLElement>) => void
}): JSX.Element {
  const t = useT()
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const workspaceRepo = useWorkspaceGithubRepo(workspacePath)
  const load = useGithubRefLoad(target)
  const known = load.status === 'ready' ? load.ref : null
  const state = known ? githubRefState(known) : null
  const Icon = state?.Icon ?? (target.kind === 'issue' ? CircleDot : GitPullRequest)
  const labelled = usesGithubRefLabel(text, href, target)
  const shortLabel = githubRefShortLabel(target, workspaceRepo)
  const ariaLabel = [state ? t(state.labelKey) : null, shortLabel, known?.title].filter(Boolean).join(' · ')
  const { anchorProps, card } = useGithubRefHoverCard<HTMLAnchorElement>({ load, label: ariaLabel, onOpenLink })

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
          {labelled && known?.title ? <span className="dc-gh-ref__title"> · {truncateGithubRefTitle(known.title)}</span> : null}
        </span>
      </a>
      {card}
    </>
  )
}
