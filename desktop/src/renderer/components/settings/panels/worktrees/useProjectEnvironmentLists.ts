import { useCallback, useEffect, useRef, useState } from 'react'
import type { ProjectEnvironmentList } from '../../../../../shared/projectActions'

export type ProjectEnvironmentListState = ProjectEnvironmentList | 'loading' | 'failed'

export interface ProjectEnvironmentLists {
  lists: Readonly<Record<string, ProjectEnvironmentListState>>
  revision: number
  refresh(projectPath: string): Promise<ProjectEnvironmentList | null>
}

export function useProjectEnvironmentLists(projectPaths: readonly string[]): ProjectEnvironmentLists {
  const [lists, setLists] = useState<Record<string, ProjectEnvironmentListState>>({})
  const [revision, setRevision] = useState(0)
  const sequence = useRef<Record<string, number>>({})
  const key = projectPaths.join('\n')

  const refresh = useCallback(async (path: string): Promise<ProjectEnvironmentList | null> => {
    const id = (sequence.current[path] ?? 0) + 1
    sequence.current[path] = id
    try {
      const list = await window.api.projectActions.listProjectEnvironments(path)
      if (sequence.current[path] === id) setLists((current) => ({ ...current, [path]: list }))
      return list
    } catch {
      if (sequence.current[path] === id) setLists((current) => ({ ...current, [path]: 'failed' }))
      return null
    }
  }, [])

  useEffect(() => {
    const paths = key ? key.split('\n') : []
    setLists((current) => Object.fromEntries(paths.map((path) => [path, current[path] ?? 'loading'])))
    const unwatch = paths.map((path) => {
      void refresh(path)
      return window.api.projectActions.watch(path, () => {
        void refresh(path)
        setRevision((value) => value + 1)
      })
    })
    return () => {
      for (const path of paths) sequence.current[path] = (sequence.current[path] ?? 0) + 1
      for (const dispose of unwatch) dispose()
    }
  }, [key, refresh])

  return { lists, revision, refresh }
}
