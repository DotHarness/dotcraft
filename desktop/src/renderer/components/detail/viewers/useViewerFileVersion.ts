import { useCallback, useEffect, useRef, useState } from 'react'

interface ViewerFileVersion {
  version: number
  stale: boolean
  refresh: () => void
}

interface FileStamp {
  mtimeMs: number
  sizeBytes: number
}

const sameStamp = (left: FileStamp, right: FileStamp) =>
  left.mtimeMs === right.mtimeMs && left.sizeBytes === right.sizeBytes

export function useViewerFileVersion(absolutePath: string): ViewerFileVersion {
  const [state, setState] = useState({ path: absolutePath, version: 0, stale: false })
  const baseline = useRef<FileStamp | null>(null)
  const latest = useRef<FileStamp | null>(null)

  useEffect(() => {
    let disposed = false
    let unwatch: (() => void) | null = null
    baseline.current = null
    latest.current = null
    setState((current) => (current.path === absolutePath ? current : { path: absolutePath, version: 0, stale: false }))
    const viewer = window.api.workspace.viewer
    viewer.classify({ absolutePath })
      .then((result) => {
        if (!disposed && !baseline.current) baseline.current = { mtimeMs: result.mtimeMs, sizeBytes: result.sizeBytes }
      })
      .catch(() => {})
    viewer.watchText({ absolutePath }, (payload) => {
      if (disposed) return
      const stamp = { mtimeMs: payload.mtimeMs, sizeBytes: payload.sizeBytes }
      latest.current = stamp
      if (baseline.current && !sameStamp(baseline.current, stamp)) {
        setState((current) => (current.stale ? current : { ...current, stale: true }))
      }
    })
      .then((dispose) => {
        if (disposed) dispose()
        else unwatch = dispose
      })
      .catch(() => {})
    return () => {
      disposed = true
      unwatch?.()
    }
  }, [absolutePath])

  const refresh = useCallback(() => {
    if (latest.current) baseline.current = latest.current
    setState((current) => ({ ...current, version: current.version + 1, stale: false }))
  }, [])

  const current = state.path === absolutePath ? state : { version: 0, stale: false }
  return { version: current.version, stale: current.stale, refresh }
}
