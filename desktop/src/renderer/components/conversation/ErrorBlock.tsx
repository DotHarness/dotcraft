import { useEffect, useRef, useState, type CSSProperties } from 'react'
import { Check, Copy } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { addToast } from '../../stores/toastStore'
import { ActionTooltip } from '../ui/ActionTooltip'

interface ErrorBlockProps {
  message: string
}

/**
 * Red-tinted error block for turn/failed or error items.
 * Spec §10.3.3 / §18.3
 */
export function ErrorBlock({ message }: ErrorBlockProps): JSX.Element {
  const t = useT()
  const [copied, setCopied] = useState(false)
  const resetTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  useEffect(() => {
    return () => {
      if (resetTimerRef.current != null) {
        clearTimeout(resetTimerRef.current)
      }
    }
  }, [])

  useEffect(() => {
    if (resetTimerRef.current != null) {
      clearTimeout(resetTimerRef.current)
      resetTimerRef.current = null
    }
    setCopied(false)
  }, [message])

  async function handleCopy(): Promise<void> {
    if (!message) return
    try {
      await navigator.clipboard.writeText(message)
      setCopied(true)
      addToast(t('toast.copied'), 'success', 2000)
      if (resetTimerRef.current != null) {
        clearTimeout(resetTimerRef.current)
      }
      resetTimerRef.current = setTimeout(() => {
        setCopied(false)
        resetTimerRef.current = null
      }, 1500)
    } catch {
      // Ignore clipboard failures silently.
    }
  }

  const copyLabel = copied ? t('error.copiedAria') : t('error.copyAria')

  return (
    <div
      role="alert"
      style={{
        backgroundColor: 'rgba(239, 68, 68, 0.1)',
        border: '1px solid var(--error)',
        borderRadius: '6px',
        padding: '10px 46px 10px 14px',
        color: 'var(--error)',
        fontSize: '13px',
        lineHeight: 1.5,
        marginTop: '4px',
        position: 'relative'
      }}
    >
      <ActionTooltip
        label={copyLabel}
        placement="top"
        wrapperStyle={copyButtonWrapperStyle}
      >
        <button
          type="button"
          aria-label={copyLabel}
          onClick={() => {
            void handleCopy()
          }}
          style={copyButtonStyle(copied)}
        >
          {copied ? <Check size={14} aria-hidden /> : <Copy size={14} aria-hidden />}
        </button>
      </ActionTooltip>
      <strong style={{ display: 'block', marginBottom: '4px', fontWeight: 600 }}>Error</strong>
      <span>{message}</span>
    </div>
  )
}

function copyButtonStyle(copied: boolean): CSSProperties {
  return {
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
    transition: 'color 120ms ease, border-color 120ms ease'
  }
}

const copyButtonWrapperStyle: CSSProperties = {
  position: 'absolute',
  top: '8px',
  right: '8px'
}
