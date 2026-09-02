import { useState } from 'react'
import type { ReactNode } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { formatDurationShort } from '../../utils/formatDurationShort'
import { ToolDisclosure } from './ToolDisclosure'

interface TurnCollapsedSummaryProps {
  elapsedMs: number
  /** Suppresses the rule when no answer follows the summary. */
  followedByContent: boolean
  children: ReactNode
}

export function TurnCollapsedSummary({
  elapsedMs,
  followedByContent,
  children
}: TurnCollapsedSummaryProps): JSX.Element {
  const t = useT()
  const [expanded, setExpanded] = useState(false)

  const duration = formatDurationShort(elapsedMs)

  return (
    <ToolDisclosure
      expanded={expanded}
      onToggle={() => setExpanded((value) => !value)}
      title={t('conversation.turnCollapsed.workedFor', { duration })}
      variant={followedByContent ? 'turn' : undefined}
    >
      {children}
    </ToolDisclosure>
  )
}
