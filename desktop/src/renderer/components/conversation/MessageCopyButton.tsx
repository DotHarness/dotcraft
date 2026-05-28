import { useState, type CSSProperties } from 'react'
import { Check, Copy } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { addToast } from '../../stores/toastStore'
import { ActionTooltip } from '../ui/ActionTooltip'

interface MessageCopyButtonProps {
  getText: () => string
  visible: boolean
  disabled?: boolean
  ariaLabel?: string
  wrapperStyle?: CSSProperties
}

/**
 * Small reusable copy button for message bubbles.
 * Uses a Copy -> Check icon transition after successful copy.
 */
export function MessageCopyButton({
  getText,
  visible,
  disabled = false,
  ariaLabel,
  wrapperStyle
}: MessageCopyButtonProps): JSX.Element | null {
  const t = useT()
  const [copied, setCopied] = useState(false)

  if (disabled) return null

  async function handleCopy(): Promise<void> {
    const text = getText()
    if (text.length === 0) return
    try {
      await navigator.clipboard.writeText(text)
      setCopied(true)
      addToast(t('toast.copied'), 'success', 2000)
      setTimeout(() => setCopied(false), 1500)
    } catch {
      // Ignore clipboard failures silently.
    }
  }

  const label = ariaLabel ?? t('conversation.copyMessage')
  const defaultWrapperStyle: CSSProperties = {
    position: 'absolute',
    right: '8px',
    bottom: '6px',
    opacity: visible ? 1 : 0,
    pointerEvents: visible ? 'auto' : 'none',
    zIndex: 2
  }

  return (
    <ActionTooltip
      label={label}
      placement="top"
      wrapperStyle={{ ...defaultWrapperStyle, ...wrapperStyle }}
    >
      <button
        type="button"
        onClick={() => {
          void handleCopy()
        }}
        aria-label={label}
        style={{
          width: '24px',
          height: '24px',
          borderRadius: '6px',
          border: '1px solid var(--border-default)',
          background: 'var(--bg-secondary)',
          color: copied ? 'var(--success)' : 'var(--text-secondary)',
          display: 'inline-flex',
          alignItems: 'center',
          justifyContent: 'center',
          cursor: 'pointer',
          transition: 'opacity 120ms ease, color 120ms ease'
        }}
      >
        {copied ? <Check size={14} aria-hidden /> : <Copy size={14} aria-hidden />}
      </button>
    </ActionTooltip>
  )
}
