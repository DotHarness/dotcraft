import { useState, type ReactNode } from 'react'
import { GitBranch } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useConnectionStore } from '../../stores/connectionStore'
import { addToast } from '../../stores/toastStore'
import { useTypewriterReveal } from '../../hooks/useTypewriterReveal'
import { useResponseSelectionStore } from './responseSelectionStore'
import { InlineVisualizationMessage } from './InlineVisualizationMessage'
import { stripInlineVisualizationDirectives } from './inlineVisualizationParser'
import { ResponseFeedback } from './ResponseFeedback'
import { MessageCopyButton } from './MessageCopyButton'
import { ActionTooltip } from '../ui/ActionTooltip'
import { IconButton } from '../ui/IconButton'
import { canForkThread, canForkWorktree, runThreadFork, type ThreadForkMode } from '../../utils/threadFork'
import { formatMessageTime } from '../../utils/messageTime'
import { ForkChoiceDialog } from './ForkChoiceDialog'
import { DesktopPluginMessageActions } from '../desktopPlugins/DesktopPluginActions'

interface AgentMessageProps {
  text: string
  threadId?: string
  turnId?: string
  itemId?: string
  streaming?: boolean
  createdAt?: string
  showFooter?: boolean
  /** Whether this message belongs to the latest turn (forks straight to local). */
  isLastTurn?: boolean
  afterContent?: ReactNode
}

/** Renders agent message text as Markdown. Spec §10.3.3. */
export function AgentMessage({
  text,
  threadId,
  turnId,
  itemId,
  streaming = false,
  createdAt,
  showFooter = true,
  isLastTurn = false,
  afterContent
}: AgentMessageProps): JSX.Element {
  const t = useT()
  const capabilities = useConnectionStore((s) => s.capabilities)
  const [hovered, setHovered] = useState(false)
  const [focusedWithin, setFocusedWithin] = useState(false)
  const [forkChoiceOpen, setForkChoiceOpen] = useState(false)
  const actionsVisible = hovered || focusedWithin
  const forkAvailable = canForkThread(capabilities) && Boolean(threadId && turnId)
  const worktreeForkAvailable = canForkWorktree(capabilities)
  const sentTime = formatMessageTime(createdAt)
  const displayText = useTypewriterReveal(text, streaming)

  function handleContextMenu(event: React.MouseEvent<HTMLDivElement>): void {
    if (event.defaultPrevented || (event.target instanceof Element &&
      event.target.closest('a, input, textarea, [contenteditable="true"], [role="menu"]'))) return
    event.preventDefault()
    event.stopPropagation()
    useResponseSelectionStore.getState().dismiss()
    void window.api.shell.showReplyTextMenu({
      x: event.clientX,
      y: event.clientY,
      selectionText: window.getSelection()?.toString() ?? '',
    }).catch(() => addToast(t('conversation.selection.menuFailed'), 'error'))
  }

  function forkInto(mode: ThreadForkMode): void {
    if (!threadId || !turnId) return
    void runThreadFork({
      threadId,
      mode,
      forkPoint: {
        turnId,
        ...(itemId ? { itemId } : {}),
        position: 'after'
      },
      t
    })
  }

  // Earlier turns are ambiguous about the working-tree state, so they prompt for local
  // vs. worktree; the last turn and the no-worktree case have only one destination.
  function handleForkClick(): void {
    if (!threadId || !turnId) return
    if (isLastTurn || !worktreeForkAvailable) {
      forkInto('local')
      return
    }
    setForkChoiceOpen(true)
  }

  return (
    <div
      style={{ userSelect: 'text' }}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      onFocusCapture={() => setFocusedWithin(true)}
      onBlurCapture={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget)) {
          setFocusedWithin(false)
        }
      }}
      onContextMenu={handleContextMenu}
    >
      {threadId && turnId && itemId && !streaming ? (
        <ResponseFeedback key={`${threadId}:${turnId}:${itemId}`} threadId={threadId} turnId={turnId} itemId={itemId}>
          <InlineVisualizationMessage text={displayText} streaming={false} threadId={threadId} turnId={turnId} itemId={itemId} />
        </ResponseFeedback>
      ) : <InlineVisualizationMessage text={displayText} streaming={streaming} threadId={threadId} turnId={turnId} itemId={itemId} />}
      {afterContent}
      {showFooter && (
        <div
          data-testid="agent-message-footer"
          style={{
            minHeight: '24px',
            marginTop: '8px',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'flex-start',
            gap: '6px',
            color: 'var(--text-tertiary)',
            fontSize: 'var(--conversation-meta-size)',
            lineHeight: 1,
            userSelect: 'none'
          }}
        >
          <MessageCopyButton
            getText={() => stripInlineVisualizationDirectives(text)}
            visible={actionsVisible && text.length > 0}
            disabled={streaming || text.length === 0}
            wrapperStyle={{
              position: 'static',
              display: 'inline-flex',
              opacity: actionsVisible && text.length > 0 ? 1 : 0,
              pointerEvents: actionsVisible && text.length > 0 ? 'auto' : 'none',
              transition: 'opacity 120ms ease'
            }}
          />
          {forkAvailable && !streaming && (
            <IconButton
              size={24}
              radius={6}
              icon={<GitBranch size={14} aria-hidden />}
              label={t('conversation.forkMessage')}
              tooltipLabel={t('conversation.forkMessage')}
              tooltipPlacement="top"
              tooltipWrapperStyle={{
                position: 'static',
                display: 'inline-flex',
                opacity: actionsVisible ? 1 : 0,
                pointerEvents: actionsVisible ? 'auto' : 'none',
                transition: 'opacity 120ms ease'
              }}
              onClick={(event) => {
                event.stopPropagation()
                handleForkClick()
              }}
            />
          )}
          {!streaming && threadId && turnId && itemId && (
            <DesktopPluginMessageActions
              visible={actionsVisible}
              message={{
                id: itemId,
                threadId,
                turnId,
                text,
                createdAt
              }}
            />
          )}
          {sentTime && (
            <ActionTooltip label={sentTime.title}>
              <span
                data-testid="agent-message-time"
                style={{
                  padding: '0 2px',
                  opacity: actionsVisible ? 1 : 0,
                  transition: 'opacity 120ms ease'
                }}
              >
                {sentTime.label}
              </span>
            </ActionTooltip>
          )}
        </div>
      )}
      {forkChoiceOpen && (
        <ForkChoiceDialog
          onChoose={(mode) => {
            setForkChoiceOpen(false)
            forkInto(mode)
          }}
          onCancel={() => setForkChoiceOpen(false)}
        />
      )}
    </div>
  )
}
