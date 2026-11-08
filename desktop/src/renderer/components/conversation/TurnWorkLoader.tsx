import { useCallback, useEffect, useRef } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { loadAllTurnItems, loadTurnItems, useTurnItemsLoadStore } from '../../stores/turnItemsLoadStore'
import { Button } from '../ui/Button'
import { Spinner } from '../ui/Spinner'
import styles from './TurnWorkLoader.module.css'

export type TurnItemsExpansionLoader = (
  threadId: string, turnId: string, isNeeded: () => boolean, complete: boolean
) => Promise<void>

interface TurnWorkLoaderProps {
  threadId: string
  turnId: string
  enabled: boolean
  complete: boolean
  cursor?: string
  loadItems?: TurnItemsExpansionLoader
}

export function TurnWorkLoader({ threadId, turnId, enabled, complete, cursor, loadItems }: TurnWorkLoaderProps): JSX.Element {
  const t = useT()
  const container = useRef<HTMLDivElement>(null)
  const status = useTurnItemsLoadStore((state) => state.statuses[turnId])
  const needed = useRef(enabled)
  needed.current = enabled
  const load = useCallback((isNeeded: () => boolean): Promise<void> => loadItems
    ? loadItems(threadId, turnId, isNeeded, complete)
    : (complete ? loadAllTurnItems : loadTurnItems)(threadId, turnId, isNeeded),
  [threadId, turnId, complete, loadItems])
  const pageCursor = complete ? undefined : cursor

  useEffect(() => {
    if (!enabled) return
    let current = true
    const isNeeded = (): boolean => current
    if (complete || !pageCursor) void load(isNeeded)
    else if (container.current && typeof IntersectionObserver !== 'undefined') {
      const observer = new IntersectionObserver(entries => {
        if (current && entries.some(entry => entry.isIntersecting)) void load(isNeeded)
      })
      observer.observe(container.current)
      return () => { current = false; observer.disconnect() }
    }
    return () => { current = false }
  }, [enabled, complete, pageCursor, load])

  return <div ref={container}>
    {enabled && (status === 'failed' ? (
      <div className={styles.failed} role="alert">
        <span>{t('conversation.turnWorkFailed')}</span>
        <Button variant="ghost" size="sm" onClick={() => void load(() => needed.current && container.current != null)}>
          {t('conversation.turnWorkRetry')}
        </Button>
      </div>
    ) : status === 'loading' ? (
      <div className={styles.loading} role="status" aria-label={t('conversation.turnWorkLoading')}>
        <Spinner size={14} />
      </div>
    ) : null)}
  </div>
}
