import { useT } from '../../contexts/LocaleContext'
import type { PendingComposerMessage } from '../../types/conversation'

interface PendingMessageIndicatorProps {
  message: PendingComposerMessage
}

/**
 * Shows a queued follow-up message below the input composer.
 * Only rendered when pendingMessage !== null in conversationStore.
 */
export function PendingMessageIndicator({ message }: PendingMessageIndicatorProps): JSX.Element {
  const t = useT()
  const trimmedText = message.text.trim()
  const filesCount = message.files?.length ?? 0
  const displaySource =
    trimmedText.length > 0
      ? trimmedText
      : t('composer.queuedFileReferences', { count: filesCount })
  const displayText = displaySource.length > 80 ? `${displaySource.slice(0, 80)}…` : displaySource

  return (
    <div
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: '6px',
        padding: '4px 12px',
        fontSize: '11px',
        color: 'var(--text-dimmed)'
      }}
    >
      <span
        style={{
          display: 'inline-block',
          width: '6px',
          height: '6px',
          borderRadius: '50%',
          backgroundColor: 'var(--warning)',
          flexShrink: 0
        }}
      />
      <span>Queued: &ldquo;{displayText}&rdquo;</span>
    </div>
  )
}
