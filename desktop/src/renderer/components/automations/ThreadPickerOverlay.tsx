import { useMemo, useState } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useThreadStore } from '../../stores/threadStore'
import type { ThreadSummary } from '../../types/thread'

interface Props {
  onSelect(thread: ThreadSummary): void
  onClose(): void
}

/**
 * Lightweight overlay used from the New Task dialog's Target pill and from Review Panel's "Change" action.
 * Lists active threads sorted by lastActiveAt; filters out archived/paused by default.
 */
export function ThreadPickerOverlay({ onSelect, onClose }: Props): JSX.Element {
  const t = useT()
  const threadList = useThreadStore((s) => s.threadList)
  const [query, setQuery] = useState('')

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase()
    const sorted = [...threadList]
      .filter((th) => th.status === 'active')
      .sort((a, b) => new Date(b.lastActiveAt).getTime() - new Date(a.lastActiveAt).getTime())
    if (!q) return sorted
    return sorted.filter((th) => (th.displayName ?? '').toLowerCase().includes(q))
  }, [threadList, query])

  return (
    <div
      onClick={onClose}
      style={{
        position: 'fixed',
        inset: 0,
        zIndex: 1100,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        backgroundColor: 'rgba(0,0,0,0.5)'
      }}
    >
      <div
        onClick={(e) => e.stopPropagation()}
        style={{
          width: '420px',
          maxHeight: '70vh',
          display: 'flex',
          flexDirection: 'column',
          backgroundColor: 'var(--bg-primary)',
          border: '1px solid var(--border-default)',
          borderRadius: '10px',
          overflow: 'hidden',
          boxShadow: '0 8px 32px rgba(0,0,0,0.3)'
        }}
      >
        <div
          style={{
            padding: '12px 16px',
            borderBottom: '1px solid var(--border-default)',
            fontSize: '14px',
            fontWeight: 600,
            color: 'var(--text-primary)'
          }}
        >
          {t('auto.newTask.threadPickerTitle')}
        </div>
        <div style={{ padding: '10px 16px', borderBottom: '1px solid var(--border-default)' }}>
          <input
            autoFocus
            type="text"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder={t('auto.newTask.threadPickerSearch')}
            style={{
              width: '100%',
              padding: '7px 10px',
              borderRadius: '6px',
              border: '1px solid var(--border-default)',
              backgroundColor: 'var(--bg-secondary)',
              color: 'var(--text-primary)',
              fontSize: '13px',
              outline: 'none'
            }}
          />
        </div>
        <div style={{ flex: 1, overflowY: 'auto' }}>
          {filtered.length === 0 && (
            <div style={{ padding: '24px 16px', fontSize: '13px', color: 'var(--text-tertiary)', textAlign: 'center' }}>
              {t('auto.newTask.threadPickerEmpty')}
            </div>
          )}
          {filtered.map((th) => (
            <button
              key={th.id}
              type="button"
              onClick={() => {
                onSelect(th)
                onClose()
              }}
              style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                width: '100%',
                padding: '10px 16px',
                border: 'none',
                borderBottom: '1px solid var(--border-default)',
                backgroundColor: 'transparent',
                color: 'var(--text-primary)',
                fontSize: '13px',
                cursor: 'pointer',
                textAlign: 'left'
              }}
              onMouseEnter={(e) => (e.currentTarget.style.backgroundColor = 'var(--bg-secondary)')}
              onMouseLeave={(e) => (e.currentTarget.style.backgroundColor = 'transparent')}
            >
              <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', flex: 1 }}>
                {th.displayName ?? t('sidebar.newConversation')}
              </span>
              <span style={{ fontSize: '11px', color: 'var(--text-tertiary)', marginLeft: '8px', flexShrink: 0 }}>
                {th.originChannel}
              </span>
            </button>
          ))}
        </div>
      </div>
    </div>
  )
}
