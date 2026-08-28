import { useEffect, useRef, useState } from 'react'
import { useHighlighterPool } from './HighlightProvider'
import type { HighlighterPool } from '../pool/highlighterPool'
import type {
  DiffHighlightRequest,
  DiffHighlightResult,
  FileHighlightRequest,
  FileHighlightResult
} from '../types'

interface Entry<T> {
  key: string | undefined
  result: T | undefined
}

// The peek and submit callbacks live at module scope because they are effect
// dependencies of useHighlight.
const peekFile = (pool: HighlighterPool, key: string | undefined): FileHighlightResult | undefined =>
  pool.peekFile(key)

const submitFile = (
  pool: HighlighterPool,
  subscriber: object,
  request: FileHighlightRequest,
  onResult: (result: FileHighlightResult) => void
): FileHighlightResult | undefined => pool.requestFile(subscriber, request, onResult)

export function useFileHighlight(
  request: FileHighlightRequest | undefined
): FileHighlightResult | undefined {
  return useHighlight(request, peekFile, submitFile)
}

const peekDiff = (pool: HighlighterPool, key: string | undefined): DiffHighlightResult | undefined =>
  pool.peekDiff(key)

const submitDiff = (
  pool: HighlighterPool,
  subscriber: object,
  request: DiffHighlightRequest,
  onResult: (result: DiffHighlightResult) => void
): DiffHighlightResult | undefined => pool.requestDiff(subscriber, request, onResult)

export function useDiffHighlight(
  request: DiffHighlightRequest | undefined
): DiffHighlightResult | undefined {
  return useHighlight(request, peekDiff, submitDiff)
}

function useHighlight<Request extends { cacheKey?: string }, Result>(
  request: Request | undefined,
  peek: (pool: HighlighterPool, key: string | undefined) => Result | undefined,
  submit: (
    pool: HighlighterPool,
    subscriber: object,
    request: Request,
    onResult: (result: Result) => void
  ) => Result | undefined
): Result | undefined {
  const pool = useHighlighterPool()
  const subscriber = useRef<object>({}).current
  // Read by the effect without being a dependency, so a caller that rebuilds the
  // request object every render still schedules only once.
  const latest = useRef(request)
  latest.current = request

  const key = request?.cacheKey
  const [entry, setEntry] = useState<Entry<Result>>(() => ({
    key,
    result: pool === undefined ? undefined : peek(pool, key)
  }))

  if (entry.key !== key) {
    // Derived during render so a cache hit paints highlighted immediately
    // instead of one frame after the effect runs.
    setEntry({ key, result: pool === undefined ? undefined : peek(pool, key) })
  }

  useEffect(() => {
    const current = latest.current
    if (pool === undefined || current === undefined) return
    const cached = submit(pool, subscriber, current, (result) => {
      setEntry({ key: current.cacheKey, result })
    })
    if (cached !== undefined) setEntry({ key: current.cacheKey, result: cached })
    return () => { pool.release(subscriber) }
  }, [pool, key, subscriber, submit])

  return entry.key === key ? entry.result : undefined
}
