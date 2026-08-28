import { useState, type CSSProperties } from 'react'
import { Check, Copy } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { addToast } from '../../stores/toastStore'
import { IconButton } from '../ui/IconButton'

interface MessageCopyButtonProps {
  getText: () => string
  visible: boolean
  disabled?: boolean
  ariaLabel?: string
  wrapperStyle?: CSSProperties
}

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
      <IconButton
        size={24}
        label={label}
        tooltipLabel={label}
        tooltipPlacement="top"
        tooltipWrapperStyle={{ ...defaultWrapperStyle, ...wrapperStyle }}
        onClick={() => {
          void handleCopy()
        }}
        style={{
          borderRadius: '6px',
          color: copied ? 'var(--success)' : undefined
        }}
        icon={copied ? <Check size={14} aria-hidden /> : <Copy size={14} aria-hidden />}
      />
  )
}
