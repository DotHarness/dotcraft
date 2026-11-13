import { chatTasks, type TaskSources } from './backgroundTasks'
import { chatKey, type ComputerState } from './state'

interface Branch {
  threadId: string
  activeTurnId: string | null
  sources: TaskSources | undefined
}

type TaskTree = ReturnType<typeof chatTasks>

function branchesOf(computer: ComputerState, key: string): Branch[] {
  const chat = computer.chats[key]
  if (!chat) return []
  const branches: Branch[] = []
  const visited = new Set<string>()
  const visit = (threadId: string, activeTurnId: string | null): void => {
    if (visited.has(threadId)) return
    visited.add(threadId)
    const sources = computer.tasks[chatKey(chat.projectId, threadId)]
    branches.push({ threadId, activeTurnId, sources })
    for (const task of sources?.agent ?? []) {
      if (task.kind === 'agent' && task.open) visit(task.id, task.activeTurnId)
    }
  }
  visit(chat.threadId, chat.runtime?.activeTurnId ?? null)
  return branches
}

function collect(branches: Branch[]): TaskTree {
  const result: TaskTree = { running: [], completed: [] }
  for (const branch of branches) {
    const tasks = chatTasks(branch.sources, branch.activeTurnId)
    result.running.push(...tasks.running)
    result.completed.push(...tasks.completed)
  }
  return result
}

export function chatTaskTree(computer: ComputerState, key: string): TaskTree {
  return collect(branchesOf(computer, key))
}

export function createWorkingSubagentSelector(): (computer: ComputerState, key: string) => ReadonlySet<string> {
  let previous: ReadonlySet<string> = new Set()
  return (computer, key) => {
    const workingThreads = new Set<string>()
    const paths = new Set<string>()
    for (const branch of branchesOf(computer, key).reverse()) {
      if (chatTasks(branch.sources, branch.activeTurnId).running.length > 0) workingThreads.add(branch.threadId)
      for (const task of branch.sources?.agent ?? []) {
        if (task.kind !== 'agent' || !task.open || (task.status !== 'running' && !workingThreads.has(task.id))) continue
        workingThreads.add(branch.threadId)
        if (task.agentPath) paths.add(task.agentPath)
      }
    }
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
      return branch.threadId === before.threadId && branch.activeTurnId === before.activeTurnId && branch.sources === before.sources
    })) return result
    previous = branches
    result = collect(branches)
    return result
  }
}
