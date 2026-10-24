import { useEffect, useState } from 'react'
import type { WorktreeEnvironmentsResult } from '../../../shared/projectActions'
import type { WorktreeCreationEnvironment } from '../../stores/worktreeCreationStore'

export function useWorktreeEnvironments(workspacePath: string | null): WorktreeEnvironmentsResult | null {
  const [result, setResult] = useState<WorktreeEnvironmentsResult | null>(null)
  useEffect(() => {
    setResult(null)
    if (!workspacePath) return
    let active = true
    let sequence = 0
    const load = (): void => {
      const request = ++sequence
      window.api.projectActions.listWorktreeEnvironments(workspacePath).then((next) => {
        if (active && request === sequence) setResult(next)
      }, () => {})
    }
    load()
    const unwatch = window.api.projectActions.watch(workspacePath, load)
    return () => {
      active = false
      unwatch()
    }
  }, [workspacePath])
  return result
}

export async function selectedWorktreeEnvironment(workspacePath: string): Promise<WorktreeCreationEnvironment | null> {
  const { environments, selectedEnvironmentId } = await window.api.projectActions.listWorktreeEnvironments(workspacePath)
  const selected = environments.find((environment) => environment.id === selectedEnvironmentId)
  return selected ? { id: selected.id, name: selected.name, path: selected.path } : null
}
