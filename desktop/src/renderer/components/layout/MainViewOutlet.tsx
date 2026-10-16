import { Activity, useEffect, useState, type JSX, type ReactNode } from 'react'

export const RETAIN_HIDDEN_VIEW_MS = 10 * 60_000

interface MainViewOutletProps {
  activeView: string
  retains: (view: string) => boolean
  renderView: (view: string) => ReactNode
}

interface RetainedViews {
  active: string
  /** Mounted views in first-visit order, so switching never moves their DOM. */
  order: readonly string[]
  hiddenAt: ReadonlyMap<string, number>
}

export function MainViewOutlet({ activeView, retains, renderView }: MainViewOutletProps): JSX.Element {
  const [state, setState] = useState<RetainedViews>(() => ({
    active: activeView,
    order: [activeView],
    hiddenAt: new Map()
  }))

  let current = state
  if (state.active !== activeView) {
    current = enterView(state, activeView, retains, Date.now())
    setState(current)
  }

  useEffect(() => {
    if (state.hiddenAt.size === 0) return
    const nextExpiry = Math.min(...state.hiddenAt.values()) + RETAIN_HIDDEN_VIEW_MS
    const timer = window.setTimeout(
      () => setState((latest) => releaseExpiredViews(latest, Date.now())),
      Math.max(0, nextExpiry - Date.now())
    )
    return () => window.clearTimeout(timer)
  }, [state.hiddenAt])

  return (
    <>
      {current.order.map((view) => (
        <Activity key={view} mode={view === current.active ? 'visible' : 'hidden'}>
          <div data-testid={`view-${view}`} style={{ display: 'contents' }}>
            {renderView(view)}
          </div>
        </Activity>
      ))}
    </>
  )
}

function enterView(
  state: RetainedViews,
  view: string,
  retains: (view: string) => boolean,
  now: number
): RetainedViews {
  const hiddenAt = new Map(state.hiddenAt)
  let order = state.order
  if (retains(state.active)) hiddenAt.set(state.active, now)
  else order = order.filter((entry) => entry !== state.active)
  hiddenAt.delete(view)
  if (!order.includes(view)) order = [...order, view]
  return { active: view, order, hiddenAt }
}

function releaseExpiredViews(state: RetainedViews, now: number): RetainedViews {
  const hiddenAt = new Map<string, number>()
  const released = new Set<string>()
  for (const [view, since] of state.hiddenAt) {
    if (now - since >= RETAIN_HIDDEN_VIEW_MS) released.add(view)
    else hiddenAt.set(view, since)
  }
  if (released.size === 0) return state
  return { ...state, order: state.order.filter((view) => !released.has(view)), hiddenAt }
}
