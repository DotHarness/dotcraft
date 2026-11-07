import type { JSX } from 'react'
import type { AppLocale } from '../../../shared/locales'
import { openThread } from '../../utils/desktopNavigation'
import {
  formatDesktopToolLabel,
  getDesktopToolIcon,
  getDesktopToolTargetThreadId,
  type DesktopToolOperation
} from '../../utils/desktopToolDisplay'
import type { ConversationItem } from '../../types/conversation'
import { RunningShimmer } from '../ui/RunningShimmer'
import { ToolDisclosure } from './ToolDisclosure'
import styles from './DesktopToolRow.module.css'

export function DesktopToolRow({ item, operation, running, success, locale }: {
  item: ConversationItem
  operation: DesktopToolOperation
  running: boolean
  success: boolean
  locale: AppLocale
}): JSX.Element {
  const label = formatDesktopToolLabel(operation, running ? 'active' : success ? 'completed' : 'failed', locale)
  const threadId = running || success ? getDesktopToolTargetThreadId(operation, item.arguments) : null
  const title = running
    ? <RunningShimmer className="dc-tool-row-running-label">{label}</RunningShimmer>
    : label
  return (
    <ToolDisclosure
      expanded={false}
      onToggle={() => undefined}
      expandable={false}
      callId={item.toolCallId}
      icon={{ icon: getDesktopToolIcon(operation) }}
      title={threadId
        ? <button type="button" className={styles.link} onClick={() => void openThread(threadId)}>{title}</button>
        : title}
    />
  )
}
