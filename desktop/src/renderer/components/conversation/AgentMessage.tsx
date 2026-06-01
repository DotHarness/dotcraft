import { useMemo, useState } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { addToast } from '../../stores/toastStore'
import { useTypewriterReveal } from '../../hooks/useTypewriterReveal'
import { ContextMenu, type ContextMenuItem, type ContextMenuPosition } from '../ui/ContextMenu'
import { MarkdownRenderer } from './MarkdownRenderer'
import { MessageCopyButton } from './MessageCopyButton'

interface AgentMessageProps {
  text: string
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
  streaming = false,
  createdAt,
  showFooter = true
}: AgentMessageProps): JSX.Element {
  const t = useT()
  const [hovered, setHovered] = useState(false)
  const [focusedWithin, setFocusedWithin] = useState(false)
  const [contextMenuPosition, setContextMenuPosition] = useState<ContextMenuPosition | null>(null)
  const [selectionText, setSelectionText] = useState('')
  const actionsVisible = hovered || focusedWithin
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
            marginTop: '2px',
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
