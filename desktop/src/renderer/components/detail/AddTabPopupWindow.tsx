import { useCallback, useEffect, useRef, useState } from 'react'
import { FolderOpen, Globe, SquareTerminal } from 'lucide-react'
import type { AddTabMenuAction, AddTabMenuItem, AddTabPopupPayload } from '../../../shared/addTabMenu'
import { applyTheme } from '../../utils/theme'

function itemIcon(action: AddTabMenuAction): JSX.Element {
  const style = { display: 'block', flexShrink: 0 }
  if (action === 'openFile') {
    return <FolderOpen size={14} aria-hidden style={style} />
  }
  if (action === 'newBrowser') {
    return <Globe size={14} aria-hidden style={style} />
  }
  return <SquareTerminal size={14} aria-hidden style={style} />
}

export function AddTabPopupWindow(): JSX.Element | null {
  const [payload, setPayload] = useState<AddTabPopupPayload | null>(null)
  const [hoveredAction, setHoveredAction] = useState<AddTabMenuAction | null>(null)
  const resolvedRef = useRef(false)

  const resolve = useCallback((action: AddTabMenuAction | null): void => {
    if (resolvedRef.current) return
    resolvedRef.current = true
    void window.api.menu.resolveAddTabMenu(action)
  }, [])

  const applyPayload = useCallback((nextPayload: AddTabPopupPayload): void => {
    resolvedRef.current = false
    setHoveredAction(null)
    setPayload(nextPayload)
    applyTheme(nextPayload.theme, { syncTitleBarOverlay: false })
  }, [])

  useEffect(() => {
    let mounted = true
    void window.api.menu.getAddTabMenuPayload().then((nextPayload) => {
      if (!mounted) return
      if (!nextPayload) {
        return
      }
      applyPayload(nextPayload)
    })
    return () => {
      mounted = false
    }
  }, [applyPayload, resolve])

  useEffect(() => {
    return window.api.menu.onAddTabMenuPayload((nextPayload) => {
      applyPayload(nextPayload)
    })
  }, [applyPayload])

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') {
        event.preventDefault()
        resolve(null)
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [resolve])

  if (!payload) return null

  return (
    <div
      role="presentation"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) {
          event.preventDefault()
          resolve(null)
        }
      }}
      style={{
        position: 'fixed',
        inset: 0,
        background: 'transparent',
        fontFamily: 'var(--font-ui)'
      }}
    >
      <div
        role="menu"
        aria-label="Add tab"
        onMouseDown={(event) => event.stopPropagation()}
        style={{
          position: 'fixed',
          left: payload.position.left,
          top: payload.position.top,
          width: payload.position.width,
          padding: '4px 0',
          border: '1px solid var(--border-default)',
          borderRadius: '8px',
          background: 'var(--bg-elevated)',
          color: 'var(--text-primary)',
          boxShadow: 'var(--shadow-level-3)',
          overflow: 'hidden'
        }}
      >
        {payload.items.map((item) => (
          <AddTabPopupItem
            key={item.action}
            item={item}
            hovered={hoveredAction === item.action}
            onHover={setHoveredAction}
            onChoose={resolve}
          />
        ))}
      </div>
    </div>
  )
}

function AddTabPopupItem({
  item,
  hovered,
  onHover,
  onChoose
}: {
  item: AddTabMenuItem
  hovered: boolean
  onHover: (action: AddTabMenuAction | null) => void
  onChoose: (action: AddTabMenuAction) => void
}): JSX.Element {
  const enabled = item.enabled
  return (
    <button
      role="menuitem"
      type="button"
      disabled={!enabled}
      onClick={() => {
        if (enabled) onChoose(item.action)
      }}
      onMouseEnter={() => onHover(item.action)}
      onMouseLeave={() => onHover(null)}
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: '9px',
        width: '100%',
        height: '32px',
        padding: '0 11px',
        border: 'none',
        background: hovered && enabled ? 'var(--bg-tertiary)' : 'transparent',
        color: enabled ? 'var(--text-primary)' : 'var(--text-dimmed)',
        cursor: enabled ? 'pointer' : 'default',
        font: 'inherit',
        fontSize: '13px',
        textAlign: 'left'
      }}
    >
      {itemIcon(item.action)}
      <span style={{
        flex: 1,
        minWidth: 0,
        overflow: 'hidden',
        textOverflow: 'ellipsis',
        whiteSpace: 'nowrap',
        fontWeight: 500
      }}>
        {item.label}
      </span>
      {item.shortcut && (
        <span style={{
          flexShrink: 0,
          color: enabled ? 'var(--text-secondary)' : 'var(--text-dimmed)',
          fontSize: '11px',
          fontWeight: 400
        }}>
          {item.shortcut}
        </span>
      )}
    </button>
  )
}
