import { useState, type JSX } from 'react'
import { Brain } from 'lucide-react'
import { translate } from '../../../shared/locales'
import { useLocale } from '../../contexts/LocaleContext'
import type { ConversationItem } from '../../types/conversation'
import type { FileDiff } from '../../types/toolCall'
import { collectMemoryFileNames, formatMemoryUpdatedLabel } from '../../utils/memoryToolDisplay'
import { toAbsoluteWorkspacePath } from '../../utils/workspacePaths'
import { RunningShimmer } from '../ui/RunningShimmer'
import { FileDiffStats } from './FileDiffStats'
import { InlineDiffView } from './InlineDiffView'
import { ToolDisclosure } from './ToolDisclosure'
import { useTranscriptItemDiffs, useTranscriptWorkspacePath } from './transcriptDiffs'
import styles from './MemoryUpdateCard.module.css'

const MEMORY_ICON = { icon: Brain }

interface MemoryUpdateCardProps {
  items: readonly ConversationItem[]
  running: boolean
}

export function MemoryUpdateCard({ items, running }: MemoryUpdateCardProps): JSX.Element {
  const locale = useLocale()
  if (!running) return <MemoryUpdatedRow items={items} />
  return (
    <ToolDisclosure
      expanded={false}
      onToggle={() => {}}
      expandable={false}
      icon={MEMORY_ICON}
      title={(
        <RunningShimmer className="dc-tool-row-running-label">
          {translate(locale, 'toolCall.memory.updating')}
        </RunningShimmer>
      )}
    />
  )
}

function MemoryUpdatedRow({ items }: { items: readonly ConversationItem[] }): JSX.Element {
  const locale = useLocale()
  const workspacePath = useTranscriptWorkspacePath()
  const itemDiffs = useTranscriptItemDiffs()
  const [expanded, setExpanded] = useState(false)
  const [hovered, setHovered] = useState(false)
  const diffs = items
    .map((item) => itemDiffs.get(item.id))
    .filter((diff): diff is FileDiff => (diff?.diffHunks.length ?? 0) > 0)
  const additions = diffs.reduce((sum, diff) => sum + diff.additions, 0)
  const deletions = diffs.reduce((sum, diff) => sum + diff.deletions, 0)

  return (
    <ToolDisclosure
      expanded={expanded}
      onToggle={() => setExpanded((value) => !value)}
      expandable={diffs.length > 0}
      onHoverChange={setHovered}
      icon={MEMORY_ICON}
      title={formatMemoryUpdatedLabel(collectMemoryFileNames(items).length, locale)}
      accessory={diffs.length > 0 && !expanded
        ? <FileDiffStats additions={additions} deletions={deletions} tone={hovered ? 'semantic' : 'inherit'} />
        : undefined}
    >
      <div className={styles.diffs}>
        {diffs.map((diff, index) => (
          <div key={index} className="dc-tool-panel-surface" data-padded="false">
            <InlineDiffView
              diff={diff}
              variant="embedded"
              headerMode="compact"
              presentation="conversation-file-tool"
              resolvedPath={workspacePath ? toAbsoluteWorkspacePath(workspacePath, diff.filePath) : diff.filePath}
              locale={locale}
            />
          </div>
        ))}
      </div>
    </ToolDisclosure>
  )
}
