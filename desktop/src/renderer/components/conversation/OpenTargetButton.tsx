import { useEffect, useMemo, useRef, useState, type CSSProperties, type ReactNode } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { ChevronDownIcon } from '../ui/AppIcons'
import { ActionTooltip } from '../ui/ActionTooltip'
import { Button } from '../ui/Button'
import {
  EDITOR_ICON_SIZE,
  listEditorsCached,
  placeExplorerFirst,
  renderEditorIcon,
  type EditorId,
  type EditorInfo
} from '../../utils/editorTargets'

interface OpenTargetButtonProps {
  targetPath: string
  tooltipLabel: string
  menuAriaLabel: string
  showPrimaryLabel?: boolean
  primaryButtonLabel?: string
  primaryIcon?: ReactNode
  tooltipPlacement?: 'top' | 'bottom' | 'left' | 'right'
}

export function OpenTargetButton({
  targetPath,
  tooltipLabel,
  menuAriaLabel,
  showPrimaryLabel = false,
  primaryButtonLabel,
  primaryIcon,
  tooltipPlacement = 'bottom'
}: OpenTargetButtonProps): JSX.Element {
  const t = useT()
  const wrapRef = useRef<HTMLDivElement>(null)
  const menuButtonRef = useRef<HTMLButtonElement>(null)
  const [open, setOpen] = useState(false)
  const [loading, setLoading] = useState(false)
  const [editors, setEditors] = useState<EditorInfo[]>([])
  const [highlight, setHighlight] = useState(0)
  const [lastOpenEditorId, setLastOpenEditorId] = useState<EditorId | undefined>(undefined)

  const orderedEditors = useMemo(() => placeExplorerFirst(editors), [editors])
  const resolvedLastOpenId = useMemo<EditorId>(() => {
    if (lastOpenEditorId && orderedEditors.some((entry) => entry.id === lastOpenEditorId)) {
      return lastOpenEditorId
    }
    return 'explorer'
  }, [lastOpenEditorId, orderedEditors])

  const primaryEditor = useMemo(() => {
    const preferred = orderedEditors.find((entry) => entry.id === resolvedLastOpenId)
    if (preferred) return preferred
    return orderedEditors[0] ?? { id: 'explorer', labelKey: 'editors.explorer', iconKey: 'explorer' }
  }, [orderedEditors, resolvedLastOpenId])
  const primaryAriaLabel = useMemo(() => {
    if (primaryEditor.id === 'explorer') return primaryButtonLabel ?? t('threadHeader.open')
    return t('threadHeader.openIn', { app: t(primaryEditor.labelKey) })
  }, [primaryButtonLabel, primaryEditor, t])

  useEffect(() => {
    window.api.settings.get()
      .then((settings) => {
        setLastOpenEditorId(settings.lastOpenEditorId)
      })
      .catch(() => {})
    setLoading(true)
    void listEditorsCached()
      .then((entries) => {
        setEditors(entries)
      })
      .catch(() => {})
      .finally(() => {
        setLoading(false)
      })
  }, [])

  useEffect(() => {
    if (!open) return
    const index = Math.max(0, orderedEditors.findIndex((entry) => entry.id === resolvedLastOpenId))
    setHighlight(index)
  }, [open, orderedEditors, resolvedLastOpenId])

  useEffect(() => {
    if (!open) return
    const handlePointerDown = (event: MouseEvent): void => {
      if (event.button !== 0) return
      if (!wrapRef.current?.contains(event.target as Node)) {
        setOpen(false)
      }
    }
    const handleKeyDown = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') {
        event.preventDefault()
        setOpen(false)
        window.setTimeout(() => menuButtonRef.current?.focus(), 0)
        return
      }
      if (orderedEditors.length === 0) return
      if (event.key === 'ArrowDown') {
        event.preventDefault()
        setHighlight((current) => Math.min(orderedEditors.length - 1, current + 1))
        return
      }
      if (event.key === 'ArrowUp') {
        event.preventDefault()
        setHighlight((current) => Math.max(0, current - 1))
        return
      }
      if (event.key === 'Enter') {
        event.preventDefault()
        const next = orderedEditors[highlight]
        if (next) {
          setOpen(false)
          void handleSwitchDefault(next.id)
        }
      }
    }
    window.addEventListener('mousedown', handlePointerDown, true)
    window.addEventListener('keydown', handleKeyDown, true)
    return () => {
      window.removeEventListener('mousedown', handlePointerDown, true)
      window.removeEventListener('keydown', handleKeyDown, true)
    }
  }, [highlight, open, orderedEditors])

  async function persistLastUsed(id: EditorId): Promise<void> {
    setLastOpenEditorId(id)
    await window.api.settings.set({ lastOpenEditorId: id })
  }

  async function handleSwitchDefault(id: EditorId): Promise<void> {
    setOpen(false)
    try {
      await persistLastUsed(id)
    } catch {
      // Keep silent to avoid interrupting regular conversation flow.
    }
    window.setTimeout(() => menuButtonRef.current?.focus(), 0)
  }

  async function handleLaunch(id: EditorId): Promise<void> {
    try {
      await window.api.shell.launchEditor(id, targetPath)
    } catch {
      // Keep silent to avoid interrupting regular conversation flow.
    }
  }

  return (
    <div ref={wrapRef} style={{ position: 'relative', display: 'flex', flexShrink: 0 }}>
      <div style={splitWrapStyle}>
        <ActionTooltip
          label={tooltipLabel}
          disabledReason={loading ? t('quickOpen.loading') : undefined}
          placement={tooltipPlacement}
        >
          <Button
            type="button"
            size="sm"
            variant="ghost"
            style={{
              ...openButtonStyle,
              borderRight: '1px solid var(--border-default)',
              borderTopRightRadius: 0,
              borderBottomRightRadius: 0
            }}
            aria-label={primaryAriaLabel}
            disabled={loading}
            onMouseDown={(event) => {
              if (event.button !== 0) {
                event.preventDefault()
              }
            }}
            onClick={() => { void handleLaunch(primaryEditor.id) }}
          >
            <span style={iconWrapStyle}>
              {primaryIcon ?? renderEditorIcon(primaryEditor, EDITOR_ICON_SIZE)}
            </span>
            {showPrimaryLabel && (
              <span style={{ whiteSpace: 'nowrap' }}>
                {primaryButtonLabel ?? t('threadHeader.open')}
              </span>
            )}
          </Button>
        </ActionTooltip>
        <ActionTooltip
          label={menuAriaLabel}
          disabledReason={loading ? t('quickOpen.loading') : undefined}
          placement={tooltipPlacement}
        >
          <Button
            ref={menuButtonRef}
            type="button"
            size="sm"
            variant="ghost"
            aria-label={menuAriaLabel}
            aria-haspopup="menu"
            aria-expanded={open}
            disabled={loading || orderedEditors.length === 0}
            onMouseDown={(event) => {
              if (event.button !== 0) {
                event.preventDefault()
              }
            }}
            onContextMenu={(event) => {
              event.preventDefault()
            }}
            onClick={() => setOpen((current) => !current)}
            style={{
              ...openButtonStyle,
              paddingInline: '6px',
              borderTopLeftRadius: 0,
              borderBottomLeftRadius: 0
            }}
          >
            <ChevronDownIcon size={13} />
          </Button>
        </ActionTooltip>
      </div>

      {open && orderedEditors.length > 0 && (
        <div
          role="menu"
          style={{
            position: 'absolute',
            top: 'calc(100% + 6px)',
            right: 0,
            minWidth: '220px',
            maxWidth: '280px',
            border: 'none',
            borderRadius: '10px',
            background: 'var(--glass-surface-strong)',
            boxShadow: 'var(--glass-shadow-soft)',
            backdropFilter: 'var(--glass-blur)',
            WebkitBackdropFilter: 'var(--glass-blur)',
            padding: '6px',
            zIndex: 80
          }}
        >
          {orderedEditors.map((entry, index) => {
            const selected = entry.id === resolvedLastOpenId
            const highlighted = highlight === index
            return (
              <button
                key={entry.id}
                type="button"
                role="menuitem"
                aria-label={t(entry.labelKey)}
                onMouseEnter={() => setHighlight(index)}
                onContextMenu={(event) => {
                  event.preventDefault()
                }}
                onClick={() => { void handleSwitchDefault(entry.id) }}
                style={{
                  width: '100%',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  gap: '8px',
                  border: 'none',
                  borderRadius: '8px',
                  padding: '8px 10px',
                  background: highlighted ? 'var(--bg-tertiary)' : 'transparent',
                  color: selected ? 'var(--text-primary)' : 'var(--text-secondary)',
                  cursor: 'pointer',
                  textAlign: 'left',
                  fontSize: '12px'
                }}
              >
                <span style={{ display: 'inline-flex', alignItems: 'center', gap: '8px', minWidth: 0 }}>
                  <span style={iconWrapStyle}>
                    {renderEditorIcon(entry, EDITOR_ICON_SIZE)}
                  </span>
                  <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                    {t(entry.labelKey)}
                  </span>
                </span>
                {selected && (
                  <span
                    aria-hidden
                    style={{
                      width: '7px',
                      height: '7px',
                      borderRadius: '999px',
                      background: 'var(--accent)',
                      flexShrink: 0
                    }}
                  />
                )}
              </button>
            )
          })}
        </div>
      )}
    </div>
  )
}

const splitWrapStyle: CSSProperties = {
  display: 'inline-flex',
  border: '1px solid var(--border-default)',
  borderRadius: '6px',
  background: 'var(--bg-secondary)'
}

const openButtonStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: '6px',
  padding: '4px 10px',
  height: '26px',
  fontSize: '12px',
  fontWeight: 500,
  color: 'var(--text-secondary)',
  backgroundColor: 'transparent',
  border: 'none',
  borderRadius: '6px',
  cursor: 'pointer',
  flexShrink: 0,
  transition: 'background-color 100ms ease, color 100ms ease',
  lineHeight: 1.2
}

const iconWrapStyle: CSSProperties = {
  width: `${EDITOR_ICON_SIZE}px`,
  height: `${EDITOR_ICON_SIZE}px`,
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  flexShrink: 0
}
