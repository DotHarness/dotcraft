import { useEffect, useId, useRef, useState, type CSSProperties, type JSX } from 'react'
import { FileText, ImagePlus, ListChecks, Plus } from 'lucide-react'
import {
  COMPOSER_FOOTER_CONTROL_HEIGHT,
  composerFooterControlActiveBackground,
  composerFooterControlBoxStyle,
  composerFooterControlHoverBackground
} from './ComposerShell'
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
  const [triggerActive, setTriggerActive] = useState(false)
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
          onMouseEnter={() => setTriggerActive(true)}
          onMouseLeave={() => setTriggerActive(false)}
          onFocus={(event) => {
            if (event.currentTarget.matches(':focus-visible')) setTriggerActive(true)
          }}
          onBlur={() => setTriggerActive(false)}
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
            borderRadius: '999px',
            border: 'none',
            background: !disabled
              ? open
                ? composerFooterControlActiveBackground
                : triggerActive
                  ? composerFooterControlHoverBackground
                  : 'transparent'
              : 'transparent',
            color: disabled ? 'var(--composer-footer-muted)' : 'var(--composer-footer-text)',
            cursor: disabled ? 'default' : 'pointer',
            lineHeight: 1,
            boxSizing: 'border-box',
            transition: 'background-color 120ms ease, color 120ms ease'
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
            border: 'none',
            borderRadius: '12px',
            background: 'var(--glass-surface-strong)',
            boxShadow: 'var(--glass-shadow-soft)',
            backdropFilter: 'var(--glass-blur)',
            WebkitBackdropFilter: 'var(--glass-blur)',
            padding: '6px'
          }}
        >
          <ComposerAttachmentMenuItem
            role="menuitem"
            disabled={attachmentsDisabled}
            title={attachmentDisabledReason}
            icon={<ImagePlus size={14} aria-hidden />}
            label={attachImageLabel}
            onClick={() => {
              if (attachmentsDisabled) return
              setOpen(false)
              fileInputRef.current?.click()
            }}
          />
          <ComposerAttachmentMenuItem
            role="menuitem"
            disabled={attachmentsDisabled}
            title={attachmentDisabledReason}
            icon={<FileText size={14} aria-hidden />}
            label={referenceFileLabel}
            onClick={() => {
              if (attachmentsDisabled) return
              setOpen(false)
              onReferenceFiles()
            }}
          />
          {showPlanModeToggle && (
            <>
              <div
                aria-hidden
                style={{
                  height: '1px',
                  background: 'color-mix(in srgb, var(--text-primary) 9%, transparent)',
                  margin: '6px 10px'
                }}
              />
              <ActionTooltip label={planModeToggleLabel ?? ''} placement="right" wrapperStyle={{ width: '100%' }}>
                <ComposerAttachmentMenuItem
                  role="menuitemcheckbox"
                  checked={planModeEnabled}
                  icon={<ListChecks size={14} aria-hidden />}
                  label={planModeLabel ?? ''}
                  trailing={(
                    <span aria-hidden style={switchTrackStyle(planModeEnabled)}>
                      <span style={switchThumbStyle(planModeEnabled)} />
                    </span>
                  )}
                  onClick={() => {
                    onTogglePlanMode?.()
                  }}
                />
              </ActionTooltip>
            </>
          )}
        </div>
      )}
    </div>
  )
}

function ComposerAttachmentMenuItem({
  role,
  checked,
  disabled = false,
  title,
  icon,
  label,
  trailing,
  onClick
}: {
  role: 'menuitem' | 'menuitemcheckbox'
  checked?: boolean
  disabled?: boolean
  title?: string
  icon: JSX.Element
  label: string
  trailing?: JSX.Element
  onClick: () => void
}): JSX.Element {
  const [active, setActive] = useState(false)

  const item = (
    <button
      type="button"
      role={role}
      aria-checked={role === 'menuitemcheckbox' ? checked : undefined}
      disabled={disabled}
      onMouseEnter={() => setActive(true)}
      onMouseLeave={() => setActive(false)}
      onFocus={() => setActive(true)}
      onBlur={() => setActive(false)}
      onClick={onClick}
      style={menuItemStyle(active, disabled, trailing != null)}
    >
      <span style={{ display: 'inline-flex', alignItems: 'center', gap: '8px', minWidth: 0 }}>
        {icon}
        <span>{label}</span>
      </span>
      {trailing}
    </button>
  )

  if (!title) return item

  return (
    <ActionTooltip label={title} wrapperStyle={{ display: 'block', width: '100%' }}>
      {item}
    </ActionTooltip>
  )
}

function menuItemStyle(active: boolean, disabled: boolean, hasTrailing: boolean): CSSProperties {
  return {
  width: '100%',
  display: 'flex',
  alignItems: 'center',
  justifyContent: hasTrailing ? 'space-between' : 'flex-start',
  gap: '8px',
  border: 'none',
  borderRadius: '10px',
  padding: '8px 10px',
  background: active && !disabled ? 'var(--bg-tertiary)' : 'transparent',
  color: disabled ? 'var(--text-dimmed)' : active ? 'var(--text-primary)' : 'var(--text-secondary)',
  cursor: disabled ? 'default' : 'pointer',
  textAlign: 'left',
  fontSize: 'var(--type-secondary-size)',
  lineHeight: 'var(--type-secondary-line-height)',
  minWidth: 0,
  transition: 'background-color 120ms ease, color 120ms ease'
  }
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
