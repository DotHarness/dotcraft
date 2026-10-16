import { createElement, lazy, useState, type ComponentProps, type ComponentType, type JSX } from 'react'

type AnyComponent = ComponentType<any>

export type PrefetchableComponent<C extends AnyComponent> = ((props: ComponentProps<C>) => JSX.Element) & {
  prefetch: () => Promise<void>
}

/** Once loaded, new mounts render the module directly, skipping Suspense and its reveal delay. */
export function lazyWithPrefetch<C extends AnyComponent>(load: () => Promise<C>): PrefetchableComponent<C> {
  let pending: Promise<C> | null = null
  let loaded: C | null = null

  const start = (): Promise<C> => {
    pending ??= load().then(
      (component) => {
        loaded = component
        return component
      },
      (error: unknown) => {
        pending = null
        throw error
      }
    )
    return pending
  }

  const Lazy = lazy(() => start().then((component) => ({ default: component })))

  function Prefetchable(props: ComponentProps<C>): JSX.Element {
    // Decided once per mount so the element type never switches under a mounted tree.
    const [Resolved] = useState(() => loaded)
    return createElement((Resolved ?? Lazy) as AnyComponent, props)
  }

  return Object.assign(Prefetchable, {
    prefetch: () => start().then(() => undefined)
  })
}
