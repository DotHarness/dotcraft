import {
  createContext,
  useContext,
  useId,
  useLayoutEffect,
  useMemo,
  useState,
  type RefObject
} from 'react'

/**
 * How far the composer mascot has to climb so it keeps standing on the topmost
 * composer surface. An overlay that opens upward at the card's own width lands
 * where the mascot stands, so it reports its reach here and ComposerShell folds
 * that into the mascot's existing `anchorOffset`.
 */
interface ComposerOverlayLiftApi {
  report(id: string, height: number): void
  release(id: string): void
}

const ComposerOverlayLiftContext = createContext<ComposerOverlayLiftApi | null>(null)

export function useComposerOverlayLiftHost(): {
  lift: number
  api: ComposerOverlayLiftApi
  Provider: typeof ComposerOverlayLiftContext.Provider
} {
  const [claims, setClaims] = useState<Record<string, number>>({})

  const api = useMemo<ComposerOverlayLiftApi>(
    () => ({
      report(id, height) {
        setClaims((current) => (current[id] === height ? current : { ...current, [id]: height }))
      },
      release(id) {
        setClaims((current) => {
          if (!(id in current)) return current
          const next = { ...current }
          delete next[id]
          return next
        })
      }
    }),
    []
  )

  const lift = useMemo(() => {
    const values = Object.values(claims)
    return values.length === 0 ? 0 : Math.max(...values)
  }, [claims])

  return { lift, api, Provider: ComposerOverlayLiftContext.Provider }
}

/** No-ops outside a composer, where these popovers are mounted standalone. */
export function useReportComposerOverlayLift(
  ref: RefObject<HTMLElement | null>,
  open: boolean,
  gap: number
): void {
  const api = useContext(ComposerOverlayLiftContext)
  const id = useId()
  const report = api?.report
  const release = api?.release

  useLayoutEffect(() => {
    const element = ref.current
    if (!open || !element || !report || !release) return undefined

    const measure = (): void => {
      report(id, Math.round(element.getBoundingClientRect().height) + gap)
    }
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(element)
    return () => {
      observer.disconnect()
      release(id)
    }
  }, [gap, id, open, ref, release, report])
}
