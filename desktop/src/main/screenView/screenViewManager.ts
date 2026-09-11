import { randomUUID } from 'node:crypto'
import {
  SCREEN_VIEW_FRAME_CHANNEL,
  SCREEN_VIEW_STATE_CHANNEL,
  type ScreenFrame,
  type ScreenViewFramePayload,
  type ScreenViewStatePayload,
  type ScreenViewStatus
} from '../../shared/screenView'
import { createScreenSocket, type ScreenSocketTarget } from './screenSocket'
import { ScreenViewSession, type ScreenViewSessionLike, type ScreenViewSessionOptions } from './screenViewSession'
import type { ScreenViewTarget } from './screenViewTarget'

export interface ScreenViewManagerDeps {
  resolveBridge(peerId: string, sessionId: string): Promise<ScreenSocketTarget>
  createSession?: (options: ScreenViewSessionOptions) => ScreenViewSessionLike
}

interface ViewRuntime {
  viewId: string
  target: ScreenViewTarget
  session: ScreenViewSessionLike
}

export class ScreenViewManager {
  private readonly byWindowId = new Map<number, ViewRuntime>()
  private readonly wiredWindowIds = new Set<number>()
  private deps: ScreenViewManagerDeps

  constructor(deps: ScreenViewManagerDeps) {
    this.deps = deps
  }

  updateDeps(deps: ScreenViewManagerDeps): void {
    this.deps = deps
  }

  open(target: ScreenViewTarget, request: { viewId: string; peerId: string; maxWidth: number }): void {
    this.closeWindow(target.id)
    this.wire(target)

    const session = (this.deps.createSession ?? defaultCreateSession)({
      peerId: request.peerId,
      maxWidth: request.maxWidth,
      watchers: target.isVisible() ? 1 : 0,
      resolveBridge: (peerId, sessionId) => this.deps.resolveBridge(peerId, sessionId),
      createSocket: createScreenSocket,
      newSessionId: randomUUID,
      onState: (status) => this.emitState(target, request.viewId, status),
      onFrame: (frame) => this.emitFrame(target, request.viewId, frame)
    })
    this.byWindowId.set(target.id, { viewId: request.viewId, target, session })
    session.start()
  }

  tune(target: ScreenViewTarget, request: { viewId: string; maxWidth: number }): void {
    this.find(target.id, request.viewId)?.session.setMaxWidth(request.maxWidth)
  }

  ack(target: ScreenViewTarget, request: { viewId: string }): void {
    this.find(target.id, request.viewId)?.session.ack()
  }

  close(target: ScreenViewTarget, request: { viewId: string }): void {
    if (!this.find(target.id, request.viewId)) return
    this.closeWindow(target.id)
  }

  closeWindow(windowId: number): void {
    const runtime = this.byWindowId.get(windowId)
    if (!runtime) return
    this.byWindowId.delete(windowId)
    runtime.session.close()
  }

  closeAll(): void {
    for (const windowId of [...this.byWindowId.keys()]) this.closeWindow(windowId)
  }

  refreshDemand(windowId: number): void {
    const runtime = this.byWindowId.get(windowId)
    if (!runtime) return
    runtime.session.setWatchers(runtime.target.isVisible() ? 1 : 0)
  }

  private wire(target: ScreenViewTarget): void {
    if (this.wiredWindowIds.has(target.id)) return
    this.wiredWindowIds.add(target.id)
    target.onVisibilityChange(() => this.refreshDemand(target.id))
    target.onGone(() => this.closeWindow(target.id))
  }

  private find(windowId: number, viewId: string): ViewRuntime | null {
    const runtime = this.byWindowId.get(windowId)
    return runtime && runtime.viewId === viewId ? runtime : null
  }

  private emitState(target: ScreenViewTarget, viewId: string, status: ScreenViewStatus): void {
    const payload: ScreenViewStatePayload = { viewId, status }
    target.send(SCREEN_VIEW_STATE_CHANNEL, payload)
  }

  private emitFrame(target: ScreenViewTarget, viewId: string, frame: ScreenFrame): void {
    const payload: ScreenViewFramePayload = {
      viewId,
      sequence: frame.sequence,
      width: frame.width,
      height: frame.height,
      capturedAtUnixMs: frame.capturedAtUnixMs,
      jpeg: standaloneBuffer(frame.jpeg)
    }
    target.send(SCREEN_VIEW_FRAME_CHANNEL, payload)
  }
}

/** `ws` hands out views into a pooled buffer, so the JPEG is copied out before it travels. */
function standaloneBuffer(bytes: Uint8Array): ArrayBuffer {
  const copy = new ArrayBuffer(bytes.byteLength)
  new Uint8Array(copy).set(bytes)
  return copy
}

function defaultCreateSession(options: ScreenViewSessionOptions): ScreenViewSessionLike {
  return new ScreenViewSession(options)
}

let sharedManager: ScreenViewManager | null = null

export function getScreenViewManager(deps: ScreenViewManagerDeps): ScreenViewManager {
  if (sharedManager) sharedManager.updateDeps(deps)
  else sharedManager = new ScreenViewManager(deps)
  return sharedManager
}

export function closeAllScreenViews(): void {
  sharedManager?.closeAll()
}
