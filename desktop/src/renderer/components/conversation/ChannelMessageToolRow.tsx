import { useState } from 'react'
import { Send } from 'lucide-react'
import { translate, type AppLocale } from '../../../shared/locales'
import type { ConversationItem } from '../../types/conversation'
import type { ChannelMessageDisplay } from '../../utils/channelMessageDisplay'
import { RunningShimmer } from '../ui/RunningShimmer'
import { ToolDisclosure } from './ToolDisclosure'
import styles from './ChannelMessageToolRow.module.css'

export function ChannelMessageToolRow({ item, display, running, locale }: {
  item: ConversationItem
  display: ChannelMessageDisplay
  running: boolean
  locale: AppLocale
}): JSX.Element {
  const [expanded, setExpanded] = useState(false)
  const expandable = !!display.text || display.attachments.length > 0 || !!display.error
  return (
    <ToolDisclosure
      expanded={expandable && expanded}
      onToggle={() => setExpanded((value) => !value)}
      expandable={expandable}
      callId={item.toolCallId}
      icon={{ icon: Send }}
      title={running
        ? <RunningShimmer className="dc-tool-row-running-label">{display.label}</RunningShimmer>
        : display.label}
    >
      <div className={`selectable dc-tool-panel-surface ${styles.panel}`} data-padded="true">
        {display.text ? <div className={styles.text}>{display.text}</div> : null}
        {display.attachments.length > 0 ? (
          <ul className={styles.attachments}>
            {display.attachments.map((attachment, index) => (
              <li key={index}>
                {attachment.sentAsFile
                  ? translate(locale, 'toolCall.channelMessage.sentAsFile', { name: attachment.name })
                  : attachment.name}
              </li>
            ))}
          </ul>
        ) : null}
        {display.error ? <div className={styles.error}>{display.error}</div> : null}
      </div>
    </ToolDisclosure>
  )
}
