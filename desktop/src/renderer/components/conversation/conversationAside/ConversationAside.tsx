import type {
  DesktopPluginConversationAsideContext,
  DesktopPluginDispose,
  DesktopPluginThreadSurfaceContext
} from '@dotcraft/plugin'
import {
  createContext,
  useCallback,
  useContext,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type CSSProperties,
  type JSX,
  type ReactNode,
  type RefObject
} from 'react'

import { useConversationTargetWidth } from '../../../contexts/ConversationLayoutContext'
import { DesktopPluginSurface } from '../../desktopPlugins/DesktopPluginSurface'
import { useDesktopPluginRegistry } from '../../../plugins/desktopPluginRegistry'
import { SummaryAside } from '../summary/SummaryAside'
import { conversationAsideLayout, type ConversationAsideLayout } from './conversationAsideLayout'

interface StreamMetrics {
  streamWidth: number
  readingWidth: number
}

interface ConversationAsideControl {
  pin(): DesktopPluginDispose
  reportMetrics(metrics: StreamMetrics | null): void
  reportRail(shown: boolean): void
}

interface ConversationAsideState {
  thread: DesktopPluginThreadSurfaceContext | null
  layout: ConversationAsideLayout | null
  insets: ConversationAsideInsets
}

export interface ConversationAsideInsets {
  leading: number
  trailing: number
}

const noop = (): void => {}

const ConversationAsideControlContext = createContext<ConversationAsideControl>({
  pin: () => noop,
  reportMetrics: noop,
  reportRail: noop
})
const NO_INSETS: ConversationAsideInsets = { leading: 0, trailing: 0 }
const ConversationAsideStateContext = createContext<ConversationAsideState>({ thread: null, layout: null, insets: NO_INSETS })
const ConversationColumnShiftContext = createContext(0)

export function useConversationColumnShift(): number {
  return useContext(ConversationColumnShiftContext)
}

export function useConversationAsideControl(): ConversationAsideControl {
  return useContext(ConversationAsideControlContext)
}

export function useConversationAsideInsets(): ConversationAsideInsets {
  return useContext(ConversationAsideStateContext).insets
}

interface ConversationAsideProviderProps {
  thread: DesktopPluginThreadSurfaceContext | null
  style: CSSProperties
  children: ReactNode
}

export function ConversationAsideProvider({ thread, style, children }: ConversationAsideProviderProps): JSX.Element {
  const targetWidth = useConversationTargetWidth()
  const [metrics, setMetrics] = useState<StreamMetrics | null>(null)
  const [railShown, setRailShown] = useState(false)
  const [pinCount, setPinCount] = useState(0)

  const pin = useCallback((): DesktopPluginDispose => {
    let live = true
    setPinCount((count) => count + 1)
    return () => {
      if (!live) return
      live = false
      setPinCount((count) => count - 1)
    }
  }, [])
  const reportMetrics = useCallback((next: StreamMetrics | null): void => {
    setMetrics((current) =>
      current && next && current.streamWidth === next.streamWidth && current.readingWidth === next.readingWidth
        ? current
        : next
    )
  }, [])
  const control = useMemo(() => ({ pin, reportMetrics, reportRail: setRailShown }), [pin, reportMetrics])

  const workspacePath = thread?.workspacePath ?? null
  const threadId = thread?.threadId ?? null
  const busy = thread?.busy ?? false
  const stableThread = useMemo(
    () => (threadId ? { workspacePath, threadId, busy } : null),
    [workspacePath, threadId, busy]
  )
  const layout = useMemo(
    () => stableThread && metrics
      ? conversationAsideLayout(targetWidth ?? metrics.streamWidth, metrics.readingWidth, pinCount > 0, railShown)
      : null,
    [stableThread, targetWidth, metrics, pinCount, railShown]
  )
  const leadingUsed = useDesktopPluginRegistry((registry) => registry.surfaces.some((surface) => surface.surface === 'conversation.aside.leading'))
  const insets = useMemo<ConversationAsideInsets>(
    () => layout
      ? {
          leading: leadingUsed ? layout.side - layout.shift : layout.leadingStart,
          trailing: layout.side + layout.shift
        }
      : NO_INSETS,
    [layout, leadingUsed]
  )
  const state = useMemo(() => ({ thread: stableThread, layout, insets }), [stableThread, layout, insets])
  const shift = layout?.shift ?? 0

  return (
    <ConversationAsideControlContext.Provider value={control}>
      <ConversationAsideStateContext.Provider value={state}>
        <ConversationColumnShiftContext.Provider value={shift}>
          <div style={{ ...style, '--conversation-column-shift': `${shift}px` } as CSSProperties}>
            {children}
          </div>
        </ConversationColumnShiftContext.Provider>
      </ConversationAsideStateContext.Provider>
    </ConversationAsideControlContext.Provider>
  )
}

export function ConversationAsides({ scrollRef }: { scrollRef: RefObject<HTMLDivElement | null> }): JSX.Element | null {
  const { thread } = useContext(ConversationAsideStateContext)
  return thread ? <ConversationAsideSeats scrollRef={scrollRef} thread={thread} /> : null
}

const releaseNothing = (): DesktopPluginDispose => noop

function ConversationAsideSeats({
  scrollRef,
  thread
}: {
  scrollRef: RefObject<HTMLDivElement | null>
  thread: DesktopPluginThreadSurfaceContext
}): JSX.Element {
  const { pin, reportMetrics } = useConversationAsideControl()
  const { layout } = useContext(ConversationAsideStateContext)
  const probeRef = useRef<HTMLDivElement>(null)

  useLayoutEffect(() => {
    const stream = scrollRef.current
    const probe = probeRef.current
    if (!stream || !probe) return
    let frame: number | null = null
    const measure = (): void => {
      reportMetrics({ streamWidth: stream.offsetWidth, readingWidth: probe.offsetWidth })
    }
    const schedule = (): void => {
      frame ??= requestAnimationFrame(() => {
        frame = null
        measure()
      })
    }
    const resizes = new ResizeObserver(schedule)
    resizes.observe(stream)
    resizes.observe(probe)
    measure()
    return () => {
      if (frame !== null) cancelAnimationFrame(frame)
      resizes.disconnect()
      reportMetrics(null)
    }
  }, [reportMetrics, scrollRef])

  const tier = layout?.tier ?? 'overlay'
  const leadingWidth = layout?.leadingWidth ?? 0
  const trailingWidth = layout?.trailingWidth ?? 0
  const leading = useMemo<DesktopPluginConversationAsideContext>(
    () => ({ ...thread, layout: tier, width: leadingWidth, pin: releaseNothing }),
    [thread, tier, leadingWidth]
  )
  const trailing = useMemo<DesktopPluginConversationAsideContext>(
    () => ({ ...thread, layout: tier, width: trailingWidth, pin }),
    [thread, tier, trailingWidth, pin]
  )

  return (
    <div className="dc-conversation-asides">
      <div ref={probeRef} className="dc-conversation-asides__probe" aria-hidden="true" />
      {layout && (
        <>
          <div className="dc-conversation-aside" style={{ left: layout.leadingStart, width: leadingWidth }}>
            <DesktopPluginSurface name="conversation.aside.leading" context={leading} />
          </div>
          <div className="dc-conversation-aside" style={{ right: 0, width: trailingWidth }}>
            <DesktopPluginSurface name="conversation.aside.trailing" context={trailing}>
              <SummaryAside context={trailing} />
            </DesktopPluginSurface>
          </div>
        </>
      )}
    </div>
  )
}
