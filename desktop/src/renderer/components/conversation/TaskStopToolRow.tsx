import { useState } from 'react'
import { CircleStop } from 'lucide-react'
import type { ConversationItem } from '../../types/conversation'
import type { TaskStopDisplay } from '../../utils/taskStopDisplay'
import { RunningShimmer } from '../ui/RunningShimmer'
import { ToolDisclosure } from './ToolDisclosure'
import styles from './TaskStopToolRow.module.css'

export function TaskStopToolRow({ item, display, running }: {
  item: ConversationItem
  display: TaskStopDisplay
  running: boolean
}): JSX.Element {
  const [expanded, setExpanded] = useState(false)
  const expandable = !running && !!display.message
  return (
    <ToolDisclosure
      expanded={expandable && expanded}
      onToggle={() => setExpanded((value) => !value)}
      expandable={expandable}
      callId={item.toolCallId}
      icon={{ icon: CircleStop }}
      title={running
        ? <RunningShimmer className="dc-tool-row-running-label">{display.label}</RunningShimmer>
        : display.label}
    >
      <div className={`selectable dc-tool-panel-surface ${styles.message}`} data-padded="true">
        {display.message}
      </div>
    </ToolDisclosure>
  )
}
