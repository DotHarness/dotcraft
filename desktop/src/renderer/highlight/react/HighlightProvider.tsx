import { createContext, useContext, useEffect, useMemo, type ReactNode } from 'react'
import { HighlighterPool, type HighlighterPoolOptions } from '../pool/highlighterPool'

const HighlighterPoolContext = createContext<HighlighterPool | undefined>(undefined)

export interface HighlightProviderProps extends HighlighterPoolOptions {
  children: ReactNode
}

export function HighlightProvider({ children, ...options }: HighlightProviderProps): JSX.Element {
  // Options are captured at mount: rebuilding the pool would terminate live
  // workers and drop every cached tokenization.
  const pool = useMemo(() => new HighlighterPool(options), [])

  useEffect(() => {
    pool.warmUp()
    return () => { pool.terminate() }
  }, [pool])

  return (
    <HighlighterPoolContext.Provider value={pool}>
      {children}
    </HighlighterPoolContext.Provider>
  )
}

export function useHighlighterPool(): HighlighterPool | undefined {
  return useContext(HighlighterPoolContext)
}
