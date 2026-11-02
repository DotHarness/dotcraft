import { CircleStop } from 'lucide-react'
import { useLocale } from '../../contexts/LocaleContext'
import type { ConversationItem } from '../../types/conversation'
import { formatTaskStopLabel, type TaskStopDisplay } from '../../utils/taskStopDisplay'
import { RunningShimmer } from '../ui/RunningShimmer'
import { SubAgentEndingChips } from './SubAgentChips'
import { ToolDisclosure } from './ToolDisclosure'

export function TaskStopToolRow({ item, display, running, threadId }: {
  item: ConversationItem
  display: TaskStopDisplay
  running: boolean
  threadId: string
}): JSX.Element {
  const locale = useLocale()
  if (display.kind === 'agent') {
    return <SubAgentEndingChips items={[item]} parentThreadId={threadId} running={running} />
  }
  const label = formatTaskStopLabel(display, locale)
  return (
    <ToolDisclosure
      expanded={false}
      onToggle={() => {}}
      expandable={false}
      callId={item.toolCallId}
      icon={{ icon: CircleStop }}
      title={running
        ? <RunningShimmer className="dc-tool-row-running-label">{label}</RunningShimmer>
        : label}
    />
  )
}
