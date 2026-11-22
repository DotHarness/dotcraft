import { useState, type JSX } from 'react'
import { GitBranch, Split } from 'lucide-react'
import { translate, type AppLocale } from '../../../shared/locales'
import type { ConversationItem } from '../../types/conversation'
import type { WorktreeToolDisplay } from '../../utils/worktreeToolDisplay'
import { RunningShimmer } from '../ui/RunningShimmer'
import { ToolDisclosure } from './ToolDisclosure'
import styles from './WorktreeToolRow.module.css'

export function WorktreeToolRow({
  item,
  display,
  running,
  locale
}: {
  item: ConversationItem
  display: WorktreeToolDisplay
  running: boolean
  locale: AppLocale
}): JSX.Element {
  const [expanded, setExpanded] = useState(false)
  const hasDetails = !running && Boolean(display.branchName || display.worktreePath || display.checkoutPath || display.error)
  const title = running ? (
    <RunningShimmer className="dc-tool-row-running-label">{display.label}</RunningShimmer>
  ) : (
    <>
      <span>{display.label}</span>
      {display.branchName ? (
        <>
          {' '}
          <span className={styles.branch}>
            <GitBranch size={11} strokeWidth={2} aria-hidden />
            <span>{display.branchName}</span>
          </span>
        </>
      ) : null}
    </>
  )

  return (
    <ToolDisclosure
      expanded={hasDetails && expanded}
      onToggle={() => setExpanded((value) => !value)}
      expandable={hasDetails}
      callId={item.toolCallId}
      icon={{ icon: Split }}
      title={title}
    >
      <div className={`selectable dc-tool-panel-surface ${styles.panel}`} data-padded="true">
        {display.branchName ? (
          <div className={styles.row}>
            <span className={styles.key}>{translate(locale, 'toolCall.worktree.detailBranch')}</span>
            <span className={styles.value}>
              {display.branchName}
              {display.baseRef ? (
                <span className={styles.meta}>
                  {` (${translate(locale, 'toolCall.worktree.detailFromBase', { baseRef: display.baseRef })})`}
                </span>
              ) : null}
            </span>
          </div>
        ) : null}
        {display.worktreePath ? (
          <div className={styles.row}>
            <span className={styles.key}>{translate(locale, 'toolCall.worktree.detailWorktree')}</span>
            <span className={styles.value}>{display.worktreePath}</span>
          </div>
        ) : null}
        {display.checkoutPath && display.operation === 'exit' ? (
          <div className={styles.row}>
            <span className={styles.key}>{translate(locale, 'toolCall.worktree.detailCheckout')}</span>
            <span className={styles.value}>{display.checkoutPath}</span>
          </div>
        ) : null}
        {display.error ? <div className={styles.error}>{display.error}</div> : null}
      </div>
    </ToolDisclosure>
  )
}
