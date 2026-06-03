import { useMemo, useState } from 'react'
import { GitBranch } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useConnectionStore } from '../../stores/connectionStore'
import { addToast } from '../../stores/toastStore'
import { useTypewriterReveal } from '../../hooks/useTypewriterReveal'
import { ContextMenu, type ContextMenuItem, type ContextMenuPosition } from '../ui/ContextMenu'
import { MarkdownRenderer } from './MarkdownRenderer'
import { MessageCopyButton } from './MessageCopyButton'
import { ActionTooltip } from '../ui/ActionTooltip'
import { canForkThread, runThreadFork } from '../../utils/threadFork'

interface AgentMessageProps {
  text: string
  threadId?: string
  turnId?: string
  itemId?: string
  streaming?: boolean
  createdAt?: string
  showFooter?: boolean
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
  showFooter = true
}: AgentMessageProps): JSX.Element {
  const t = useT()
  const capabilities = useConnectionStore((s) => s.capabilities)
  const [hovered, setHovered] = useState(false)
  const [focusedWithin, setFocusedWithin] = useState(false)
  const [forkButtonHovered, setForkButtonHovered] = useState(false)
  const [forkButtonFocused, setForkButtonFocused] = useState(false)
  const [contextMenuPosition, setContextMenuPosition] = useState<ContextMenuPosition | null>(null)
  const [selectionText, setSelectionText] = useState('')
  const actionsVisible = hovered || focusedWithin
  const forkButtonChromeVisible = forkButtonHovered || forkButtonFocused
  const forkAvailable = canForkThread(capabilities) && Boolean(threadId && turnId)
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

  function forkMessage(): void {
    if (!threadId || !turnId) return
    void runThreadFork({
      threadId,
      mode: 'local',
      forkPoint: {
        turnId,
        ...(itemId ? { itemId } : {}),
        position: 'after'
      },
      t
    })
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
        void copyText(text)
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
      <MarkdownRenderer content={displayText} />
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
            getText={() => text}
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
                  forkMessage()
                }}
                style={{
                  width: '24px',
                  height: '24px',
                  borderRadius: '6px',
                  border: forkButtonChromeVisible ? '1px solid var(--border-default)' : '1px solid transparent',
                  background: forkButtonChromeVisible ? 'var(--bg-secondary)' : 'transparent',
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
          {sentTime && (
            <span
              data-testid="agent-message-time"
              title={sentTime.title}
              style={{
                padding: '0 2px',
                opacity: actionsVisible ? 1 : 0,
                transition: 'opacity 120ms ease'
              }}
            >
              {sentTime.label}
            </span>
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
