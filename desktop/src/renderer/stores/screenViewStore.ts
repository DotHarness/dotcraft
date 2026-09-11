import { create } from 'zustand'
import {
  requestedFrameWidth,
  type ScreenViewDockPosition,
  type ScreenViewMode,
  type ScreenViewStatePayload,
  type ScreenViewStatus
} from '../../shared/screenView'
import { TITLE_BAR_OVERLAY_HEIGHT } from '../../shared/titleBarOverlay'

export const DOCK_WIDTH_MIN = 168
export const DOCK_WIDTH_MAX = 720
export const DOCK_WIDTH_DEFAULT = 360

export const DOCK_MARGIN = 8
export const DOCK_TOP_INSET = TITLE_BAR_OVERLAY_HEIGHT

export interface DockGeometry {
  width: number
  position: ScreenViewDockPosition
}

interface Size {
  width: number
  height: number
}

/** `closed` is a remembered choice, not the absence of one. */
export type ThreadScreenViewMode = ScreenViewMode | 'closed'

interface ScreenViewState {
  viewId: string | null
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
  applyState(payload: ScreenViewStatePayload): void
  setDockGeometry(geometry: DockGeometry): void
  loadDockGeometry(): Promise<void>
  tuneWidth(maxWidth: number): void
}

export type ScreenViewStore = ScreenViewState & ScreenViewActions

export function clampDockWidth(width: number, available?: number): number {
  const ceiling = available != null && available > 0 ? Math.min(DOCK_WIDTH_MAX, available) : DOCK_WIDTH_MAX
  return Math.round(Math.min(Math.max(width, DOCK_WIDTH_MIN), Math.max(DOCK_WIDTH_MIN, ceiling)))
}

export function clampDockPosition(
  position: ScreenViewDockPosition,
  size: Size,
  viewport: Size
): ScreenViewDockPosition {
  const maxX = viewport.width - size.width - DOCK_MARGIN
  const maxY = viewport.height - size.height - DOCK_MARGIN
  return {
    x: Math.round(Math.min(Math.max(position.x, DOCK_MARGIN), Math.max(DOCK_MARGIN, maxX))),
    y: Math.round(Math.min(Math.max(position.y, DOCK_TOP_INSET), Math.max(DOCK_TOP_INSET, maxY)))
  }
}

const initialState: ScreenViewState = {
  viewId: null,
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

function newViewId(): string {
  return `screen-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`
}

export const useScreenViewStore = create<ScreenViewStore>((set, get) => ({
  ...initialState,

  toggle(threadId, peerId) {
    const state = get()
    if (state.threadId === threadId && state.viewId) {
      get().close()
      return
    }
    get().open(threadId, peerId, 'dock')
  },

  open(threadId, peerId, mode) {
    const previous = get().viewId
    if (previous) void window.api.screenView.close({ viewId: previous })

    const maxWidth = requestedFrameWidth(get().dockWidth, window.devicePixelRatio)
    const viewId = newViewId()
    set((state) => ({
      viewId,
      threadId,
      peerId,
      mode,
      status: { kind: 'connecting' },
      requestedWidth: maxWidth,
      modeByThread: { ...state.modeByThread, [threadId]: mode }
    }))
    void window.api.screenView.open({ viewId, peerId, maxWidth })
  },

  setMode(mode) {
    const { viewId, threadId, mode: current } = get()
    if (!viewId || current === mode) return
    set((state) => ({
      mode,
      modeByThread: threadId ? { ...state.modeByThread, [threadId]: mode } : state.modeByThread
    }))
  },

  close() {
    const { viewId, threadId } = get()
    if (viewId) void window.api.screenView.close({ viewId })
    set((state) => ({
      viewId: null,
      threadId: null,
      peerId: null,
      status: { kind: 'connecting' },
      requestedWidth: null,
      modeByThread: threadId ? { ...state.modeByThread, [threadId]: 'closed' } : state.modeByThread
    }))
  },

  syncThread(threadId, peerId, canOpen) {
    const state = get()
    if (state.viewId && (state.threadId !== threadId || state.peerId !== peerId)) {
      void window.api.screenView.close({ viewId: state.viewId })
      set({ viewId: null, threadId: null, peerId: null, requestedWidth: null, status: { kind: 'connecting' } })
    }
    if (!threadId || !peerId) return
    if (get().viewId) return
    const remembered = get().modeByThread[threadId]
    if (!remembered || remembered === 'closed') return
    if (!canOpen) return
    get().open(threadId, peerId, remembered)
  },

  applyState(payload) {
    if (payload.viewId !== get().viewId) return
    set({ status: payload.status })
  },

  setDockGeometry({ width, position }) {
    const dockWidth = clampDockWidth(width)
    set({ dockWidth, dockPosition: position })
    void window.api.settings.set({ screenViewDockWidth: dockWidth, screenViewDockPosition: position })
  },

  async loadDockGeometry() {
    if (get().dockGeometryLoaded) return
    set({ dockGeometryLoaded: true })
    try {
      const settings = await window.api.settings.get()
      const width = settings.screenViewDockWidth
      const position = settings.screenViewDockPosition
      set({
        ...(typeof width === 'number' && Number.isFinite(width) ? { dockWidth: clampDockWidth(width) } : {}),
        ...(position ? { dockPosition: { x: position.x, y: position.y } } : {})
      })
    } catch {}
  },

  tuneWidth(maxWidth) {
    const { viewId, requestedWidth } = get()
    if (!viewId || requestedWidth === maxWidth) return
    set({ requestedWidth: maxWidth })
    void window.api.screenView.tune({ viewId, maxWidth })
  }
}))

let subscribed = false

export function bootstrapScreenView(): void {
  if (subscribed) return
  subscribed = true
  window.api.screenView.onState((payload) => useScreenViewStore.getState().applyState(payload))
}
