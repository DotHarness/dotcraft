import { useCallback, useEffect, useState } from 'react'
import type { BrowserControlsState } from './BrowserFeedbackControls'

const initialState: BrowserControlsState = {
  downloads: [],
  find: { open: false, query: '', current: 0, total: 0 },
  zoom: 100,
  error: '',
}

export function useBrowserFeedback(tabId: string) {
  const [state, setState] = useState(initialState)
  const run = useCallback((operation: () => Promise<unknown>) => {
    setState((value) => ({ ...value, error: '' }))
    void operation().catch((error) =>
      setState((value) => ({
        ...value,
        error: String(error instanceof Error ? error.message : error),
      })),
    )
  }, [])
  useEffect(() => {
    setState(initialState)
    const api = window.api.workspace.viewer.browser
    const unsubscribe = api.onFeedback((event) => {
      if (event.type === 'error' && event.tabId === tabId)
        setState(value => ({ ...value, error: event.message }))
      if (event.type === 'downloads')
        setState((value) => ({ ...value, downloads: event.records }))
      if (event.type === 'find' && event.tabId === tabId)
        setState((value) => ({ ...value, find: event.state }))
      if (event.type === 'zoom' && event.tabId === tabId)
        setState((value) => ({ ...value, zoom: event.percent }))
    })
    let current = true
    void api
      .downloads()
      .then((downloads) => {
        if (current) setState((value) => ({ ...value, downloads }))
      })
      .catch((error) => {
        if (current) setState((value) => ({ ...value, error: String(error) }))
      })
    return () => {
      current = false
      unsubscribe()
    }
  }, [tabId])
  const initialize = useCallback(
    (snapshot: { find: BrowserControlsState['find']; zoomPercent: number }) => {
      setState((value) => ({
        ...value,
        find: snapshot.find,
        zoom: snapshot.zoomPercent,
      }))
    },
    [],
  )
  return { state, run, initialize }
}
