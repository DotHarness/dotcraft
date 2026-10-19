import { useEffect, useState } from 'react'
import { linkPreviewKey, type LinkPreviewResult } from '../../shared/linkPreviews'

export type LinkPreviewLoad = { status: 'loading' } | LinkPreviewResult

const REUSE_MS = 60_000
const UNAVAILABLE: LinkPreviewResult = { status: 'error', error: 'unavailable' }

const results = new Map<string, { result: LinkPreviewResult; at: number }>()
const inFlight = new Map<string, Promise<LinkPreviewResult>>()

function cached(key: string): LinkPreviewResult | null {
  const entry = results.get(key)
  return entry && Date.now() - entry.at < REUSE_MS ? entry.result : null
}

function requestLinkPreview(key: string): Promise<LinkPreviewResult> {
  let request = inFlight.get(key)
  if (!request) {
    request = Promise.resolve()
      .then(() => window.api.linkPreview.get(key))
      .catch(() => UNAVAILABLE)
      .then((result) => {
        results.set(key, { result, at: Date.now() })
        inFlight.delete(key)
        return result
      })
    inFlight.set(key, request)
  }
  return request
}

// Fetch only once a card opens, never for every link in a transcript.
export function useLinkPreview(url: string, enabled: boolean): LinkPreviewLoad {
  const key = linkPreviewKey(url)
  const [load, setLoad] = useState<{ key: string; value: LinkPreviewLoad }>(() => ({
    key,
    value: cached(key) ?? { status: 'loading' }
  }))

  useEffect(() => {
    if (!enabled) return
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
  }, [enabled, key])

  return load.key === key ? load.value : { status: 'loading' }
}
