import { useEffect } from 'react'
import { create } from 'zustand'
import type { ThreadWorktreeStatus } from '@dotcraft/sdk/contracts'
import { useSourceControlStore } from '../../../../stores/sourceControlStore'
import { requestWorkspaceResource, workspaceResourceKey, type WorkspaceResourceScope } from '../../../../stores/workspaceResourceScope'
import { isSettingsResourceScopeCurrent, useSettingsResourceScope } from '../../useSettingsProviderResources'

export type ListedWorktree = ThreadWorktreeStatus & { path: string; branchName: string }

interface WorktreeListSnapshot {
  key: string
  data: ListedWorktree[] | null
  error: string | null
}

const useWorktreeListStore = create<{ snapshot: WorktreeListSnapshot | null }>(() => ({ snapshot: null }))
let pending: { key: string; promise: Promise<void> } | null = null

function getSnapshot(key: string): WorktreeListSnapshot | null {
  const { snapshot } = useWorktreeListStore.getState()
  return snapshot?.key === key ? snapshot : null
}

export function refreshWorktreeList(scope: WorkspaceResourceScope, removedPath?: string): Promise<void> {
  if (!isSettingsResourceScopeCurrent(scope)) return Promise.resolve()
  const key = workspaceResourceKey(scope)
  if (removedPath === undefined && pending?.key === key) return pending.promise

  const snapshot = getSnapshot(key)
  if (removedPath !== undefined && snapshot?.data) {
    useWorktreeListStore.setState({ snapshot: { key, data: snapshot.data.filter((row) => row.path !== removedPath), error: null } })
  } else if (snapshot?.error != null) {
    useWorktreeListStore.setState({ snapshot: null })
  }

  const isCurrent = (): boolean => pending?.promise === promise && isSettingsResourceScopeCurrent(scope)
  const promise = requestWorkspaceResource(scope, 'worktree/list', {})
    .then((result) => {
      if (!isCurrent()) return
      const data = (result.data ?? []).filter((row): row is ListedWorktree =>
        typeof row.path === 'string' && typeof row.branchName === 'string')
      useWorktreeListStore.setState({ snapshot: { key, data, error: null } })
    })
    .catch((error: unknown) => {
      if (!isCurrent() || getSnapshot(key)?.data) return
      useWorktreeListStore.setState({
        snapshot: { key, data: null, error: error instanceof Error ? error.message : String(error) }
      })
    })
    .finally(() => {
      if (pending?.promise === promise) pending = null
    })
  pending = { key, promise }
  return promise
}

export function useWorktreeList(scope: WorkspaceResourceScope): WorktreeListSnapshot | null {
  const key = workspaceResourceKey(scope)
  return useWorktreeListStore((state) => state.snapshot?.key === key ? state.snapshot : null)
}

export function useWorktreeAvailability(workspacePath?: string): 'loading' | 'unavailable' | 'git' {
  return useSourceControlStore((state) => {
    if (!workspacePath) return 'unavailable'
    if (state.workspacePath !== workspacePath || state.bindingStatus === 'loading') return 'loading'
    return state.effectiveProvider === 'git' ? 'git' : 'unavailable'
  })
}

export function usePrefetchSettingsWorktrees(workspacePath?: string): void {
  const scope = useSettingsResourceScope(Boolean(workspacePath))
  const ensure = useSourceControlStore((state) => state.ensure)
  const availability = useWorktreeAvailability(workspacePath)
  useEffect(() => {
    if (scope && workspacePath) ensure(workspacePath, true)
  }, [ensure, scope, workspacePath])
  useEffect(() => {
    if (scope && availability === 'git') void refreshWorktreeList(scope)
  }, [scope, availability])
}
