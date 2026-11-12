import { useSubAgentTaskTree } from '../../hooks/useSubAgentTaskTree'
import { RunningTasksIndicator } from '../conversation/TurnWorkingLine'
import styles from '../conversation/TurnWorkingLine.module.css'

export function SubagentRunningTasks({ threadId }: { threadId: string }): JSX.Element | null {
  const tree = useSubAgentTaskTree(threadId)
  const count = (tree.ownTasks?.terminals.length ?? 0) + (tree.ownTasks?.workflows.length ?? 0)
    + tree.activeAgents
    + tree.owners.reduce((total, owner) => total + owner.terminals.length + owner.workflows.length, 0)
  return count > 0 ? <div className={styles.line}><RunningTasksIndicator count={count} /></div> : null
}
