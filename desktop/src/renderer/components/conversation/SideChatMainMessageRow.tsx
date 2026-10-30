import { useState, type ReactNode } from 'react'
import { MessageSquareText } from 'lucide-react'
import { translate, type AppLocale } from '../../../shared/locales'
import type { SideChat } from '../../stores/sideChatStore'
import type { ConversationItem } from '../../types/conversation'
import { parseToolResultObject } from '../../utils/toolCallDisplay'
import { showMainThreadOf } from '../../utils/sideChat'
import { RunningShimmer } from '../ui/RunningShimmer'
import { ToolDisclosure } from './ToolDisclosure'

const TARGET_TOKEN = '__DOTCRAFT_SIDE_CHAT_TARGET__'

export function isSideChatMainMessage(operation: unknown, args: Record<string, unknown> | undefined): boolean {
  return operation === 'sendMessage'
    && typeof args?.target === 'string'
    && args.target.trim().toLowerCase() === 'main'
}

export function SideChatMainMessageRow({ item, chat, args, running, success, locale }: {
  item: ConversationItem
  chat: SideChat
  args: Record<string, unknown> | undefined
  running: boolean
  success: boolean
  locale: AppLocale
}): JSX.Element {
  const [expanded, setExpanded] = useState(false)
  const message = typeof args?.message === 'string' ? args.message.trim() : ''
  const parsed = parseToolResultObject(item.result)
  const error = !running && !success
    ? (typeof parsed?.error === 'string' ? parsed.error : item.result?.trim()) || null
    : null
  const key = running
    ? 'sideChat.toolMain.sending'
    : success ? 'sideChat.toolMain.sent' : 'sideChat.toolMain.failed'
  const target = translate(locale, 'sideChat.toolMain.target')
  const title = renderWithTarget(translate(locale, key, { target: TARGET_TOKEN }), (
    <button
      type="button"
      className="dc-subagent-name"
      onClick={(event) => { event.stopPropagation(); showMainThreadOf(chat) }}
      aria-label={translate(locale, 'sideChat.toolMain.openAria')}
    >
      {target}
    </button>
  ))
  const expandable = !!message || !!error

  return (
    <ToolDisclosure
      expanded={expandable && expanded}
      onToggle={() => setExpanded((value) => !value)}
      expandable={expandable}
      callId={item.toolCallId}
      icon={{ icon: MessageSquareText }}
      title={running ? <RunningShimmer className="dc-tool-row-running-label">{title}</RunningShimmer> : title}
    >
      <div
        className="selectable dc-tool-panel-surface"
        data-padded="true"
        style={{ whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}
      >
        {error ?? message}
      </div>
    </ToolDisclosure>
  )
}

function renderWithTarget(template: string, target: ReactNode): JSX.Element {
  const parts = template.split(TARGET_TOKEN)
  return (
    <span>
      {parts.map((part, index) => (
        <span key={index}>
          {part}
          {index < parts.length - 1 && target}
        </span>
      ))}
    </span>
  )
}
