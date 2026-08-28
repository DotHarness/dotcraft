import { useT } from '../../contexts/LocaleContext'
import type { ThreadSummary, ThreadGroup as ThreadGroupType } from '../../types/thread'
import type { MessageKey } from '../../../shared/locales'
import { ThreadEntry } from './ThreadEntry'

interface ThreadGroupProps {
  label: ThreadGroupType | 'Pinned'
  threads: ThreadSummary[]
}

const GROUP_LABEL_KEY: Record<ThreadGroupType | 'Pinned', MessageKey> = {
  Pinned: 'threadGroup.pinned',
  Today: 'threadGroup.today',
  Yesterday: 'threadGroup.yesterday',
  'Previous 7 Days': 'threadGroup.prev7Days',
  'Previous 30 Days': 'threadGroup.prev30Days',
  Older: 'threadGroup.older'
}

export function ThreadGroup({ label, threads }: ThreadGroupProps): JSX.Element {
  const t = useT()
  return (
    <div style={{ marginBottom: '4px' }}>
      <div
        style={{
          padding: '6px 16px 2px',
          fontSize: 'var(--type-secondary-size)',
          lineHeight: 'var(--type-secondary-line-height)',
          fontWeight: 'var(--type-ui-emphasis-weight)',
          textTransform: 'uppercase',
          color: 'var(--text-dimmed)',
          letterSpacing: '0.04em'
        }}
      >
        {t(GROUP_LABEL_KEY[label])}
      </div>

      {threads.map((thread) => (
        <ThreadEntry key={thread.id} thread={thread} />
      ))}
    </div>
  )
}
