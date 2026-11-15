import { useSubAgentTaskTree } from '../../hooks/useSubAgentTaskTree'
import { RunningTasksIndicator } from '../conversation/TurnWorkingLine'
import styles from '../conversation/TurnWorkingLine.module.css'

export function SubagentRunningTasks({ threadId }: { threadId: string }): JSX.Element | null {
  const { count } = useSubAgentTaskTree(threadId)
  return count > 0 ? <div className={styles.line}><RunningTasksIndicator count={count} /></div> : null
}
