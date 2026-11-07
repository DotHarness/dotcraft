import { useEffect, useRef } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { loadAllTurnItems, loadTurnItems, useTurnItemsLoadStore } from '../../stores/turnItemsLoadStore'
import { Button } from '../ui/Button'
import { SkeletonList } from '../ui/Skeleton'
import styles from './TurnWorkLoader.module.css'

export type TurnItemsExpansionLoader = (threadId: string, turnId: string, isNeeded: () => boolean) => Promise<void>

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
  const failed = useTurnItemsLoadStore((s) => s.statuses[turnId] === 'failed')
  const load = complete ? loadItems ?? loadAllTurnItems : loadTurnItems
  const pageCursor = complete ? undefined : cursor

  useEffect(() => {
    if (!enabled || (loadItems && !complete)) return
    let current = true
    const isNeeded = (): boolean => current
    if (complete || !pageCursor) void load(threadId, turnId, isNeeded)
    else if (container.current && typeof IntersectionObserver !== 'undefined') {
      const observer = new IntersectionObserver(entries => {
        if (entries.some(entry => entry.isIntersecting)) void load(threadId, turnId, isNeeded)
      })
      observer.observe(container.current)
      return () => { current = false; observer.disconnect() }
    }
    return () => { current = false }
  }, [threadId, turnId, enabled, complete, pageCursor, load, loadItems])

  return <div ref={container}>
    {failed ? (
      <div className={styles.failed} role="alert">
        <span>{t('conversation.turnWorkFailed')}</span>
        <Button variant="ghost" size="sm" onClick={() => void load(threadId, turnId, () => enabled)}>
          {t('conversation.turnWorkRetry')}
        </Button>
      </div>
    ) : (
      <SkeletonList
        count={3}
        gap={12}
        ariaLabel={t('conversation.turnWorkLoading')}
        rowProps={{ media: 14, mediaRadius: 4, lines: ['38%'], lineHeight: 10, lineGap: 0 }}
        rowStyle={{ gap: 8 }}
        style={{ padding: '4px 0' }}
      />
    )}
  </div>
}
