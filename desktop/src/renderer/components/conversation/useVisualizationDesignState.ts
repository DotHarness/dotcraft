import { useCallback, useEffect, useRef, useState } from 'react'
import { visualizationKey } from './visualizationParser'
import {
  initialVisualizationDesignValues,
  readVisualizationDesignControls,
  reconcileVisualizationDesignValues,
  type VisualizationDesignControls,
  type VisualizationDesignValue,
  type VisualizationDesignValues
} from './visualizationDesignControls'

type ValuesListener = (values: VisualizationDesignValues) => void

const shared = new Map<string, { values: VisualizationDesignValues; views: Map<string, ValuesListener> }>()

export function clearVisualizationDesignValues(threadId: string, path: string): void {
  const entry = shared.get(visualizationKey(threadId, path))
  if (entry) entry.values = {}
}

export interface VisualizationDesignState {
  controls: VisualizationDesignControls | null
  values: VisualizationDesignValues
  showingOriginal: boolean
  receive: (params: unknown) => void
  clear: () => void
  change: (id: string, value: VisualizationDesignValue) => void
  reset: () => void
  toggleOriginal: () => void
}

export function useVisualizationDesignState({ threadId, path, viewId, post }: {
  threadId: string
  path: string
  viewId: string
  post: (values: VisualizationDesignValues) => void
}): VisualizationDesignState {
  const [controls, setControls] = useState<VisualizationDesignControls | null>(null)
  const [values, setValues] = useState<VisualizationDesignValues>({})
  const [showingOriginal, setShowingOriginal] = useState(false)
  const controlsRef = useRef(controls)
  const valuesRef = useRef(values)
  const showingOriginalRef = useRef(showingOriginal)
  const postRef = useRef(post)
  postRef.current = post
  const key = visualizationKey(threadId, path)

  const apply = useCallback((next: VisualizationDesignValues, send: boolean): void => {
    const previous = valuesRef.current
    valuesRef.current = next
    setValues(next)
    if (!send || showingOriginalRef.current) return
    const changed = Object.entries(next).filter(([id, value]) => previous[id] !== value)
    if (changed.length > 0) postRef.current(Object.fromEntries(changed))
  }, [])

  const publish = useCallback((next: VisualizationDesignValues): void => {
    const entry = shared.get(key)
    if (!entry) return
    entry.values = { ...entry.values, ...next }
    for (const [id, listener] of [...entry.views]) if (id !== viewId) listener(entry.values)
  }, [key, viewId])

  useEffect(() => {
    const entry = shared.get(key) ?? { values: {}, views: new Map<string, ValuesListener>() }
    shared.set(key, entry)
    entry.views.set(viewId, incoming => {
      const current = controlsRef.current
      if (current) apply(reconcileVisualizationDesignValues(current, { ...valuesRef.current, ...incoming }), true)
    })
    return () => {
      entry.views.delete(viewId)
      if (entry.views.size === 0 && shared.get(key) === entry) shared.delete(key)
    }
  }, [apply, key, viewId])

  const receive = useCallback((params: unknown): void => {
    const next = readVisualizationDesignControls(params)
    if (!next) return
    const initial = initialVisualizationDesignValues(next)
    if (!controlsRef.current) valuesRef.current = initial
    controlsRef.current = next
    setControls(next)
    apply(reconcileVisualizationDesignValues(next, { ...valuesRef.current, ...shared.get(key)?.values }), true)
  }, [apply, key])

  const clear = useCallback((): void => {
    controlsRef.current = null
    valuesRef.current = {}
    showingOriginalRef.current = false
    setControls(null)
    setValues({})
    setShowingOriginal(false)
  }, [])

  const setOriginal = (showing: boolean): void => {
    showingOriginalRef.current = showing
    setShowingOriginal(showing)
  }

  return {
    controls,
    values,
    showingOriginal,
    receive,
    clear,
    change: (id, value) => {
      if (showingOriginalRef.current) return
      const next = { ...valuesRef.current, [id]: value }
      apply(next, true)
      publish({ [id]: value })
    },
    reset: () => {
      if (!controls) return
      const initial = initialVisualizationDesignValues(controls)
      setOriginal(false)
      valuesRef.current = initial
      setValues(initial)
      postRef.current(initial)
      publish(initial)
    },
    toggleOriginal: () => {
      if (!controls) return
      postRef.current(showingOriginalRef.current ? valuesRef.current : initialVisualizationDesignValues(controls))
      setOriginal(!showingOriginalRef.current)
    }
  }
}
