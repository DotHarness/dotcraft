import { useMemo, useState, type ReactNode } from 'react'
import { GitBranch } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useConnectionStore } from '../../stores/connectionStore'
import { addToast } from '../../stores/toastStore'
import { useTypewriterReveal } from '../../hooks/useTypewriterReveal'
import { ContextMenu, type ContextMenuItem, type ContextMenuPosition } from '../ui/ContextMenu'
import { InlineVisualizationMessage } from './InlineVisualizationMessage'
import { stripInlineVisualizationDirectives } from './inlineVisualizationParser'
import { MessageCopyButton } from './MessageCopyButton'
import { ActionTooltip } from '../ui/ActionTooltip'
import { canForkThread, canForkWorktree, runThreadFork, type ThreadForkMode } from '../../utils/threadFork'
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

/**
 * Renders agent message text as Markdown.
 * Spec §10.3.3
 */
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
  const [forkButtonHovered, setForkButtonHovered] = useState(false)
  const [forkButtonFocused, setForkButtonFocused] = useState(false)
  const [forkChoiceOpen, setForkChoiceOpen] = useState(false)
  const [contextMenuPosition, setContextMenuPosition] = useState<ContextMenuPosition | null>(null)
  const [selectionText, setSelectionText] = useState('')
  const actionsVisible = hovered || focusedWithin
  const forkButtonChromeVisible = forkButtonHovered || forkButtonFocused
  const forkAvailable = canForkThread(capabilities) && Boolean(threadId && turnId)
  const worktreeForkAvailable = canForkWorktree(capabilities)
  const sentTime = formatMessageTime(createdAt)
  // Steady-cadence typewriter reveal while streaming; full text once finalized.
  const displayText = useTypewriterReveal(text, streaming)

  async function copyText(content: string): Promise<void> {
    if (content.length === 0) return
    try {
      await navigator.clipboard.writeText(content)
      addToast(t('toast.copied'), 'success', 2000)
    } catch {
      // Ignore clipboard failures silently.
    }
  }

  function handleContextMenu(event: React.MouseEvent<HTMLDivElement>): void {
    event.preventDefault()
    const selected = window.getSelection()?.toString() ?? ''
    setSelectionText(selected)
    setContextMenuPosition({ x: event.clientX, y: event.clientY })
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

  // Last turn: fork straight into a local chat (matches the existing one-click
  // behavior). Earlier turns are ambiguous about the working-tree state, so let
  // the user choose local vs. worktree — unless worktree forks aren't available,
  // in which case there's only one destination and the prompt adds nothing.
  function handleForkClick(): void {
    if (!threadId || !turnId) return
    if (isLastTurn || !worktreeForkAvailable) {
      forkInto('local')
      return
    }
    setForkChoiceOpen(true)
  }

  const contextItems = useMemo<ContextMenuItem[]>(() => {
    const items: ContextMenuItem[] = []
    if (selectionText.trim().length > 0) {
      items.push({
        label: t('conversation.copySelection'),
        onClick: () => {
          void copyText(selectionText)
        }
      })
    }
    items.push({
      label: t('conversation.copyMessage'),
      onClick: () => {
        void copyText(stripInlineVisualizationDirectives(text))
      }
    })
    return items
  }, [selectionText, t, text])

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
      <InlineVisualizationMessage text={displayText} streaming={streaming} threadId={threadId} turnId={turnId} itemId={itemId} />
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
            fontSize: '11px',
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
            <ActionTooltip
              label={t('conversation.forkMessage')}
              placement="top"
              wrapperStyle={{
                position: 'static',
                display: 'inline-flex',
                opacity: actionsVisible ? 1 : 0,
                pointerEvents: actionsVisible ? 'auto' : 'none',
                transition: 'opacity 120ms ease'
              }}
            >
              <button
                type="button"
                aria-label={t('conversation.forkMessage')}
                onMouseEnter={() => setForkButtonHovered(true)}
                onMouseLeave={() => setForkButtonHovered(false)}
                onFocus={() => setForkButtonFocused(true)}
                onBlur={() => setForkButtonFocused(false)}
                onClick={(event) => {
                  event.stopPropagation()
                  handleForkClick()
                }}
                style={{
                  width: '24px',
                  height: '24px',
                  borderRadius: '6px',
                  border: '1px solid transparent',
                  background: forkButtonChromeVisible ? 'var(--bg-tertiary)' : 'transparent',
                  color: forkButtonChromeVisible ? 'var(--text-primary)' : 'var(--text-secondary)',
                  display: 'inline-flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  cursor: 'pointer',
                  transition: 'opacity 120ms ease, color 120ms ease, background 120ms ease, border-color 120ms ease'
                }}
              >
                <GitBranch size={14} strokeWidth={2.1} aria-hidden />
              </button>
            </ActionTooltip>
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
      {contextMenuPosition && (
        <ContextMenu
          items={contextItems}
          position={contextMenuPosition}
          onClose={() => {
            setContextMenuPosition(null)
          }}
        />
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

function formatMessageTime(createdAt?: string): { label: string; title: string } | null {
  if (!createdAt) return null
  const date = new Date(createdAt)
  if (!Number.isFinite(date.getTime())) return null

  return {
    label: new Intl.DateTimeFormat(undefined, {
      hour: '2-digit',
      minute: '2-digit',
      hour12: false,
      hourCycle: 'h23'
    }).format(date),
    title: new Intl.DateTimeFormat(undefined, {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hour12: false,
      hourCycle: 'h23'
    }).format(date)
  }
}
