import { useSubAgentTaskTree } from '../../hooks/useSubAgentTaskTree'
import { runningWorkOf } from '../conversation/summary/store'
import { RunningTasksIndicator } from '../conversation/TurnWorkingLine'
import styles from '../conversation/TurnWorkingLine.module.css'

export function SubagentRunningTasks({ threadId }: { threadId: string }): JSX.Element | null {
  const { activeAgents, workflows, terminals } = useSubAgentTaskTree(threadId)
  const work = runningWorkOf(activeAgents, workflows, terminals)
  return work.total > 0 ? <div className={styles.line}><RunningTasksIndicator work={work} /></div> : null
}
