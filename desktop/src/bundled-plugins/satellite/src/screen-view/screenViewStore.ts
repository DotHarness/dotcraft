import { create } from 'zustand'
import type { DesktopPluginScreenSession } from '@dotcraft/plugin'
import { satelliteHost } from '../runtime'
import { requestedFrameWidth, type ScreenViewDockPosition, type ScreenViewMode, type ScreenViewStatus } from './screenViewTypes'

export const DOCK_WIDTH_MIN = 168
export const DOCK_WIDTH_MAX = 720
export const DOCK_WIDTH_DEFAULT = 360
export const DOCK_MARGIN = 8
// The dock stays below the native caption controls.
export const DOCK_TOP_INSET = 36

export interface DockGeometry {
  width: number
  position: ScreenViewDockPosition
}

interface Size { width: number; height: number }
export type ThreadScreenViewMode = ScreenViewMode | 'closed'

interface ScreenViewState {
  session: DesktopPluginScreenSession | null
  threadId: string | null
  peerId: string | null
  mode: ScreenViewMode
  status: ScreenViewStatus
  modeByThread: Record<string, ThreadScreenViewMode>
  dockWidth: number
  dockPosition: ScreenViewDockPosition | null
  dockGeometryLoaded: boolean
  requestedWidth: number | null
}

interface ScreenViewActions {
  toggle(threadId: string, peerId: string): void
  open(threadId: string, peerId: string, mode: ScreenViewMode): void
  setMode(mode: ScreenViewMode): void
  close(): void
  syncThread(threadId: string | null, peerId: string | null, canOpen: boolean): void
  setDockGeometry(geometry: DockGeometry): void
  loadDockGeometry(): Promise<void>
  tuneWidth(maxWidth: number): void
}

export type ScreenViewStore = ScreenViewState & ScreenViewActions

export function clampDockWidth(width: number, available?: number): number {
  const ceiling = available != null && available > 0 ? Math.min(DOCK_WIDTH_MAX, available) : DOCK_WIDTH_MAX
  return Math.round(Math.min(Math.max(width, DOCK_WIDTH_MIN), Math.max(DOCK_WIDTH_MIN, ceiling)))
}

export function clampDockPosition(position: ScreenViewDockPosition, size: Size, viewport: Size): ScreenViewDockPosition {
  const maxX = viewport.width - size.width - DOCK_MARGIN
  const maxY = viewport.height - size.height - DOCK_MARGIN
  return {
    x: Math.round(Math.min(Math.max(position.x, DOCK_MARGIN), Math.max(DOCK_MARGIN, maxX))),
    y: Math.round(Math.min(Math.max(position.y, DOCK_TOP_INSET), Math.max(DOCK_TOP_INSET, maxY)))
  }
}

const initialState: ScreenViewState = {
  session: null,
  threadId: null,
  peerId: null,
  mode: 'dock',
  status: { kind: 'connecting' },
  modeByThread: {},
  dockWidth: DOCK_WIDTH_DEFAULT,
  dockPosition: null,
  dockGeometryLoaded: false,
  requestedWidth: null
}

let generation = 0

function releaseSession(): void {
  useScreenViewStore.getState().session?.close()
}

export const useScreenViewStore = create<ScreenViewStore>((set, get) => ({
  ...initialState,

  toggle(threadId, peerId) {
    const state = get()
    if (state.threadId === threadId && state.session) {
      get().close()
      return
    }
    get().open(threadId, peerId, 'dock')
  },

  open(threadId, peerId, mode) {
    releaseSession()
    const maxWidth = requestedFrameWidth(get().dockWidth, window.devicePixelRatio)
    const session = satelliteHost().screenView.open({ peerId, maxWidth })
    set((state) => ({
      session, threadId, peerId, mode,
      status: { kind: 'connecting' },
      requestedWidth: maxWidth,
      modeByThread: { ...state.modeByThread, [threadId]: mode }
    }))
    session.onState((status) => {
      if (get().session === session) set({ status })
    })
  },

  setMode(mode) {
    const { session, threadId, mode: current } = get()
    if (!session || current === mode) return
    set((state) => ({ mode, modeByThread: threadId ? { ...state.modeByThread, [threadId]: mode } : state.modeByThread }))
  },

  close() {
    const { threadId } = get()
    releaseSession()
    set((state) => ({
      session: null, threadId: null, peerId: null,
      status: { kind: 'connecting' }, requestedWidth: null,
      modeByThread: threadId ? { ...state.modeByThread, [threadId]: 'closed' } : state.modeByThread
    }))
  },

  syncThread(threadId, peerId, canOpen) {
    const state = get()
    if (state.session && (state.threadId !== threadId || state.peerId !== peerId)) {
      releaseSession()
      set({ session: null, threadId: null, peerId: null, requestedWidth: null, status: { kind: 'connecting' } })
    }
    if (!threadId || !peerId || get().session || !canOpen) return
    const remembered = get().modeByThread[threadId]
    if (remembered && remembered !== 'closed') get().open(threadId, peerId, remembered)
  },

  setDockGeometry({ width, position }) {
    const dockWidth = clampDockWidth(width)
    set({ dockWidth, dockPosition: position })
    void satelliteHost().screenView.setLayout({ width: dockWidth, position }).catch(() => {})
  },

  async loadDockGeometry() {
    if (get().dockGeometryLoaded) return
    const current = generation
    set({ dockGeometryLoaded: true })
    try {
      const layout = await satelliteHost().screenView.getLayout()
      if (current !== generation) return
      set({
        ...(typeof layout.width === 'number' && Number.isFinite(layout.width) ? { dockWidth: clampDockWidth(layout.width) } : {}),
        ...(layout.position ? { dockPosition: layout.position } : {})
      })
    } catch {}
  },

  tuneWidth(maxWidth) {
    const { session, requestedWidth } = get()
    if (!session || requestedWidth === maxWidth) return
    set({ requestedWidth: maxWidth })
    void session.tune(maxWidth).catch(() => {})
  }
}))

export function disposeScreenView(): void {
  generation += 1
  releaseSession()
  useScreenViewStore.setState({ ...initialState, modeByThread: {} })
}
