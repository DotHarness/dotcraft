import { useMemo, useState, type ReactNode } from 'react'
import { MessagesSquare } from 'lucide-react'
import { useLocale } from '../../contexts/LocaleContext'
import { translate } from '../../../shared/locales'
import { useSubAgentLookup } from '../../hooks/useSubAgentLookup'
import type { ConversationItem } from '../../types/conversation'
import {
  describeBoardAccessory,
  describeBoardCall,
  firstLine,
  parseBoardCall,
  type BoardCall,
  type BoardLabel,
  type BoardOperation
} from '../../utils/boardToolDisplay'
import { extractBoardArguments } from '../../utils/toolCallDisplay'
import { RunningShimmer } from '../ui/RunningShimmer'
import { BoardAgentList, BoardAgentName, renderWithNode, type BoardAgentContext } from './BoardAgentName'
import { BoardToolDetail, hasBoardToolDetail } from './BoardToolDetail'
import { ToolDisclosure } from './ToolDisclosure'
import styles from './BoardToolCard.module.css'

const BOARD_ICON = { icon: MessagesSquare }
const QUOTE_CHARS = 140

export function BoardToolCard({
  item,
  operation,
  threadId,
  running
}: {
  item: ConversationItem
  operation: BoardOperation
  threadId: string
  running: boolean
}): JSX.Element {
  const { lookup } = useSubAgentLookup(threadId)
  const call = useMemo(() => parseBoardCall({
    operation,
    args: item.arguments ?? (item.argumentsPreview ? extractBoardArguments(item.argumentsPreview) : undefined),
    result: item.result,
    running,
    invocationError: !running && item.success === false
      ? item.errorMessage ?? item.resultPreview ?? item.result ?? ''
      : null
  }), [item.arguments, item.argumentsPreview, item.errorMessage, item.result, item.resultPreview, item.success, operation, running])
  const context: BoardAgentContext = { threadId, lookup }

  if (call.operation === 'post' && call.phase !== 'failed') return <BoardPostRow call={call} context={context} />
  return <BoardCallRow call={call} context={context} />
}

function BoardPostRow({ call, context }: { call: BoardCall; context: BoardAgentContext }): JSX.Element {
  const locale = useLocale()
  const [expanded, setExpanded] = useState(false)
  const running = call.phase === 'running'
  const label = useBoardLabel(describeBoardCall(call), context)
  const text = call.text ?? ''
  const expandable = !running && (Array.from(text).length > QUOTE_CHARS || /\r?\n/.test(text.trim()))

  return (
    <ToolDisclosure
      expanded={expanded}
      onToggle={() => setExpanded((value) => !value)}
      expandable={expandable}
      title={
        <span className={styles.post}>
          <span className={styles.postHead}>
            <MessagesSquare className="dc-tool-row-icon" size={16} strokeWidth={1.6} aria-hidden />
            {running
              ? <RunningShimmer className="dc-tool-row-running-label">{label}</RunningShimmer>
              : <span className={styles.ellipsis}>{label}</span>}
          </span>
          {text && !expanded && <span className={styles.quote}>{firstLine(text, QUOTE_CHARS)}</span>}
          {call.recipients.length > 0 && (
            <span className={styles.recipients}>
              {renderWithNode(
                (names) => translate(locale, 'toolCall.board.notified', { names }),
                <BoardAgentList paths={call.recipients} context={context} />
              )}
            </span>
          )}
        </span>
      }
    >
      <div className="dc-tool-panel-surface" data-padded="true">
        <p className={`${styles.text} selectable`}>{text}</p>
      </div>
    </ToolDisclosure>
  )
}

function BoardCallRow({ call, context }: { call: BoardCall; context: BoardAgentContext }): JSX.Element {
  const locale = useLocale()
  const [expanded, setExpanded] = useState(false)
  const label = useBoardLabel(describeBoardCall(call), context)
  const accessory = describeBoardAccessory(call)
  const failed = call.phase === 'failed'

  return (
    <ToolDisclosure
      expanded={expanded}
      onToggle={() => setExpanded((value) => !value)}
      expandable={hasBoardToolDetail(call)}
      icon={BOARD_ICON}
      tone={failed ? 'error' : undefined}
      title={call.phase === 'running'
        ? <RunningShimmer className="dc-tool-row-running-label">{label}</RunningShimmer>
        : (
          <>
            {label}
            {failed && call.errorMessage && <span className={styles.failure}> — {call.errorMessage.slice(0, 160)}</span>}
          </>
        )}
      accessory={accessory ? translate(locale, accessory.key, accessory.vars) : undefined}
    >
      <BoardToolDetail call={call} context={context} />
    </ToolDisclosure>
  )
}

function useBoardLabel(label: BoardLabel, context: BoardAgentContext): ReactNode {
  const locale = useLocale()
  if (!label.agentPath) return translate(locale, label.key, label.vars)
  return renderWithNode(
    (name) => translate(locale, label.key, { ...label.vars, name }),
    <BoardAgentName path={label.agentPath} context={context} />
  )
}
