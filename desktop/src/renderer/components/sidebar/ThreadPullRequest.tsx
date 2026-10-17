import type { JSX } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { githubRefUrl, truncateGithubRefTitle, type GithubPullSummary } from '../../../shared/githubReferences'
import { GithubRefGlyph, pullRequestLabel } from '../conversation/githubReferences/githubRefState'
import { openGithubLink } from '../conversation/githubReferences/openGithubLink'
import { ActionTooltip } from '../ui/ActionTooltip'
import { SidebarEntryDetailsActionRow } from './SidebarEntryDetailsActionRow'

export function ThreadPullRequestGlyph({ pull }: { pull: GithubPullSummary }): JSX.Element {
  const t = useT()
  const label = pullRequestLabel(pull, t)
  return (
    <ActionTooltip label={label} placement="top">
      <span className="dc-thread-pull-glyph" role="img" aria-label={label}>
        <GithubRefGlyph refData={pull} size={16} />
      </span>
    </ActionTooltip>
  )
}

export function ThreadPullRequestRow({ pull }: { pull: GithubPullSummary }): JSX.Element {
  const t = useT()
  return (
    <SidebarEntryDetailsActionRow
      icon={<GithubRefGlyph refData={pull} size={14} />}
      label={`#${pull.number} · ${truncateGithubRefTitle(pull.title)}`}
      title={pull.title}
      ariaLabel={pullRequestLabel(pull, t)}
      affordance
      onClick={(event) => {
        event.stopPropagation()
        void openGithubLink(githubRefUrl(pull), event, t)
      }}
    />
  )
}
