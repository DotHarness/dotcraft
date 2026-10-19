import { useEffect, useState } from 'react'
import { linkPreviewKey, type LinkPreviewResult } from '../../shared/linkPreviews'

export type LinkPreviewLoad = { status: 'loading' } | LinkPreviewResult

const REUSE_MS = 30_000
const MAX_RESULTS = 32
const UNAVAILABLE: LinkPreviewResult = { status: 'error', error: 'unavailable' }

const results = new Map<string, { result: LinkPreviewResult; at: number }>()
const inFlight = new Map<string, Promise<LinkPreviewResult>>()

function cached(key: string): LinkPreviewResult | null {
  const entry = results.get(key)
  if (!entry) return null
  if (Date.now() - entry.at >= REUSE_MS) {
    results.delete(key)
    return null
  }
  return entry.result
}

function remember(key: string, result: LinkPreviewResult): void {
  const now = Date.now()
  results.delete(key)
  results.set(key, { result, at: now })
  for (const [oldKey, entry] of results) {
    if (results.size <= MAX_RESULTS && now - entry.at < REUSE_MS) break
    results.delete(oldKey)
  }
}

function requestLinkPreview(key: string): Promise<LinkPreviewResult> {
  let request = inFlight.get(key)
  if (!request) {
    request = Promise.resolve()
      .then(() => window.api.linkPreview.get(key))
      .catch(() => UNAVAILABLE)
      .then((result) => {
        remember(key, result)
        inFlight.delete(key)
        return result
      })
    inFlight.set(key, request)
  }
  return request
}

// Looks up only when a card opens (`openCount` > 0), never for every link in a transcript;
// each opening revalidates a result older than the reuse window.
export function useLinkPreview(url: string, openCount: number): LinkPreviewLoad {
  const key = linkPreviewKey(url)
  const [load, setLoad] = useState<{ key: string; value: LinkPreviewLoad }>(() => ({
    key,
    value: cached(key) ?? { status: 'loading' }
  }))

  useEffect(() => {
    if (openCount === 0) return
    const known = cached(key)
    if (known) {
      setLoad({ key, value: known })
      return
    }
    let live = true
    void requestLinkPreview(key).then((result) => {
      if (live) setLoad({ key, value: result })
    })
    return () => {
      live = false
    }
  }, [openCount, key])

  return load.key === key ? load.value : { status: 'loading' }
}
