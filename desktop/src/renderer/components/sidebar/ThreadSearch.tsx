import { useEffect, useMemo, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { Monitor, Search } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useThreadStore } from '../../stores/threadStore'
import { useUIStore } from '../../stores/uiStore'
import type { ThreadSummary } from '../../types/thread'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ShortcutBadge } from '../ui/ShortcutBadge'
import { ACTION_SHORTCUTS } from '../ui/shortcutKeys'
import {
  SIDEBAR_NAV_BORDER_INACTIVE,
  SIDEBAR_NAV_ICON_SLOT,
  SIDEBAR_NAV_LABEL,
  SIDEBAR_NAV_ROW_OUTER
} from './sidebarNavRowStyles'

interface ThreadSearchProps {
  workspaceName: string
}

/**
 * Sidebar search entry. The actual search input lives in a centered dialog so
 * the sidebar stays visually quiet.
 */
export function ThreadSearch({ workspaceName }: ThreadSearchProps): JSX.Element {
  const t = useT()
  const [open, setOpen] = useState(false)

  useEffect(() => {
    const openSearch = (): void => setOpen(true)
    ;(window as Window & { __sidebarSearchFocus?: () => void }).__sidebarSearchFocus = openSearch
    return () => {
      delete (window as Window & { __sidebarSearchFocus?: () => void }).__sidebarSearchFocus
    }
  }, [])

  return (
    <>
      <div style={{ padding: '0 0 4px', flexShrink: 0 }}>
        <ActionTooltip
          label={t('threadSearch.open')}
          shortcut={ACTION_SHORTCUTS.search}
          wrapperStyle={{ display: 'block', width: '100%' }}
        >
          <button
            type="button"
            onClick={() => setOpen(true)}
            aria-label={t('threadSearch.open')}
            style={{
              ...SIDEBAR_NAV_ROW_OUTER,
              ...SIDEBAR_NAV_BORDER_INACTIVE,
              cursor: 'pointer',
              color: 'var(--text-secondary)',
              background: 'transparent',
              transition: 'background-color 120ms ease, color 120ms ease'
            }}
            onMouseEnter={(e) => {
              ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--sidebar-control-hover)'
              ;(e.currentTarget as HTMLButtonElement).style.color = 'var(--text-primary)'
            }}
            onMouseLeave={(e) => {
              ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'transparent'
              ;(e.currentTarget as HTMLButtonElement).style.color = 'var(--text-secondary)'
            }}
          >
            <span style={SIDEBAR_NAV_ICON_SLOT}>
              <Search size={16} strokeWidth={2} aria-hidden style={{ display: 'block' }} />
            </span>
            <span style={SIDEBAR_NAV_LABEL}>{t('threadSearch.open')}</span>
          </button>
        </ActionTooltip>
      </div>

      {open && (
        <ThreadSearchDialog
          workspaceName={workspaceName}
          onClose={() => setOpen(false)}
        />
      )}
    </>
  )
}

interface ThreadSearchDialogProps {
  workspaceName: string
  onClose: () => void
}

function ThreadSearchDialog({ workspaceName, onClose }: ThreadSearchDialogProps): JSX.Element {
  const t = useT()
  const inputRef = useRef<HTMLInputElement>(null)
  const [query, setQuery] = useState('')
  const [highlighted, setHighlighted] = useState(0)
  const threadList = useThreadStore((s) => s.threadList)
  const setActiveThreadId = useThreadStore((s) => s.setActiveThreadId)
  const setActiveMainView = useUIStore((s) => s.setActiveMainView)

  const visibleThreads = useMemo(() => {
    const visible = threadList
      .filter((thread) => thread.status !== 'archived')
      .sort((a, b) => new Date(b.lastActiveAt).getTime() - new Date(a.lastActiveAt).getTime())

    const trimmed = query.trim().toLowerCase()
    const filtered = trimmed
      ? visible.filter((thread) => (thread.displayName ?? '').toLowerCase().includes(trimmed))
      : visible

    return filtered.slice(0, 9)
  }, [query, threadList])

  useEffect(() => {
    inputRef.current?.focus()
  }, [])

  useEffect(() => {
    setHighlighted(0)
  }, [query])

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent): void {
      const ctrl = event.ctrlKey || event.metaKey
      if (event.key === 'Escape') {
        event.preventDefault()
        onClose()
        return
      }

      if (event.key === 'ArrowDown') {
        event.preventDefault()
        setHighlighted((current) => Math.max(0, Math.min(visibleThreads.length - 1, current + 1)))
        return
      }

      if (event.key === 'ArrowUp') {
        event.preventDefault()
        setHighlighted((current) => Math.max(0, current - 1))
        return
      }

      if (event.key === 'Enter') {
        event.preventDefault()
        openThread(visibleThreads[highlighted])
        return
      }

      if (ctrl && /^[1-9]$/.test(event.key)) {
        event.preventDefault()
        openThread(visibleThreads[Number(event.key) - 1])
      }
    }

    window.addEventListener('keydown', handleKeyDown, true)
    return () => window.removeEventListener('keydown', handleKeyDown, true)
  }, [highlighted, onClose, visibleThreads])

  function openThread(thread: ThreadSummary | undefined): void {
    if (!thread) return
    setActiveThreadId(thread.id)
    setActiveMainView('conversation')
    onClose()
  }

  const title = query.trim()
    ? t('threadSearchDialog.matches')
    : t('threadSearchDialog.recent')

  return createPortal(
    <div
      role="presentation"
      style={{
        position: 'fixed',
        inset: 0,
        zIndex: 900,
        display: 'flex',
        alignItems: 'flex-start',
        justifyContent: 'center',
        paddingTop: '18vh',
        background: 'color-mix(in srgb, var(--bg-primary) 58%, transparent)',
        backdropFilter: 'blur(2px)'
      }}
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onClose()
      }}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-label={t('threadSearchDialog.aria')}
        style={{
          width: 'min(780px, calc(100vw - 48px))',
          border: '1px solid var(--border-default)',
          borderRadius: '28px',
          background: 'var(--bg-secondary)',
          boxShadow: 'var(--glass-shadow-soft)',
          padding: '16px 8px 10px'
        }}
      >
        <input
          ref={inputRef}
          value={query}
          onChange={(event) => setQuery(event.currentTarget.value)}
          placeholder={t('threadSearchDialog.placeholder')}
          aria-label={t('threadSearchDialog.placeholder')}
          style={{
            width: '100%',
            height: '30px',
            border: 'none',
            outline: 'none',
            background: 'transparent',
            color: 'var(--text-primary)',
            fontSize: 'var(--type-body-size)',
            lineHeight: 'var(--type-body-line-height)',
            padding: '0 12px',
            boxSizing: 'border-box'
          }}
        />

        <div
          style={{
            padding: '12px 12px 6px',
            color: 'var(--text-dimmed)',
            fontSize: 'var(--type-ui-size)',
            lineHeight: 'var(--type-ui-line-height)',
            fontWeight: 'var(--type-ui-emphasis-weight)'
          }}
        >
          {title}
        </div>

        <div style={{ display: 'grid', gap: '2px' }}>
          {visibleThreads.map((thread, index) => {
            const selected = highlighted === index
            return (
              <button
                key={thread.id}
                type="button"
                onClick={() => openThread(thread)}
                onMouseEnter={() => setHighlighted(index)}
                style={{
                  width: '100%',
                  minHeight: '42px',
                  border: 'none',
                  borderRadius: '18px',
                  display: 'grid',
                  gridTemplateColumns: '24px minmax(0, 1fr) auto auto',
                  alignItems: 'center',
                  gap: '10px',
                  padding: '0 12px',
                  background: selected ? 'var(--sidebar-control-active)' : 'transparent',
                  color: selected ? 'var(--text-primary)' : 'var(--text-secondary)',
                  cursor: 'pointer',
                  textAlign: 'left',
                  fontSize: 'var(--type-body-size)',
                  lineHeight: 'var(--type-body-line-height)'
                }}
              >
                <Monitor size={17} strokeWidth={1.8} aria-hidden />
                <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                  {thread.displayName ?? t('sidebar.newConversation')}
                </span>
                <span
                  style={{
                    color: 'var(--text-dimmed)',
                    fontSize: 'var(--type-ui-size)',
                    lineHeight: 'var(--type-ui-line-height)',
                    overflow: 'hidden',
                    textOverflow: 'ellipsis',
                    whiteSpace: 'nowrap',
                    maxWidth: '140px'
                  }}
                >
                  {workspaceName}
                </span>
                <ShortcutBadge shortcut={['Mod', String(index + 1)]} />
              </button>
            )
          })}
        </div>

        {visibleThreads.length === 0 && (
          <div
            style={{
              padding: '18px 12px 20px',
              color: 'var(--text-dimmed)',
              fontSize: 'var(--type-body-size)',
              lineHeight: 'var(--type-body-line-height)'
            }}
          >
            {t('threadSearchDialog.empty')}
          </div>
        )}
      </div>
    </div>,
    document.body
  )
}
