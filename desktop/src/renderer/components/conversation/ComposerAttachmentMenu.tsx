import { useEffect, useId, useRef, useState, type CSSProperties, type JSX } from 'react'
import { FileText, ImagePlus, ListChecks, Plus } from 'lucide-react'
import { COMPOSER_FOOTER_CONTROL_HEIGHT, composerFooterControlBoxStyle } from './ComposerShell'
import { ActionTooltip } from '../ui/ActionTooltip'

interface ComposerAttachmentMenuProps {
  title: string
  ariaLabel: string
  attachImageLabel: string
  referenceFileLabel: string
  onAttachImages: (files: File[]) => void
  onReferenceFiles: () => void
  attachmentDisabledReason?: string
  planModeLabel?: string
  planModeToggleLabel?: string
  planModeEnabled?: boolean
  onTogglePlanMode?: () => void
  disabled?: boolean
}

export function ComposerAttachmentMenu({
  title,
  ariaLabel,
  attachImageLabel,
  referenceFileLabel,
  onAttachImages,
  onReferenceFiles,
  attachmentDisabledReason,
  planModeLabel,
  planModeToggleLabel,
  planModeEnabled = false,
  onTogglePlanMode,
  disabled = false
}: ComposerAttachmentMenuProps): JSX.Element {
  const [open, setOpen] = useState(false)
  const wrapRef = useRef<HTMLDivElement>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)
  const menuId = useId()
  const showPlanModeToggle = Boolean(planModeLabel && onTogglePlanMode)
  const attachmentsDisabled = Boolean(attachmentDisabledReason)

  useEffect(() => {
    if (!open) return

    const handlePointerDown = (event: MouseEvent): void => {
      if (!wrapRef.current?.contains(event.target as Node)) {
        setOpen(false)
      }
    }

    const handleKeyDown = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') {
        event.preventDefault()
        setOpen(false)
      }
    }

    window.addEventListener('mousedown', handlePointerDown, true)
    window.addEventListener('keydown', handleKeyDown, true)
    return () => {
      window.removeEventListener('mousedown', handlePointerDown, true)
      window.removeEventListener('keydown', handleKeyDown, true)
    }
  }, [open])

  return (
    <div
      ref={wrapRef}
      style={{
        ...composerFooterControlBoxStyle,
        position: 'relative',
        flexShrink: 0
      }}
    >
      <input
        ref={fileInputRef}
        type="file"
        accept="image/*"
        multiple
        tabIndex={-1}
        aria-hidden
        style={{ display: 'none' }}
        onChange={(event) => {
          const files = Array.from(event.currentTarget.files ?? [])
          event.currentTarget.value = ''
          if (files.length === 0) return
          onAttachImages(files)
        }}
      />

      <ActionTooltip label={title} placement="top">
        <button
          type="button"
          aria-label={ariaLabel}
          aria-haspopup="menu"
          aria-expanded={open}
          aria-controls={open ? menuId : undefined}
          disabled={disabled}
          onClick={() => {
            if (disabled) return
            setOpen((current) => !current)
          }}
          style={{
            display: 'inline-flex',
            alignItems: 'center',
            justifyContent: 'center',
            width: COMPOSER_FOOTER_CONTROL_HEIGHT,
            height: COMPOSER_FOOTER_CONTROL_HEIGHT,
            padding: 0,
            borderRadius: '8px',
            border: 'none',
            background: 'transparent',
            color: disabled ? 'var(--composer-footer-muted)' : 'var(--composer-footer-text)',
            cursor: disabled ? 'default' : 'pointer',
            lineHeight: 1,
            boxSizing: 'border-box'
          }}
      >
          <Plus size={16} strokeWidth={2} aria-hidden />
        </button>
      </ActionTooltip>

      {open && !disabled && (
        <div
          id={menuId}
          role="menu"
          aria-label={ariaLabel}
          style={{
            position: 'absolute',
            left: 0,
            bottom: 'calc(100% + 8px)',
            minWidth: '180px',
            zIndex: 70,
            border: '1px solid var(--glass-border)',
            borderRadius: '12px',
            background: 'var(--glass-surface-strong)',
            boxShadow: 'var(--glass-shadow-soft)',
            backdropFilter: 'var(--glass-blur)',
            WebkitBackdropFilter: 'var(--glass-blur)',
            padding: '6px'
          }}
        >
          <button
            type="button"
            role="menuitem"
            disabled={attachmentsDisabled}
            title={attachmentDisabledReason}
            onClick={() => {
              if (attachmentsDisabled) return
              setOpen(false)
              fileInputRef.current?.click()
            }}
            style={attachmentsDisabled ? disabledMenuItemStyle : menuItemStyle}
          >
            <ImagePlus size={14} aria-hidden />
            <span>{attachImageLabel}</span>
          </button>
          <button
            type="button"
            role="menuitem"
            disabled={attachmentsDisabled}
            title={attachmentDisabledReason}
            onClick={() => {
              if (attachmentsDisabled) return
              setOpen(false)
              onReferenceFiles()
            }}
            style={attachmentsDisabled ? disabledMenuItemStyle : menuItemStyle}
          >
            <FileText size={14} aria-hidden />
            <span>{referenceFileLabel}</span>
          </button>
          {showPlanModeToggle && (
            <>
              <div
                aria-hidden
                style={{
                  height: '1px',
                  background: 'var(--glass-border)',
                  margin: '6px 4px'
                }}
              />
              <ActionTooltip label={planModeToggleLabel ?? ''} placement="right" wrapperStyle={{ width: '100%' }}>
                <button
                  type="button"
                  role="menuitemcheckbox"
                  aria-checked={planModeEnabled}
                  onClick={() => {
                    onTogglePlanMode?.()
                  }}
                  style={planModeMenuItemStyle}
                >
                  <span style={{ display: 'inline-flex', alignItems: 'center', gap: '8px', minWidth: 0 }}>
                    <ListChecks size={14} aria-hidden />
                    <span>{planModeLabel}</span>
                  </span>
                  <span aria-hidden style={switchTrackStyle(planModeEnabled)}>
                    <span style={switchThumbStyle(planModeEnabled)} />
                  </span>
                </button>
              </ActionTooltip>
            </>
          )}
        </div>
      )}
    </div>
  )
}

const menuItemStyle = {
  width: '100%',
  display: 'flex',
  alignItems: 'center',
  gap: '8px',
  border: 'none',
  borderRadius: '10px',
  padding: '8px 10px',
  background: 'transparent',
  color: 'var(--text-secondary)',
  cursor: 'pointer',
  textAlign: 'left',
  fontSize: 'var(--type-secondary-size)',
  lineHeight: 'var(--type-secondary-line-height)'
} as const

const disabledMenuItemStyle = {
  ...menuItemStyle,
  color: 'var(--text-dimmed)',
  cursor: 'default'
} as const

const planModeMenuItemStyle: CSSProperties = {
  ...menuItemStyle,
  justifyContent: 'space-between',
  minWidth: 0
}

function switchTrackStyle(enabled: boolean): CSSProperties {
  return {
    width: '28px',
    height: '16px',
    borderRadius: '999px',
    backgroundColor: enabled ? 'var(--accent)' : 'var(--bg-tertiary)',
    flexShrink: 0,
    display: 'inline-flex',
    alignItems: 'center',
    padding: '2px',
    transition: 'background-color 120ms ease'
  }
}

function switchThumbStyle(enabled: boolean): CSSProperties {
  return {
    width: '12px',
    height: '12px',
    borderRadius: '999px',
    backgroundColor: '#fff',
    boxShadow: '0 1px 2px rgba(0, 0, 0, 0.22)',
    transform: enabled ? 'translateX(12px)' : 'translateX(0)',
    transition: 'transform 120ms ease'
  }
}
