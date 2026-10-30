import { useEffect } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { loadTurnItems, useTurnItemsLoadStore } from '../../stores/turnItemsLoadStore'
import { Button } from '../ui/Button'
import { SkeletonList } from '../ui/Skeleton'
import styles from './TurnWorkLoader.module.css'

export function TurnWorkLoader({ threadId, turnId }: { threadId: string; turnId: string }): JSX.Element {
  const t = useT()
  const failed = useTurnItemsLoadStore((s) => s.statuses[turnId] === 'failed')

  useEffect(() => {
    void loadTurnItems(threadId, turnId)
  }, [threadId, turnId])

  if (failed) {
    return (
      <div className={styles.failed} role="alert">
        <span>{t('conversation.turnWorkFailed')}</span>
        <Button variant="ghost" size="sm" onClick={() => void loadTurnItems(threadId, turnId)}>
          {t('conversation.turnWorkRetry')}
        </Button>
      </div>
    )
  }
  return (
    <SkeletonList
      count={3}
      gap={12}
      ariaLabel={t('conversation.turnWorkLoading')}
      rowProps={{ media: 14, mediaRadius: 4, lines: ['38%'], lineHeight: 10, lineGap: 0 }}
      rowStyle={{ gap: 8 }}
      style={{ padding: '4px 0' }}
    />
  )
}
