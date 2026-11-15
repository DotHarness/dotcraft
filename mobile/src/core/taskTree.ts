import { chatTasks, type BackgroundTask, type TaskSources } from './backgroundTasks'
import { chatKey, type ComputerState } from './state'

interface Branch {
  threadId: string
  sources: TaskSources | undefined
}

type TaskTree = ReturnType<typeof chatTasks>

function branchesOf(computer: ComputerState, key: string): Branch[] {
  const chat = computer.chats[key]
  if (!chat) return []
  const branches: Branch[] = []
  const visited = new Set<string>()
  const visit = (threadId: string): void => {
    if (visited.has(threadId)) return
    visited.add(threadId)
    const sources = computer.tasks[chatKey(chat.projectId, threadId)]
    branches.push({ threadId, sources })
    for (const task of sources?.agent ?? []) {
      if (task.kind === 'agent' && task.open) visit(task.id)
    }
  }
  visit(chat.threadId)
  return branches
}

function project(branches: Branch[]): { tasks: TaskTree; paths: Set<string> } {
  const sources = new Map<string, TaskSources>()
  const working = new Set<string>()
  const paths = new Set<string>()
  for (const branch of [...branches].reverse()) {
    const agent = branch.sources?.agent?.map((task): BackgroundTask => {
      if (task.kind !== 'agent' || !task.open || (task.status !== 'running' && !task.reconciling && !working.has(task.id))) return task
      if (task.agentPath) paths.add(task.agentPath)
      return task.status === 'running' ? task : { ...task, status: 'running', endedAt: null }
    })
    const projected = { ...branch.sources, agent }
    if (chatTasks(projected).running.length > 0) working.add(branch.threadId)
    sources.set(branch.threadId, projected)
  }
  const tasks: TaskTree = { running: [], completed: [] }
  for (const branch of branches) {
    const own = chatTasks(sources.get(branch.threadId))
    tasks.running.push(...own.running)
    tasks.completed.push(...own.completed)
  }
  return { tasks, paths }
}

export function chatTaskTree(computer: ComputerState, key: string): TaskTree {
  return project(branchesOf(computer, key)).tasks
}

export function createWorkingSubagentSelector(): (computer: ComputerState, key: string) => ReadonlySet<string> {
  let previous: ReadonlySet<string> = new Set()
  return (computer, key) => {
    const paths = project(branchesOf(computer, key)).paths
    if (paths.size === previous.size && [...paths].every((path) => previous.has(path))) return previous
    previous = paths
    return paths
  }
}

export function createTaskTreeSelector(): (computer: ComputerState, key: string) => TaskTree {
  let previous: Branch[] = []
  let result: TaskTree = { running: [], completed: [] }
  return (computer, key) => {
    const branches = branchesOf(computer, key)
    if (branches.length === previous.length && branches.every((branch, index) => {
      const before = previous[index]
      return branch.threadId === before.threadId && branch.sources === before.sources
    })) return result
    previous = branches
    result = project(branches).tasks
    return result
  }
}
