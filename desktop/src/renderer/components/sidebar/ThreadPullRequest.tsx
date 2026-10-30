import type { JSX } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { forgeRefNumberLabel, forgeRefUrl, truncateForgeRefTitle, type ForgePullSummary } from '../../../shared/forgeReferences'
import { ForgeRefGlyph, pullRequestLabel } from '../conversation/forgeReferences/forgeRefState'
import { openForgeLink } from '../conversation/forgeReferences/openForgeLink'
import { ActionTooltip } from '../ui/ActionTooltip'
import { SidebarEntryDetailsActionRow } from './SidebarEntryDetailsActionRow'

export function ThreadPullRequestGlyph({ pull }: { pull: ForgePullSummary }): JSX.Element {
  const t = useT()
  const label = pullRequestLabel(pull, t)
  return (
    <ActionTooltip label={label} placement="top">
      <span className="dc-thread-pull-glyph" role="img" aria-label={label}>
        <ForgeRefGlyph refData={pull} size={16} />
      </span>
    </ActionTooltip>
  )
}

export function ThreadPullRequestRow({ pull }: { pull: ForgePullSummary }): JSX.Element {
  const t = useT()
  return (
    <SidebarEntryDetailsActionRow
      icon={<ForgeRefGlyph refData={pull} size={14} />}
      label={`${forgeRefNumberLabel(pull)} · ${truncateForgeRefTitle(pull.title)}`}
      title={pull.title}
      ariaLabel={pullRequestLabel(pull, t)}
      affordance
      onClick={(event) => {
        event.stopPropagation()
        void openForgeLink(forgeRefUrl(pull), event, t)
      }}
    />
  )
}
